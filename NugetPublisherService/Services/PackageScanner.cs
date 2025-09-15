using System.Text.RegularExpressions;
using NuGet.Configuration;
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

    public enum PublishStatus {
        Published,
        Failed
    }

    public class PackageScanner(ScanConfig scanConfig, GitLabConfig gitLabConfig, ILogger logger)
    {
        private readonly Regex _nugetPathPattern = new Regex(
            scanConfig.PathPatternRegex, 
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public async Task<List<PackageInfo>> FindNewPackagesAsync()
        {
            var newPackages = new List<PackageInfo>();
            var latestFolder = FindLatestNugetSourceFolder();

            if (latestFolder == null)
            {
                logger.LogWarning("Не найдена подходящая папка NugetSource.");
                return newPackages;
            }

            foreach (var file in Directory.EnumerateFiles(latestFolder, "*.nupkg"))
            {
                // Обработка каждого файла
                var match = Regex.Match(Path.GetFileName(file), @"^(.+)\.(\d+\.\d+\.\d+(-.+)?)\.nupkg$");
                if (!match.Success)
                {
                    logger.LogWarning("Не удалось распознать имя пакета: {file}", Path.GetFileName(file));
                    continue;
                }

                var packageId = match.Groups[1].Value;
                var version = match.Groups[2].Value;

                if (!await IsPackagePublishedAsync(packageId, version))
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

        private string? FindLatestNugetSourceFolder()
        {
            return Directory
                .GetDirectories(scanConfig.BasePath, "*", SearchOption.AllDirectories)
                .Where(p => _nugetPathPattern.IsMatch(p))
                .OrderByDescending(p => Directory.GetLastWriteTime(p))
                .FirstOrDefault();
        }

        private async Task<bool> IsPackagePublishedAsync(string packageId, string version)
        {
            try
            {
                var logger1 = new NuGetLogger(logger);

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
                var resource = await repository.GetResourceAsync<FindPackageByIdResource>();
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
                    logger1,
                    CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при проверке публикации пакета {pkg}", packageId);
                return false;
            }
        }
    }
}