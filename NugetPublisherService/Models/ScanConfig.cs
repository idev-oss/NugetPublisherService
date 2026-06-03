using System.ComponentModel.DataAnnotations;

namespace NugetPublisherService.Models
{
    /// <summary>
    /// Настройки сканирования сетевой папки с пакетами.
    /// Структура путей: BasePath\{YearFolderFormat}\{DateFolderFormat}\{LeafRelativePath}\*.nupkg
    /// Пример: \\nas\app\update\_2026\2026-05-18\Refactor\NugetSource\Foo.1.2.3.nupkg
    /// </summary>
    public class ScanConfig
    {
        /// <summary>Корневая папка (сетевая шара), в которой лежат папки годов.</summary>
        [Required(AllowEmptyStrings = false)]
        public required string BasePath { get; set; }

        /// <summary>
        /// Формат имени папки года (стандартный формат DateTime).
        /// По умолчанию "_yyyy" → папка "_2026".
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string YearFolderFormat { get; set; } = "_yyyy";

        /// <summary>
        /// Формат имени папки даты (стандартный формат DateOnly/DateTime).
        /// По умолчанию "yyyy-MM-dd" → папка "2026-05-18".
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string DateFolderFormat { get; set; } = "yyyy-MM-dd";

        /// <summary>
        /// Относительный подпуть от папки даты до папки с .nupkg.
        /// По умолчанию "Refactor\NugetSource".
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string LeafRelativePath { get; set; } = @"Refactor\NugetSource";

        /// <summary>
        /// Окно сканирования: обрабатывать папки дат не старше указанного числа дней.
        /// Покрывает «прошлый + текущий месяц» при значении ~60.
        /// </summary>
        [Range(1, 3650)]
        public int LookbackDays { get; set; } = 60;

        /// <summary>
        /// Учитывать также папку предыдущего года, когда окно захватывает декабрь
        /// (корректная работа на стыке года в начале января).
        /// </summary>
        public bool IncludePreviousYearFolder { get; set; } = true;

        /// <summary>Интервал сканирования в рабочие часы, минуты.</summary>
        [Range(1, 1440)]
        public required int ScanIntervalMinutes { get; set; }

        /// <summary>Начало рабочих часов (час, 0–23). В это время используется ScanIntervalMinutes.</summary>
        [Range(0, 23)]
        public int WorkingHourStart { get; set; } = 9;

        /// <summary>Конец рабочих часов (час, 1–24, исключительно).</summary>
        [Range(1, 24)]
        public int WorkingHourEnd { get; set; } = 21;

        /// <summary>Интервал сканирования вне рабочих часов и в выходные, часы.</summary>
        [Range(1, 168)]
        public int OffHoursIntervalHours { get; set; } = 2;
    }
}
