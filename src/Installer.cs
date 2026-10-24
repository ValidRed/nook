using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinGroups
{
    // Nook runs from wherever you keep Nook.exe and never copies itself anywhere. The only things it adds outside its
    // own folder are the two you can pick (at first run, or later on Home):
    //   HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Nook = "Nook.exe" --widgets   (start with Windows)
    //   Desktop\Nook.lnk                                                                 (a shortcut to open Nook)
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

        // ---- the desktop shortcut, only when you ask for it: Desktop\Nook.lnk, pointing at this Nook.exe ----
        // Nothing else on the desktop is ever touched, and a Nook.lnk that isn't Nook's is left alone.

        static string DesktopShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Nook.lnk"); } }

        static string TargetOf(string lnk)
        {
            object shell = null, sc = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                shell = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                return sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string;
            }
            catch { return null; }
            finally
            {
                if (sc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(sc);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }

        static bool IsOurs(string lnk)
        {
            var target = TargetOf(lnk) ?? "";
            return target.EndsWith("Nook.exe", StringComparison.OrdinalIgnoreCase) || target.EndsWith("WinGroups.exe", StringComparison.OrdinalIgnoreCase);
        }

        public static bool HasDesktopShortcut { get { return File.Exists(DesktopShortcut) && IsOurs(DesktopShortcut); } }

        // False if it couldn't be made (or a Nook.lnk of someone else's is already there).
        public static bool AddDesktopShortcut()
        {
            string lnk = DesktopShortcut;
            if (File.Exists(lnk) && !IsOurs(lnk)) return false;
            object shell = null, sc = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                shell = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                var st = sc.GetType();
                st.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { CurrentExe });
                st.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(CurrentExe) });
                st.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { CurrentExe + ",0" });
                st.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "Nook: everything on your desktop has its own place" });
                st.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                return true;
            }
            catch { return false; }
            finally
            {
                if (sc != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(sc);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }

        public static void RemoveDesktopShortcut()
        {
            try { if (HasDesktopShortcut) File.Delete(DesktopShortcut); } catch { }
        }

        // Is Nook running from somewhere it may not stay (Downloads, or a temporary folder)? Shortcuts and the
        // startup entry point at wherever Nook.exe is.
        public static bool InPassingFolder
        {
            get
            {
                string dir = (Path.GetDirectoryName(CurrentExe) ?? "").TrimEnd('\\') + "\\";
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") + "\\";
                string temp = Path.GetTempPath();
                return dir.StartsWith(downloads, StringComparison.OrdinalIgnoreCase) || dir.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
            }
        }

        // "Remove Nook from this PC": deletes groups and settings, the startup entry and Nook's desktop shortcut.
        // Nook.exe stays where you put it.
        public static void RemoveFromPc()
        {
            SetStartup(false);
            RemoveDesktopShortcut();
            if (HasLegacyFileType) RemoveLegacyFileType();
            try { if (Directory.Exists(Settings.DataDir)) Directory.Delete(Settings.DataDir, true); } catch { }
        }
    }
}
