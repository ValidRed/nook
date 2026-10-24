using System;
using System.Collections.Generic;
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

        // The island at the top of the screen
        public bool IslandOn { get; set; }
        public string IslandStyle { get; set; }      // black | glass
        public int IslandBlur { get; set; }          // glass only: 0 (clear) to 100 (frosted)
        public string IslandShape { get; set; }      // float | notch
        public bool IslandMusic { get; set; }        // album art, visualizer and controls while music plays
        public bool IslandScrollVolume { get; set; } // scroll over the island to change the volume
        public bool IslandCharging { get; set; }     // laptops: shows when the charger is plugged in or out
        public bool IslandHideFullScreen { get; set; }
        public string IslandScreen { get; set; }     // monitor device name; "" = the main screen
        public bool IslandHeadphones { get; set; }   // shows headphones for a moment when they connect
        public bool IslandSystem { get; set; }       // a System page (CPU, memory, GPU, network) when the island is open

        // The big clock in the middle of the desktop
        public bool ClockOn { get; set; }
        public List<string> ClockScreens { get; set; } // monitor device names; empty = the main screen
        public string ClockSize { get; set; }          // small | medium | large
        public bool ClockDate { get; set; }

        public bool CheckUpdates { get; set; }         // looks for a newer version on GitHub now and then

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
            IslandOn = false;
            IslandStyle = "black";
            IslandBlur = 60;
            IslandShape = "float";
            IslandMusic = true;
            IslandScrollVolume = true;
            IslandCharging = true;
            IslandHideFullScreen = true;
            IslandScreen = "";
            IslandHeadphones = true;
            IslandSystem = true;
            ClockOn = false;
            ClockScreens = new List<string>();
            ClockSize = "medium";
            ClockDate = true;
            CheckUpdates = true;
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
                        if (s.IslandStyle != "glass") s.IslandStyle = "black";
                        if (s.IslandShape != "notch") s.IslandShape = "float";
                        s.IslandBlur = Math.Max(0, Math.Min(100, s.IslandBlur));
                        if (s.IslandScreen == null) s.IslandScreen = "";
                        if (s.ClockScreens == null) s.ClockScreens = new List<string>();
                        if (s.ClockSize != "small" && s.ClockSize != "large") s.ClockSize = "medium";
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
