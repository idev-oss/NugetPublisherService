using NuGet.Common;
using NugetPublisherService.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;
using NugetLogLevel = NuGet.Common.LogLevel;

namespace NugetPublisherService
{
    /// <summary>Перенаправляет сообщения NuGet SDK в Microsoft.Extensions.Logging.</summary>
    public sealed class NuGetLogger(ILogger logger) : LoggerBase
    {
        public override void Log(ILogMessage message)
        {
            var level = MapLevel(message.Level);
            var text = message.Message;
            NugetPublisherService.Logging.Log.NuGetMessage(logger, level, text);
        }

        public override Task LogAsync(ILogMessage message)
        {
            Log(message);
            return Task.CompletedTask;
        }

        private static MsLogLevel MapLevel(NugetLogLevel level) => level switch
        {
            NugetLogLevel.Debug or NugetLogLevel.Verbose => MsLogLevel.Debug,
            NugetLogLevel.Information or NugetLogLevel.Minimal => MsLogLevel.Information,
            NugetLogLevel.Warning => MsLogLevel.Warning,
            NugetLogLevel.Error => MsLogLevel.Error,
            _ => MsLogLevel.Information
        };
    }
}
