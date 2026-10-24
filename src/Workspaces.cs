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

    // Opens a workspace's apps one after another and puts each window in its place. Apps that are already open
    // are moved instead of opened again. It only looks at windows for a few seconds after you click, never otherwise.
    class WorkspaceRun
    {
        const int WaitPerApp = 9000, AcceptAnyAfter = 2500;

        readonly Group group;
        readonly LayoutPreset layout;
        readonly ScreenInfo screen;
        readonly List<GroupApp> apps;
        readonly HashSet<IntPtr> before, used = new HashSet<IntPtr>();
        readonly DispatcherTimer poll;
        readonly List<Tuple<IntPtr, Rect?, DateTime>> settle = new List<Tuple<IntPtr, Rect?, DateTime>>();
        Action done;
        int index = -1;
        DateTime stepStart;
        string wantExe, wantAumid;

        public WorkspaceRun(Group g)
        {
            group = g;
            layout = LayoutPreset.Find(g.Data.Layout);
            screen = Screens.Find(g.Data.Screen);
            apps = g.Data.Apps.ToList();
            before = new HashSet<IntPtr>(OtherWindows.AppWindows());
            poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            poll.Tick += (s, e) => Tick();
        }

        public void Start(Action finished)
        {
            done = finished;
            poll.Start();
            Next();
        }

        void Next()
        {
            index++;
            if (index >= apps.Count) { stepStart = DateTime.MinValue; return; }
            var a = apps[index];
            var zone = layout.ZoneFor(index);
            if (zone == null || screen == null)
            {
                Launcher.Launch(a, false);
                Next();
                return;
            }
            Identity(a, out wantExe, out wantAumid);
            // Already open? Move that window instead of opening another.
            var open = OtherWindows.AppWindows().FirstOrDefault(w => !used.Contains(w) && OtherWindows.Matches(w, wantExe, wantAumid));
            if (open != IntPtr.Zero)
            {
                Claim(open, zone);
                Next();
                return;
            }
            Launcher.Launch(a, false);
            stepStart = DateTime.UtcNow;
        }

        void Tick()
        {
            // Some apps resize themselves just after they appear, so each window is put in place a few times.
            var now = DateTime.UtcNow;
            foreach (var s in settle.ToList())
            {
                if (now < s.Item3) continue;
                settle.Remove(s);
                OtherWindows.Place(s.Item1, s.Item2, layout.Maximize, screen);
            }

            if (index < apps.Count && stepStart != DateTime.MinValue)
            {
                double waited = (now - stepStart).TotalMilliseconds;
                var fresh = OtherWindows.AppWindows().Where(w => !before.Contains(w) && !used.Contains(w)).ToList();
                var mine = fresh.FirstOrDefault(w => OtherWindows.Matches(w, wantExe, wantAumid));
                // Launchers sometimes open a window from a different program; take the first new window after a moment.
                if (mine == IntPtr.Zero && waited > AcceptAnyAfter) mine = fresh.FirstOrDefault();
                if (mine != IntPtr.Zero)
                {
                    stepStart = DateTime.MinValue;
                    Claim(mine, layout.ZoneFor(index));
                    Next();
                }
                else if (waited > WaitPerApp)
                {
                    stepStart = DateTime.MinValue;
                    Next();
                }
            }

            if (index >= apps.Count && settle.Count == 0)
            {
                poll.Stop();
                if (done != null) done();
            }
        }

        void Claim(IntPtr w, Rect? zone)
        {
            used.Add(w);
            before.Add(w);
            var px = zone.HasValue ? ToPixels(zone.Value) : (Rect?)null;
            OtherWindows.Place(w, px, layout.Maximize, screen);
            var now = DateTime.UtcNow;
            settle.Add(Tuple.Create(w, px, now.AddMilliseconds(700)));
            settle.Add(Tuple.Create(w, px, now.AddMilliseconds(1800)));
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
