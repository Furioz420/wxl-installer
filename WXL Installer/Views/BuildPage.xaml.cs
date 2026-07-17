using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WXL_Installer.Views
{
    public partial class BuildPage : Page
    {
        private readonly WizardState _state = WizardState.Current;

        public BuildPage() { InitializeComponent(); }

        private async void BtnBuild_Click(object sender, RoutedEventArgs e)
        {
            var wxlPath = _state.Settings.WxlPath;
            var clientPath = _state.Settings.ClientPath;
            var buildScript = Path.Combine(wxlPath ?? "", "build.ps1");

            if (string.IsNullOrEmpty(wxlPath) || !File.Exists(buildScript))
            {
                SetStatus($"✗  build.ps1 not found in WXL folder ({wxlPath}).", "WxlDangerBrush");
                return;
            }
            if (string.IsNullOrEmpty(clientPath) || !File.Exists(Path.Combine(clientPath, "Wow.exe")))
            {
                SetStatus("✗  WoW client path is not valid (Welcome step).", "WxlDangerBrush");
                return;
            }

            BtnBuild.IsEnabled = false;
            TxtLog.Clear();
            Progress.Value = 3;
            SetStatus("Preparing build…", "WxlTextMutedBrush");

            var args = new StringBuilder();
            args.Append("-NoProfile -ExecutionPolicy Bypass -File \"").Append(buildScript).Append("\"");
            args.Append(" -Config Release");
            args.Append(" -ClientPath \"").Append(clientPath).Append("\"");
            args.Append(" -BuildHost");
            if (ChkClean.IsChecked == true) args.Append(" -Clean");
            if (ChkAutoPatch.IsChecked == true) args.Append(" -AutoPatch");

            var psi = new ProcessStartInfo("powershell.exe", args.ToString())
            {
                WorkingDirectory = wxlPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            // Stop MSBuild from keeping its worker nodes alive after the build,
            // which causes lingering msbuild.exe processes.
            psi.EnvironmentVariables["MSBUILDDISABLENODEREUSE"] = "1";

            try
            {
                int exit = await Task.Run(() =>
                {
                    using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    proc.OutputDataReceived += (_, ev) => OnLine(ev.Data, false);
                    proc.ErrorDataReceived  += (_, ev) => OnLine(ev.Data, true);
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit();
                    return proc.ExitCode;
                });

                if (exit != 0) throw new Exception("Build failed with exit code " + exit + ".");

                Progress.Value = 100;
                SetStatus("✓  Build, deployment"
                          + (ChkAutoPatch.IsChecked == true ? ", and patch" : "")
                          + " completed.", "WxlSuccessBrush");
                _state.BuildSucceeded = true;
            }
            catch (Exception ex)
            {
                SetStatus("✗  " + ex.Message, "WxlDangerBrush");
            }
            finally
            {
                BtnBuild.IsEnabled = true;
            }
        }

        private void OnLine(string line, bool isError)
        {
            if (line == null) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnLine(line, isError)));
                return;
            }

            TxtLog.AppendText(line + Environment.NewLine);
            // Move the caret to the end — this is the reliable way to auto-scroll
            // a TextBox; ScrollToEnd() can fire before layout is updated.
            TxtLog.CaretIndex = TxtLog.Text.Length;

            int pct = -1; string msg = null;
            if (line.StartsWith("=== WarcraftXL.dll",     StringComparison.OrdinalIgnoreCase)) { pct = 10; msg = "Building DLL, proxy, and patcher…"; }
            else if (line.StartsWith("=== WarcraftXLHost.exe", StringComparison.OrdinalIgnoreCase)) { pct = 60; msg = "Building and deploying the asset host…"; }
            else if (line.StartsWith("=== AutoPatch",         StringComparison.OrdinalIgnoreCase)) { pct = 88; msg = "Patching Wow.exe…"; }
            else if (line.StartsWith("OK - build",             StringComparison.OrdinalIgnoreCase)) { pct = 100; msg = "Build, deployment, and patch completed."; }

            if (pct >= 0)
            {
                if (pct > Progress.Value) Progress.Value = pct;
                SetStatus(msg, "WxlTextMutedBrush");
            }
        }

        private void TxtLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            // ScrollToEnd is already handled in OnLine at Background priority.
            // This handler is kept to satisfy the XAML attribute.
        }

        private void SetStatus(string text, string brushKey)
        {
            LblStatus.Text = text;
            LblStatus.Foreground = (Brush)Application.Current.Resources[brushKey];
        }
    }
}
