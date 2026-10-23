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
        public bool StartWithWindows { get; set; } // shows only the widgets at sign-in
        public string Theme { get; set; }          // windows | light | dark
        public string Accent { get; set; }         // "" = match Windows, or #RRGGBB
        public string IconStyle { get; set; }      // grid | folder
        public int Blur { get; set; }              // glass blur strength, 0 (clear) to 100
        public bool LockWidgets { get; set; }
        public bool QuickSearch { get; set; }      // Ctrl+Alt+Space
        public double WinX { get; set; }           // main window bounds (0 = not saved yet)
        public double WinY { get; set; }
        public double WinW { get; set; }
        public double WinH { get; set; }
        public bool WinMax { get; set; }

        public Settings()
        {
            CloseAfterLaunch = true;
            ShowNames = true;
            StartWithWindows = false;
            Theme = "windows";
            Accent = "";
            IconStyle = "grid";
            Blur = 60;
            LockWidgets = false;
            QuickSearch = true;
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
                    string json = File.ReadAllText(FilePath);
                    var s = new JavaScriptSerializer().Deserialize<Settings>(json);
                    if (s != null)
                    {
                        if (s.Theme != "light" && s.Theme != "dark") s.Theme = "windows";
                        if (s.IconStyle != "folder") s.IconStyle = "grid";
                        if (s.Accent == null) s.Accent = "";
                        // settings saved by older versions don't have these yet
                        if (!json.Contains("\"Blur\"")) s.Blur = 60;
                        if (!json.Contains("\"QuickSearch\"")) s.QuickSearch = true;
                        s.Blur = Math.Max(0, Math.Min(100, s.Blur));
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
