using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public int LookVersion { get; set; }       // 2 from 0.11: Nook's own blue became the starting accent
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
        public string IslandScreen { get; set; }     // monitor device name; "" = the main screen, "*" = every screen
        public int IslandWidth { get; set; }         // resting width in DIPs; anything up to 112 just fits the time
        public bool IslandHeadphones { get; set; }   // shows headphones for a moment when they connect
        public bool IslandSystem { get; set; }       // a System page (CPU, memory, GPU, network) when the island is open
        public bool IslandTimer { get; set; }        // a Timer page; while a timer runs, its countdown stays on the island
        public bool IslandTimerSound { get; set; }   // Windows' alarm sound when the timer ends
        public int IslandTimerMinutes { get; set; }  // the length you set last
        public bool IslandShelf { get; set; }        // drag things onto the island to park them, then drag them off where they go

        // The taskbar: a few of Nook's own buttons on the empty part of Windows' taskbar
        public bool TaskbarOn { get; set; }
        public bool TaskbarGroups { get; set; }      // groups, popping open above the taskbar
        public bool TaskbarWorkspaces { get; set; }  // workspaces, one click each
        public bool TaskbarSound { get; set; }       // switch between speakers and headphones
        public bool TaskbarMic { get; set; }         // mute every microphone
        public string TaskbarSide { get; set; }      // auto | left | right (next to the tray)
        public string IslandTint { get; set; }       // "" = none, "accent", or #RRGGBB

        // The big clock in the middle of the desktop
        public bool ClockOn { get; set; }
        public List<string> ClockScreens { get; set; } // monitor device names; empty = the main screen
        public string ClockSize { get; set; }          // small | medium | large (before 0.11; ClockTextSize replaces it)
        public int ClockTextSize { get; set; }         // how big the time is, 48 to 240 (DIPs)
        public bool ClockDate { get; set; }
        public string ClockFont { get; set; }          // which font in ClockFontList (its Key)
        public List<ClockFontChoice> ClockFontList { get; set; } // the fonts to pick from: three to start, plus any you add
        public string ClockLook { get; set; }          // solid | glass (frosted glass digits)
        public int ClockGlassBlur { get; set; }        // glass only: 0 (clear) to 100 (frosted)
        public bool ClockShine { get; set; }           // glass only: a light sweeps across the digits now and then
        public bool ClockDateAbove { get; set; }       // the date sits above the time, like a phone's lock screen
        public string ClockPosition { get; set; }      // upper (the upper third, like a lock screen) | center
        public int ClockStretch { get; set; }          // digit height, 100 (as drawn) to 170 percent
        public bool ClockGlance { get; set; }          // the battery and what's playing under the time
        public string ClockWeight { get; set; }        // thin | light | regular | semibold | bold
        public string ClockColor { get; set; }         // "" = suits the wallpaper, "accent", or #RRGGBB
        public string ClockFormat { get; set; }        // "" = like Windows, "12" or "24"
        public bool ClockSeconds { get; set; }

        public bool CheckUpdates { get; set; }         // looks for a newer version on GitHub now and then
        public bool TutorialHidden { get; set; }       // the "How to use Nook" card on Home was closed
        public bool SetupDone { get; set; }            // the first-run setup (startup, desktop shortcut) has been seen
        public string AppLook { get; set; }            // Nook's own window: solid | glass
        public int AppBlur { get; set; }               // glass only: 0 (clearest) to 100 (most frosted)

        public Settings()
        {
            CloseAfterLaunch = true;
            ShowNames = true;
            StartWithWindows = false;
            Theme = "windows";
            Accent = "#3D8BF2";
            LookVersion = 2;
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
            IslandTimer = true;
            IslandTimerSound = true;
            IslandTimerMinutes = 5;
            IslandShelf = true;
            TaskbarOn = false;
            TaskbarGroups = TaskbarWorkspaces = TaskbarSound = TaskbarMic = true;
            TaskbarSide = "auto";
            IslandTint = "";
            ClockOn = false;
            ClockScreens = new List<string>();
            ClockSize = "medium";
            ClockTextSize = 112;
            ClockDate = true;
            ClockFont = "Helvetica";
            ClockFontList = ClockFonts.Defaults();
            ClockLook = "solid";
            ClockGlassBlur = 60;
            ClockShine = true;
            ClockDateAbove = true;
            ClockPosition = "upper";
            ClockStretch = 100;
            ClockGlance = true;
            ClockWeight = "bold";
            ClockColor = "";
            ClockFormat = "";
            CheckUpdates = true;
            AppLook = "solid";
            AppBlur = 50;
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
                        // 0.11 moved to Nook's deep blues: an accent that was only ever the default becomes Nook blue.
                        // ("Match Windows" picked after this stays picked.)
                        if (!json.Contains("\"LookVersion\"")) { if (s.Accent == "") s.Accent = "#3D8BF2"; s.LookVersion = 2; }
                        // settings saved by older versions don't have these yet
                        if (!json.Contains("\"Blur\"")) s.Blur = 60;
                        if (!json.Contains("\"QuickSearch\"")) s.QuickSearch = true;
                        s.Blur = Math.Max(0, Math.Min(100, s.Blur));
                        if (s.IslandStyle != "glass") s.IslandStyle = "black";
                        if (s.IslandShape != "notch") s.IslandShape = "float";
                        s.IslandBlur = Math.Max(0, Math.Min(100, s.IslandBlur));
                        if (s.IslandScreen == null) s.IslandScreen = "";
                        if (s.IslandTint == null) s.IslandTint = "";
                        s.IslandWidth = Math.Max(0, Math.Min(420, s.IslandWidth));
                        s.IslandTimerMinutes = s.IslandTimerMinutes <= 0 ? 5 : Math.Min(180, s.IslandTimerMinutes);
                        if (s.TaskbarSide != "left" && s.TaskbarSide != "right") s.TaskbarSide = "auto";
                        if (s.ClockScreens == null) s.ClockScreens = new List<string>();
                        if (s.ClockSize != "small" && s.ClockSize != "large") s.ClockSize = "medium";
                        // Sizes picked before 0.11 carry over to the slider.
                        if (!json.Contains("\"ClockTextSize\"")) s.ClockTextSize = s.ClockSize == "small" ? 72 : s.ClockSize == "large" ? 168 : 112;
                        s.ClockTextSize = Math.Max(48, Math.Min(240, s.ClockTextSize));
                        if (s.ClockFontList == null) s.ClockFontList = ClockFonts.Defaults();
                        s.ClockFontList.RemoveAll(f => f == null || string.IsNullOrWhiteSpace(f.Name));
                        foreach (var f in s.ClockFontList) if (f.File == null) f.File = "";
                        // Fonts picked before 0.10 aren't in the list: start on Helvetica.
                        if (s.ClockFont == null || !s.ClockFontList.Any(f => f.Key == s.ClockFont)) s.ClockFont = s.ClockFontList.Count > 0 ? s.ClockFontList.Last().Key : "";
                        if (s.ClockLook != "glass") s.ClockLook = "solid";
                        if (!json.Contains("\"ClockGlassBlur\"")) s.ClockGlassBlur = 60;
                        s.ClockGlassBlur = Math.Max(0, Math.Min(100, s.ClockGlassBlur));
                        // New in 0.10: the lock-screen layout is the starting point.
                        if (!json.Contains("\"ClockShine\"")) s.ClockShine = true;
                        if (!json.Contains("\"ClockDateAbove\"")) s.ClockDateAbove = true;
                        if (!json.Contains("\"ClockGlance\"")) s.ClockGlance = true;
                        if (s.ClockPosition != "center") s.ClockPosition = "upper";
                        s.ClockStretch = Math.Max(100, Math.Min(170, s.ClockStretch));
                        if (Array.IndexOf(new[] { "thin", "light", "regular", "semibold", "bold" }, s.ClockWeight) < 0) s.ClockWeight = "light";
                        if (s.ClockColor == null) s.ClockColor = "";
                        if (s.ClockFormat != "12" && s.ClockFormat != "24") s.ClockFormat = "";
                        if (s.AppLook != "glass") s.AppLook = "solid";
                        if (!json.Contains("\"SetupDone\"")) s.SetupDone = true; // people who used Nook before 0.11 skip setup
                        if (!json.Contains("\"AppBlur\"")) s.AppBlur = 50;
                        s.AppBlur = Math.Max(0, Math.Min(100, s.AppBlur));
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
