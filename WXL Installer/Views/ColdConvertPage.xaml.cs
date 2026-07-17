using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;

namespace WXL_Installer.Views
{
    public partial class ColdConvertPage : Page
    {
        private const string CcOwner = "WarcraftXL";
        private const string CcRepo = "wxl-cold-converter";
        // Prefer x64 build over x86.
        private static readonly string[] AssetPreference = { "x64", "x86" };

        private readonly WizardState _state = WizardState.Current;

        public ColdConvertPage()
        {
            InitializeComponent();
            RefreshInstalledLabel();
        }

        private void RefreshInstalledLabel()
        {
            var exe = FindConverterExe();
            if (exe != null)
            {
                var ver = _state.Settings.ColdConverterVersion;
                LblInstalled.Text = string.IsNullOrEmpty(ver)
                    ? "Installed at: " + exe
                    : $"Installed {ver} — {exe}";
                BtnLaunch.Content = "Run cold converter";
                BtnReinstall.IsEnabled = true;
                BtnOpenFolder.IsEnabled = true;
            }
            else
            {
                LblInstalled.Text = "Not downloaded yet.";
                BtnLaunch.Content = "Download & run latest";
                BtnReinstall.IsEnabled = false;
                BtnOpenFolder.IsEnabled = false;
            }
        }

        private string FindConverterExe()
        {
            var dir = _state.Settings.ColdConverterPath;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
            // Look for the GUI exe -- ships as WarcraftXL-Cold-Converter.exe or similar; pick the largest .exe.
            var exes = Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories).ToArray();
            if (exes.Length == 0) return null;
            return exes.OrderByDescending(p => new FileInfo(p).Length).First();
        }

        private async void BtnLaunch_Click(object sender, RoutedEventArgs e)
        {
            var exe = FindConverterExe();
            if (exe != null)
            {
                LaunchExe(exe);
                return;
            }
            await DownloadAndLaunchAsync();
        }

        private async void BtnReinstall_Click(object sender, RoutedEventArgs e)
        {
            await DownloadAndLaunchAsync(forceReinstall: true, launchAfter: false);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = _state.Settings.ColdConverterPath;
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }

        private void LaunchExe(string exe)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = Path.GetDirectoryName(exe),
                    UseShellExecute = true,
                });
                SetStatus("✓  Launched " + Path.GetFileName(exe), "WxlSuccessBrush");
            }
            catch (Exception ex)
            {
                SetStatus("✗  " + ex.Message, "WxlDangerBrush");
            }
        }

        private async Task DownloadAndLaunchAsync(bool forceReinstall = false, bool launchAfter = true)
        {
            BtnLaunch.IsEnabled = false;
            BtnReinstall.IsEnabled = false;
            Progress.Value = 0;

            try
            {
                SetStatus("Fetching latest cold-converter release…", "WxlTextMutedBrush");
                var release = await Task.Run(() => GitHubHelper.GetLatestRelease(CcOwner, CcRepo));
                if (release == null) throw new Exception("No published cold-converter release found on GitHub.");

                var asset = PickAsset(release);
                if (asset == null) throw new Exception("The cold-converter release has no downloadable zip asset.");

                var installRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WxlInstaller", "cold-converter");
                Directory.CreateDirectory(installRoot);

                var versionDir = Path.Combine(installRoot, SafeName(release.TagName ?? "unknown"));
                if (forceReinstall && Directory.Exists(versionDir))
                {
                    try { Directory.Delete(versionDir, true); }
                    catch (Exception ex) { throw new Exception("Could not remove existing install: " + ex.Message); }
                }
                Directory.CreateDirectory(versionDir);

                var zipPath = Path.Combine(versionDir, asset.Name);
                SetStatus($"Downloading {asset.Name}…", "WxlTextMutedBrush");
                var prog = new Progress<DownloadProgress>(t =>
                {
                    if (t.Total > 0) Progress.Value = 80.0 * t.Downloaded / t.Total;
                });
                await GitHubHelper.DownloadFileAsync(asset.BrowserDownloadUrl, zipPath, prog);

                SetStatus("Extracting…", "WxlTextMutedBrush");
                Progress.Value = 90;
                var extractDir = Path.Combine(versionDir, "extracted");
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                // Flatten if the zip nests everything under one folder.
                var payload = FlattenPayload(extractDir);

                _state.Settings.ColdConverterPath = payload;
                _state.Settings.ColdConverterVersion = release.TagName ?? "";
                _state.Settings.Save();

                Progress.Value = 100;
                RefreshInstalledLabel();

                if (launchAfter)
                {
                    var exe = FindConverterExe();
                    if (exe == null) throw new Exception("No executable found in the extracted archive.");
                    LaunchExe(exe);
                }
                else
                {
                    SetStatus($"✓  Installed {release.TagName} at {payload}", "WxlSuccessBrush");
                }
            }
            catch (Exception ex)
            {
                Progress.Value = 0;
                SetStatus("✗  " + ex.Message, "WxlDangerBrush");
            }
            finally
            {
                BtnLaunch.IsEnabled = true;
                BtnReinstall.IsEnabled = FindConverterExe() != null;
            }
        }

        private static GitHubReleaseAsset PickAsset(GitHubRelease release)
        {
            if (release.Assets == null || release.Assets.Count == 0) return null;
            var zips = release.Assets
                .Where(a => a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (zips.Length == 0) return null;
            foreach (var pref in AssetPreference)
            {
                var hit = zips.FirstOrDefault(a => a.Name.IndexOf(pref, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit != null) return hit;
            }
            return zips[0];
        }

        private static string FlattenPayload(string extractDir)
        {
            var current = extractDir;
            for (int i = 0; i < 4; i++)
            {
                var entries = Directory.GetFileSystemEntries(current);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    current = entries[0];
                    continue;
                }
                break;
            }
            return current;
        }

        private static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
            }
            catch { }
            e.Handled = true;
        }

        private void SetStatus(string text, string brushKey)
        {
            LblStatus.Text = text;
            LblStatus.Foreground = (Brush)Application.Current.Resources[brushKey];
        }
    }
}
