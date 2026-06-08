using System.Net;
using NugetPublisherService.Logging;
using NugetPublisherService.Models;
using NugetPublisherService.Services;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NugetPublisherService
{
    public sealed class Worker(
        ILogger<Worker> logger,
        AppSettings settings,
        PackageScanner scanner,
        EmailNotifier emailNotifier,
        PackageStateStore stateStore,
        TimeProvider timeProvider,
        IHostApplicationLifetime lifetime) : BackgroundService
    {
        private const int MaxRetryAttempts = 3;
        private static readonly TimeSpan[] RetryDelays =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30)
        ];

        private enum PushResult
        {
            Published,
            Failed,
            Unauthorized
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await stateStore.InitializeAsync(stoppingToken);

            Log.ConfigurationLoaded(logger, settings.DryRun);

            while (!stoppingToken.IsCancellationRequested)
            {
                var scanStartedAt = timeProvider.GetLocalNow();
                Log.ScanStarted(logger, scanStartedAt);

                try
                {
                    var newPackages = await scanner.FindNewPackagesAsync(stoppingToken);

                    if (newPackages.Count > 0)
                    {
                        Log.NewPackagesFound(logger, newPackages.Count);

                        PackageInfo? authFailure = null;
                        if (settings.DryRun)
                        {
                            foreach (var package in newPackages)
                            {
                                package.PublishStatus = PublishStatus.Skipped;
                            }
                        }
                        else
                        {
                            var sourceUrl = $"{settings.GitLab.BaseUrl}/projects/{settings.GitLab.ProjectId}/packages/nuget/index.json";
                            authFailure = await PushPackagesWithSdkAsync(newPackages, sourceUrl, settings.GitLab.PrivateToken, stoppingToken);
                        }

                        var processedAt = timeProvider.GetUtcNow().UtcDateTime;

                        // Ошибка авторизации (401/403) — фатальная: ретраи и дальнейшие циклы
                        // бессмысленны. Уведомляем только администратора и останавливаем сервис.
                        if (authFailure is not null)
                        {
                            await stateStore.RecordFailureAsync(authFailure, authFailure.LastError, processedAt, stoppingToken);
                            Log.AuthFailureStopping(logger, authFailure.FileName);
                            await emailNotifier.SendAuthFailureAlertAsync(authFailure, stoppingToken);
                            lifetime.StopApplication();
                            return;
                        }

                        foreach (var package in newPackages)
                        {
                            if (package.PublishStatus == PublishStatus.Failed)
                            {
                                // Накопительный счётчик ошибок (для разового алерта по порогу).
                                package.FailureCount = await stateStore.RecordFailureAsync(
                                    package, package.LastError, processedAt, stoppingToken);
                            }
                            else
                            {
                                await stateStore.SaveAsync(package, processedAt, stoppingToken);
                            }
                        }

                        // Обычный отчёт — только по успешно опубликованным/пропущенным пакетам.
                        var succeeded = newPackages
                            .Where(p => p.PublishStatus is PublishStatus.Published or PublishStatus.Skipped)
                            .ToList();
                        if (succeeded.Count > 0)
                        {
                            await emailNotifier.SendReportAsync(succeeded, stoppingToken);
                        }

                        // Письмо об ошибке — один раз, ровно при достижении порога неудачных попыток.
                        var alerting = newPackages
                            .Where(p => p.PublishStatus == PublishStatus.Failed
                                        && p.FailureCount == settings.FailureAlertThreshold)
                            .ToList();
                        if (alerting.Count > 0)
                        {
                            await emailNotifier.SendFailureAlertAsync(
                                alerting, settings.FailureAlertThreshold, stoppingToken);
                        }
                    }
                    else
                    {
                        Log.NoNewPackages(logger);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    Log.ScanCancelled(logger);
                    break;
                }
                catch (Exception ex)
                {
                    Log.ScanError(logger, ex);
                }

                try
                {
                    await Task.Delay(GetDelayInterval(), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// В рабочие дни (Пн–Пт) в рабочие часы используется ScanIntervalMinutes,
        /// в остальное время — OffHoursIntervalHours.
        /// </summary>
        private TimeSpan GetDelayInterval()
        {
            var now = timeProvider.GetLocalNow();
            var scan = settings.Scan;

            var isWorkingDay = now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            var isWorkingHours = now.Hour >= scan.WorkingHourStart && now.Hour < scan.WorkingHourEnd;

            return isWorkingDay && isWorkingHours
                ? TimeSpan.FromMinutes(scan.ScanIntervalMinutes)
                : TimeSpan.FromHours(scan.OffHoursIntervalHours);
        }

        /// <summary>
        /// Публикует пакеты по одному. Возвращает пакет, на котором произошла ошибка
        /// авторизации (401/403), либо null, если фатальной ошибки авторизации не было.
        /// При ошибке авторизации публикация остальных пакетов прекращается.
        /// </summary>
        private async Task<PackageInfo?> PushPackagesWithSdkAsync(
            List<PackageInfo> packages, string sourceUrl, string apiKey, CancellationToken cancellationToken)
        {
            var nugetLogger = new NuGetLogger(logger);

            var packageSource = new PackageSource(sourceUrl) { ProtocolVersion = 3 };

            var packageUpdateResource = await Repository.Factory
                .GetCoreV3(packageSource)
                .GetResourceAsync<PackageUpdateResource>(cancellationToken);

            var timeout = TimeSpan.FromSeconds(30);

            // Публикуем пакеты по одному для корректной обработки ошибок.
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await PushPackageWithRetryAsync(
                    package, packageUpdateResource, timeout, apiKey, nugetLogger, cancellationToken);

                if (result == PushResult.Unauthorized)
                {
                    package.PublishStatus = PublishStatus.Failed;
                    return package; // токен невалиден — для остальных пакетов результат тот же.
                }

                package.PublishStatus = result == PushResult.Published ? PublishStatus.Published : PublishStatus.Failed;
            }

            return null;
        }

        private async Task<PushResult> PushPackageWithRetryAsync(
            PackageInfo package,
            PackageUpdateResource packageUpdateResource,
            TimeSpan timeout,
            string apiKey,
            NuGetLogger nugetLogger,
            CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    await packageUpdateResource.Push(
                        [package.FullPath],
                        symbolSource: null,
                        timeoutInSecond: (int)timeout.TotalSeconds,
                        disableBuffering: false,
                        getApiKey: _ => apiKey,
                        getSymbolApiKey: _ => null,
                        noServiceEndpoint: false,
                        skipDuplicate: true,
                        symbolPackageUpdateResource: null,
                        allowInsecureConnections: true,
                        nugetLogger);

                    Log.PackagePublished(logger, package.FileName);
                    package.LastError = null;
                    return PushResult.Published;
                }
                catch (Exception ex)
                {
                    package.LastError = $"{ex.GetType().Name}: {ex.Message}";

                    // Ошибка авторизации — ретраить бессмысленно, прекращаем сразу.
                    if (IsAuthError(ex))
                    {
                        return PushResult.Unauthorized;
                    }

                    if (attempt < MaxRetryAttempts)
                    {
                        var delay = RetryDelays[attempt];
                        Log.PackagePublishRetry(logger, package.FileName, attempt + 1, MaxRetryAttempts, delay.TotalSeconds, ex);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }

                    Log.PackagePublishFailed(logger, package.FileName, MaxRetryAttempts + 1, ex);
                    return PushResult.Failed;
                }
            }

            return PushResult.Failed;
        }

        /// <summary>Определяет, является ли исключение ошибкой авторизации (HTTP 401/403).</summary>
        private static bool IsAuthError(Exception exception)
        {
            for (var e = exception; e is not null; e = e.InnerException)
            {
                if (e is HttpRequestException http &&
                    http.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    return true;
                }

                // Fallback на случай, если StatusCode не заполнен.
                if (e.Message.Contains("(401)", StringComparison.Ordinal) ||
                    e.Message.Contains("(403)", StringComparison.Ordinal) ||
                    e.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                    e.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
