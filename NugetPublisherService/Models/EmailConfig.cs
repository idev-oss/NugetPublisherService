namespace NugetPublisherService.Models
{
    public class EmailConfig
    {
        public required string Server { get; set; }
        public required int Port { get; set; }
        public required bool UseSsl { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
        public required string From { get; set; }
        public required string[] To { get; set; }
    }
}