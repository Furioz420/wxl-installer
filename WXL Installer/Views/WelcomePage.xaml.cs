using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WXL_Installer.Views
{
    public partial class WelcomePage : Page
    {
        private readonly WizardState _state = WizardState.Current;
        private bool _loading;

        public WelcomePage()
        {
            InitializeComponent();
            _loading = true;
            TxtWxlPath.Text    = _state.Settings.WxlPath ?? "";
            TxtClientPath.Text = _state.Settings.ClientPath ?? "";
            _loading = false;
            UpdateClientStatus();
            LblVersion.Text = "Installed version: v" + Updater.CurrentVersion;
            Loaded += async (_, __) => await LoadLogoAsync();
        }

        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            try { await Updater.CheckAsync(showUpToDate: true); }
            finally { BtnCheckUpdate.IsEnabled = true; }
        }

        private void Path_Changed(object sender, TextChangedEventArgs e)
        {
            if (_loading) return;
            _state.Settings.WxlPath    = TxtWxlPath.Text?.Trim();
            _state.Settings.ClientPath = TxtClientPath.Text?.Trim();
            _state.Settings.Save();
            _state.RaisePathsChanged();
            UpdateClientStatus();
        }

        private void UpdateClientStatus()
        {
            var p = TxtClientPath.Text?.Trim() ?? "";
            if (p.Length == 0) { LblClientStatus.Text = ""; return; }
            if (File.Exists(Path.Combine(p, "Wow.exe")))
            {
                LblClientStatus.Text = "✓  Wow.exe found";
                LblClientStatus.Foreground = (Brush)Application.Current.Resources["WxlSuccessBrush"];
            }
            else
            {
                LblClientStatus.Text = "✗  Wow.exe not found in this folder";
                LblClientStatus.Foreground = (Brush)Application.Current.Resources["WxlDangerBrush"];
            }
        }

        private void BrowseWxl_Click(object sender, RoutedEventArgs e)   => PickFolder(TxtWxlPath);
        private void BrowseClient_Click(object sender, RoutedEventArgs e) => PickFolder(TxtClientPath);

        private static void PickFolder(TextBox target)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder",
                InitialDirectory = string.IsNullOrWhiteSpace(target.Text) ? "" : target.Text
            };
            if (dlg.ShowDialog() == true)
                target.Text = dlg.FolderName;
        }

        private async System.Threading.Tasks.Task LoadLogoAsync()
        {
            try
            {
                const string url = "https://github.com/WarcraftXL/.github/raw/main/profile/assets/logo.png";
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("WarcraftXL-Installer");
                var bytes = await http.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                Logo.Source = img;
            }
            catch { /* logo is optional */ }
        }
    }
}
