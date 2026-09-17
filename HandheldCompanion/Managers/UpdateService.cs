using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HandheldCompanion.Managers
{
    public class ReleaseInfo
    {
        public string TagName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public Version Version { get; set; } = new Version(0, 0, 0, 0);
        public string DownloadUrl { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public long AssetSize { get; set; }
        public bool IsNewer { get; set; }
    }

    public static class UpdateService
    {
        private const string RepoApiUrl = "https://api.github.com/repos/r0bledas/LegionGoCompanion/releases/latest";
        private static readonly HttpClient httpClient;

        static UpdateService()
        {
            httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LegionGoCompanion-Updater/1.0");
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        }

        public static Version CurrentVersion
        {
            get
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                return ver ?? new Version(1, 1, 0, 0);
            }
        }

        public static async Task<ReleaseInfo?> CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                using var response = await httpClient.GetAsync(RepoApiUrl, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
                string releaseName = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "";
                string body = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";

                string cleanVer = tagName.TrimStart('v', 'V');
                int dashIdx = cleanVer.IndexOf('-');
                if (dashIdx > 0) cleanVer = cleanVer.Substring(0, dashIdx);

                if (!Version.TryParse(cleanVer, out var remoteVersion))
                {
                    cleanVer = releaseName.ToLower().Replace("release", "").Trim().TrimStart('v');
                    dashIdx = cleanVer.IndexOf('-');
                    if (dashIdx > 0) cleanVer = cleanVer.Substring(0, dashIdx);
                    if (!Version.TryParse(cleanVer, out remoteVersion))
                    {
                        remoteVersion = new Version(0, 0, 0, 0);
                    }
                }

                string downloadUrl = string.Empty;
                string assetName = string.Empty;
                long assetSize = 0;

                if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsElem.EnumerateArray())
                    {
                        string aName = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            assetName = aName;
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                            assetSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl))
                    {
                        foreach (var asset in assetsElem.EnumerateArray())
                        {
                            string aName = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            {
                                assetName = aName;
                                downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                                assetSize = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                                break;
                            }
                        }
                    }
                }

                var current = CurrentVersion;
                var currentNormalized = new Version(Math.Max(0, current.Major), Math.Max(0, current.Minor), Math.Max(0, current.Build));
                var remoteNormalized = new Version(Math.Max(0, remoteVersion.Major), Math.Max(0, remoteVersion.Minor), Math.Max(0, remoteVersion.Build));

                bool isNewer = remoteNormalized > currentNormalized;

                return new ReleaseInfo
                {
                    TagName = tagName,
                    Name = releaseName,
                    Body = body,
                    Version = remoteVersion,
                    DownloadUrl = downloadUrl,
                    AssetName = assetName,
                    AssetSize = assetSize,
                    IsNewer = isNewer
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[UpdateService] Error checking for update: {ex}");
                return null;
            }
        }

        public static async Task<string> DownloadUpdateAsync(ReleaseInfo release, IProgress<(long downloaded, long total, int percent)>? progress = null, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(release.DownloadUrl))
                throw new InvalidOperationException("No download URL available for this update.");

            string cacheDir = Path.Combine(Path.GetTempPath(), "LegionGoCompanionUpdate");
            if (!Directory.Exists(cacheDir))
                Directory.CreateDirectory(cacheDir);

            string targetFileName = string.IsNullOrEmpty(release.AssetName) ? "LegionGoCompanion-Setup.exe" : release.AssetName;
            string destPath = Path.Combine(cacheDir, targetFileName);

            using var response = await httpClient.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? release.AssetSize;

            using var sourceStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                totalRead += bytesRead;

                int percent = totalBytes > 0 ? (int)((totalRead * 100) / totalBytes) : 0;
                progress?.Report((totalRead, totalBytes, percent));
            }

            return destPath;
        }

        public static void LaunchInstallerAndExit(string installerPath, bool silent = false)
        {
            if (!File.Exists(installerPath))
                throw new FileNotFoundException("Installer not found", installerPath);

            var psi = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = silent ? "/SILENT /SUPPRESSMSGBOXES" : "",
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(psi);
            Environment.Exit(0);
        }
    }
}
