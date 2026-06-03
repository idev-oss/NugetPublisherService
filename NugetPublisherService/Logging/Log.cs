namespace NugetPublisherService.Logging
{
    /// <summary>
    /// Высокопроизводительное логирование через source-generated делегаты
    /// (LoggerMessage). Устраняет boxing и парсинг шаблонов в рантайме.
    /// </summary>
    public static partial class Log
    {
        // --- Worker / жизненный цикл ---

        [LoggerMessage(EventId = 1, Level = LogLevel.Information,
            Message = "Конфигурация успешно загружена. DryRun: {DryRun}")]
        public static partial void ConfigurationLoaded(ILogger logger, bool dryRun);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information,
            Message = "Сканирование пакетов начато в {Time}")]
        public static partial void ScanStarted(ILogger logger, DateTimeOffset time);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information,
            Message = "Найдено новых пакетов: {Count}")]
        public static partial void NewPackagesFound(ILogger logger, int count);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information,
            Message = "Новых пакетов не найдено. Ожидание следующего цикла сканирования.")]
        public static partial void NoNewPackages(ILogger logger);

        [LoggerMessage(EventId = 5, Level = LogLevel.Information,
            Message = "Сканирование отменено по запросу остановки сервиса.")]
        public static partial void ScanCancelled(ILogger logger);

        [LoggerMessage(EventId = 6, Level = LogLevel.Error,
            Message = "Ошибка во время сканирования или отправки email.")]
        public static partial void ScanError(ILogger logger, Exception exception);

        // --- Публикация ---

        [LoggerMessage(EventId = 10, Level = LogLevel.Information,
            Message = "Успешно опубликован: {FileName}")]
        public static partial void PackagePublished(ILogger logger, string fileName);

        [LoggerMessage(EventId = 11, Level = LogLevel.Warning,
            Message = "Ошибка при публикации пакета {FileName}, попытка {Attempt}/{Max}. Повтор через {DelaySeconds} сек.")]
        public static partial void PackagePublishRetry(ILogger logger, string fileName, int attempt, int max, double delaySeconds, Exception exception);

        [LoggerMessage(EventId = 12, Level = LogLevel.Error,
            Message = "Ошибка при публикации пакета {FileName} после {Attempts} попыток")]
        public static partial void PackagePublishFailed(ILogger logger, string fileName, int attempts, Exception exception);

        // --- Сканер ---

        [LoggerMessage(EventId = 20, Level = LogLevel.Information,
            Message = "Сканирование папки: {Folder}")]
        public static partial void ScanningFolder(ILogger logger, string folder);

        [LoggerMessage(EventId = 21, Level = LogLevel.Warning,
            Message = "Базовая папка сканирования недоступна: {Path}")]
        public static partial void BasePathUnavailable(ILogger logger, string path);

        [LoggerMessage(EventId = 22, Level = LogLevel.Information,
            Message = "Не найдено ни одной папки-кандидата для сканирования в окне {LookbackDays} дн.")]
        public static partial void NoCandidateFolders(ILogger logger, int lookbackDays);

        [LoggerMessage(EventId = 23, Level = LogLevel.Information,
            Message = "Файл ещё копируется, пропускаем: {File}")]
        public static partial void FileNotReady(ILogger logger, string file);

        [LoggerMessage(EventId = 24, Level = LogLevel.Debug,
            Message = "Пропуск по кэшу (уже опубликован): {File}")]
        public static partial void FileSkippedByCache(ILogger logger, string file);

        [LoggerMessage(EventId = 25, Level = LogLevel.Warning,
            Message = "Не удалось извлечь метаданные пакета: {File}")]
        public static partial void MetadataExtractFailed(ILogger logger, string file);

        [LoggerMessage(EventId = 26, Level = LogLevel.Warning,
            Message = "Ошибка чтения метаданных пакета {File}, попытка парсинга имени файла")]
        public static partial void MetadataReadError(ILogger logger, string file, Exception exception);

        [LoggerMessage(EventId = 27, Level = LogLevel.Warning,
            Message = "Нет доступа к директории при обходе: {Path}")]
        public static partial void DirectoryAccessDenied(ILogger logger, string path, Exception exception);

        [LoggerMessage(EventId = 28, Level = LogLevel.Error,
            Message = "Ошибка при перечислении папок в {Path}")]
        public static partial void FolderEnumerationError(ILogger logger, string path, Exception exception);

        [LoggerMessage(EventId = 29, Level = LogLevel.Error,
            Message = "Ошибка при проверке публикации пакета {PackageId} v{Version}")]
        public static partial void PackageCheckError(ILogger logger, string packageId, string version, Exception exception);

        // --- Хранилище состояния ---

        [LoggerMessage(EventId = 40, Level = LogLevel.Information,
            Message = "Хранилище состояния готово: {DatabasePath}")]
        public static partial void StateStoreInitialized(ILogger logger, string databasePath);

        // --- Мост к логам NuGet SDK (уровень определяется в рантайме) ---

        [LoggerMessage(EventId = 60, Message = "{NuGetMessage}")]
        public static partial void NuGetMessage(ILogger logger, LogLevel level, string nuGetMessage);

        // --- Email ---

        [LoggerMessage(EventId = 50, Level = LogLevel.Information,
            Message = "Письмо с отчётом отправлено: {Recipients}")]
        public static partial void EmailSent(ILogger logger, string recipients);

        [LoggerMessage(EventId = 51, Level = LogLevel.Error,
            Message = "Ошибка при отправке письма")]
        public static partial void EmailError(ILogger logger, Exception exception);
    }
}
