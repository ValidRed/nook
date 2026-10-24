using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace WinGroups
{
    // A way to lay apps out on a screen. Zones are fractions of the screen's work area (above the taskbar).
    class LayoutPreset
    {
        public string Id, Label;
        public Rect[] Zones;          // empty = just open the apps
        public bool Maximize;         // every app full screen

        public static readonly List<LayoutPreset> All = new List<LayoutPreset>
        {
            new LayoutPreset { Id = "open", Label = "Just open", Zones = new Rect[0] },
            new LayoutPreset { Id = "full", Label = "Full screen", Zones = new[] { new Rect(0, 0, 1, 1) }, Maximize = true },
            new LayoutPreset { Id = "halves", Label = "Side by side", Zones = new[] { new Rect(0, 0, 0.5, 1), new Rect(0.5, 0, 0.5, 1) } },
            new LayoutPreset { Id = "main", Label = "Main and side", Zones = new[] { new Rect(0, 0, 0.65, 1), new Rect(0.65, 0, 0.35, 1) } },
            new LayoutPreset { Id = "thirds", Label = "Three columns", Zones = new[] { new Rect(0, 0, 1 / 3.0, 1), new Rect(1 / 3.0, 0, 1 / 3.0, 1), new Rect(2 / 3.0, 0, 1 / 3.0, 1) } },
            new LayoutPreset { Id = "stack", Label = "Main and two", Zones = new[] { new Rect(0, 0, 0.5, 1), new Rect(0.5, 0, 0.5, 0.5), new Rect(0.5, 0.5, 0.5, 0.5) } },
            new LayoutPreset { Id = "quarters", Label = "Four corners", Zones = new[] { new Rect(0, 0, 0.5, 0.5), new Rect(0.5, 0, 0.5, 0.5), new Rect(0, 0.5, 0.5, 0.5), new Rect(0.5, 0.5, 0.5, 0.5) } },
        };

        public static LayoutPreset Find(string id) { return All.FirstOrDefault(l => l.Id == id) ?? All[0]; }

        // How many apps get a place (the rest just open). Full screen places them all.
        public int Places(int apps) { return Maximize ? apps : Math.Min(apps, Zones.Length); }

        public Rect? ZoneFor(int index) { return Maximize ? Zones[0] : index < Zones.Length ? Zones[index] : (Rect?)null; }
    }

    static class WorkspaceIcons
    {
        // Simple Segoe Fluent icons, so a workspace button can say what it's for without a name.
        public static readonly List<KeyValuePair<string, string>> All = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("", "Work"), new KeyValuePair<string, string>("", "Code"),
            new KeyValuePair<string, string>("", "Games"), new KeyValuePair<string, string>("", "Music"),
            new KeyValuePair<string, string>("", "Listening"), new KeyValuePair<string, string>("", "Video"),
            new KeyValuePair<string, string>("", "Photos"), new KeyValuePair<string, string>("", "Design"),
            new KeyValuePair<string, string>("", "Chat"), new KeyValuePair<string, string>("", "Mail"),
            new KeyValuePair<string, string>("", "People"), new KeyValuePair<string, string>("", "Web"),
            new KeyValuePair<string, string>("", "Documents"), new KeyValuePair<string, string>("", "Study"),
            new KeyValuePair<string, string>("", "Calendar"), new KeyValuePair<string, string>("", "Home"),
            new KeyValuePair<string, string>("", "Favorites"), new KeyValuePair<string, string>("", "Quick"),
            new KeyValuePair<string, string>("", "Screen"), new KeyValuePair<string, string>("", "Night"),
            new KeyValuePair<string, string>("", "Day"), new KeyValuePair<string, string>("", "Goals"),
        };

        public static string Default { get { return All[0].Key; } }
    }

    // Opens a workspace's apps all at once and puts each window in its place as soon as it shows up. Apps that are
    // already open are moved instead of opened again. It only looks at windows for a few seconds after you click, never otherwise.
    class WorkspaceRun
    {
        const int GiveUpAfter = 20000;   // ms: a window that hasn't shown up by then is left alone
        const int AcceptAnyAfter = 3000; // ms: then a new window from an unexpected program may stand in (some apps start through a launcher)
        const int HoldFor = 3000;        // ms: windows that move or resize themselves right after appearing are put back

        class Item
        {
            public GroupApp App; public Rect? Zone; public string Exe, Aumid; public IntPtr Window;
            public Item After;                 // an earlier copy of the same app: this one opens once that one's window is in
            public bool Launched; public DateTime LaunchedAt;
            public string Key { get { return Exe ?? Aumid; } }
        }
        class Held { public IntPtr Window; public Rect Zone; public DateTime Until; public int Fixes; }

        readonly LayoutPreset layout;
        readonly ScreenInfo screen;
        readonly List<Item> items;
        readonly HashSet<IntPtr> before, used = new HashSet<IntPtr>();
        readonly List<Held> held = new List<Held>();
        readonly DispatcherTimer poll;
        DateTime started;
        Action done;

        public WorkspaceRun(Group g)
        {
            layout = LayoutPreset.Find(g.Data.Layout);
            screen = Screens.Find(g.Data.Screen);
            items = g.Data.Apps.Select((a, i) => new Item { App = a, Zone = screen == null ? null : layout.ZoneFor(i) }).ToList();
            before = new HashSet<IntPtr>(OtherWindows.AppWindows());
            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            poll.Tick += (s, e) => Tick();
        }

        public void Start(Action finished)
        {
            done = finished;
            started = DateTime.UtcNow;
            foreach (var it in items) Identity(it.App, out it.Exe, out it.Aumid);

            // Already open? Move that window instead of opening another.
            var open = OtherWindows.AppWindows();
            foreach (var it in items.Where(i => i.Zone != null))
            {
                var w = open.FirstOrDefault(h => !used.Contains(h) && OtherWindows.Matches(h, it.Exe, it.Aumid));
                if (w != IntPtr.Zero) Claim(it, w);
            }

            // Everything else opens at once. A second copy of the same app waits for the first one's window,
            // so the copies land in the order you set.
            var pending = items.Where(i => i.Window == IntPtr.Zero).ToList();
            for (int k = 0; k < pending.Count; k++)
                if (pending[k].Key != null)
                    pending[k].After = pending.Take(k).LastOrDefault(p => string.Equals(p.Key, pending[k].Key, StringComparison.OrdinalIgnoreCase));
            Launch(pending.Where(i => i.After == null).ToList());

            poll.Start();
            Tick();
        }

        // Opens apps in order, off the UI thread so the widget keeps animating.
        void Launch(List<Item> list)
        {
            if (list.Count == 0) return;
            var now = DateTime.UtcNow;
            foreach (var it in list) { it.Launched = true; it.LaunchedAt = now; }
            var t = new System.Threading.Thread(() => { foreach (var it in list) Launcher.Launch(it.App, false); });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        void Tick()
        {
            var now = DateTime.UtcNow;
            double waited = (now - started).TotalMilliseconds;
            bool waiting = items.Any(i => i.Window == IntPtr.Zero) && waited < GiveUpAfter;
            if (waiting)
            {
                Launch(items.Where(i => !i.Launched && i.Window == IntPtr.Zero && i.After != null && i.After.Launched
                    && (i.After.Window != IntPtr.Zero || (now - i.After.LaunchedAt).TotalMilliseconds > 4000)).ToList());
                var fresh = OtherWindows.AppWindows().Where(w => !before.Contains(w) && !used.Contains(w)).ToList();
                // Each new window goes to the first app still waiting that runs the program it belongs to.
                foreach (var w in fresh)
                {
                    var it = items.FirstOrDefault(i => i.Launched && i.Window == IntPtr.Zero && OtherWindows.Matches(w, i.Exe, i.Aumid));
                    if (it != null) Claim(it, w);
                }
                // After a moment, a new window that no app here runs goes to the first app with a place that still has nothing.
                if (waited > AcceptAnyAfter)
                    foreach (var w in fresh.Where(w => !used.Contains(w)))
                    {
                        var it = items.FirstOrDefault(i => i.Launched && i.Window == IntPtr.Zero && i.Zone != null);
                        if (it != null) Claim(it, w);
                    }
            }

            // Some apps move or resize themselves just after they appear (restoring where they were last time, or
            // adjusting to the screen's scale); put them back, a few times at most.
            foreach (var h in held.ToList())
            {
                if (now > h.Until || !Native.IsWindow(h.Window)) { held.Remove(h); continue; }
                if (h.Fixes < 6 && !OtherWindows.IsAt(h.Window, h.Zone, layout.Maximize))
                {
                    h.Fixes++;
                    OtherWindows.Place(h.Window, h.Zone, layout.Maximize, screen);
                }
            }

            // Done when every app with a place has its window (apps without one just open).
            bool placing = items.Any(i => i.Zone != null && i.Window == IntPtr.Zero) && waited < GiveUpAfter;
            if (!placing && done != null) { var d = done; done = null; d(); }
            if (!placing && held.Count == 0) poll.Stop();
        }

        void Claim(Item it, IntPtr w)
        {
            it.Window = w;
            used.Add(w);
            if (it.Zone == null) return;
            var px = ToPixels(it.Zone.Value);
            OtherWindows.Place(w, px, layout.Maximize, screen);
            held.Add(new Held { Window = w, Zone = px, Until = DateTime.UtcNow.AddMilliseconds(HoldFor) });
        }

        Rect ToPixels(Rect z)
        {
            double l = screen.Work.Left, t = screen.Work.Top, w = screen.Work.Right - screen.Work.Left, h = screen.Work.Bottom - screen.Work.Top;
            double x1 = Math.Round(l + z.Left * w), y1 = Math.Round(t + z.Top * h), x2 = Math.Round(l + z.Right * w), y2 = Math.Round(t + z.Bottom * h);
            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }

        // What program a group item opens, so its window can be recognized.
        static void Identity(GroupApp a, out string exe, out string aumid)
        {
            exe = aumid = null;
            if (a.Kind == "aumid") { aumid = a.Target; return; }
            string target = a.Kind == "link" ? ShortcutTarget(AppIcons.PathFor(a)) : a.Target;
            if (!string.IsNullOrEmpty(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = target;
        }

        static string ShortcutTarget(string lnk)
        {
            try
            {
                if (!lnk.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                try
                {
                    object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                    try { return sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, sc, null) as string; }
                    finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(sc); }
                }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
            }
            catch { return null; }
        }
    }

    // Reading and moving other apps' main windows (only while a workspace is opening).
    static class OtherWindows
    {
        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        // Visible, top-level app windows: the ones you'd see in Alt+Tab.
        public static List<IntPtr> AppWindows()
        {
            var list = new List<IntPtr>();
            Native.EnumWindows((h, l) =>
            {
                if (IsAppWindow(h)) list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static bool IsAppWindow(IntPtr h)
        {
            if (!Native.IsWindowVisible(h) || Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
            if (Native.GetWindowTextLength(h) == 0) return false;
            long ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 || (ex & Native.WS_EX_NOACTIVATE) != 0) return false;
            int cloaked;
            if (Native.DwmGetWindowAttributeInt(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return false;
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            if (pid == myPid) return false;
            var cls = new StringBuilder(64);
            Native.GetClassName(h, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd") return false;
            Native.RECT r;
            // Skip splash screens and other small windows.
            if (!Native.IsIconic(h) && (!Native.GetWindowRect(h, out r) || r.Right - r.Left < 320 || r.Bottom - r.Top < 200)) return false;
            return true;
        }

        public static bool Matches(IntPtr h, string exe, string aumid)
        {
            if (exe == null && aumid == null) return false;
            if (aumid != null)
            {
                string id = WindowAumid(h);
                if (id != null && string.Equals(id, aumid, StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (exe != null)
            {
                string path = ProcessPath(h);
                if (path != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static string ProcessPath(IntPtr h)
        {
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            IntPtr p = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (p == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(p, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(p); }
        }

        // Store apps (and apps that set one) carry an app ID on their window or process.
        static string WindowAumid(IntPtr h)
        {
            try
            {
                var iid = new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
                Native.IPropertyStore store;
                if (Native.SHGetPropertyStoreForWindow(h, ref iid, out store) == 0 && store != null)
                {
                    try
                    {
                        string id = Native.ReadString(store, Native.PKEY_AppUserModel_ID);
                        if (!string.IsNullOrEmpty(id)) return id;
                    }
                    finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(store); }
                }
            }
            catch { }
            uint pid;
            Native.GetWindowThreadProcessId(h, out pid);
            IntPtr p = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (p == IntPtr.Zero) return null;
            try
            {
                int len = 256;
                var sb = new StringBuilder(len);
                return Native.GetApplicationUserModelId(p, ref len, sb) == 0 ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(p); }
        }

        // Whether a window is where Place put it (within a few pixels), judged by its visible frame.
        public static bool IsAt(IntPtr h, Rect zone, bool maximize)
        {
            if (maximize) return Native.IsZoomed(h);
            Native.RECT fr;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out fr, 16) != 0) return true;
            return Math.Abs(fr.Left - zone.Left) <= 3 && Math.Abs(fr.Top - zone.Top) <= 3
                && Math.Abs(fr.Right - zone.Right) <= 3 && Math.Abs(fr.Bottom - zone.Bottom) <= 3;
        }

        // Puts a window exactly in a zone (screen pixels), allowing for the invisible resize border most windows have.
        public static void Place(IntPtr h, Rect? zone, bool maximize, ScreenInfo screen)
        {
            if (!Native.IsWindow(h)) return;
            if (Native.IsIconic(h) || (Native.IsZoomed(h) && !maximize)) Native.ShowWindow(h, Native.SW_RESTORE);
            if (maximize && screen != null)
            {
                // Move onto the right screen first, so it maximizes there.
                if (!Native.IsZoomed(h))
                    Native.SetWindowPos(h, IntPtr.Zero, screen.Work.Left + 40, screen.Work.Top + 40, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                Native.ShowWindow(h, Native.SW_MAXIMIZE);
                return;
            }
            if (!zone.HasValue) return;
            var z = zone.Value;
            Native.RECT wr, fr;
            int l = 0, t = 0, r = 0, b = 0;
            if (Native.GetWindowRect(h, out wr) && Native.DwmGetWindowAttribute(h, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out fr, 16) == 0)
            {
                l = fr.Left - wr.Left; t = fr.Top - wr.Top; r = wr.Right - fr.Right; b = wr.Bottom - fr.Bottom;
            }
            Native.SetWindowPos(h, IntPtr.Zero, (int)z.X - l, (int)z.Y - t, (int)z.Width + l + r, (int)z.Height + t + b, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }
    }
}
