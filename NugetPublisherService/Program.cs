using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NLog;
using NLog.Extensions.Logging;
using NugetPublisherService;
using NugetPublisherService.Services;
using NugetPublisherService.Validation;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

var logger = LogManager.Setup()
    .LoadConfigurationFromFile("nlog.config")
    .GetCurrentClassLogger();

try
{
    logger.Info("Запуск NugetPublisherService");

    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

    builder.Services.AddWindowsService(options => options.ServiceName = "NugetPublisherService");

    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    builder.Logging.AddNLog();

    // Конфигурация с fail-fast валидацией на старте (рекурсивно через source-gen валидатор).
    builder.Services.AddOptionsWithValidateOnStart<AppSettings>()
        .Bind(builder.Configuration);
    builder.Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IValidateOptions<AppSettings>, AppSettingsValidator>());

    builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<AppSettings>().Scan);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<AppSettings>().GitLab);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<AppSettings>().Smtp);
    builder.Services.AddSingleton(sp => sp.GetRequiredService<AppSettings>().State);

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<PackageStateStore>();
    builder.Services.AddSingleton<PackageScanner>();
    builder.Services.AddSingleton<EmailNotifier>();
    builder.Services.AddHostedService<Worker>();

    builder.Build().Run();
}
catch (Exception ex)
{
    logger.Error(ex, "Ошибка при запуске приложения.");
    throw;
}
finally
{
    LogManager.Shutdown();
}
