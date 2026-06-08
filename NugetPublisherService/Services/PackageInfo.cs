namespace NugetPublisherService.Services
{
    /// <summary>Статус обработки пакета.</summary>
    public enum PublishStatus
    {
        /// <summary>Найден, ещё не обработан.</summary>
        Pending = 0,

        /// <summary>Успешно опубликован (или уже существовал в реестре).</summary>
        Published = 1,

        /// <summary>Ошибка публикации.</summary>
        Failed = 2,

        /// <summary>Пропущен (режим DryRun) — публикация не выполнялась.</summary>
        Skipped = 3
    }

    /// <summary>Информация о найденном пакете и результате его обработки.</summary>
    public sealed class PackageInfo
    {
        public required string FileName { get; set; }
        public required string PackageId { get; set; }
        public required string Version { get; set; }
        public required string FullPath { get; set; }
        public required long FileSize { get; set; }
        public required DateTime FileWriteTimeUtc { get; set; }
        public PublishStatus PublishStatus { get; set; } = PublishStatus.Pending;

        /// <summary>Текст последней ошибки публикации (для письма администратору).</summary>
        public string? LastError { get; set; }

        /// <summary>
        /// Накопительное число неудачных циклов публикации этого пакета (из БД).
        /// Используется, чтобы слать письмо об ошибке один раз при достижении порога.
        /// </summary>
        public int FailureCount { get; set; }
    }
}
