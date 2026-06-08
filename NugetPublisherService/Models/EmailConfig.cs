using System.ComponentModel.DataAnnotations;

namespace NugetPublisherService.Models
{
    public class EmailConfig
    {
        [Required(AllowEmptyStrings = false)]
        public required string Server { get; set; }

        [Range(1, 65535)]
        public required int Port { get; set; }

        public required bool UseSsl { get; set; }

        [Required(AllowEmptyStrings = false)]
        public required string Username { get; set; }

        [Required(AllowEmptyStrings = false)]
        public required string Password { get; set; }

        [Required(AllowEmptyStrings = false)]
        [EmailAddress]
        public required string From { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Smtp.To должен содержать хотя бы один email адрес")]
        public required string[] To { get; set; }

        /// <summary>
        /// Администраторы сервиса. Им отправляются подробности ошибок публикации
        /// (текст исключения и т.п.). Если список пуст — детали уходят на адреса To.
        /// </summary>
        public string[] Admin { get; set; } = [];
    }
}
