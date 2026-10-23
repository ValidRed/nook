using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinGroups
{
    // One app the user can put in a group.
    public class CatalogApp
    {
        public string Name { get; set; }
        public string Section { get; set; } // desktop | installed | windows
        public string Kind { get; set; }    // aumid | desktop
        public string Target { get; set; }  // AppUserModelID / Start parsing name, or a file on the desktop

        public string Source { get { return Kind + ":" + Target; } }
        public string IconPath { get { return Kind == "aumid" ? "shell:AppsFolder\\" + Target : Target; } }
    }

    // Lists apps from Start (shell:AppsFolder) and the shortcuts on the desktop. Read-only: nothing here changes a file.
    static class AppCatalog
    {
        const string AppsFolder = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";

        public static string UserDesktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string PublicDesktop { get { return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory); } }

        // Runs the scan on its own STA thread (the Shell needs one) and returns the result.
        public static List<CatalogApp> LoadBlocking()
        {
            List<CatalogApp> result = null;
            var t = new Thread(() => { result = Load(); });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            t.Join();
            return result ?? new List<CatalogApp>();
        }

        static List<CatalogApp> Load()
        {
            var list = new List<CatalogApp>();
            var desktopSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in new[] { UserDesktop, PublicDesktop })
            {
                try
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".lnk" && ext != ".url" && ext != ".exe" && ext != ".appref-ms") continue;
                        string name = Path.GetFileNameWithoutExtension(f);
                        if (!desktopSeen.Add(name)) continue;
                        list.Add(new CatalogApp { Name = name, Section = "desktop", Kind = "desktop", Target = f });
                    }
                }
                catch { }
            }

            try
            {
                Native.IShellItem folder;
                Native.SHCreateItemFromParsingName(AppsFolder, IntPtr.Zero, typeof(Native.IShellItem).GUID, out folder);
                IntPtr pEnum;
                folder.BindToHandler(IntPtr.Zero, Native.BHID_EnumItems, typeof(Native.IEnumShellItems).GUID, out pEnum);
                var en = (Native.IEnumShellItems)Marshal.GetObjectForIUnknown(pEnum);
                Marshal.Release(pEnum);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var one = new Native.IShellItem[1];
                uint fetched;
                while (en.Next(1, one, out fetched) == 0 && fetched == 1)
                {
                    var it = one[0];
                    try
                    {
                        string name, parse;
                        it.GetDisplayName(0, out name);                // SIGDN_NORMALDISPLAY
                        it.GetDisplayName(0x80018001, out parse);      // SIGDN_PARENTRELATIVEPARSING
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parse)) continue;
                        if (name.IndexOf("uninstall", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (parse.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                        if (!seen.Add(parse)) continue;
                        list.Add(new CatalogApp { Name = name, Section = IsWindowsApp(parse) ? "windows" : "installed", Kind = "aumid", Target = parse });
                    }
                    catch { }
                    finally { Marshal.ReleaseComObject(it); }
                }
                Marshal.ReleaseComObject(en);
                Marshal.ReleaseComObject(folder);
            }
            catch { }

            return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // Apps that come with Windows: Microsoft-published packages and programs under the Windows folder.
        static bool IsWindowsApp(string parse)
        {
            string s = parse.ToLowerInvariant();
            if (s.Contains("_8wekyb3d8bbwe!") || s.Contains("_cw5n1h2txyewy!")) return true;
            if (s.StartsWith("microsoft.windows") || s.StartsWith("microsoftwindows.") || s.StartsWith("windows.")) return true;
            if (s.StartsWith("{1ac14e77-02e7-4e5d-b744-2eb1ae5198b7}") ||   // System32
                s.StartsWith("{f38bf404-1d43-42f2-9305-67de0b28fc23}") ||   // Windows
                s.StartsWith("{d65231b0-b2f1-4857-a4ce-a8e7c6ea7d27}")) return true; // SysWOW64
            if (s.StartsWith(@"c:\windows\")) return true;
            return false;
        }
    }
}
