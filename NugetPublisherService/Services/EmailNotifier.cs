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
        /// <summary>Обычный отчёт об успешно опубликованных/пропущенных пакетах (получателям To).</summary>
        public async Task SendReportAsync(List<PackageInfo> packages, CancellationToken cancellationToken = default)
        {
            await SendAsync(config.To, BuildSubject(packages), BuildHtmlReport(packages), cancellationToken);
        }

        /// <summary>
        /// Письмо об ошибке публикации — отправляется один раз при достижении порога неудач.
        /// Обычным получателям (To) уходит уведомление без деталей с просьбой обратиться к
        /// администратору; администраторам (Admin) — те же пакеты плюс технические детали.
        /// Если Admin не задан, детали уходят на To.
        /// </summary>
        public async Task SendFailureAlertAsync(
            List<PackageInfo> failedPackages, int threshold, CancellationToken cancellationToken = default)
        {
            const string subject = "Ошибка публикации NuGet пакетов — требуется вмешательство администратора";

            var admins = config.Admin.Length > 0 ? config.Admin : config.To;
            await SendAsync(admins, subject, BuildAdminAlert(failedPackages, threshold), cancellationToken);

            // Если администраторы заданы отдельно — обычным получателям шлём версию без деталей.
            if (config.Admin.Length > 0)
            {
                await SendAsync(config.To, subject, BuildUserAlert(failedPackages, threshold), cancellationToken);
            }
        }

        /// <summary>
        /// Письмо об ошибке авторизации в GitLab (401/403). Отправляется ТОЛЬКО администраторам
        /// (если Admin не задан — на To). Обычные получатели не уведомляются.
        /// </summary>
        public async Task SendAuthFailureAlertAsync(PackageInfo package, CancellationToken cancellationToken = default)
        {
            const string subject = "GitLab: ошибка авторизации (401/403) — сервис остановлен";
            var admins = config.Admin.Length > 0 ? config.Admin : config.To;
            await SendAsync(admins, subject, BuildAuthAlert(package), cancellationToken);
        }

        private async Task SendAsync(
            string[] recipients, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            if (recipients.Length == 0)
            {
                return;
            }

            try
            {
                var message = new MimeMessage();
                message.From.Add(MailboxAddress.Parse(config.From));
                foreach (var to in recipients)
                {
                    message.To.Add(MailboxAddress.Parse(to));
                }

                message.Subject = subject;
                message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

                using var smtp = new SmtpClient();
                await smtp.ConnectAsync(config.Server, config.Port, config.UseSsl, cancellationToken);
                await smtp.AuthenticateAsync(config.Username, config.Password, cancellationToken);
                await smtp.SendAsync(message, cancellationToken);
                await smtp.DisconnectAsync(true, cancellationToken);

                var recipientList = string.Join(", ", recipients);
                Log.EmailSent(logger, recipientList);
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
            var hasSkipped = packages.Any(p => p.PublishStatus == PublishStatus.Skipped);

            if (hasSkipped && !hasPublished)
            {
                return "Новые NuGet пакеты (режим DryRun, публикация не выполнялась)";
            }

            return "Отчет о публикации NuGet пакетов";
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

        private static string BuildUserAlert(List<PackageInfo> packages, int threshold)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("<html><body>");
            sb.Append("<h2 style='color:#b00;'>Ошибка публикации NuGet пакетов</h2>");
            sb.Append(ci, $"<p>Не удалось опубликовать перечисленные пакеты после {threshold} попыток. Пожалуйста, обратитесь к администратору сервиса.</p>");
            AppendPackageList(sb, packages, includeError: false);
            sb.Append("<p>Это автоматическое уведомление от NugetPublisherService.</p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string BuildAdminAlert(List<PackageInfo> packages, int threshold)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("<html><body>");
            sb.Append("<h2 style='color:#b00;'>Ошибка публикации NuGet пакетов (детали для администратора)</h2>");
            sb.Append(ci, $"<p>Публикация перечисленных пакетов не удалась после {threshold} попыток. Проверьте доступность GitLab и срок действия/права токена (требуется запись в реестр пакетов).</p>");
            AppendPackageList(sb, packages, includeError: true);
            sb.Append("<p>Это автоматическое уведомление от NugetPublisherService.</p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static string BuildAuthAlert(PackageInfo package)
        {
            var sb = new StringBuilder();
            sb.Append("<html><body>");
            sb.Append("<h2 style='color:#b00;'>Ошибка авторизации в GitLab</h2>");
            sb.Append("<p>Сервис публикации NuGet остановлен: GitLab вернул 401/403 (отказ в доступе). " +
                      "Дальнейшие попытки публикации бессмысленны до исправления токена.</p>");
            sb.Append("<p>Проверьте срок действия и права токена (требуется запись в реестр пакетов проекта), " +
                      "затем запустите службу снова.</p>");
            AppendPackageList(sb, [package], includeError: true);
            sb.Append("<p>Это автоматическое уведомление от NugetPublisherService.</p>");
            sb.Append("</body></html>");
            return sb.ToString();
        }

        private static void AppendPackageList(StringBuilder sb, List<PackageInfo> packages, bool includeError)
        {
            var ci = CultureInfo.InvariantCulture;
            sb.Append("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse;'>");
            sb.Append("<thead style=\"background:#f0f0f0;\"><tr><th>Имя пакета</th><th>Версия</th><th>Путь</th>");
            if (includeError)
            {
                sb.Append("<th>Ошибка</th>");
            }
            sb.Append("</tr></thead><tbody>");

            foreach (var package in packages)
            {
                sb.Append("<tr>");
                sb.Append(ci, $"<td>{HttpUtility.HtmlEncode(package.PackageId)}</td>");
                sb.Append(ci, $"<td>{HttpUtility.HtmlEncode(package.Version)}</td>");
                sb.Append(ci, $"<td>{HttpUtility.HtmlEncode(package.FullPath)}</td>");
                if (includeError)
                {
                    sb.Append(ci, $"<td><code>{HttpUtility.HtmlEncode(package.LastError ?? "—")}</code></td>");
                }
                sb.Append("</tr>");
            }

            sb.Append("</tbody></table>");
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
