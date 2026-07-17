using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace WXL_Installer
{
    public partial class MainWindow : FluentWindow
    {
        // Windows 11 DWM: paint the window border with a neutral color instead of
        // the system accent (which we've retinted gold for the app's controls).
        private const int DWMWA_BORDER_COLOR = 34;
        // 0x00BBGGRR — dark grey border that blends with our dark theme.
        private const uint BorderColor = 0x002A2A2A;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint value, int size);

        // Set while we handle the prompt/install flow to avoid re-entrancy.
        private bool _navBypass;

        public MainWindow()
        {
            InitializeComponent();
            Title = "WarcraftXL Installer  v" + Updater.CurrentVersion;
            Loaded += async (_, __) =>
            {
                Nav.Navigate(typeof(Views.WelcomePage));
                // Fire and forget — never block startup on the network check.
                await Updater.CheckAsync();
            };
            SourceInitialized += (_, __) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    uint color = BorderColor;
                    DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref color, sizeof(uint));
                }
            };
        }

        private async void Nav_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            if (_navBypass) return;

            // Only intercept when leaving the modules page with pending selection changes.
            var current = FindCurrentPage();
            if (current is Views.CoreModulesPage modules && modules.HasPendingChanges)
            {
                e.Cancel = true;

                var result = MessageBox.Show(
                    "You have selected modules that haven't been installed yet.\n\n" +
                    "Do you want to install them before leaving this page?",
                    "Install selected modules?",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel) return;

                if (result == MessageBoxResult.Yes)
                {
                    bool ok = await modules.InstallSelectedModulesAsync();
                    if (!ok) return; // stay on modules page on failure
                }

                // Re-issue the navigation the user requested.
                _navBypass = true;
                try
                {
                    if (e.Page != null) Nav.Navigate(e.Page.GetType());
                }
                finally { _navBypass = false; }
            }
        }

        private object FindCurrentPage()
        {
            // Wpf.Ui NavigationView hosts pages inside an internal Frame; find it.
            var frame = FindVisualChild<System.Windows.Controls.Frame>(Nav);
            return frame?.Content;
        }

        private static T FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            if (parent == null) return null;
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var deeper = FindVisualChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
