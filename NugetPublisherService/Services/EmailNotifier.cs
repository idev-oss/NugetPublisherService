using System.Globalization;
using System.Text;
using System.Web;
using MailKit.Net.Smtp;
using MimeKit;
using NugetPublisherService.Logging;
using NugetPublisherService.Models;

namespace NugetPublisherService.Services
{
    public sealed class EmailNotifier(EmailConfig config, ILogger<EmailNotifier> logger)
    {
        public async Task SendReportAsync(List<PackageInfo> packages, CancellationToken cancellationToken = default)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(config.From));
                foreach (var to in config.To)
                {
                    message.To.Add(MailboxAddress.Parse(to));
                }

                message.Subject = BuildSubject(packages);

                var builder = new BodyBuilder { HtmlBody = BuildHtmlReport(packages) };
                message.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(config.Server, config.Port, config.UseSsl, cancellationToken);
                await smtp.AuthenticateAsync(config.Username, config.Password, cancellationToken);
                await smtp.SendAsync(message, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);

                var recipients = string.Join(", ", config.To);
                Log.EmailSent(logger, recipients);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.EmailError(logger, ex);
            }
        }

        private static string BuildSubject(List<PackageInfo> packages)
        {
            var hasPublished = packages.Any(p => p.PublishStatus == PublishStatus.Published);
            var hasFailed = packages.Any(p => p.PublishStatus == PublishStatus.Failed);
            var hasSkipped = packages.Any(p => p.PublishStatus == PublishStatus.Skipped);

            if (hasSkipped && !hasPublished && !hasFailed)
            {
                return "Новые NuGet пакеты (режим DryRun, публикация не выполнялась)";
            }

            return (hasPublished, hasFailed) switch
            {
                (true, true) => "Отчет о публикации NuGet пакетов (есть ошибки)",
                (true, false) => "Отчет о публикации NuGet пакетов",
                (false, true) => "Ошибка публикации NuGet пакетов",
                _ => "Новые NuGet пакеты для публикации"
            };
        }

        private static string BuildHtmlReport(List<PackageInfo> packages)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("<html><body>");
            sb.Append("<h2>Отчет по NuGet пакетам</h2>");

            if (packages.Count == 0)
            {
                sb.Append("<p>Нет новых пакетов для публикации.</p>");
            }
            else
            {
                sb.Append("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse;'>");
                sb.Append("<thead style=\"background:#f0f0f0;\"><tr><th>Имя пакета</th><th>Версия</th><th>Путь</th><th>Статус публикации</th></tr></thead><tbody>");

                foreach (var package in packages)
                {
                    sb.Append("<tr>");
                    sb.Append(ci, $"<td>{HttpUtility.HtmlEncode(package.PackageId)}</td>");
                    sb.Append(ci, $"<td>{HttpUtility.HtmlEncode(package.Version)}</td>");
                    sb.Append(ci, $"<td>{HttpUtility.HtmlEncode(package.FullPath)}</td>");
                    sb.Append(ci, $"<td>{RenderStatus(package.PublishStatus)}</td>");
                    sb.Append("</tr>");
                }

                sb.Append("</tbody></table>");
            }

            sb.Append("<br>");
            sb.Append("<p>Это автоматическое уведомление от NugetPublisherService.</p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string RenderStatus(PublishStatus status) => status switch
        {
            PublishStatus.Published => "<span style='color: green;'>Успешно</span>",
            PublishStatus.Failed => "<span style='color: red;'>Ошибка</span>",
            PublishStatus.Skipped => "<span style='color: gray;'>Пропущено (DryRun)</span>",
            _ => "<span style='color: orange;'>Ожидает</span>"
        };
    }
}
