using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace WXL_Installer
{
    [DataContract]
    public class InstallerSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WxlInstaller", "settings.json");

        [DataMember(Name = "wxlPath")]
        public string WxlPath { get; set; }

        [DataMember(Name = "clientPath")]
        public string ClientPath { get; set; }

        [DataMember(Name = "assetsPath")]
        public string AssetsPath { get; set; }

        [DataMember(Name = "db2StagingPath")]
        public string Db2StagingPath { get; set; }

        public InstallerSettings()
        {
            WxlPath = string.Empty;
            ClientPath = string.Empty;
            AssetsPath = string.Empty;
            Db2StagingPath = string.Empty;
        }

        public static InstallerSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return new InstallerSettings();

                var bytes = File.ReadAllBytes(SettingsPath);
                using (var ms = new MemoryStream(bytes))
                {
                    var ser = new DataContractJsonSerializer(typeof(InstallerSettings));
                    return (InstallerSettings)ser.ReadObject(ms) ?? new InstallerSettings();
                }
            }
            catch
            {
                return new InstallerSettings();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                using (var ms = new MemoryStream())
                {
                    var ser = new DataContractJsonSerializer(typeof(InstallerSettings));
                    ser.WriteObject(ms, this);
                    File.WriteAllBytes(SettingsPath, ms.ToArray());
                }
            }
            catch { /* non-fatal */ }
        }
    }
}
