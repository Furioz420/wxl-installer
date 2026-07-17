using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WXL_Installer.Views
{
    public partial class ColdConvertPage : Page
    {
        private readonly WizardState _state = WizardState.Current;

        public ColdConvertPage()
        {
            InitializeComponent();
            TxtAssetsPath.Text = _state.Settings.AssetsPath ?? "";
            TxtOutputPath.Text = _state.Settings.ColdOutputPath ?? "";
        }

        private void TxtAssets_Changed(object sender, TextChangedEventArgs e)
        {
            _state.Settings.AssetsPath = TxtAssetsPath.Text?.Trim();
            _state.Settings.Save();
        }

        private void TxtOutput_Changed(object sender, TextChangedEventArgs e)
        {
            _state.Settings.ColdOutputPath = TxtOutputPath.Text?.Trim();
            _state.Settings.Save();
        }

        private void BrowseAssets_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = TxtAssetsPath.Text ?? "" };
            if (dlg.ShowDialog() == true) TxtAssetsPath.Text = dlg.FolderName;
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = TxtOutputPath.Text ?? "" };
            if (dlg.ShowDialog() == true) TxtOutputPath.Text = dlg.FolderName;
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Cold converting pre-processes your custom model and texture assets " +
                "(.m2 / .blp files) into a form WarcraftXL can load quickly at runtime.\n\n" +
                "• It only touches the assets folder you point it at — your client is untouched.\n" +
                "• You only need to run it when you add or change custom assets.\n" +
                "• This step is optional; skip it if you're not using custom assets yet.",
                "About Cold Converting",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            var assetsPath = TxtAssetsPath.Text?.Trim();
            if (string.IsNullOrEmpty(assetsPath) || !Directory.Exists(assetsPath))
            {
                SetStatus("Please select a valid assets folder first.", "WxlDangerBrush");
                return;
            }

            var outputPath = TxtOutputPath.Text?.Trim();
            if (string.IsNullOrEmpty(outputPath))
            {
                SetStatus("Please select an output folder for the converted assets.", "WxlDangerBrush");
                return;
            }
            try { Directory.CreateDirectory(outputPath); }
            catch (Exception ex)
            {
                SetStatus("Could not create output folder: " + ex.Message, "WxlDangerBrush");
                return;
            }

            BtnRun.IsEnabled = false;
            SetStatus("Fetching latest cold-converter release…", "WxlTextMutedBrush");
            Progress.Value = 5;

            try
            {
                var release = await Task.Run(() => GitHubHelper.GetLatestRelease("WarcraftXL", "wxl-cold-converter"));
                GitHubReleaseAsset asset = null;
                foreach (var a in release.Assets)
                    if (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { asset = a; break; }
                if (asset == null) throw new Exception("No zip asset in latest cold-converter release.");

                var tempDir = Path.Combine(Path.GetTempPath(), "wxl-cold-converter-" + release.TagName);
                var zipPath = Path.Combine(tempDir, asset.Name);

                SetStatus("Downloading cold-converter…", "WxlTextMutedBrush");
                var prog = new Progress<DownloadProgress>(t =>
                {
                    if (t.Total > 0) Progress.Value = 5 + 55.0 * t.Downloaded / t.Total;
                });
                await GitHubHelper.DownloadFileAsync(asset.BrowserDownloadUrl, zipPath, prog);
                Progress.Value = 60;

                SetStatus("Extracting…", "WxlTextMutedBrush");
                var extractDir = Path.Combine(tempDir, "extracted");
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractDir);
                Progress.Value = 70;

                var exes = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);
                if (exes.Length == 0) throw new Exception("No executable in the cold-converter archive.");
                var exePath = exes[0];

                SetStatus("Running cold-converter — this can take a while…", "WxlTextMutedBrush");
                Progress.Value = 75;
                var psi = new ProcessStartInfo(exePath, $"\"{assetsPath}\" \"{outputPath}\"")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    await Task.Run(() => proc.WaitForExit());
                    if (proc.ExitCode != 0)
                    {
                        var err = proc.StandardError.ReadToEnd();
                        throw new Exception($"Cold-converter exited with code {proc.ExitCode}.\n{err}");
                    }
                }

                Progress.Value = 100;
                SetStatus("✓  Cold conversion complete.", "WxlSuccessBrush");
            }
            catch (Exception ex)
            {
                Progress.Value = 0;
                SetStatus("✗  " + ex.Message, "WxlDangerBrush");
            }
            finally
            {
                BtnRun.IsEnabled = true;
            }
        }

        private void SetStatus(string text, string brushKey)
        {
            LblStatus.Text = text;
            LblStatus.Foreground = (Brush)Application.Current.Resources[brushKey];
        }
    }
}
