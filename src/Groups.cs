using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinGroups
{
    // One app inside a group.
    public class GroupApp
    {
        public string Name { get; set; }
        public string Kind { get; set; }     // aumid (Start app) | link (copy of a .lnk/.url) | path (exe or any file)
        public string Target { get; set; }   // AUMID, file path, or the original shortcut path for links
        public string LinkData { get; set; } // base64 copy of the shortcut, so deleting the desktop shortcut later doesn't break the group
        public string LinkExt { get; set; }
        public string Source { get; set; }   // where it was picked from, used to re-select it when editing

        public static GroupApp FromCatalog(CatalogApp c)
        {
            if (c.Kind == "aumid")
                return new GroupApp { Name = c.Name, Kind = "aumid", Target = c.Target, Source = c.Source };
            var app = FromFile(c.Target);
            app.Name = c.Name;
            app.Source = c.Source;
            return app;
        }

        // Reads the file but never changes or moves it.
        public static GroupApp FromFile(string file)
        {
            file = Native.LongPath(file);
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string name = Directory.Exists(file) ? Path.GetFileName(file.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(file);
            if (ext == ".lnk" || ext == ".url" || ext == ".appref-ms")
            {
                return new GroupApp
                {
                    Name = name, Kind = "link", Target = file, LinkExt = ext,
                    LinkData = Convert.ToBase64String(File.ReadAllBytes(file)), Source = "file:" + file
                };
            }
            return new GroupApp { Name = name, Kind = "path", Target = file, Source = "file:" + file };
        }
    }

    public class GroupData
    {
        public int V { get; set; }
        public List<GroupApp> Apps { get; set; }
        public GroupData() { V = 1; Apps = new List<GroupApp>(); }
    }

    public class Group
    {
        public string Path;
        public string Name;
        public GroupData Data;
        public DateTime Created;
    }

    // A .wingroup file is an .ico (so Explorer draws it as the desktop icon) followed by the group's JSON.
    static class GroupFile
    {
        public const string Ext = ".wingroup";
        static readonly byte[] Marker = Encoding.ASCII.GetBytes("\n#WINGROUP1\n");

        public static GroupData Read(string path)
        {
            byte[] b = null;
            for (int tries = 0; b == null; tries++)
            {
                try { b = File.ReadAllBytes(path); }
                catch (IOException) { if (tries > 5) throw; Thread.Sleep(60); }
            }
            int at = LastIndexOf(b, Marker);
            if (at < 0) return new GroupData();
            try
            {
                string json = Encoding.UTF8.GetString(b, at + Marker.Length, b.Length - at - Marker.Length);
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<GroupData>(json);
                if (d == null) return new GroupData();
                if (d.Apps == null) d.Apps = new List<GroupApp>();
                return d;
            }
            catch { return new GroupData(); }
        }

        public static void Write(string path, byte[] ico, GroupData data)
        {
            byte[] json = Encoding.UTF8.GetBytes(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(data));
            for (int tries = 0; ; tries++)
            {
                try
                {
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        fs.Write(ico, 0, ico.Length);
                        fs.Write(Marker, 0, Marker.Length);
                        fs.Write(json, 0, json.Length);
                    }
                    return;
                }
                catch (IOException) { if (tries > 5) throw; Thread.Sleep(60); }
            }
        }

        static int LastIndexOf(byte[] hay, byte[] needle)
        {
            for (int i = hay.Length - needle.Length; i >= 0; i--)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }

    // Keeps private copies of shortcuts that were added to groups.
    static class LinkCache
    {
        static readonly object gate = new object();

        public static string Hash(string data)
        {
            using (var sha = SHA1.Create())
            {
                var h = sha.ComputeHash(Encoding.UTF8.GetBytes(data ?? ""));
                return BitConverter.ToString(h, 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string Materialize(GroupApp a)
        {
            string dir = Path.Combine(Settings.DataDir, "links");
            string name = SafeName(a.Name) + "-" + Hash(a.LinkData) + (string.IsNullOrEmpty(a.LinkExt) ? ".lnk" : a.LinkExt);
            string path = Path.Combine(dir, name);
            lock (gate)
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(dir);
                    File.WriteAllBytes(path, Convert.FromBase64String(a.LinkData));
                }
            }
            return path;
        }

        public static string SafeName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").Trim())
                sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '-' : c);
            string s = sb.ToString().Trim().TrimEnd('.');
            return s.Length == 0 ? "Group" : s;
        }
    }

    static class AppIcons
    {
        public static string Key(GroupApp a)
        {
            return a.Kind == "link" ? "link:" + LinkCache.Hash(a.LinkData) : a.Kind + ":" + a.Target;
        }

        public static string PathFor(GroupApp a)
        {
            if (a.Kind == "aumid") return "shell:AppsFolder\\" + a.Target;
            if (a.Kind == "link") return LinkCache.Materialize(a);
            return a.Target;
        }

        public static BitmapSource GetNow(GroupApp a, int size)
        {
            return IconLoader.GetNow(Key(a), () => PathFor(a), size);
        }

        public static void Request(GroupApp a, int size, Action<BitmapSource> done)
        {
            IconLoader.Request(Key(a), () => PathFor(a), size, done);
        }
    }

    static class Launcher
    {
        public static bool Launch(GroupApp a, bool admin)
        {
            try
            {
                string file = AppIcons.PathFor(a);
                var sei = new Native.SHELLEXECUTEINFO();
                sei.cbSize = Marshal.SizeOf(typeof(Native.SHELLEXECUTEINFO));
                sei.lpFile = file;
                sei.lpVerb = admin ? "runas" : null;
                sei.nShow = 1;
                if (a.Kind == "path" && File.Exists(file)) sei.lpDirectory = Path.GetDirectoryName(file);
                return Native.ShellExecuteEx(ref sei);
            }
            catch { return false; }
        }
    }

    static class ShellNotify
    {
        // Tells Explorer the file's icon changed so the desktop redraws it.
        public static void Updated(string path, bool created)
        {
            try
            {
                if (created)
                {
                    Native.SHChangeNotifyPath(Native.SHCNE_CREATE, Native.SHCNF_PATHW | Native.SHCNF_FLUSHNOWAIT, path, null);
                    return;
                }
                var sfi = new Native.SHFILEINFO();
                Native.SHGetFileInfo(path, 0, ref sfi, (uint)Marshal.SizeOf(typeof(Native.SHFILEINFO)), 0x4000 /*SHGFI_SYSICONINDEX*/);
                Native.SHUpdateImage(path, 0, 0x2 /*GIL_PERINSTANCE*/, sfi.iIcon);
                Native.SHUpdateImage(path, 0, 0, sfi.iIcon);
                Native.SHChangeNotifyPath(Native.SHCNE_UPDATEITEM, Native.SHCNF_PATHW | Native.SHCNF_FLUSHNOWAIT, path, null);
            }
            catch { }
        }

        public static void AssociationsChanged()
        {
            Native.SHChangeNotify(Native.SHCNE_ASSOCCHANGED, Native.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
    }

    // All groups = the .wingroup files on the user's desktop.
    class GroupStore
    {
        readonly Settings settings;
        FileSystemWatcher watcher;
        DispatcherTimer debounce;
        volatile Dictionary<string, string> byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<Group> Groups = new List<Group>();
        public event Action Changed;

        public static string Dir { get { return AppCatalog.UserDesktop; } }

        public GroupStore(Settings settings)
        {
            this.settings = settings;
        }

        public void StartWatching()
        {
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            debounce.Tick += (s, e) => { debounce.Stop(); Reload(); };
            try
            {
                watcher = new FileSystemWatcher(Dir, "*" + GroupFile.Ext);
                watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite;
                FileSystemEventHandler h = (s, e) => Kick();
                watcher.Created += h;
                watcher.Deleted += h;
                watcher.Changed += h;
                watcher.Renamed += (s, e) => Kick();
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        void Kick()
        {
            var d = debounce;
            if (d == null) return;
            d.Dispatcher.BeginInvoke(new Action(() => { d.Stop(); d.Start(); }));
        }

        public void Reload()
        {
            var list = new List<Group>();
            try
            {
                foreach (var f in Directory.GetFiles(Dir, "*" + GroupFile.Ext))
                {
                    try { list.Add(Load(f)); } catch { }
                }
            }
            catch { }
            Groups = list.OrderBy(g => g.Created).ThenBy(g => g.Name).ToList();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in Groups) map[g.Name] = g.Path;
            byName = map;
            var h = Changed;
            if (h != null) h();
        }

        public static Group Load(string path)
        {
            return new Group
            {
                Path = path,
                Name = Path.GetFileNameWithoutExtension(path),
                Data = GroupFile.Read(path),
                Created = File.GetCreationTimeUtc(path)
            };
        }

        public Group Find(string path)
        {
            return Groups.FirstOrDefault(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        // Safe to call from any thread.
        public string PathForName(string displayName)
        {
            string p;
            return displayName != null && byName.TryGetValue(displayName, out p) ? p : null;
        }

        public Group Create(string name, List<GroupApp> apps)
        {
            string path = UniquePath(name, null);
            var data = new GroupData { Apps = apps };
            Write(path, data);
            Reload();
            return Find(path);
        }

        public void Update(Group g, string name, List<GroupApp> apps)
        {
            string path = g.Path;
            if (!string.Equals(LinkCache.SafeName(name), g.Name, StringComparison.Ordinal))
            {
                string np = UniquePath(name, g.Path);
                File.Move(path, np);
                path = np;
            }
            g.Data.Apps = apps;
            Write(path, g.Data);
            Reload();
        }

        public void Save(Group g)
        {
            Write(g.Path, g.Data);
            Reload();
        }

        public void Delete(Group g)
        {
            try
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(g.Path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            catch { }
            Reload();
        }

        public void AddFiles(Group g, IEnumerable<string> files)
        {
            foreach (var f in files)
            {
                try
                {
                    if (string.Equals(Path.GetExtension(f), GroupFile.Ext, StringComparison.OrdinalIgnoreCase)) continue;
                    var app = GroupApp.FromFile(f);
                    if (g.Data.Apps.Any(x => x.Source == app.Source)) continue;
                    g.Data.Apps.Add(app);
                }
                catch { }
            }
            Save(g);
        }

        public void RewriteAll()
        {
            foreach (var g in Groups.ToList())
            {
                try { Write(g.Path, g.Data); } catch { }
            }
        }

        void Write(string path, GroupData data)
        {
            var icons = new List<ImageSource>();
            foreach (var a in data.Apps.Take(4)) icons.Add(AppIcons.GetNow(a, 128));
            byte[] ico = IconRenderer.BuildIco(icons, Theme.SystemDark, settings.IconStyle == "folder");
            bool existed = File.Exists(path);
            GroupFile.Write(path, ico, data);
            ShellNotify.Updated(path, !existed);
        }

        static string UniquePath(string name, string except)
        {
            string baseName = LinkCache.SafeName(name);
            string path = Path.Combine(Dir, baseName + GroupFile.Ext);
            for (int n = 2; File.Exists(path) && !string.Equals(path, except, StringComparison.OrdinalIgnoreCase); n++)
                path = Path.Combine(Dir, baseName + " (" + n + ")" + GroupFile.Ext);
            return path;
        }
    }
}
