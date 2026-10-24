using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace WinGroups
{
    // User preferences, stored as JSON in %LOCALAPPDATA%\Nook\settings.json.
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
        public string IslandTint { get; set; }       // "" = none, "accent", or #RRGGBB

        // The big clock in the middle of the desktop
        public bool ClockOn { get; set; }
        public List<string> ClockScreens { get; set; } // monitor device names; empty = the main screen
        public string ClockSize { get; set; }          // small | medium | large
        public bool ClockDate { get; set; }
        public string ClockFont { get; set; }          // any installed font family; "" = Segoe UI Variable
        public string ClockWeight { get; set; }        // thin | light | regular | semibold | bold
        public string ClockColor { get; set; }         // "" = suits the wallpaper, "accent", or #RRGGBB
        public string ClockFormat { get; set; }        // "" = like Windows, "12" or "24"
        public bool ClockSeconds { get; set; }

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
            IslandTint = "";
            ClockOn = false;
            ClockScreens = new List<string>();
            ClockSize = "medium";
            ClockDate = true;
            ClockFont = "";
            ClockWeight = "light";
            ClockColor = "";
            ClockFormat = "";
            CheckUpdates = true;
        }

        // Developer commands keep their files here instead, so they never touch your groups or settings.
        internal static string DevDataDir;

        public static string DataDir
        {
            get { return DevDataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nook"); }
        }

        static string OldDataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinGroups"); }
        }

        // Nook was called WinGroups until 0.7: bring its groups and settings over once, before Nook has any of its own.
        // Moves the folder when it can, copies it when it can't (for example if the old version is still running).
        public static void MoveFromOldName()
        {
            try
            {
                if (!Directory.Exists(OldDataDir)) return;
                if (File.Exists(Path.Combine(DataDir, "settings.json")) || Directory.Exists(Path.Combine(DataDir, "groups"))) return;
                if (!Directory.Exists(DataDir))
                {
                    try { Directory.Move(OldDataDir, DataDir); return; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                Copy(OldDataDir, DataDir);
            }
            catch { }
        }

        static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
            {
                string dest = Path.Combine(to, Path.GetFileName(f));
                try { if (!File.Exists(dest)) File.Copy(f, dest); } catch { }
            }
            foreach (var d in Directory.GetDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
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
                        if (s.IslandTint == null) s.IslandTint = "";
                        if (s.ClockScreens == null) s.ClockScreens = new List<string>();
                        if (s.ClockSize != "small" && s.ClockSize != "large") s.ClockSize = "medium";
                        if (s.ClockFont == null) s.ClockFont = "";
                        if (Array.IndexOf(new[] { "thin", "light", "regular", "semibold", "bold" }, s.ClockWeight) < 0) s.ClockWeight = "light";
                        if (s.ClockColor == null) s.ClockColor = "";
                        if (s.ClockFormat != "12" && s.ClockFormat != "24") s.ClockFormat = "";
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
