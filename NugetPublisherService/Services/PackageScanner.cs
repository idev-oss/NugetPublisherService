using System.Globalization;
using System.Text.RegularExpressions;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NugetPublisherService.Logging;
using NugetPublisherService.Models;

namespace NugetPublisherService.Services
{
    /// <summary>
    /// Поиск новых .nupkg на сетевой шаре. Вместо рекурсивного обхода всего дерева
    /// строит пути напрямую по настраиваемой структуре BasePath\{год}\{дата}\{leaf}
    /// и сканирует только окно последних дней. Уже обработанные пакеты отсеиваются
    /// через локальный SQLite-кэш.
    /// </summary>
    public sealed partial class PackageScanner(
        ScanConfig scanConfig,
        GitLabConfig gitLabConfig,
        PackageStateStore stateStore,
        TimeProvider timeProvider,
        ILogger<PackageScanner> logger)
    {
        private FindPackageByIdResource? _findResource;

        public async Task<List<PackageInfo>> FindNewPackagesAsync(CancellationToken cancellationToken = default)
        {
            var newPackages = new List<PackageInfo>();

            var folders = EnumerateCandidateFolders();
            if (folders.Count == 0)
            {
                Log.NoCandidateFolders(logger, scanConfig.LookbackDays);
                return newPackages;
            }

            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log.ScanningFolder(logger, folder);

                foreach (var file in Directory.EnumerateFiles(folder, "*.nupkg"))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileInfo = new FileInfo(file);

                    // Файл ещё копируется или заблокирован — пропускаем до следующего цикла.
                    if (!IsFileReady(file))
                    {
                        Log.FileNotReady(logger, fileInfo.Name);
                        continue;
                    }

                    // Быстрый отсев по идентичности файла (без чтения .nupkg и без сети).
                    if (await stateStore.IsFilePublishedAsync(
                            file, fileInfo.Length, fileInfo.LastWriteTimeUtc, cancellationToken))
                    {
                        Log.FileSkippedByCache(logger, fileInfo.Name);
                        continue;
                    }

                    var (packageId, version) = ExtractPackageMetadata(file);
                    if (packageId is null || version is null)
                    {
                        Log.MetadataExtractFailed(logger, fileInfo.Name);
                        continue;
                    }

                    var package = new PackageInfo
                    {
                        FileName = fileInfo.Name,
                        PackageId = packageId,
                        Version = version,
                        FullPath = file,
                        FileSize = fileInfo.Length,
                        FileWriteTimeUtc = fileInfo.LastWriteTimeUtc,
                        PublishStatus = PublishStatus.Pending
                    };

                    // Тот же пакет уже отмечен опубликованным (возможно, из другой папки) —
                    // обновляем идентичность файла в кэше и пропускаем.
                    if (await stateStore.IsPackagePublishedAsync(packageId, version, cancellationToken))
                    {
                        package.PublishStatus = PublishStatus.Published;
                        await stateStore.SaveAsync(package, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
                        continue;
                    }

                    // Незнакомый кэшу пакет — разовая проверка в GitLab, чтобы засеять кэш
                    // без повторной публикации уже существующих пакетов.
                    if (await IsPublishedInGitLabAsync(packageId, version, cancellationToken))
                    {
                        package.PublishStatus = PublishStatus.Published;
                        await stateStore.SaveAsync(package, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
                        continue;
                    }

                    newPackages.Add(package);
                }
            }

            return newPackages;
        }

        /// <summary>
        /// Строит список папок-кандидатов прямым обходом известной структуры,
        /// без рекурсии по всему дереву. Возвращает только существующие leaf-папки
        /// для дат в окне [today - LookbackDays; today].
        /// </summary>
        private List<string> EnumerateCandidateFolders()
        {
            var result = new List<string>();

            if (!Directory.Exists(scanConfig.BasePath))
            {
                Log.BasePathUnavailable(logger, scanConfig.BasePath);
                return result;
            }

            var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            var windowStart = today.AddDays(-scanConfig.LookbackDays);

            var startYear = scanConfig.IncludePreviousYearFolder ? windowStart.Year : today.Year;
            for (var year = startYear; year <= today.Year; year++)
            {
                AddCandidateFoldersForYear(result, year, today, windowStart);
            }

            return result
                .OrderByDescending(SafeLastWriteTime)
                .ToList();
        }

        private void AddCandidateFoldersForYear(List<string> result, int year, DateOnly today, DateOnly windowStart)
        {
            var yearFolderName = new DateTime(year, 1, 1).ToString(scanConfig.YearFolderFormat, CultureInfo.InvariantCulture);
            var yearPath = Path.Combine(scanConfig.BasePath, yearFolderName);

            if (!Directory.Exists(yearPath))
            {
                return;
            }

            IEnumerable<string> dateDirectories;
            try
            {
                dateDirectories = Directory.EnumerateDirectories(yearPath, "*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.DirectoryAccessDenied(logger, yearPath, ex);
                return;
            }
            catch (IOException ex)
            {
                Log.FolderEnumerationError(logger, yearPath, ex);
                return;
            }

            foreach (var dateDir in dateDirectories)
            {
                var name = Path.GetFileName(dateDir);
                if (!DateOnly.TryParseExact(name, scanConfig.DateFolderFormat,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var folderDate))
                {
                    continue;
                }

                if (folderDate < windowStart || folderDate > today)
                {
                    continue;
                }

                var leaf = Path.Combine(dateDir, scanConfig.LeafRelativePath);
                if (Directory.Exists(leaf))
                {
                    result.Add(leaf);
                }
            }
        }

        private static DateTime SafeLastWriteTime(string path)
        {
            try
            {
                return Directory.GetLastWriteTimeUtc(path);
            }
            catch (IOException)
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>Проверяет, что файл полностью записан и не заблокирован другим процессом.</summary>
        private static bool IsFileReady(string filePath)
        {
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return stream.Length > 0;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Извлекает (Id, Version) пакета через NuGet.Packaging с fallback на парсинг имени файла.</summary>
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
                Log.MetadataReadError(logger, Path.GetFileName(filePath), ex);

                var match = PackageFileNameRegex().Match(Path.GetFileName(filePath));
                if (match.Success)
                {
                    return (match.Groups[1].Value, match.Groups[2].Value);
                }

                return (null, null);
            }
        }

        private async Task<bool> IsPublishedInGitLabAsync(string packageId, string version, CancellationToken cancellationToken)
        {
            try
            {
                var resource = await GetFindResourceAsync(cancellationToken);
                var nugetVersion = new NuGetVersion(version);

                using var cache = new SourceCacheContext
                {
                    NoCache = true,
                    DirectDownload = true,
                    RefreshMemoryCache = true
                };

                return await resource.DoesPackageExistAsync(
                    packageId, nugetVersion, cache, new NuGetLogger(logger), cancellationToken);
            }
            catch (Exception ex)
            {
                Log.PackageCheckError(logger, packageId, version, ex);
                return false;
            }
        }

        private async Task<FindPackageByIdResource> GetFindResourceAsync(CancellationToken cancellationToken)
        {
            if (_findResource is not null)
            {
                return _findResource;
            }

            var packageSource = new PackageSource(
                $"{gitLabConfig.BaseUrl}/projects/{gitLabConfig.ProjectId}/packages/nuget/index.json")
            {
                ProtocolVersion = 3,
                Credentials = PackageSourceCredential.FromUserInput(
                    source: $"{gitLabConfig.BaseUrl}/projects/{gitLabConfig.ProjectId}/packages/nuget/index.json",
                    username: "gitlab",
                    password: gitLabConfig.PrivateToken,
                    storePasswordInClearText: true,
                    validAuthenticationTypesText: null)
            };

            var repository = Repository.Factory.GetCoreV3(packageSource);
            _findResource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
            return _findResource;
        }

        [GeneratedRegex(@"^(.+?)\.(\d+\.\d+\.\d+[^.]*)\.nupkg$")]
        private static partial Regex PackageFileNameRegex();
    }
}
