using NugetPublisherService.Services;
using NuGet.Configuration;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol;

namespace NugetPublisherService
{
    public class Worker(ILogger<Worker> logger, AppSettings settings) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            ValidateConfiguration();

            var scanner = new PackageScanner(settings.Scan, settings.GitLab, logger);
            var emailNotifier = new EmailNotifier(settings.Smtp, logger);

            logger.LogInformation("Конфигурация успешно загружена. DryRun: {dryRun}", settings.DryRun);

            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Сканирование пакетов начато в {time}", DateTime.Now);

                try
                {
                    var newPackages = await scanner.FindNewPackagesAsync(stoppingToken);

                    if (newPackages.Count > 0)
                    {
                        logger.LogInformation("Найдено новых пакетов: {count}", newPackages.Count);

                        if (!settings.DryRun)
                        {
                            string sourceUrl = $"{settings.GitLab.BaseUrl}/projects/{settings.GitLab.ProjectId}/packages/nuget/index.json";
                            string apiKey = settings.GitLab.PrivateToken;

                            await PushPackagesWithSdkAsync(newPackages, sourceUrl, apiKey, stoppingToken);
                        }

                        await emailNotifier.SendReportAsync(newPackages, stoppingToken);
                    }
                    else
                    {
                        logger.LogInformation("Новых пакетов не найдено. Ожидание следующего цикла сканирования.");
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    logger.LogInformation("Сканирование отменено по запросу остановки сервиса.");
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка во время сканирования или отправки email.");
                }

                await Task.Delay(GetDelayInterval(), stoppingToken);
            }
        }
        
        private TimeSpan GetDelayInterval()
        {
            var now = DateTime.Now;
            bool isWorkingDay = now.DayOfWeek != DayOfWeek.Saturday && now.DayOfWeek != DayOfWeek.Sunday;
            bool isWorkingHours = now.Hour >= 9 && now.Hour < 21;
    
            // В рабочие дни с 9:00 до 21:00 - интервал 20 минут
            if (isWorkingDay && isWorkingHours)
            {
                return TimeSpan.FromMinutes(settings.Scan.ScanIntervalMinutes);
            }
            // В остальное время - интервал 2 часа
            return TimeSpan.FromHours(2);
        }

        private void ValidateConfiguration()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(settings.Scan.BasePath))
                errors.Add("Scan.BasePath не может быть пустым");

            if (!Directory.Exists(settings.Scan.BasePath))
                errors.Add($"Scan.BasePath директория не существует: {settings.Scan.BasePath}");

            if (string.IsNullOrWhiteSpace(settings.Scan.PathPatternRegex))
                errors.Add("Scan.PathPatternRegex не может быть пустым");

            if (string.IsNullOrWhiteSpace(settings.GitLab.BaseUrl))
                errors.Add("GitLab.BaseUrl не может быть пустым");

            if (!Uri.TryCreate(settings.GitLab.BaseUrl, UriKind.Absolute, out _))
                errors.Add($"GitLab.BaseUrl имеет некорректный формат URL: {settings.GitLab.BaseUrl}");

            if (string.IsNullOrWhiteSpace(settings.GitLab.PrivateToken))
                errors.Add("GitLab.PrivateToken не может быть пустым");

            if (string.IsNullOrWhiteSpace(settings.Smtp.Server))
                errors.Add("Smtp.Server не может быть пустым");

            if (settings.Smtp.To == null || settings.Smtp.To.Length == 0)
                errors.Add("Smtp.To должен содержать хотя бы один email адрес");

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    logger.LogError("Ошибка конфигурации: {error}", error);
                }
                throw new InvalidOperationException($"Обнаружены ошибки конфигурации: {string.Join("; ", errors)}");
            }
        }
        
        private async Task PushPackagesWithSdkAsync(List<PackageInfo> packages, string sourceUrl, string apiKey, CancellationToken cancellationToken)
        {
            var nugetLogger = new NuGetLogger(logger);

            var packageSource = new PackageSource(sourceUrl)
            {
                ProtocolVersion = 3
            };

            var packageUpdateResource = await Repository.Factory
                .GetCoreV3(packageSource)
                .GetResourceAsync<PackageUpdateResource>(cancellationToken);

            var timeout = TimeSpan.FromSeconds(30);

            // Публикуем пакеты по одному для корректной обработки ошибок
            foreach (var package in packages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await packageUpdateResource.Push(
                        new[] { package.FullPath },
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

                    package.PublishStatus = PublishStatus.Published;
                    logger.LogInformation("Успешно опубликован: {pkg}", package.FileName);
                }
                catch (Exception ex)
                {
                    package.PublishStatus = PublishStatus.Failed;
                    logger.LogError(ex, "Ошибка при публикации пакета {pkg}", package.FileName);
                }
            }
        }
    }
}