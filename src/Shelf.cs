using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace WinGroups
{
    // One thing on the shelf.
    public class ShelfItem
    {
        public string Kind { get; set; }    // file | text | link
        public string Path { get; set; }    // file: where it is (your own file, or Nook's copy in its shelf folder)
        public bool Own { get; set; }       // the file is a copy Nook made: a picture, or a file a program handed over without one on disk
        public string Text { get; set; }    // text, or a link's address
        public string Title { get; set; }   // a link's page title, when the browser gave one
        public DateTime Added { get; set; }
    }

    // The island's shelf: drag files, links, text or pictures onto the island to park them, then drag them off
    // where they go. Your files stay where they are (the shelf only points at them) and dragging one off copies it.
    // There's one shelf for the whole app; it's kept in Nook's folder so it's still there after a restart.
    class Shelf
    {
        public const int Max = 10;
        public List<ShelfItem> Items { get; private set; }
        public event Action Changed;
        readonly bool keep;

        public static string Dir { get { return Path.Combine(Settings.DataDir, "shelf"); } }
        static string FilePath { get { return Path.Combine(Settings.DataDir, "shelf.json"); } }

        // keep = false for tests and renders: nothing is read or written.
        public Shelf(bool keep)
        {
            this.keep = keep;
            Items = new List<ShelfItem>();
            if (!keep) return;
            try
            {
                if (File.Exists(FilePath))
                    Items = new JavaScriptSerializer().Deserialize<List<ShelfItem>>(File.ReadAllText(FilePath)) ?? new List<ShelfItem>();
            }
            catch { Items = new List<ShelfItem>(); }
            // Files that have gone (moved or deleted since) leave the shelf.
            Items.RemoveAll(i => i == null || (i.Kind == "file" && !File.Exists(i.Path) && !Directory.Exists(i.Path)) || (i.Kind != "file" && string.IsNullOrEmpty(i.Text)));
            Tidy();
        }

        public bool Full { get { return Items.Count >= Max; } }

        void Save()
        {
            if (keep)
            {
                try
                {
                    Directory.CreateDirectory(Settings.DataDir);
                    File.WriteAllText(FilePath, new JavaScriptSerializer().Serialize(Items));
                }
                catch { }
            }
            if (Changed != null) Changed();
        }

        // ---- taking things ----

        // Can the shelf take what's being dragged?
        public static bool CanTake(IDataObject d)
        {
            try
            {
                return d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent("FileGroupDescriptorW") || d.GetDataPresent(DataFormats.UnicodeText)
                    || d.GetDataPresent(DataFormats.Text) || d.GetDataPresent("UniformResourceLocatorW") || d.GetDataPresent("PNG") || d.GetDataPresent(DataFormats.Bitmap);
            }
            catch { return false; }
        }

        // Puts what was dropped on the shelf; returns how many things it took.
        public int Take(IDataObject d)
        {
            int before = Items.Count;
            try { TakeInto(d); } catch { }
            int added = Items.Count - before;
            if (added > 0) Save();
            return added;
        }

        void TakeInto(IDataObject d)
        {
            // Files you already have: the shelf points at them.
            var files = d.GetDataPresent(DataFormats.FileDrop) ? d.GetData(DataFormats.FileDrop) as string[] : null;
            if (files != null && files.Length > 0)
            {
                foreach (var f in files) AddFile(f, false);
                return;
            }
            // Files a program hands over without one on disk (a picture from a browser, a mail attachment) get a copy
            // in Nook's folder. A browser link comes as a ".url" file; that's a link, not a file.
            var names = d.GetDataPresent("FileGroupDescriptorW") ? VirtualNames(d) : new List<string>();
            if (names.Count > 0 && !names.All(n => n.EndsWith(".url", StringComparison.OrdinalIgnoreCase)))
            {
                for (int i = 0; i < names.Count && !Full; i++)
                {
                    string path = NewOwnPath(names[i]);
                    if (SaveVirtual(d, i, path)) AddFile(path, true);
                    else DeleteOwn(path);
                }
                return;
            }
            // A link.
            string title;
            string url = LinkOf(d, out title);
            if (url != null) { AddLink(url, title); return; }
            // Text (a lone web address counts as a link).
            string text = d.GetDataPresent(DataFormats.UnicodeText) ? d.GetData(DataFormats.UnicodeText) as string
                        : d.GetDataPresent(DataFormats.Text) ? d.GetData(DataFormats.Text) as string : null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (IsWebAddress(text.Trim())) AddLink(text.Trim(), null);
                else AddText(text);
                return;
            }
            // A picture without a file.
            BitmapSource picture = null;
            var png = d.GetDataPresent("PNG") ? d.GetData("PNG") as Stream : null;
            if (png != null) { try { picture = BitmapFrame.Create(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); } catch { } }
            if (picture == null && d.GetDataPresent(DataFormats.Bitmap)) picture = d.GetData(DataFormats.Bitmap) as BitmapSource;
            if (picture != null)
            {
                string path = NewOwnPath("Picture " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ".png");
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(picture));
                using (var fs = File.Create(path)) enc.Save(fs);
                AddFile(path, true);
            }
        }

        void AddFile(string path, bool own)
        {
            if (Full || string.IsNullOrEmpty(path)) return;
            if (!own && Items.Any(i => i.Kind == "file" && string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) return;
            Items.Add(new ShelfItem { Kind = "file", Path = path, Own = own, Added = DateTime.Now });
        }

        void AddText(string text)
        {
            if (Full || Items.Any(i => i.Kind == "text" && i.Text == text)) return;
            Items.Add(new ShelfItem { Kind = "text", Text = text, Added = DateTime.Now });
        }

        void AddLink(string url, string title)
        {
            if (Full || Items.Any(i => i.Kind == "link" && i.Text == url)) return;
            Items.Add(new ShelfItem { Kind = "link", Text = url, Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(), Added = DateTime.Now });
        }

        static bool IsWebAddress(string s)
        {
            Uri u;
            return s.IndexOfAny(new[] { ' ', '\n', '\r', '\t' }) < 0 && Uri.TryCreate(s, UriKind.Absolute, out u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        }

        // A browser's link: its address, and its title when there is one.
        static string LinkOf(IDataObject d, out string title)
        {
            title = null;
            try
            {
                if (d.GetDataPresent("text/x-moz-url"))
                {
                    var ms = d.GetData("text/x-moz-url") as MemoryStream;
                    if (ms != null)
                    {
                        var parts = Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0').Split('\n');
                        if (parts.Length > 1) title = parts[1].Trim();
                        if (IsWebAddress(parts[0].Trim())) return parts[0].Trim();
                    }
                }
                if (d.GetDataPresent("UniformResourceLocatorW"))
                {
                    var ms = d.GetData("UniformResourceLocatorW") as MemoryStream;
                    string url = ms == null ? null : Encoding.Unicode.GetString(ms.ToArray()).TrimEnd('\0').Trim();
                    if (url != null && IsWebAddress(url)) return url;
                }
            }
            catch { }
            return null;
        }

        // ---- files a program hands over without one on disk ----

        static List<string> VirtualNames(IDataObject d)
        {
            var names = new List<string>();
            try
            {
                var ms = d.GetData("FileGroupDescriptorW") as MemoryStream;
                if (ms == null) return names;
                var b = ms.ToArray();
                int count = BitConverter.ToInt32(b, 0);
                const int size = 592, nameAt = 72; // FILEDESCRIPTORW, and where its file name starts
                for (int i = 0; i < count && 4 + (i + 1) * size <= b.Length; i++)
                {
                    string n = Encoding.Unicode.GetString(b, 4 + i * size + nameAt, 520);
                    int end = n.IndexOf('\0');
                    names.Add(Path.GetFileName(end >= 0 ? n.Substring(0, end) : n));
                }
            }
            catch { }
            return names;
        }

        // The index-th file's contents, written to path.
        static bool SaveVirtual(IDataObject d, int index, string path)
        {
            try
            {
                var com = d as ComDataObject;
                if (com != null)
                {
                    var fmt = new ComTypes.FORMATETC
                    {
                        cfFormat = (short)DataFormats.GetDataFormat("FileContents").Id,
                        dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT,
                        lindex = index,
                        ptd = IntPtr.Zero,
                        tymed = ComTypes.TYMED.TYMED_ISTREAM | ComTypes.TYMED.TYMED_HGLOBAL
                    };
                    ComTypes.STGMEDIUM med;
                    com.GetData(ref fmt, out med);
                    try
                    {
                        using (var fs = File.Create(path))
                        {
                            if (med.tymed == ComTypes.TYMED.TYMED_ISTREAM)
                            {
                                var s = (ComTypes.IStream)Marshal.GetObjectForIUnknown(med.unionmember);
                                var buf = new byte[81920];
                                IntPtr read = Marshal.AllocCoTaskMem(4);
                                try
                                {
                                    while (true)
                                    {
                                        s.Read(buf, buf.Length, read);
                                        int n = Marshal.ReadInt32(read);
                                        if (n <= 0) break;
                                        fs.Write(buf, 0, n);
                                    }
                                }
                                finally { Marshal.FreeCoTaskMem(read); Marshal.ReleaseComObject(s); }
                            }
                            else if (med.tymed == ComTypes.TYMED.TYMED_HGLOBAL)
                            {
                                int n = (int)GlobalSize(med.unionmember);
                                IntPtr p = GlobalLock(med.unionmember);
                                try { var buf = new byte[n]; Marshal.Copy(p, buf, 0, n); fs.Write(buf, 0, n); }
                                finally { GlobalUnlock(med.unionmember); }
                            }
                        }
                    }
                    finally { ReleaseStgMedium(ref med); }
                    return true;
                }
            }
            catch { }
            // Fallback: the first file, the ordinary way.
            try
            {
                if (index != 0) return false;
                var ms = d.GetData("FileContents") as MemoryStream;
                if (ms == null) return false;
                File.WriteAllBytes(path, ms.ToArray());
                return true;
            }
            catch { return false; }
        }

        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);
        [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);

        // Each copy Nook makes gets its own folder, so it keeps its name.
        static string NewOwnPath(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            if (string.IsNullOrWhiteSpace(name)) name = "File";
            string dir = Path.Combine(Dir, Guid.NewGuid().ToString("N").Substring(0, 12));
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, name);
        }

        // ---- taking things off ----

        public void Remove(ShelfItem item)
        {
            if (!Items.Remove(item)) return;
            Save();
            // Nook's own copy goes a little later: whatever it was dropped into may still be copying it.
            if (item.Own) Later(() => DeleteOwn(item.Path));
        }

        public void Clear()
        {
            var own = Items.Where(i => i.Own).Select(i => i.Path).ToList();
            Items.Clear();
            Save();
            Later(() => { foreach (var p in own) DeleteOwn(p); });
        }

        static void Later(Action a)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            t.Tick += (s, e) => { t.Stop(); a(); };
            t.Start();
        }

        // Only ever inside Nook's shelf folder.
        static void DeleteOwn(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (dir != null && string.Equals(Path.GetDirectoryName(dir), Dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(dir, true);
            }
            catch { }
        }

        // Copies left over from last time (taken off just before Nook closed) go now.
        void Tidy()
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                var inUse = new HashSet<string>(Items.Where(i => i.Own).Select(i => Path.GetDirectoryName(i.Path)), StringComparer.OrdinalIgnoreCase);
                foreach (var d in Directory.GetDirectories(Dir)) if (!inUse.Contains(d)) try { Directory.Delete(d, true); } catch { }
            }
            catch { }
        }

        // What dragging an item off hands over. A file is offered as a copy (or a shortcut to your own file), so
        // dropping it somewhere never moves your original.
        public static DataObject DataFor(ShelfItem item, out DragDropEffects allowed)
        {
            var d = new DataObject();
            allowed = DragDropEffects.Copy;
            if (item.Kind == "file")
            {
                d.SetData(DataFormats.FileDrop, new[] { item.Path });
                if (!item.Own) allowed |= DragDropEffects.Link;
            }
            else if (item.Kind == "link")
            {
                d.SetData(DataFormats.UnicodeText, item.Text);
                d.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes(item.Text + "\0")));
                d.SetData("UniformResourceLocator", new MemoryStream(Encoding.Default.GetBytes(item.Text + "\0")));
                d.SetData("text/x-moz-url", new MemoryStream(Encoding.Unicode.GetBytes(item.Text + "\n" + (item.Title ?? item.Text) + "\0")));
                allowed |= DragDropEffects.Link;
            }
            else
            {
                d.SetData(DataFormats.UnicodeText, item.Text);
                d.SetData(DataFormats.Text, item.Text);
            }
            return d;
        }

        // A click: a file opens in its app, a link in your browser, and text is copied.
        public static bool Open(ShelfItem item)
        {
            try
            {
                if (item.Kind == "text") { Clipboard.SetText(item.Text); return true; }
                if (item.Kind == "link") { if (IsWebAddress(item.Text)) Process.Start(item.Text); return true; }
                if (Directory.Exists(item.Path)) Process.Start("explorer.exe", "\"" + item.Path + "\"");
                else if (File.Exists(item.Path)) Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }
    }

    // How a shelf item looks on the island.
    public class ShelfTile
    {
        static readonly string[] Pictures = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".heic" };
        internal readonly ShelfItem Item;

        internal ShelfTile(ShelfItem item, Action<ShelfItem> remove)
        {
            Item = item;
            RemoveCommand = new Relay(() => remove(item));
            if (item.Kind == "file")
            {
                Name = Path.GetFileName(item.Path.TrimEnd('\\'));
                if (string.IsNullOrEmpty(Name)) Name = item.Path;
                Tip = item.Own ? Name : item.Path;
                Icon = Array.IndexOf(Pictures, Path.GetExtension(item.Path).ToLowerInvariant()) >= 0 ? Thumbnail(item.Path) : null;
                IsPicture = Icon != null;
                if (Icon == null) { try { Icon = ShellIcons.Get(item.Path, 48); } catch { } }
            }
            else if (item.Kind == "link")
            {
                Uri u;
                string host = Uri.TryCreate(item.Text, UriKind.Absolute, out u) ? u.Host.Replace("www.", "") : item.Text;
                Name = item.Title ?? host;
                Tip = (item.Title != null ? item.Title + "\n" : "") + item.Text;
                IsLink = true;
            }
            else
            {
                string flat = string.Join(" ", item.Text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
                Name = flat.Length > 40 ? flat.Substring(0, 40) : flat;
                Snippet = flat.Length > 90 ? flat.Substring(0, 90) + "…" : flat;
                Tip = item.Text.Length > 400 ? item.Text.Substring(0, 400) + "…" : item.Text;
                IsText = true;
            }
        }

        public string Name { get; private set; }
        public string Tip { get; private set; }
        public string Snippet { get; private set; }
        public ImageSource Icon { get; private set; }
        public bool IsPicture { get; private set; }
        public bool IsIcon { get { return Icon != null && !IsPicture; } }
        public bool IsLink { get; private set; }
        public bool IsText { get; private set; }
        public ICommand RemoveCommand { get; private set; }

        static BitmapSource Thumbnail(string path)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad; // so the file isn't kept open
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelHeight = 96;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }
    }
}
