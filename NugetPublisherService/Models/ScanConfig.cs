namespace NugetPublisherService.Models
{
    public class ScanConfig
    {
        public required string BasePath { get; set; }
        public required int ScanIntervalMinutes { get; set; }
        public required string PathPatternRegex { get; set; } =
            @"\\_\d{4}\\\d{4}-\d{2}-\d{2}\\Refactor\\NugetSource$";
    }
}