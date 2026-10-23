using System;
using System.IO;
using System.Web.Script.Serialization;

namespace WinGroups
{
    // User preferences, stored as JSON in %LOCALAPPDATA%\WinGroups\settings.json.
    public class Settings
    {
        public bool CloseAfterLaunch { get; set; }
        public bool ShowNames { get; set; }
        public string Theme { get; set; }      // windows | light | dark
        public string IconStyle { get; set; }  // grid | folder
        public string IconTheme { get; set; }  // Windows mode the desktop icons were last drawn for

        public Settings()
        {
            CloseAfterLaunch = true;
            ShowNames = true;
            Theme = "windows";
            IconStyle = "grid";
        }

        public static string DataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGroups"); }
        }

        static string FilePath { get { return Path.Combine(DataDir, "settings.json"); } }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath));
                    if (s != null)
                    {
                        if (s.Theme != "light" && s.Theme != "dark") s.Theme = "windows";
                        if (s.IconStyle != "folder") s.IconStyle = "grid";
                        return s;
                    }
                }
            }
            catch { }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(this));
            }
            catch { }
        }
    }
}
