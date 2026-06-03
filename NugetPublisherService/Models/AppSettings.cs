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
        public StateConfig State { get; set; } = new() { DatabasePath = @"Data\state.db" };

        public required bool DryRun { get; set; }
    }
}
