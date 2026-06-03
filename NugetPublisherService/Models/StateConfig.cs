using System.ComponentModel.DataAnnotations;

namespace NugetPublisherService.Models
{
    /// <summary>
    /// Настройки локального хранилища состояния (SQLite-кэш обработанных пакетов).
    /// </summary>
    public class StateConfig
    {
        /// <summary>
        /// Путь к файлу базы SQLite. Относительный путь разрешается от рабочего каталога сервиса.
        /// По умолчанию "Data\state.db".
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string DatabasePath { get; set; } = @"Data\state.db";
    }
}
