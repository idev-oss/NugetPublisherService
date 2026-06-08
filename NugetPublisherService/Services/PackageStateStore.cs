using System.Globalization;
using Microsoft.Data.Sqlite;
using NugetPublisherService.Logging;
using NugetPublisherService.Models;

namespace NugetPublisherService.Services
{
    /// <summary>
    /// Локальный кэш обработанных пакетов на SQLite. Позволяет на повторных циклах
    /// пропускать уже опубликованные пакеты без чтения .nupkg и без обращения к GitLab.
    /// Дедуп переживает рестарт сервиса.
    /// </summary>
    public sealed class PackageStateStore(StateConfig config, ILogger<PackageStateStore> logger)
    {
        private const string DateTimeFormat = "O"; // round-trip ISO-8601

        private readonly string _databaseFullPath = ResolveDatabasePath(config.DatabasePath);
        private readonly string _connectionString = BuildConnectionString(config.DatabasePath);

        /// <summary>
        /// Резолвит путь к файлу БД. Относительный путь считается от каталога приложения
        /// (рядом с .exe), а НЕ от текущего рабочего каталога: у Windows-службы CWD = C:\Windows\system32.
        /// </summary>
        private static string ResolveDatabasePath(string databasePath)
        {
            var fullPath = Path.IsPathRooted(databasePath)
                ? databasePath
                : Path.Combine(AppContext.BaseDirectory, databasePath);
            return Path.GetFullPath(fullPath);
        }

        private static string BuildConnectionString(string databasePath)
        {
            var fullPath = ResolveDatabasePath(databasePath);

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return new SqliteConnectionStringBuilder
            {
                DataSource = fullPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true
            }.ToString();
        }

        /// <summary>Создаёт схему БД и включает WAL. Вызывается один раз при старте.</summary>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode=WAL;

                CREATE TABLE IF NOT EXISTS ProcessedPackages (
                    PackageId        TEXT    NOT NULL,
                    Version          TEXT    NOT NULL,
                    FilePath         TEXT    NOT NULL,
                    FileSize         INTEGER NOT NULL,
                    FileWriteTimeUtc TEXT    NOT NULL,
                    Status           INTEGER NOT NULL,
                    ProcessedAtUtc   TEXT    NOT NULL,
                    FailureCount     INTEGER NOT NULL DEFAULT 0,
                    LastError        TEXT    NULL,
                    PRIMARY KEY (PackageId, Version)
                );

                CREATE INDEX IF NOT EXISTS IX_Processed_File
                    ON ProcessedPackages(FilePath, FileSize, FileWriteTimeUtc);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            // Миграция БД, созданных до появления столбцов FailureCount/LastError.
            await EnsureColumnAsync(connection, "FailureCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, "LastError", "TEXT NULL", cancellationToken);

            Log.StateStoreInitialized(logger, _databaseFullPath);
        }

        private static async Task EnsureColumnAsync(
            SqliteConnection connection, string column, string definition, CancellationToken cancellationToken)
        {
            await using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ProcessedPackages') WHERE name = $name;";
            check.Parameters.AddWithValue("$name", column);
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
            if (exists)
            {
                return;
            }

            await using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE ProcessedPackages ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>
        /// Быстрый отсев по идентичности файла (путь+размер+время записи). Если файл уже
        /// был успешно опубликован — его не нужно читать и проверять повторно.
        /// </summary>
        public async Task<bool> IsFilePublishedAsync(
            string filePath, long fileSize, DateTime fileWriteTimeUtc, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT 1 FROM ProcessedPackages
                WHERE FilePath = $path AND FileSize = $size
                  AND FileWriteTimeUtc = $time AND Status = $published
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$path", filePath);
            command.Parameters.AddWithValue("$size", fileSize);
            command.Parameters.AddWithValue("$time", fileWriteTimeUtc.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$published", (int)PublishStatus.Published);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null;
        }

        /// <summary>Проверяет, помечен ли пакет (id+версия) как опубликованный.</summary>
        public async Task<bool> IsPackagePublishedAsync(
            string packageId, string version, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT 1 FROM ProcessedPackages
                WHERE PackageId = $id AND Version = $version AND Status = $published
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", packageId);
            command.Parameters.AddWithValue("$version", version);
            command.Parameters.AddWithValue("$published", (int)PublishStatus.Published);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is not null;
        }

        /// <summary>
        /// Сохраняет успешный результат (Published/Skipped). Счётчик ошибок и текст ошибки
        /// сбрасываются.
        /// </summary>
        public async Task SaveAsync(PackageInfo package, DateTime processedAtUtc, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ProcessedPackages
                    (PackageId, Version, FilePath, FileSize, FileWriteTimeUtc, Status, ProcessedAtUtc, FailureCount, LastError)
                VALUES ($id, $version, $path, $size, $time, $status, $processed, 0, NULL)
                ON CONFLICT(PackageId, Version) DO UPDATE SET
                    FilePath         = excluded.FilePath,
                    FileSize         = excluded.FileSize,
                    FileWriteTimeUtc = excluded.FileWriteTimeUtc,
                    Status           = excluded.Status,
                    ProcessedAtUtc   = excluded.ProcessedAtUtc,
                    FailureCount     = 0,
                    LastError        = NULL;
                """;
            command.Parameters.AddWithValue("$id", package.PackageId);
            command.Parameters.AddWithValue("$version", package.Version);
            command.Parameters.AddWithValue("$path", package.FullPath);
            command.Parameters.AddWithValue("$size", package.FileSize);
            command.Parameters.AddWithValue("$time", package.FileWriteTimeUtc.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$status", (int)package.PublishStatus);
            command.Parameters.AddWithValue("$processed", processedAtUtc.ToString(DateTimeFormat, CultureInfo.InvariantCulture));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <summary>
        /// Фиксирует неудачную попытку публикации: увеличивает накопительный счётчик ошибок,
        /// сохраняет текст последней ошибки и возвращает новое значение счётчика.
        /// </summary>
        public async Task<int> RecordFailureAsync(
            PackageInfo package, string? error, DateTime processedAtUtc, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ProcessedPackages
                    (PackageId, Version, FilePath, FileSize, FileWriteTimeUtc, Status, ProcessedAtUtc, FailureCount, LastError)
                VALUES ($id, $version, $path, $size, $time, $status, $processed, 1, $error)
                ON CONFLICT(PackageId, Version) DO UPDATE SET
                    FilePath         = excluded.FilePath,
                    FileSize         = excluded.FileSize,
                    FileWriteTimeUtc = excluded.FileWriteTimeUtc,
                    Status           = excluded.Status,
                    ProcessedAtUtc   = excluded.ProcessedAtUtc,
                    FailureCount     = ProcessedPackages.FailureCount + 1,
                    LastError        = excluded.LastError
                RETURNING FailureCount;
                """;
            command.Parameters.AddWithValue("$id", package.PackageId);
            command.Parameters.AddWithValue("$version", package.Version);
            command.Parameters.AddWithValue("$path", package.FullPath);
            command.Parameters.AddWithValue("$size", package.FileSize);
            command.Parameters.AddWithValue("$time", package.FileWriteTimeUtc.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$status", (int)PublishStatus.Failed);
            command.Parameters.AddWithValue("$processed", processedAtUtc.ToString(DateTimeFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
    }
}
