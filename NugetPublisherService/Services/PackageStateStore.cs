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

        private readonly string _connectionString = BuildConnectionString(config.DatabasePath);

        private static string BuildConnectionString(string databasePath)
        {
            // Относительный путь резолвим от каталога приложения (рядом с .exe),
            // а НЕ от текущего рабочего каталога: у Windows-службы CWD = C:\Windows\system32.
            var fullPath = Path.IsPathRooted(databasePath)
                ? databasePath
                : Path.Combine(AppContext.BaseDirectory, databasePath);
            fullPath = Path.GetFullPath(fullPath);

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
                    PRIMARY KEY (PackageId, Version)
                );

                CREATE INDEX IF NOT EXISTS IX_Processed_File
                    ON ProcessedPackages(FilePath, FileSize, FileWriteTimeUtc);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var databaseFullPath = Path.GetFullPath(config.DatabasePath);
            Log.StateStoreInitialized(logger, databaseFullPath);
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

        /// <summary>Сохраняет (вставляет или обновляет) запись о результате обработки пакета.</summary>
        public async Task SaveAsync(PackageInfo package, DateTime processedAtUtc, CancellationToken cancellationToken)
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO ProcessedPackages
                    (PackageId, Version, FilePath, FileSize, FileWriteTimeUtc, Status, ProcessedAtUtc)
                VALUES ($id, $version, $path, $size, $time, $status, $processed)
                ON CONFLICT(PackageId, Version) DO UPDATE SET
                    FilePath         = excluded.FilePath,
                    FileSize         = excluded.FileSize,
                    FileWriteTimeUtc = excluded.FileWriteTimeUtc,
                    Status           = excluded.Status,
                    ProcessedAtUtc   = excluded.ProcessedAtUtc;
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
    }
}
