using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinGroups
{
    // WinGroups runs from wherever you keep WinGroups.exe. The only thing it registers is the group file type,
    // per user, so Explorer knows how to draw and open .wingroup files:
    //   HKCU\Software\Classes\.wingroup and HKCU\Software\Classes\WinGroups.Group
    static class Installer
    {
        const string ProgId = "WinGroups.Group";
        const string TypeName = "WinGroups group";
        const string WshDropHandler = "{60254CA5-953B-11CF-8C96-00AA00B8708C}"; // lets you drop apps onto a group

        public static string CurrentExe { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        // Points the file type at this exe (again, if the exe was moved).
        public static void EnsureRegistered()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + ProgId + @"\shell\open\command"))
                {
                    string cmd = k == null ? null : k.GetValue("") as string;
                    if (cmd == OpenCommand(CurrentExe)) return;
                }
                Register(CurrentExe);
            }
            catch { }
        }

        static string OpenCommand(string exe)
        {
            return "\"" + exe + "\" open \"%1\" %*";
        }

        static void Register(string exe)
        {
            using (var ext = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + GroupFile.Ext))
                ext.SetValue("", ProgId);
            using (var p = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ProgId))
            {
                p.SetValue("", TypeName);
                p.SetValue("FriendlyTypeName", TypeName);
                p.SetValue("NeverShowExt", "");
                using (var icon = p.CreateSubKey("DefaultIcon")) icon.SetValue("", "%1");
                using (var shell = p.CreateSubKey("shell")) shell.SetValue("", "open");
                using (var open = p.CreateSubKey(@"shell\open")) open.SetValue("", "Open");
                using (var cmd = p.CreateSubKey(@"shell\open\command")) cmd.SetValue("", OpenCommand(exe));
                using (var drop = p.CreateSubKey(@"shellex\DropHandler")) drop.SetValue("", WshDropHandler);
            }
            ShellNotify.AssociationsChanged();
        }

        // "Remove WinGroups from this PC": moves the groups to the Recycle Bin, unregisters the file type and
        // deletes WinGroups' settings. Nothing else on the desktop is touched. WinGroups.exe itself stays where you put it.
        public static void RemoveFromPc()
        {
            try
            {
                foreach (var f in Directory.GetFiles(GroupStore.Dir, "*" + GroupFile.Ext))
                {
                    try
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch { }
                }
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + GroupFile.Ext, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, false); } catch { }
            try { if (Directory.Exists(Settings.DataDir)) Directory.Delete(Settings.DataDir, true); } catch { }
            ShellNotify.AssociationsChanged();
        }
    }
}
