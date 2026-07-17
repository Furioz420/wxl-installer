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
        private const string Db2PackageUrl =
            "https://raw.githubusercontent.com/Furioz420/wxl-installer/master/DBFilesClient.zip";

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
                var tempZip = Path.Combine(Path.GetTempPath(),
                    "wxl-DBFilesClient-preview-" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    await GitHubHelper.DownloadFileAsync(Db2PackageUrl, tempZip, null);
                    var rows = new List<EquipFileRow>();
                    using (var arch = ZipFile.OpenRead(tempZip))
                    {
                        foreach (var en in arch.Entries
                                               .Where(en => en.Length > 0)
                                               .OrderBy(en => en.FullName))
                        {
                            if (!string.Equals(Path.GetExtension(en.FullName), ".db2",
                                               StringComparison.OrdinalIgnoreCase)) continue;
                            var name = Path.GetFileName(en.FullName);
                            if (string.IsNullOrEmpty(name)) continue;
                            rows.Add(new EquipFileRow
                            {
                                FileName = name,
                                TargetDisplay = "→ " + patchDisplay
                            });
                        }
                    }
                    RequiredFilesList.ItemsSource = rows;
                    LblRequiredListStatus.Text = $"{rows.Count} DB2 file(s) in DBFilesClient.zip";
                }
                finally
                {
                    try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                }
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
            SetStatus("Downloading DBFilesClient.zip…", "WxlTextMutedBrush");

            try
            {
                var tempZip = Path.Combine(Path.GetTempPath(),
                                           "wxl-DBFilesClient-" + Guid.NewGuid().ToString("N") + ".zip");
                var prog = new Progress<DownloadProgress>(t =>
                {
                    if (t.Total > 0)
                    {
                        Progress.IsIndeterminate = false;
                        Progress.Value = 60.0 * t.Downloaded / t.Total;
                    }
                });
                await GitHubHelper.DownloadFileAsync(Db2PackageUrl, tempZip, prog);
                Progress.IsIndeterminate = false;
                Progress.Value = 60;

                SetStatus("Extracting DB2 files…", "WxlTextMutedBrush");
                var tempExtract = Path.Combine(Path.GetTempPath(),
                                               "wxl-db2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempExtract);
                int fileCount = 0;
                await Task.Run(() =>
                {
                    using var archive = ZipFile.OpenRead(tempZip);
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Length <= 0) continue;
                        if (!string.Equals(Path.GetExtension(entry.FullName), ".db2",
                                           StringComparison.OrdinalIgnoreCase)) continue;
                        var name = Path.GetFileName(entry.FullName);
                        if (string.IsNullOrEmpty(name)) continue;
                        entry.ExtractToFile(Path.Combine(tempExtract, name), true);
                        fileCount++;
                    }
                });
                Progress.Value = 75;
                if (fileCount == 0) throw new Exception("DBFilesClient.zip contained no DB2 files.");

                var dataDir = Path.Combine(clientPath, "Data");
                Directory.CreateDirectory(dataDir);

                string targetPatch = null;
                foreach (var dir in Directory.GetDirectories(dataDir, "Patch-*.mpq")
                                             .Where(d => Regex.IsMatch(Path.GetFileName(d),
                                                       @"^Patch-[A-Z]\.mpq$", RegexOptions.IgnoreCase))
                                             .OrderByDescending(d => d))
                {
                    if (Directory.Exists(Path.Combine(dir, "DBFilesClient")))
                    {
                        targetPatch = dir;
                        break;
                    }
                }
                if (targetPatch == null)
                {
                    for (char c = 'Z'; c >= 'A'; c--)
                    {
                        var cand = Path.Combine(dataDir, "Patch-" + c + ".mpq");
                        if (!Directory.Exists(cand)) { targetPatch = cand; break; }
                    }
                    if (targetPatch == null)
                        throw new Exception("No free Patch-A..Z.mpq folder available in " + dataDir);
                }

                SetStatus("Installing DB2 files into " + Path.GetFileName(targetPatch) + "…", "WxlTextMutedBrush");
                var db2Path = Path.Combine(targetPatch, "DBFilesClient");
                Directory.CreateDirectory(db2Path);
                foreach (var f in Directory.GetFiles(tempExtract))
                    File.Copy(f, Path.Combine(db2Path, Path.GetFileName(f)), true);

                try { File.Delete(tempZip); } catch { }
                try { Directory.Delete(tempExtract, true); } catch { }

                Progress.Value = 100;
                SetStatus($"✓  Installed {fileCount} DB2 file(s) in {db2Path}", "WxlSuccessBrush");
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
