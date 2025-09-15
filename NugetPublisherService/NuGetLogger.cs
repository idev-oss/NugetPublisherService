using NuGet.Common;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NugetLogLevel = NuGet.Common.LogLevel;

public class NuGetLogger : LoggerBase
{
    private readonly ILogger _logger;

    public NuGetLogger(ILogger logger)
    {
        _logger = logger;
    }

    public override void Log(ILogMessage message)
    {
        switch (message.Level)
        {
            case NugetLogLevel.Debug:
            case NugetLogLevel.Verbose:
                _logger.LogDebug(message.Message);
                break;
            case NugetLogLevel.Information:
            case NugetLogLevel.Minimal:
                _logger.LogInformation(message.Message);
                break;
            case NugetLogLevel.Warning:
                _logger.LogWarning(message.Message);
                break;
            case NugetLogLevel.Error:
                _logger.LogError(message.Message);
                break;
        }
    }

    public override Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }
}