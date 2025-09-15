namespace NugetPublisherService.Models
{
    public class GitLabConfig
    {
        public required string BaseUrl { get; set; }
        public required int ProjectId { get; set; }
        public required string PrivateToken { get; set; }
    }
}