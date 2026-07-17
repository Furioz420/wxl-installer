using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WXL_Installer
{
    [DataContract]
    public class GitHubReleaseAsset
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        [DataMember(Name = "size")]
        public long Size { get; set; }
    }

    [DataContract]
    public class GitHubRelease
    {
        [DataMember(Name = "tag_name")]
        public string TagName { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "assets")]
        public List<GitHubReleaseAsset> Assets { get; set; }
    }

    [DataContract]
    public class GitHubRepo
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "full_name")]
        public string FullName { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }

        [DataMember(Name = "default_branch")]
        public string DefaultBranch { get; set; }

        [DataMember(Name = "pushed_at")]
        public string PushedAt { get; set; }

        [DataMember(Name = "html_url")]
        public string HtmlUrl { get; set; }
    }

    [DataContract]
    public class GitHubSearchResult
    {
        [DataMember(Name = "items")]
        public List<GitHubRepo> Items { get; set; }
    }

    public class DownloadProgress
    {
        public long Downloaded { get; set; }
        public long Total { get; set; }
    }

    public static class GitHubHelper
    {
        private const string UserAgent = "WarcraftXL-Installer";
        private const string ApiBase = "https://api.github.com";

        private static readonly HttpClient Http = CreateHttp();

        private static HttpClient CreateHttp()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            c.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                     ?? Environment.GetEnvironmentVariable("WXL_GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
            {
                c.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            return c;
        }

        private static T GetJson<T>(string url)
        {
            // Simple on-disk cache to survive GitHub's 60-req/hour anon rate limit.
            var cachePath = GetCachePath(url);
            var fresh = TimeSpan.FromMinutes(30);

            try
            {
                if (File.Exists(cachePath) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < fresh)
                {
                    return Deserialize<T>(File.ReadAllText(cachePath));
                }
            }
            catch { /* ignore cache read errors */ }

            try
            {
                var json = Http.GetStringAsync(url).GetAwaiter().GetResult();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                    File.WriteAllText(cachePath, json);
                }
                catch { /* ignore cache write errors */ }
                return Deserialize<T>(json);
            }
            catch (HttpRequestException)
            {
                // On failure (rate limit / offline), fall back to any stale cache.
                if (File.Exists(cachePath))
                {
                    try { return Deserialize<T>(File.ReadAllText(cachePath)); }
                    catch { }
                }
                throw;
            }
        }

        private static T Deserialize<T>(string json)
        {
            using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var ser = new DataContractJsonSerializer(typeof(T));
            return (T)ser.ReadObject(ms);
        }

        private static string GetCachePath(string url)
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   "WxlInstaller", "gh-cache");
            var safe = new StringBuilder(url.Length);
            foreach (var ch in url)
                safe.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            if (safe.Length > 120) safe.Length = 120;
            return Path.Combine(dir, safe.ToString() + ".json");
        }

        public static GitHubRelease GetLatestRelease(string owner, string repo)
            => GetJson<GitHubRelease>(ApiBase + "/repos/" + owner + "/" + repo + "/releases/latest");

        public static GitHubRepo GetRepo(string owner, string repo)
            => GetJson<GitHubRepo>(ApiBase + "/repos/" + owner + "/" + repo);

        public static List<GitHubRepo> SearchByTopic(string topic)
        {
            var url = ApiBase + "/search/repositories?q=" + Uri.EscapeDataString("topic:" + topic) + "&per_page=100&sort=updated";
            return GetJson<GitHubSearchResult>(url).Items ?? new List<GitHubRepo>();
        }

        public static List<GitHubRepo> SearchByTopic(string org, string topic)
        {
            var url = ApiBase + "/search/repositories?q=" + Uri.EscapeDataString("org:" + org + " topic:" + topic) + "&per_page=100";
            return GetJson<GitHubSearchResult>(url).Items ?? new List<GitHubRepo>();
        }

        /// <summary>
        /// Downloads the default-branch zipball of a repo and extracts it into <paramref name="destDir"/>.
        /// The top-level "owner-repo-hash" folder from GitHub's zipball is flattened away.
        /// </summary>
        public static async Task DownloadAndExtractRepoAsync(
            string owner, string repo, string destDir,
            IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            // Resolve the actual default branch (main, master, ...) — codeload's HEAD alias returns 403.
            string branch = "main";
            try { branch = GetRepo(owner, repo).DefaultBranch ?? "main"; }
            catch { }

            var url = "https://github.com/" + owner + "/" + repo + "/archive/refs/heads/" + branch + ".zip";
            var tempZip = Path.Combine(Path.GetTempPath(), owner + "-" + repo + "-" + Guid.NewGuid().ToString("N") + ".zip");
            var tempExtract = tempZip + "-x";

            try
            {
                await DownloadFileAsync(url, tempZip, progress, ct).ConfigureAwait(false);

                if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempExtract);

                // Zipball contains a single top-level folder like "repo-branch"
                var roots = Directory.GetDirectories(tempExtract);
                var srcRoot = roots.Length == 1 ? roots[0] : tempExtract;

                if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                Directory.CreateDirectory(destDir);
                CopyDirectory(srcRoot, destDir);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true); } catch { }
            }
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
        }


        public static async Task DownloadFileAsync(
            string url,
            string destPath,
            IProgress<DownloadProgress> progress,
            CancellationToken ct)
        {
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1L;
            long downloaded = 0;

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buf = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                downloaded += read;
                progress?.Report(new DownloadProgress { Downloaded = downloaded, Total = total });
            }
        }

        public static Task DownloadFileAsync(string url, string destPath, IProgress<DownloadProgress> progress)
            => DownloadFileAsync(url, destPath, progress, CancellationToken.None);
    }
}
