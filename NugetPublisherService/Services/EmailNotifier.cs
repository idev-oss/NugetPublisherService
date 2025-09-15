using Microsoft.Extensions.Logging;
using MailKit.Net.Smtp;
using MimeKit;
using NugetPublisherService.Models;
using System.Text;
using System.Web;

namespace NugetPublisherService.Services
{
    public class EmailNotifier(EmailConfig config, ILogger logger)
    {
        public async Task SendReportAsync(List<PackageInfo> packages)
        {
            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(config.From));
                foreach (var to in config.To)
                    message.To.Add(MailboxAddress.Parse(to));
                
                bool hasPublishedPackages = packages.Any(p => p.PublishStatus == PublishStatus.Published);
                string subject = hasPublishedPackages 
                    ? "Отчет о публикации NuGet пакетов" 
                    : "Новые NuGet пакеты для публикации";
                
                message.Subject = subject;

                var builder = new BodyBuilder();
                builder.HtmlBody = BuildHtmlReport(packages);
                message.Body = builder.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(config.Server, config.Port, config.UseSsl);
                await smtp.AuthenticateAsync(config.Username, config.Password);
                await smtp.SendAsync(message);
                await smtp.DisconnectAsync(true);

                logger.LogInformation("Письмо с отчетом отправлено: {emails}", string.Join(", ", config.To));
            }
            catch (Exception ex)
            {
                logger.LogError("Ошибка при отправке письма: {msg}", ex.Message);
            }
        }

        private string BuildHtmlReport(List<PackageInfo> packages)
        {
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
                    sb.Append($"<td>{HttpUtility.HtmlEncode(package.PackageId)}</td>");
                    sb.Append($"<td>{HttpUtility.HtmlEncode(package.Version)}</td>");
                    sb.Append($"<td>{HttpUtility.HtmlEncode(package.FullPath)}</td>");
                    
                    string status = package.PublishStatus == PublishStatus.Published 
                        ? "<span style='color: green;'>Успешно</span>" 
                        : "<span style='color: red;'>Ошибка</span>";
                    sb.Append($"<td>{status}</td>");
                    
                    sb.Append("</tr>");
                }
                
                sb.Append("</tbody>");
                sb.Append("</table>");
            }
            
            sb.Append("<br>");
            sb.Append("<p>Это автоматическое уведомление от NugetPublisherService.</p>");
            
            sb.Append("</body></html>");
            return sb.ToString();
        }
    }
}