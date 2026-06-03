using System.ComponentModel.DataAnnotations;

namespace NugetPublisherService.Models
{
    /// <summary>
    /// Настройки локального хранилища состояния (SQLite-кэш обработанных пакетов).
    /// </summary>
    public class StateConfig
    {
        /// <summary>
        /// Путь к файлу базы SQLite. Относительный путь разрешается от каталога приложения
        /// (рядом с .exe), а не от текущего рабочего каталога (у службы это C:\Windows\system32).
        /// Можно указать абсолютный путь, например C:\ProgramData\NugetPublisherService\state.db.
        /// По умолчанию "state.db" — рядом с исполняемым файлом.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string DatabasePath { get; set; } = "state.db";
    }
}
