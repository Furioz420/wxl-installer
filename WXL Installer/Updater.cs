using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace WXL_Installer
{
    /// <summary>
    /// Very small auto-updater that checks a GitHub repo's latest release
    /// and self-updates by dropping a helper .bat that swaps files after we exit.
    /// </summary>
    public static class Updater
    {
        private const string Owner = "Furioz420";
        private const string Repo = "wxl-installer";
        // The release zip asset must match this (case-insensitive contains).
        private const string ReleaseAssetHint = "WXL-Installer";

        public static Version CurrentVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
                // Normalize to 3 parts.
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        /// <summary>
        /// Runs a background check. If an update is available, prompts the user;
        /// if they accept, performs the swap and exits the app.
        /// </summary>
        public static async Task CheckAsync(bool showUpToDate = false)
        {
            try
            {
                var release = await Task.Run(() => GitHubHelper.GetLatestRelease(Owner, Repo));
                if (release == null) return;

                var latest = ParseVersion(release.TagName);
                if (latest == null) return;

                if (latest <= CurrentVersion)
                {
                    if (showUpToDate)
                    {
                        MessageBox.Show(
                            $"You are on the latest version ({CurrentVersion}).",
                            "WarcraftXL Installer",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                var asset = release.Assets?.FirstOrDefault(a =>
                    a.Name != null &&
                    a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    a.Name.IndexOf(ReleaseAssetHint, StringComparison.OrdinalIgnoreCase) >= 0);
                if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl)) return;

                var result = MessageBox.Show(
                    $"A new version of WXL Installer is available.\n\n" +
                    $"    Current: {CurrentVersion}\n" +
                    $"    Latest:   {latest}\n\n" +
                    $"Download and install it now? The app will restart.",
                    "Update available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes) return;

                await ApplyUpdateAsync(asset.BrowserDownloadUrl, latest);
            }
            catch
            {
                // Silent failure — updater must never block app startup.
            }
        }

        private static async Task ApplyUpdateAsync(string zipUrl, Version newVersion)
        {
            var stagingRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WxlInstaller", "update-" + newVersion);
            if (Directory.Exists(stagingRoot))
            {
                try { Directory.Delete(stagingRoot, true); } catch { }
            }
            Directory.CreateDirectory(stagingRoot);

            var zipPath = Path.Combine(stagingRoot, "release.zip");
            var extractPath = Path.Combine(stagingRoot, "extracted");
            Directory.CreateDirectory(extractPath);

            await GitHubHelper.DownloadFileAsync(zipUrl, zipPath, null);
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            // GitHub-uploaded release zips may contain the files directly or nested
            // in one top-level folder — flatten if so.
            var topEntries = Directory.GetFileSystemEntries(extractPath);
            string sourceDir = extractPath;
            if (topEntries.Length == 1 && Directory.Exists(topEntries[0]))
                sourceDir = topEntries[0];

            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
                throw new InvalidOperationException("Cannot resolve current executable path.");
            var installDir = Path.GetDirectoryName(exePath)!;
            var exeName = Path.GetFileName(exePath);

            var batPath = Path.Combine(stagingRoot, "apply-update.bat");
            var log = Path.Combine(stagingRoot, "update.log");

            // Wait a moment for our process to exit, then robocopy /MIR-style overlay
            // (without /MIR because we do not want to delete user data), then relaunch.
            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine($"echo [%DATE% %TIME%] Applying WXL Installer update {newVersion} > \"{log}\"");
            // Wait up to ~10s for the old process to release its exe.
            script.AppendLine("set /a tries=0");
            script.AppendLine(":waitloop");
            script.AppendLine($"tasklist /FI \"IMAGENAME eq {exeName}\" | find /I \"{exeName}\" >nul");
            script.AppendLine("if errorlevel 1 goto ready");
            script.AppendLine("timeout /T 1 /NOBREAK >nul");
            script.AppendLine("set /a tries+=1");
            script.AppendLine("if %tries% LSS 15 goto waitloop");
            script.AppendLine(":ready");
            script.AppendLine($"robocopy \"{sourceDir}\" \"{installDir}\" /E /R:3 /W:1 >> \"{log}\"");
            script.AppendLine($"start \"\" \"{Path.Combine(installDir, exeName)}\"");
            script.AppendLine("endlocal");
            File.WriteAllText(batPath, script.ToString(), Encoding.ASCII);

            Process.Start(new ProcessStartInfo
            {
                FileName = batPath,
                WorkingDirectory = stagingRoot,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });

            // Exit — the .bat will relaunch after files are swapped.
            Application.Current.Shutdown();
        }

        private static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var t = tag.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase)) t = t.Substring(1);
            // Drop any pre-release suffix like "-beta".
            int dash = t.IndexOf('-');
            if (dash > 0) t = t.Substring(0, dash);
            return Version.TryParse(t, out var v) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
        }
    }
}
