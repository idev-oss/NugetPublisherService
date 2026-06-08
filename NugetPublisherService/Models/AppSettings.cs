using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using NugetPublisherService.Models;

namespace NugetPublisherService
{
    public class AppSettings
    {
        [Required]
        [ValidateObjectMembers]
        public required ScanConfig Scan { get; set; }

        [Required]
        [ValidateObjectMembers]
        public required GitLabConfig GitLab { get; set; }

        [Required]
        [ValidateObjectMembers]
        public required EmailConfig Smtp { get; set; }

        [Required]
        [ValidateObjectMembers]
        public StateConfig State { get; set; } = new() { DatabasePath = "state.db" };

        public required bool DryRun { get; set; }

        /// <summary>
        /// Число неудачных циклов публикации пакета, после которого один раз отправляется
        /// письмо об ошибке (обычным получателям — «обратитесь к администратору»,
        /// администраторам — с деталями). По умолчанию 5.
        /// </summary>
        [Range(1, 100)]
        public int FailureAlertThreshold { get; set; } = 5;
    }
}
