using System.Text.RegularExpressions;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NugetPublisherService.Models;
using NuGet.Versioning;

namespace NugetPublisherService.Services
{
    public class PackageInfo
    {
        public required string FileName { get; set; }
        public required string PackageId { get; set; }
        public required string Version { get; set; }
        public required string FullPath { get; set; }
        public required PublishStatus PublishStatus { get; set; }
    }

    public enum PublishStatus
    {
        Published,
        Failed
    }

    public class PackageScanner(ScanConfig scanConfig, GitLabConfig gitLabConfig, ILogger logger)
    {
        private readonly Regex _nugetPathPattern = new(
            scanConfig.PathPatternRegex,
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public async Task<List<PackageInfo>> FindNewPackagesAsync(CancellationToken cancellationToken = default)
        {
            var newPackages = new List<PackageInfo>();
            var latestFolder = FindLatestNugetSourceFolder();

            if (latestFolder == null)
            {
                logger.LogWarning("Не найдена подходящая папка NugetSource.");
                return newPackages;
            }

            logger.LogInformation("Сканирование папки: {folder}", latestFolder);

            foreach (var file in Directory.EnumerateFiles(latestFolder, "*.nupkg"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Проверяем, что файл полностью записан и не заблокирован
                if (!IsFileReady(file))
                {
                    logger.LogInformation("Файл ещё копируется, пропускаем: {file}", Path.GetFileName(file));
                    continue;
                }

                var (packageId, version) = ExtractPackageMetadata(file);
                if (packageId == null || version == null)
                {
                    logger.LogWarning("Не удалось извлечь метаданные пакета: {file}", Path.GetFileName(file));
                    continue;
                }

                if (!await IsPackagePublishedAsync(packageId, version, cancellationToken))
                {
                    newPackages.Add(new PackageInfo
                    {
                        FileName = Path.GetFileName(file),
                        PackageId = packageId,
                        Version = version,
                        FullPath = file,
                        PublishStatus = PublishStatus.Failed
                    });
                }
            }

            return newPackages;
        }

        /// <summary>
        /// Проверяет, что файл полностью записан и не заблокирован другим процессом
        /// </summary>
        private bool IsFileReady(string filePath)
        {
            try
            {
                // Пытаемся открыть файл с эксклюзивным доступом
                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);

                // Дополнительно проверяем, что файл имеет минимальный размер для .nupkg
                return stream.Length > 0;
            }
            catch (IOException)
            {
                // Файл заблокирован - ещё копируется
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Нет доступа к файлу
                return false;
            }
        }

        /// <summary>
        /// Извлекает метаданные пакета используя NuGet.Packaging для надёжного парсинга
        /// </summary>
        private (string? PackageId, string? Version) ExtractPackageMetadata(string filePath)
        {
            try
            {
                using var packageReader = new PackageArchiveReader(filePath);
                var identity = packageReader.GetIdentity();
                return (identity.Id, identity.Version.ToString());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка чтения метаданных пакета {file}, попытка парсинга имени файла",
                    Path.GetFileName(filePath));

                // Fallback: парсинг имени файла
                var match = Regex.Match(Path.GetFileName(filePath), @"^(.+?)\.(\d+\.\d+\.\d+[^.]*)\.nupkg$");
                if (match.Success)
                {
                    return (match.Groups[1].Value, match.Groups[2].Value);
                }

                return (null, null);
            }
        }

        private string? FindLatestNugetSourceFolder()
        {
            try
            {
                var directories = new List<string>();

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(scanConfig.BasePath, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            if (_nugetPathPattern.IsMatch(dir))
                            {
                                directories.Add(dir);
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // Пропускаем директории без доступа
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    logger.LogWarning(ex, "Нет доступа к некоторым директориям в {path}", scanConfig.BasePath);
                }

                return directories
                    .OrderByDescending(p => Directory.GetLastWriteTime(p))
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при поиске директорий в {path}", scanConfig.BasePath);
                return null;
            }
        }

        private async Task<bool> IsPackagePublishedAsync(string packageId, string version, CancellationToken cancellationToken)
        {
            try
            {
                var nugetLogger = new NuGetLogger(logger);

                var packageSource =
                    new PackageSource(
                        $"{gitLabConfig.BaseUrl}/projects/{gitLabConfig.ProjectId}/packages/nuget/index.json")
                    {
                        ProtocolVersion = 3
                    };

                packageSource.Credentials = PackageSourceCredential.FromUserInput(
                    source: packageSource.Source,
                    username: "gitlab",
                    password: gitLabConfig.PrivateToken,
                    storePasswordInClearText: true,
                    validAuthenticationTypesText: null
                );

                var repository = Repository.Factory.GetCoreV3(packageSource);
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
                var nugetVersion = new NuGetVersion(version);

                using var cache = new SourceCacheContext
                {
                    NoCache = true,
                    DirectDownload = true,
                    RefreshMemoryCache = true
                };

                return await resource.DoesPackageExistAsync(
                    packageId,
                    nugetVersion,
                    cache,
                    nugetLogger,
                    cancellationToken
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при проверке публикации пакета {pkg} v{version}", packageId, version);
                return false;
            }
        }
    }
}