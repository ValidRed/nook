using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
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
        public const int MaxSize = 3;

        public int V { get; set; }
        public string Name { get; set; }
        public int Cols { get; set; }        // widget width in desktop icon spaces (1-3)
        public int Rows { get; set; }        // widget length in desktop icon spaces (1-3)
        public int X { get; set; }           // widget position in screen pixels; int.MinValue = not placed yet
        public int Y { get; set; }
        public string Color { get; set; }    // widget background: "" = theme default, "accent", or "#RRGGBB"
        public List<GroupApp> Apps { get; set; }

        public GroupData()
        {
            V = 2;
            Cols = 1;
            Rows = 1;
            X = int.MinValue;
            Y = int.MinValue;
            Apps = new List<GroupApp>();
        }

        public static int ClampSize(int n) { return Math.Max(1, Math.Min(MaxSize, n)); }
    }

    public class Group
    {
        public string Path;
        public GroupData Data;
        public DateTime Created;
        public string Name { get { return Data.Name; } }
        public bool IsPlaced { get { return Data.X != int.MinValue && Data.Y != int.MinValue; } }
    }

    static class GroupFile
    {
        public const string LegacyExt = ".wingroup";
        static readonly byte[] LegacyMarker = Encoding.ASCII.GetBytes("\n#WINGROUP1\n");

        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }; }

        static byte[] ReadAll(string path)
        {
            for (int tries = 0; ; tries++)
            {
                try { return File.ReadAllBytes(path); }
                catch (IOException) { if (tries > 5) throw; Thread.Sleep(60); }
            }
        }

        public static GroupData Read(string path)
        {
            var d = Json().Deserialize<GroupData>(Encoding.UTF8.GetString(ReadAll(path)));
            return Normalize(d ?? new GroupData(), Path.GetFileNameWithoutExtension(path));
        }

        // Version 0.1 kept groups on the desktop as an icon followed by JSON.
        public static GroupData ReadLegacy(string path)
        {
            byte[] b = ReadAll(path);
            GroupData d = null;
            for (int i = b.Length - LegacyMarker.Length; i >= 0 && d == null; i--)
            {
                int j = 0;
                while (j < LegacyMarker.Length && b[i + j] == LegacyMarker[j]) j++;
                if (j < LegacyMarker.Length) continue;
                try { d = Json().Deserialize<GroupData>(Encoding.UTF8.GetString(b, i + j, b.Length - i - j)); } catch { }
                break;
            }
            d = d ?? new GroupData();
            d.Name = Path.GetFileNameWithoutExtension(path);
            return Normalize(d, d.Name);
        }

        static GroupData Normalize(GroupData d, string fallbackName)
        {
            if (d.Apps == null) d.Apps = new List<GroupApp>();
            if (string.IsNullOrWhiteSpace(d.Name)) d.Name = fallbackName;
            d.Cols = GroupData.ClampSize(d.Cols);
            d.Rows = GroupData.ClampSize(d.Rows);
            if (d.V < 2) { d.X = int.MinValue; d.Y = int.MinValue; d.V = 2; }
            return d;
        }

        public static void Write(string path, GroupData data)
        {
            byte[] json = Encoding.UTF8.GetBytes(Json().Serialize(data));
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, json);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
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
            return s.Length == 0 ? "App" : s;
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

    // All groups live in %LOCALAPPDATA%\WinGroups\groups, one JSON file each. Each one is shown as a desktop widget.
    class GroupStore
    {
        public static string Dir = Path.Combine(Settings.DataDir, "groups");

        FileSystemWatcher watcher;
        DispatcherTimer debounce;

        public List<Group> Groups = new List<Group>();
        public event Action Changed;

        public void StartWatching()
        {
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            debounce.Tick += (s, e) => { debounce.Stop(); Reload(); };
            try
            {
                Directory.CreateDirectory(Dir);
                watcher = new FileSystemWatcher(Dir, "*.json");
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
                if (Directory.Exists(Dir))
                    foreach (var f in Directory.GetFiles(Dir, "*.json"))
                    {
                        try { list.Add(new Group { Path = f, Data = GroupFile.Read(f), Created = File.GetCreationTimeUtc(f) }); } catch { }
                    }
            }
            catch { }
            Groups = list.OrderBy(g => g.Created).ThenBy(g => g.Name).ToList();
            var h = Changed;
            if (h != null) h();
        }

        public Group Find(string path)
        {
            return Groups.FirstOrDefault(g => string.Equals(g.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        public Group Create(string name, List<GroupApp> apps, int cols, int rows, string color)
        {
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, Guid.NewGuid().ToString("N") + ".json");
            var data = new GroupData { Name = name, Apps = apps, Cols = GroupData.ClampSize(cols), Rows = GroupData.ClampSize(rows), Color = color ?? "" };
            GroupFile.Write(path, data);
            Reload();
            return Find(path);
        }

        public void Update(Group g, string name, List<GroupApp> apps, int cols, int rows, string color)
        {
            g.Data.Name = name;
            g.Data.Apps = apps;
            g.Data.Cols = GroupData.ClampSize(cols);
            g.Data.Rows = GroupData.ClampSize(rows);
            g.Data.Color = color ?? "";
            Save(g);
        }

        public void Save(Group g)
        {
            GroupFile.Write(g.Path, g.Data);
            Reload();
        }

        // Position changes don't need the widgets rebuilt.
        public void SavePosition(Group g, int x, int y)
        {
            g.Data.X = x;
            g.Data.Y = y;
            try { GroupFile.Write(g.Path, g.Data); } catch { }
        }

        public void Delete(Group g)
        {
            try { File.Delete(g.Path); } catch { }
            Reload();
        }

        // Adds dropped files to a group. The files themselves are only read, never moved or changed.
        public void AddFiles(Group g, IEnumerable<string> files)
        {
            foreach (var f in files)
            {
                try
                {
                    var app = GroupApp.FromFile(f);
                    if (g.Data.Apps.Any(x => x.Source == app.Source)) continue;
                    g.Data.Apps.Add(app);
                }
                catch { }
            }
            Save(g);
        }

        // Brings over groups made with version 0.1 (files on the desktop), then sends those old files to the Recycle Bin.
        public int MigrateLegacy()
        {
            int moved = 0;
            try
            {
                foreach (var f in Directory.GetFiles(AppCatalog.UserDesktop, "*" + GroupFile.LegacyExt))
                {
                    try
                    {
                        var d = GroupFile.ReadLegacy(f);
                        Directory.CreateDirectory(Dir);
                        GroupFile.Write(Path.Combine(Dir, Guid.NewGuid().ToString("N") + ".json"), d);
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(f,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        moved++;
                    }
                    catch { }
                }
            }
            catch { }
            return moved;
        }
    }
}
