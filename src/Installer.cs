using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinGroups
{
    // Nook runs from wherever you keep Nook.exe and installs nothing. The only system setting it can change
    // is the optional "Start with Windows" entry, and only while that setting is on:
    //   HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Nook = "Nook.exe" --widgets
    static class Installer
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "Nook";
        const string OldRunValue = "WinGroups"; // the entry from before the rename
        const string LegacyProgId = "WinGroups.Group";

        public static string CurrentExe { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        static string StartupCommand { get { return "\"" + CurrentExe + "\" --widgets"; } }

        public static void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunValue, StartupCommand);
                    else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue);
                    if (k.GetValue(OldRunValue) != null) k.DeleteValue(OldRunValue);
                }
            }
            catch { }
        }

        // If the exe was moved, point the startup entry at its new location.
        public static void RefreshStartup(bool on)
        {
            if (!on) return;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k != null && (k.GetValue(RunValue) as string) == StartupCommand && k.GetValue(OldRunValue) == null) return;
                }
            }
            catch { }
            SetStartup(true);
        }

        // Version 0.1 registered a .wingroup file type for desktop icons. Widgets replaced those, so remove it.
        public static bool HasLegacyFileType
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + LegacyProgId)) return k != null;
            }
        }

        public static void RemoveLegacyFileType()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + GroupFile.LegacyExt, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + LegacyProgId, false); } catch { }
            Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        // "Remove Nook from this PC": deletes groups and settings and the startup entry. Nook.exe stays where you put it.
        public static void RemoveFromPc()
        {
            SetStartup(false);
            if (HasLegacyFileType) RemoveLegacyFileType();
            try { if (Directory.Exists(Settings.DataDir)) Directory.Delete(Settings.DataDir, true); } catch { }
        }
    }
}
