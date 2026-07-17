using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WXL_Installer.Views
{
    public partial class CoreModulesPage : Page
    {
        private readonly WizardState _state = WizardState.Current;
        private const string MandatoryModule = "wxl-host-extension";
        private const string EquipModule = "wxl-equip-module-DB2";
        private const string RetailDb2Module = "wxl-retail-db2";

        // Repos to inject into the catalog even if they don't carry a wxl-* topic tag.
        // Each entry is (owner, repo).
        private static readonly (string Owner, string Repo)[] ExtraCatalogEntries =
        {
            ("Furioz420",    "wxl-retail-db2"),
            ("Furioz420",    "wxl-equip-module-DB2"),
            ("Furioz420",    "wxl-client-extensions"),
            ("WarcraftXL",   "wxl-unit-outline"),
        };

        private List<GitHubRepo> _catalog;
        private readonly Dictionary<string, CheckBox> _checks =
            new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);

        // Tracks per-checkbox baseline state (what is currently installed / applied).
        // Used to detect pending changes when the user tries to navigate away.
        private readonly Dictionary<string, bool> _baseline =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public bool HasPendingChanges
        {
            get
            {
                foreach (var kv in _checks)
                {
                    bool cur = kv.Value.IsChecked == true;
                    _baseline.TryGetValue(kv.Key, out bool baseVal);
                    if (cur != baseVal) return true;
                }
                return false;
            }
        }

        public CoreModulesPage()
        {
            InitializeComponent();
            Loaded += async (_, __) => await InitAsync();
        }

        // PreviewMouseWheel on the StackPanel tunnels DOWN before any child sees it,
        // so checked/unchecked CheckBoxes cannot swallow it. We scroll the parent
        // ScrollViewer directly and mark Handled so the NavigationView stays still.
        private void ModulesList_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            ModulesScroll.ScrollToVerticalOffset(ModulesScroll.VerticalOffset + (e.Delta > 0 ? -60 : 60));
            e.Handled = true;
        }

        private async Task InitAsync()
        {
            var wxl = _state.Settings.WxlPath;
            bool installed = !string.IsNullOrEmpty(wxl)
                             && File.Exists(Path.Combine(wxl, "CMakeLists.txt"));

            GitHubRepo repo = null;
            try { repo = await Task.Run(() => GitHubHelper.GetRepo("WarcraftXL", "wxl-core")); }
            catch { /* offline ok */ }

            if (installed)
            {
                SetCoreStatus($"✓  WXL core is installed at {wxl}"
                              + (repo != null ? $" (remote updated {repo.PushedAt})" : ""), "WxlSuccessBrush");
                BtnInstallCore.Content = "Update core & install selected modules";
                _state.CoreInstalled = true;
            }
            else
            {
                SetCoreStatus("✗  WXL core is not installed. Click Install to fetch it.", "WxlWarnBrush");
                BtnInstallCore.Content = "Install core & selected modules";
                _state.CoreInstalled = false;
            }
            BtnInstallCore.IsEnabled = !string.IsNullOrEmpty(wxl);

            await LoadCatalogAsync();
        }

        private async void BtnInstallCore_Click(object sender, RoutedEventArgs e)
        {
            var wxl = _state.Settings.WxlPath;
            if (string.IsNullOrEmpty(wxl))
            {
                SetCoreStatus("WXL install path is not set (Welcome step).", "WxlDangerBrush");
                return;
            }

            if (Directory.Exists(wxl) &&
                (Directory.EnumerateFileSystemEntries(wxl).Any()))
            {
                var confirm = MessageBox.Show(
                    $"The folder\n\n    {wxl}\n\nis not empty. Installing the core will DELETE its current contents and replace them with a fresh copy of wxl-core from GitHub.\n\nContinue?",
                    "Overwrite existing core?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    SetCoreStatus("Install cancelled.", "WxlTextMutedBrush");
                    return;
                }

                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(wxl))
                        Directory.Delete(dir, true);
                    foreach (var file in Directory.EnumerateFiles(wxl))
                        File.Delete(file);
                }
                catch (Exception ex)
                {
                    SetCoreStatus("Could not clear folder: " + ex.Message, "WxlDangerBrush");
                    return;
                }
            }

            BtnInstallCore.IsEnabled = false;
            PbCore.Value = 0;
            PbCore.IsIndeterminate = true;
            SetCoreStatus("Downloading WXL core…", "WxlTextMutedBrush");

            try
            {
                var prog = new Progress<DownloadProgress>(t =>
                {
                    if (t.Total > 0)
                    {
                        PbCore.IsIndeterminate = false;
                        PbCore.Value = 90.0 * t.Downloaded / t.Total;
                    }
                });
                await GitHubHelper.DownloadAndExtractRepoAsync("WarcraftXL", "wxl-core", wxl, prog, CancellationToken.None);
                PbCore.IsIndeterminate = false;
                PbCore.Value = 100;
                SetCoreStatus("✓  WXL core installed at " + wxl, "WxlSuccessBrush");
                BtnInstallCore.Content = "Update core & install selected modules";
                _state.CoreInstalled = true;

                // Immediately install selected modules as part of the same action.
                await InstallSelectedModulesAsync();
            }
            catch (Exception ex)
            {
                PbCore.Value = 0;
                PbCore.IsIndeterminate = false;
                SetCoreStatus("✗  " + ex.Message, "WxlDangerBrush");
            }
            finally
            {
                BtnInstallCore.IsEnabled = true;
            }
        }

        private async Task LoadCatalogAsync()
        {
            ModulesList.Children.Clear();
            var loading = new TextBlock
            {
                Text = "Loading module catalog from GitHub…",
                Foreground = (Brush)Application.Current.Resources["WxlTextMutedBrush"],
                Margin = new Thickness(4)
            };
            ModulesList.Children.Add(loading);

            List<GitHubRepo> repos;
            try
            {
                repos = await Task.Run(() =>
                {
                    var topics = new[] { "wxl-modules", "wxl-scripts", "warcraftxl-modules", "warcraftxl-scripts" };
                    var deny = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "warcraftxl.github.io", ".github", "wxl-modern-m2" };

                    var byName = new Dictionary<string, GitHubRepo>(StringComparer.OrdinalIgnoreCase);
                    foreach (var t in topics)
                    {
                        try
                        {
                            foreach (var r in GitHubHelper.SearchByTopic(t))
                            {
                                if (r == null || string.IsNullOrEmpty(r.Name)) continue;
                                if (deny.Contains(r.Name)) continue;
                                byName[r.FullName ?? r.Name] = r;
                            }
                        }
                        catch { /* per-topic failure is fine */ }
                    }

                    // Manually pin extra catalog repos (not tagged with wxl-* topics).
                    foreach (var (owner, repo) in ExtraCatalogEntries)
                    {
                        try
                        {
                            var r = GitHubHelper.GetRepo(owner, repo);
                            if (r != null && !string.IsNullOrEmpty(r.Name) && !deny.Contains(r.Name))
                                byName[r.FullName ?? r.Name] = r;
                        }
                        catch { /* offline / rate-limited — ignore */ }
                    }

                    return byName.Values.OrderBy(r => r.Name).ToList();
                });
            }
            catch (Exception ex)
            {
                loading.Text = "Failed to load catalog: " + ex.Message;
                loading.Foreground = (Brush)Application.Current.Resources["WxlDangerBrush"];
                return;
            }

            _catalog = repos;
            ModulesList.Children.Clear();
            _checks.Clear();
            _baseline.Clear();

            foreach (var r in repos)
            {
                bool isMandatory = string.Equals(r.Name, MandatoryModule, StringComparison.OrdinalIgnoreCase);
                var scriptsDir = Path.Combine(_state.Settings.WxlPath ?? "", "scripts", r.Name);
                bool installed = Directory.Exists(scriptsDir);

                var chk = new CheckBox
                {
                    Tag = r.Name,
                    Content = r.Name + (isMandatory ? "   (required)" : "") + (installed ? "   [installed]" : ""),
                    Foreground = (Brush)Application.Current.Resources["WxlTextBrush"],
                    IsChecked = isMandatory || installed,
                    IsEnabled = !isMandatory,
                    Margin = new Thickness(0, 4, 0, 0),
                    FontWeight = isMandatory ? FontWeights.SemiBold : FontWeights.Normal
                };
                if (string.Equals(r.Name, EquipModule, StringComparison.OrdinalIgnoreCase))
                {
                    chk.Checked   += EquipModule_CheckedChanged;
                    chk.Unchecked += EquipModule_CheckedChanged;
                }
                _checks[r.Name] = chk;
                _baseline[r.Name] = chk.IsChecked == true;
                ModulesList.Children.Add(chk);

                if (!string.IsNullOrWhiteSpace(r.Description))
                {
                    ModulesList.Children.Add(new TextBlock
                    {
                        Text = r.Description,
                        Foreground = (Brush)Application.Current.Resources["WxlTextMutedBrush"],
                        FontSize = 11,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(24, 0, 0, 4)
                    });
                }
            }
        }

        /// <summary>
        /// Installs currently-selected modules. Returns true on success (or nothing to do),
        /// false on failure. Callers can await this before navigating away.
        /// </summary>
        public async Task<bool> InstallSelectedModulesAsync()
        {
            var wxl = _state.Settings.WxlPath;
            if (!_state.CoreInstalled || string.IsNullOrEmpty(wxl))
            {
                MessageBox.Show("Install WXL core first.", "WarcraftXL Installer",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var scriptsRoot = Path.Combine(wxl, "scripts");
            Directory.CreateDirectory(scriptsRoot);

            var selected = _checks.Where(k => k.Value.IsChecked == true).Select(k => k.Key).ToList();
            if (!selected.Contains(MandatoryModule, StringComparer.OrdinalIgnoreCase))
                selected.Add(MandatoryModule);

            BtnInstallCore.IsEnabled = false;

            try
            {
                int total = selected.Count;
                for (int i = 0; i < total; i++)
                {
                    var name = selected[i];
                    int idx = i;
                    SetCoreStatus($"Installing module {idx + 1}/{total}: {name}…", "WxlTextMutedBrush");

                    // GitHub zipball responses often lack Content-Length (chunked),
                    // so we can't get a real byte-level percentage. Fall back to
                    // per-module segments + indeterminate spinner during download.
                    PbCore.IsIndeterminate = true;

                    var prog = new Progress<DownloadProgress>(t =>
                    {
                        if (t.Total > 0)
                        {
                            PbCore.IsIndeterminate = false;
                            double moduleFraction = (double)t.Downloaded / t.Total;
                            double overall = ((idx + moduleFraction) / total) * 100.0;
                            PbCore.Value = overall;
                        }
                    });

                    var repo = _catalog?.FirstOrDefault(r =>
                        string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
                    string owner = "WarcraftXL";
                    if (repo != null && !string.IsNullOrEmpty(repo.FullName) && repo.FullName.Contains("/"))
                        owner = repo.FullName.Split('/')[0];

                    var moduleDir = Path.Combine(scriptsRoot, name);
                    try
                    {
                        await GitHubHelper.DownloadAndExtractRepoAsync(owner, name, moduleDir, prog, CancellationToken.None);
                    }
                    catch (Exception exModule)
                    {
                        throw new Exception($"Failed to install '{owner}/{name}': {exModule.Message}", exModule);
                    }

                    PbCore.IsIndeterminate = false;
                    PbCore.Value = ((idx + 1.0) / total) * 100.0;
                }

                PbCore.IsIndeterminate = false;
                PbCore.Value = 100;
                SetCoreStatus($"✓  Installed {selected.Count} module(s).", "WxlSuccessBrush");

                // Refresh baseline so newly-installed selections no longer count as "pending".
                foreach (var kv in _checks)
                    _baseline[kv.Key] = kv.Value.IsChecked == true;

                return true;
            }
            catch (Exception ex)
            {
                SetCoreStatus("✗  " + ex.Message, "WxlDangerBrush");
                return false;
            }
            finally
            {
                PbCore.IsIndeterminate = false;
                BtnInstallCore.IsEnabled = true;
            }
        }

        private void ModulesScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Handled by ModulesList_PreviewMouseWheel.
        }

        private void Page_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            // Handled by ModulesList_PreviewMouseWheel.
        }


        private void EquipModule_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_checks.TryGetValue(EquipModule, out var equipChk)) return;
            if (!_checks.TryGetValue(RetailDb2Module, out var retailChk)) return;

            bool equipOn = equipChk.IsChecked == true;
            if (equipOn)
            {
                retailChk.IsChecked = true;
                retailChk.IsEnabled = false;
                retailChk.ToolTip = "Required by " + EquipModule;
            }
            else
            {
                // Only re-enable if not mandatory itself.
                if (!string.Equals(RetailDb2Module, MandatoryModule, StringComparison.OrdinalIgnoreCase))
                    retailChk.IsEnabled = true;
                retailChk.ToolTip = null;
            }
        }

        private void SetCoreStatus(string text, string brushKey)
        {
            LblCoreStatus.Text = text;
            LblCoreStatus.Foreground = (Brush)Application.Current.Resources[brushKey];
        }
    }
}
