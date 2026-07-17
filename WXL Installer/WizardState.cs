using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WXL_Installer
{
    /// <summary>
    /// Shared, observable state passed between wizard pages so gating between
    /// steps (core installed → modules → DB2 → build) can be enforced by the shell.
    /// </summary>
    public sealed class WizardState : INotifyPropertyChanged
    {
        public static WizardState Current { get; } = new WizardState();

        public InstallerSettings Settings { get; } = InstallerSettings.Load();

        private bool _coreInstalled;
        public bool CoreInstalled
        {
            get => _coreInstalled;
            set { if (_coreInstalled != value) { _coreInstalled = value; OnChanged(); } }
        }

        private bool _db2Installed;
        public bool Db2Installed
        {
            get => _db2Installed;
            set { if (_db2Installed != value) { _db2Installed = value; OnChanged(); } }
        }

        private bool _buildSucceeded;
        public bool BuildSucceeded
        {
            get => _buildSucceeded;
            set { if (_buildSucceeded != value) { _buildSucceeded = value; OnChanged(); } }
        }

        public bool PathsValid
        {
            get
            {
                var s = Settings;
                return !string.IsNullOrWhiteSpace(s.WxlPath)
                    && !string.IsNullOrWhiteSpace(s.ClientPath)
                    && System.IO.File.Exists(System.IO.Path.Combine(s.ClientPath, "Wow.exe"));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnChanged([CallerMemberName] string p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        public void RaisePathsChanged() => OnChanged(nameof(PathsValid));
    }
}
