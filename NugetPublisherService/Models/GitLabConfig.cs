using System.ComponentModel.DataAnnotations;

namespace NugetPublisherService.Models
{
    public class GitLabConfig
    {
        /// <summary>Базовый URL GitLab API, например http://git.example.local/api/v4</summary>
        [Required(AllowEmptyStrings = false)]
        [Url]
        public required string BaseUrl { get; set; }

        /// <summary>Идентификатор проекта в GitLab.</summary>
        [Range(1, int.MaxValue)]
        public required int ProjectId { get; set; }

        /// <summary>Personal/Project access token с правами на запись в NuGet-реестр.</summary>
        [Required(AllowEmptyStrings = false)]
        public required string PrivateToken { get; set; }
    }
}
