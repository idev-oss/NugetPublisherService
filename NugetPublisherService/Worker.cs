using Newtonsoft.Json;
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
            LoadConfigs();

            var scanner = new PackageScanner(settings.Scan, settings.GitLab, logger);
            var emailNotifier = new EmailNotifier(settings.Smtp, logger);

            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Сканирование пакетов начато в {time}", DateTime.Now);

                try
                {
                    var newPackages = await scanner.FindNewPackagesAsync();

                    if (newPackages.Count > 0)
                    {
                        logger.LogInformation("Найдено новых пакетов: {count}", newPackages.Count);

                        if (!settings.DryRun)
                        {
                            string sourceUrl = $"{settings.GitLab.BaseUrl}/projects/{settings.GitLab.ProjectId}/packages/nuget/index.json";
                            string apiKey = settings.GitLab.PrivateToken;
                            
                            // Публикуем все пакеты за один вызов
                            await PushPackagesWithSdkAsync(newPackages, sourceUrl, apiKey);
                        }
                        
                        await emailNotifier.SendReportAsync(newPackages);
                    }
                    else
                    {
                        logger.LogInformation("Новых пакетов не найдено. Ожидание следующего цикла сканирования.");
                    }
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

        private void LoadConfigs()
        {
            string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

            if (!File.Exists(configPath))
            {
                logger.LogError("Файл конфигурации не найден: {path}", configPath);
                throw new FileNotFoundException("appsettings.json не найден", configPath);
            }

            var configJson = File.ReadAllText(configPath);
            settings = JsonConvert.DeserializeObject<AppSettings>(configJson)
                       ?? throw new InvalidOperationException("Ошибка десериализации конфигурации");

            logger.LogInformation("Конфигурация успешно загружена. DryRun: {dryRun}", settings.DryRun);
        }
        
        private async Task PushPackagesWithSdkAsync(List<PackageInfo> packages, string sourceUrl, string apiKey)
        {
            var packagePaths = packages.Select(p => p.FullPath).ToList();

            try
            {
                var logger1 = new NuGetLogger(logger);

                var packageSource = new PackageSource(sourceUrl)
                {
                    ProtocolVersion = 3
                };

                var repository = NullSettings.Instance;
                var packageUpdateResource = await Repository.Factory
                    .GetCoreV3(packageSource)
                    .GetResourceAsync<PackageUpdateResource>();

                var timeout = TimeSpan.FromSeconds(30);

                // Используем перегрузку метода Push с поддержкой множественных путей
                await packageUpdateResource.Push(
                    packagePaths,
                    symbolSource: null,
                    timeoutInSecond: (int)timeout.TotalSeconds,
                    disableBuffering: false,
                    getApiKey: _ => apiKey,
                    getSymbolApiKey: _ => null,
                    noServiceEndpoint: false,
                    skipDuplicate: true,
                    symbolPackageUpdateResource: null,
                    allowInsecureConnections: true,
                    logger1);

                // Отмечаем все пакеты как успешно опубликованные
                foreach (var package in packages)
                {
                    string fileName = Path.GetFileName(package.FullPath);
                    logger.LogInformation("Успешно опубликован через NuGet SDK: {pkg}", fileName);
                    
                    package.PublishStatus = PublishStatus.Published;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при публикации нескольких пакетов через SDK");
                foreach (var package in packages)
                {
                    package.PublishStatus = PublishStatus.Failed;
                }
            }
        }
    }
}