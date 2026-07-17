using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WXL_Installer.Views
{
    public partial class Db2Page : Page
    {
        private readonly WizardState _state = WizardState.Current;

        // Latest ModelFilePath.db2 / TextureFilePath.db2 come from DB2Gen releases.
        private const string Db2GenOwner = "WarcraftXL";
        private const string Db2GenRepo = "DB2Gen";
        // Only assets whose names end in ".db2" are considered required DB2 files.
        private static readonly string[] RequiredAssetExtensions = { ".db2" };

        public Db2Page() { InitializeComponent(); Loaded += async (_, __) => { RefreshEquipSection(); await LoadRequiredFilesPreviewAsync(); }; }

        private const string EquipModuleName = "wxl-equip-module-DB2";
        private const string EquipZipName = "DBCandDB2.zip";
        private string _equipZipPath;

        private void RefreshEquipSection()
        {
            _equipZipPath = null;
            EquipDb2Section.Visibility = Visibility.Collapsed;
            EquipFilesList.ItemsSource = null;
            LblEquipTarget.Text = "";

            var wxl = _state.Settings.WxlPath;
            if (string.IsNullOrEmpty(wxl)) return;
            var moduleDir = Path.Combine(wxl, "scripts", EquipModuleName);
            if (!Directory.Exists(moduleDir)) return;

            string zip = null;
            try
            {
                zip = Directory.EnumerateFiles(moduleDir, EquipZipName, SearchOption.AllDirectories)
                               .FirstOrDefault();
            }
            catch { }
            if (zip == null || !File.Exists(zip)) return;

            _equipZipPath = zip;
            LblEquipZipPath.Text = "Source: " + zip;

            // Resolve where these files WILL be installed so we can show it up-front.
            string targetDir = null;
            try
            {
                var clientPath = _state.Settings.ClientPath;
                if (!string.IsNullOrEmpty(clientPath) && File.Exists(Path.Combine(clientPath, "Wow.exe")))
                    targetDir = ResolvePatchDbFilesClient(clientPath);
            }
            catch { }

            string patchDisplay = targetDir != null
                ? Path.GetFileName(Path.GetDirectoryName(targetDir)) + "\\DBFilesClient"
                : "<set a valid WoW client in Welcome>";
            LblEquipTarget.Text = "These files will be pushed to: " + patchDisplay;

            try
            {
                using var arch = ZipFile.OpenRead(zip);
                var entries = arch.Entries
                                  .Where(en => en.Length > 0 && !string.IsNullOrEmpty(en.Name))
                                  .Where(en =>
                                  {
                                      var ext = Path.GetExtension(en.FullName);
                                      return string.Equals(ext, ".db2", StringComparison.OrdinalIgnoreCase)
                                          || string.Equals(ext, ".dbc", StringComparison.OrdinalIgnoreCase);
                                  })
                                  .OrderBy(en => en.FullName)
                                  .Select(en => new EquipFileRow
                                  {
                                      FileName = en.FullName,
                                      TargetDisplay = "→ " + patchDisplay
                                  })
                                  .ToList();
                EquipFilesList.ItemsSource = entries;
            }
            catch { }

            EquipDb2Section.Visibility = Visibility.Visible;
        }

        private sealed class EquipFileRow
        {
            public string FileName { get; set; }
            public string TargetDisplay { get; set; }
        }

        private async Task LoadRequiredFilesPreviewAsync()
        {
            // Resolve target patch (best effort).
            string targetDir = null;
            try
            {
                var clientPath = _state.Settings.ClientPath;
                if (!string.IsNullOrEmpty(clientPath) && File.Exists(Path.Combine(clientPath, "Wow.exe")))
                    targetDir = ResolvePatchDbFilesClient(clientPath);
            }
            catch { }

            string patchDisplay = targetDir != null
                ? Path.GetFileName(Path.GetDirectoryName(targetDir)) + "\\DBFilesClient"
                : "<set a valid WoW client in Welcome>";
            LblRequiredTarget.Text = "These files will be pushed to: " + patchDisplay;

            LblRequiredListStatus.Text = "Loading file list…";
            RequiredFilesList.ItemsSource = null;

            try
            {
                var release = await Task.Run(() => GitHubHelper.GetLatestRelease(Db2GenOwner, Db2GenRepo));
                var assets = (release?.Assets ?? new List<GitHubReleaseAsset>())
                    .Where(a => a != null && !string.IsNullOrEmpty(a.Name) &&
                                RequiredAssetExtensions.Any(ext =>
                                    a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(a => a.Name)
                    .ToList();

                var rows = assets.Select(a => new EquipFileRow
                {
                    FileName = a.Name,
                    TargetDisplay = "→ " + patchDisplay
                }).ToList();

                RequiredFilesList.ItemsSource = rows;
                LblRequiredListStatus.Text = rows.Count == 0
                    ? "No .db2 assets found in the latest DB2Gen release."
                    : $"{rows.Count} DB2 file(s) from DB2Gen release {release?.TagName ?? "(latest)"}";
            }
            catch (Exception ex)
            {
                LblRequiredListStatus.Text = "Could not fetch file list: " + ex.Message;
            }
        }

        private void BtnOpenEquipZip_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_equipZipPath) || !File.Exists(_equipZipPath)) return;
            try
            {
                System.Diagnostics.Process.Start("explorer.exe",
                    "/select,\"" + _equipZipPath + "\"");
            }
            catch { }
        }

        private void BtnInstallEquipZip_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_equipZipPath) || !File.Exists(_equipZipPath))
            {
                SetStatus("✗  Equip DBCandDB2.zip not found.", "WxlDangerBrush");
                return;
            }
            var clientPath = _state.Settings.ClientPath;
            if (string.IsNullOrEmpty(clientPath) || !File.Exists(Path.Combine(clientPath, "Wow.exe")))
            {
                SetStatus("✗  WoW client path is not valid (Welcome step).", "WxlDangerBrush");
                return;
            }

            try
            {
                var db2Path = ResolvePatchDbFilesClient(clientPath);
                Directory.CreateDirectory(db2Path);

                int count = 0;
                using (var arch = ZipFile.OpenRead(_equipZipPath))
                {
                    foreach (var en in arch.Entries)
                    {
                        if (en.Length <= 0) continue;
                        var ext = Path.GetExtension(en.FullName);
                        if (!string.Equals(ext, ".db2", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(ext, ".dbc", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var name = Path.GetFileName(en.FullName);
                        if (string.IsNullOrEmpty(name)) continue;
                        en.ExtractToFile(Path.Combine(db2Path, name), true);
                        count++;
                    }
                }
                SetStatus($"✓  Installed {count} equip DBC/DB2 file(s) in {db2Path}", "WxlSuccessBrush");
            }
            catch (Exception ex)
            {
                SetStatus("✗  " + ex.Message, "WxlDangerBrush");
            }
        }

        private string ResolvePatchDbFilesClient(string clientPath)
        {
            var dataDir = Path.Combine(clientPath, "Data");
            Directory.CreateDirectory(dataDir);
            foreach (var dir in Directory.GetDirectories(dataDir, "Patch-*.mpq")
                                         .Where(d => Regex.IsMatch(Path.GetFileName(d),
                                                   @"^Patch-[A-Z]\.mpq$", RegexOptions.IgnoreCase))
                                         .OrderByDescending(d => d))
            {
                if (Directory.Exists(Path.Combine(dir, "DBFilesClient")))
                    return Path.Combine(dir, "DBFilesClient");
            }
            for (char c = 'Z'; c >= 'A'; c--)
            {
                var cand = Path.Combine(dataDir, "Patch-" + c + ".mpq");
                if (!Directory.Exists(cand)) return Path.Combine(cand, "DBFilesClient");
            }
            throw new Exception("No free Patch-A..Z.mpq folder available in " + dataDir);
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var clientPath = _state.Settings.ClientPath;
            if (string.IsNullOrEmpty(clientPath) || !File.Exists(Path.Combine(clientPath, "Wow.exe")))
            {
                SetStatus("✗  WoW client path is not valid (Welcome step).", "WxlDangerBrush");
                return;
            }

            BtnInstall.IsEnabled = false;
            Progress.Value = 0;
            Progress.IsIndeterminate = true;
            SetStatus("Fetching latest DB2Gen release…", "WxlTextMutedBrush");

            try
            {
                var release = await Task.Run(() => GitHubHelper.GetLatestRelease(Db2GenOwner, Db2GenRepo));
                var assets = (release?.Assets ?? new List<GitHubReleaseAsset>())
                    .Where(a => a != null && !string.IsNullOrEmpty(a.BrowserDownloadUrl) &&
                                RequiredAssetExtensions.Any(ext =>
                                    a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (assets.Count == 0)
                    throw new Exception("Latest DB2Gen release has no .db2 assets.");

                // Resolve target patch first so any error surfaces before downloading.
                var db2Path = ResolvePatchDbFilesClient(clientPath);
                Directory.CreateDirectory(db2Path);

                int fileCount = 0;
                for (int i = 0; i < assets.Count; i++)
                {
                    var asset = assets[i];
                    int idx = i;
                    SetStatus($"Downloading {asset.Name} ({idx + 1}/{assets.Count})…", "WxlTextMutedBrush");
                    Progress.IsIndeterminate = true;

                    var tempFile = Path.Combine(Path.GetTempPath(),
                                                "wxl-db2-" + Guid.NewGuid().ToString("N") + "-" + asset.Name);
                    var prog = new Progress<DownloadProgress>(t =>
                    {
                        if (t.Total > 0)
                        {
                            Progress.IsIndeterminate = false;
                            double moduleFraction = (double)t.Downloaded / t.Total;
                            double overall = ((idx + moduleFraction) / assets.Count) * 80.0;
                            Progress.Value = overall;
                        }
                    });

                    try
                    {
                        await GitHubHelper.DownloadFileAsync(asset.BrowserDownloadUrl, tempFile, prog);
                        File.Copy(tempFile, Path.Combine(db2Path, asset.Name), true);
                        fileCount++;
                    }
                    finally
                    {
                        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                    }

                    Progress.IsIndeterminate = false;
                    Progress.Value = ((idx + 1.0) / assets.Count) * 80.0;
                }

                Progress.Value = 100;
                SetStatus($"✓  Installed {fileCount} DB2 file(s) from DB2Gen {release?.TagName} in {db2Path}",
                          "WxlSuccessBrush");
                _state.Db2Installed = true;

                // If the equip module ships a DBCandDB2.zip, install those into the same patch.
                if (!string.IsNullOrEmpty(_equipZipPath) && File.Exists(_equipZipPath))
                {
                    SetStatus("Installing equip module DBC/DB2 files…", "WxlTextMutedBrush");
                    int equipCount = 0;
                    using (var arch = ZipFile.OpenRead(_equipZipPath))
                    {
                        foreach (var en in arch.Entries)
                        {
                            if (en.Length <= 0) continue;
                            var ext = Path.GetExtension(en.FullName);
                            if (!string.Equals(ext, ".db2", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(ext, ".dbc", StringComparison.OrdinalIgnoreCase))
                                continue;
                            var name = Path.GetFileName(en.FullName);
                            if (string.IsNullOrEmpty(name)) continue;
                            en.ExtractToFile(Path.Combine(db2Path, name), true);
                            equipCount++;
                        }
                    }
                    SetStatus($"✓  Installed {fileCount} DB2 file(s) + {equipCount} equip DBC/DB2 file(s) in {db2Path}",
                              "WxlSuccessBrush");
                }

                RefreshEquipSection();
            }
            catch (Exception ex)
            {
                Progress.Value = 0;
                Progress.IsIndeterminate = false;
                SetStatus("✗  " + ex.Message, "WxlDangerBrush");
            }
            finally
            {
                BtnInstall.IsEnabled = true;
            }
        }

        private void SetStatus(string text, string brushKey)
        {
            LblStatus.Text = text;
            LblStatus.Foreground = (Brush)Application.Current.Resources[brushKey];
        }
    }
}
