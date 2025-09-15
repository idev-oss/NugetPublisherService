using NugetPublisherService.Models;

namespace NugetPublisherService
{
    public class AppSettings
    {
        public required ScanConfig Scan { get; set; }
        public required GitLabConfig GitLab { get; set; }
        public required EmailConfig Smtp { get; set; }
        public required bool DryRun { get; set; }
    }
}