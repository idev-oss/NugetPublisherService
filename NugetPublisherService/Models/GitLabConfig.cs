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

        /// <summary>
        /// Таймаут одной попытки публикации пакета, секунды. Первый запрос к GitLab
        /// может быть медленным (прогрев соединения, NTLM-аутентификация ~20–30 сек),
        /// поэтому значение по умолчанию увеличено. По умолчанию 120.
        /// </summary>
        [Range(10, 600)]
        public int PushTimeoutSeconds { get; set; } = 120;
    }
}
