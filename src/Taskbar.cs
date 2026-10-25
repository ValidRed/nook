using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinGroups
{
    // ---- sound: which speakers play, and every microphone's mute ----
    static class AudioDevices
    {
        public class Output
        {
            public string Id, Name, Short;
            public bool Headphones;
        }

        static Native.IMMDeviceEnumerator Enumerator() { return (Native.IMMDeviceEnumerator)new Native.MMDeviceEnumeratorClass(); }

        // The devices of one kind (speakers or microphones) that are plugged in and working.
        static List<Native.IMMDevice> Active(Native.IMMDeviceEnumerator en, int flow)
        {
            var list = new List<Native.IMMDevice>();
            IntPtr raw;
            if (en.EnumAudioEndpoints(flow, Native.DEVICE_STATE_ACTIVE, out raw) != 0 || raw == IntPtr.Zero) return list;
            var col = (Native.IMMDeviceCollection)Marshal.GetObjectForIUnknown(raw);
            Marshal.Release(raw);
            try
            {
                uint n;
                if (col.GetCount(out n) != 0) return list;
                for (uint i = 0; i < n; i++)
                {
                    Native.IMMDevice d;
                    if (col.Item(i, out d) == 0 && d != null) list.Add(d);
                }
            }
            finally { Marshal.ReleaseComObject(col); }
            return list;
        }

        // The speakers Windows plays through right now.
        public static string CurrentOutput()
        {
            Native.IMMDeviceEnumerator en = null;
            try
            {
                en = Enumerator();
                Native.IMMDevice def;
                if (en.GetDefaultAudioEndpoint(Native.eRender, Native.eMultimedia, out def) != 0 || def == null) return null;
                try { string id; def.GetId(out id); return id; }
                finally { Marshal.ReleaseComObject(def); }
            }
            catch { return null; }
            finally { if (en != null) Marshal.ReleaseComObject(en); }
        }

        // Every speaker and pair of headphones Windows can play through right now.
        public static List<Output> Outputs()
        {
            var outs = new List<Output>();
            Native.IMMDeviceEnumerator en = null;
            try
            {
                en = Enumerator();
                foreach (var d in Active(en, Native.eRender))
                {
                    try
                    {
                        string id;
                        d.GetId(out id);
                        string name = null, desc = null;
                        uint? form = null;
                        Native.IPropertyStore ps;
                        if (d.OpenPropertyStore(0, out ps) == 0 && ps != null)
                        {
                            name = Native.ReadString(ps, Native.PKEY_Device_FriendlyName);
                            desc = Native.ReadString(ps, Native.PKEY_Device_DeviceDesc);
                            form = Native.ReadUInt(ps, Native.PKEY_AudioEndpoint_FormFactor);
                            Marshal.ReleaseComObject(ps);
                        }
                        // 3 = headphones, 5 = a headset
                        outs.Add(new Output { Id = id, Name = name ?? desc ?? "Speakers", Short = desc ?? name ?? "Speakers", Headphones = form == 3 || form == 5 });
                    }
                    finally { Marshal.ReleaseComObject(d); }
                }
            }
            catch { }
            finally { if (en != null) Marshal.ReleaseComObject(en); }
            // Two with the same short name ("Speakers") go by their full names instead.
            foreach (var o in outs) if (outs.Count(x => x.Short == o.Short) > 1) o.Short = o.Name;
            return outs;
        }

        // Makes it the speakers Windows plays everything through: apps, media and calls alike.
        public static bool SetOutput(string id)
        {
            object o = null;
            try
            {
                o = new Native.PolicyConfigClass();
                var pc = (Native.IPolicyConfig)o;
                bool ok = true;
                foreach (int role in new[] { Native.eConsole, Native.eMultimedia, Native.eCommunications })
                    ok &= pc.SetDefaultEndpoint(id, role) == 0;
                return ok;
            }
            catch { return false; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        // Each working microphone's volume control.
        static void EachMic(Action<Native.IAudioEndpointVolume> act)
        {
            Native.IMMDeviceEnumerator en = null;
            try
            {
                en = Enumerator();
                foreach (var d in Active(en, Native.eCapture))
                {
                    try
                    {
                        object o;
                        var iid = Native.IID_IAudioEndpointVolume;
                        if (d.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out o) != 0 || o == null) continue;
                        try { act((Native.IAudioEndpointVolume)o); }
                        finally { Marshal.ReleaseComObject(o); }
                    }
                    finally { Marshal.ReleaseComObject(d); }
                }
            }
            catch { }
            finally { if (en != null) Marshal.ReleaseComObject(en); }
        }

        // True when every microphone is muted; null when there's no microphone.
        public static bool? MicsMuted()
        {
            int mics = 0, muted = 0;
            EachMic(v => { bool m; if (v.GetMute(out m) == 0) { mics++; if (m) muted++; } });
            return mics == 0 ? (bool?)null : muted == mics;
        }

        static Guid context = Guid.NewGuid();

        public static void MuteMics(bool mute)
        {
            EachMic(v => v.SetMute(mute, ref context));
        }
    }

    // ---- where on the taskbar there's room ----
    // Read from the taskbar's own buttons with UI Automation, the accessibility interface (only read, never changed):
    // with icons in the middle the room is on the left; with icons on the left it's beside the tray.
    static class TaskbarSpot
    {
        public class Room
        {
            public Native.RECT Bar;
            public double Left, Right;   // screen pixels
            public bool AtRight;
            public double Scale;
            public bool AutoHide;
        }

        public static IntPtr Handle { get { return Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null); } }

        public static Room Find(string side)
        {
            var tray = Handle;
            Native.RECT bar;
            if (tray == IntPtr.Zero || !Native.GetWindowRect(tray, out bar) || bar.Right <= bar.Left) return null;
            uint dpi = Native.GetDpiForWindow(tray);
            double s = dpi > 0 ? dpi / 96.0 : 1;
            var room = new Room { Bar = bar, Scale = s };
            var abd = new Native.APPBARDATA { cbSize = Marshal.SizeOf(typeof(Native.APPBARDATA)), hWnd = tray };
            room.AutoHide = (Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd) & Native.ABS_AUTOHIDE) != 0;

            double appsLeft = double.MaxValue, appsRight = double.MinValue, trayLeft = double.MaxValue, widgetsRight = bar.Left;
            try
            {
                foreach (AutomationElement e in AutomationElement.FromHandle(tray).FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition))
                {
                    var c = e.Current;
                    var r = c.BoundingRectangle;
                    if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                    string cls = c.ClassName ?? "", id = c.AutomationId ?? "";
                    if (cls.StartsWith("SystemTray")) trayLeft = Math.Min(trayLeft, r.Left);
                    else if (id == "WidgetsButton") widgetsRight = Math.Max(widgetsRight, r.Right);
                    else if (id == "StartButton" || id.StartsWith("Search") || id == "TaskViewButton" || cls.Contains("TaskListButton"))
                    {
                        appsLeft = Math.Min(appsLeft, r.Left);
                        appsRight = Math.Max(appsRight, r.Right);
                    }
                }
            }
            catch { }
            var notify = Native.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            Native.RECT nr;
            if (notify != IntPtr.Zero && Native.GetWindowRect(notify, out nr) && nr.Right > nr.Left) trayLeft = Math.Min(trayLeft, nr.Left);
            if (trayLeft == double.MaxValue) trayLeft = bar.Right - 220 * s;
            if (appsLeft == double.MaxValue) appsLeft = appsRight = bar.Left + (bar.Right - bar.Left) / 2.0;

            bool right = AtRight(side);
            room.AtRight = right;
            if (right) { room.Left = appsRight + 16 * s; room.Right = trayLeft - 8 * s; }
            else { room.Left = widgetsRight + 8 * s; room.Right = appsLeft - 16 * s; }
            return room;
        }

        // Nook's buttons go beside the tray when asked, or when the taskbar's icons are on the left (the room is on the right).
        public static bool AtRight(string side) { return side == "right" || (side != "left" && !Centered()); }

        // Icons in the middle (Windows 11's default) or on the left.
        public static bool Centered()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    var v = k == null ? null : k.GetValue("TaskbarAl");
                    return !(v is int) || (int)v != 0;
                }
            }
            catch { return true; }
        }

        // Is the taskbar light (Windows' light mode for the taskbar and Start)?
        public static bool Light()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        // Windows' own "Transparency effects": what makes the taskbar (and Start) see-through.
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        public static bool SeeThrough
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                    {
                        var v = k == null ? null : k.GetValue("EnableTransparency");
                        return !(v is int) || (int)v != 0;
                    }
                }
                catch { return true; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(PersonalizeKey)) k.SetValue("EnableTransparency", value ? 1 : 0, RegistryValueKind.DWord);
                    // Tells Windows the color settings changed, the same way its own Settings app does.
                    IntPtr result;
                    Native.SendMessageTimeout(Native.HWND_BROADCAST, Native.WM_SETTINGCHANGE, IntPtr.Zero, "ImmersiveColorSet", Native.SMTO_ABORTIFHUNG, 2000, out result);
                }
                catch { }
            }
        }
    }

    // One of Nook's buttons on the taskbar: a group (its first letter) or a workspace (its icon), white on frosted glass.
    public class TaskbarItemVM : Observable
    {
        internal Group Group;
        bool busy;
        public string Name { get; set; }
        public bool IsGroup { get; set; }
        public bool IsWorkspace { get { return !IsGroup; } }
        public string Letter { get; set; }
        public string Glyph { get; set; }
        public bool IsBusy { get { return busy; } set { Set(ref busy, value); } }
    }

    // A speaker or pair of headphones, in the sound button's right-click menu.
    public class SoundChoiceVM
    {
        public string Name { get; set; }
        public bool IsCurrent { get; set; }
        public string Mark { get { return IsCurrent ? "" : ""; } } // a check by the one in use
        public ICommand Command { get; set; }
    }

    public class TaskbarVM : Observable
    {
        readonly App app;
        readonly List<TaskbarItemVM> allGroups = new List<TaskbarItemVM>(), allSpaces = new List<TaskbarItemVM>();
        public ObservableCollection<TaskbarItemVM> Groups { get; private set; }
        public ObservableCollection<TaskbarItemVM> Spaces { get; private set; }
        public ObservableCollection<SoundChoiceVM> Outputs { get; private set; }

        public TaskbarVM(App app)
        {
            this.app = app;
            Groups = new ObservableCollection<TaskbarItemVM>();
            Spaces = new ObservableCollection<TaskbarItemVM>();
            Outputs = new ObservableCollection<SoundChoiceVM>();
            SoundCommand = new Relay(() => NextOutput());
            MicCommand = new Relay(() => ToggleMic());
            SettingsCommand = new Relay(() => app.OpenTaskbarSettings());
            HideCommand = new Relay(() => app.SetTaskbarOn(false));
            Refresh();
        }

        public ICommand SettingsCommand { get; private set; }
        public ICommand HideCommand { get; private set; }

        // A workspace's button pulses while it lays the workspace out.
        public void SetBusy(string path, bool busy)
        {
            foreach (var w in allSpaces) if (string.Equals(w.Group.Path, path, StringComparison.OrdinalIgnoreCase)) w.IsBusy = busy;
        }

        // ---- what's shown ----
        int room = int.MaxValue; // how many group and workspace buttons fit

        public bool ShowSound { get { return app.Settings.TaskbarSound; } }
        public bool ShowMic { get { return app.Settings.TaskbarMic; } }
        public bool ShowGroups { get { return Groups.Count > 0; } }
        public bool ShowSpaces { get { return Spaces.Count > 0; } }
        // A thin line between the kinds of buttons.
        public bool LineAfterGroups { get { return ShowGroups && (ShowSpaces || ShowSound || ShowMic); } }
        public bool LineAfterSpaces { get { return ShowSpaces && (ShowSound || ShowMic); } }

        public void Refresh()
        {
            allGroups.Clear();
            allSpaces.Clear();
            foreach (var g in app.Store.Groups)
            {
                if (g.IsWorkspace)
                {
                    if (!app.Settings.TaskbarWorkspaces) continue;
                    allSpaces.Add(new TaskbarItemVM { Group = g, IsGroup = false, Name = string.IsNullOrEmpty(g.Name) ? "Workspace" : g.Name, Glyph = string.IsNullOrEmpty(g.Data.Icon) ? WorkspaceIcons.Default : g.Data.Icon });
                }
                else if (app.Settings.TaskbarGroups)
                {
                    string name = (g.Name ?? "").Trim();
                    allGroups.Add(new TaskbarItemVM { Group = g, IsGroup = true, Name = name.Length == 0 ? "Group" : name, Letter = name.Length == 0 ? "G" : char.ToUpper(name[0]).ToString() });
                }
            }
            Fill();
        }

        // Only as many as fit in the room the taskbar has (groups first).
        public void Fit(int buttons)
        {
            if (buttons == room) return;
            room = buttons;
            Fill();
        }

        public int Wanted { get { return allGroups.Count + allSpaces.Count; } }

        void Fill()
        {
            Groups.Clear();
            Spaces.Clear();
            int left = room;
            foreach (var g in allGroups) if (left-- > 0) Groups.Add(g);
            foreach (var w in allSpaces) if (left-- > 0) Spaces.Add(w);
            foreach (var p in new[] { "ShowGroups", "ShowSpaces", "ShowSound", "ShowMic", "LineAfterGroups", "LineAfterSpaces" }) Raise(p);
        }

        // ---- sound output ----
        string current;
        bool headphones;
        string outputName = "Speakers";
        public string SoundGlyph { get { return headphones ? "" : ""; } }
        public string SoundTip { get { return outputName + "\nClick to switch, or right-click to pick"; } }
        public ICommand SoundCommand { get; private set; }
        public Action<string, bool> Switched; // the strip shows where sound now goes

        // Rereads which speakers are in use (it can change in Windows' own settings too).
        public void ReadSound(bool all)
        {
            if (!ShowSound) return;
            string id = AudioDevices.CurrentOutput();
            if (!all && id == current) return;
            current = id;
            var outs = AudioDevices.Outputs();
            var now = outs.FirstOrDefault(o => o.Id == id);
            headphones = now != null && now.Headphones;
            outputName = now != null ? now.Name : "No speakers";
            Outputs.Clear();
            foreach (var o in outs)
            {
                var choice = o;
                Outputs.Add(new SoundChoiceVM { Name = o.Short, IsCurrent = o.Id == id, Command = new Relay(() => Choose(choice)) });
            }
            Raise("SoundGlyph"); Raise("SoundTip");
        }

        // One click: the next speakers or headphones in the list (with two, that's a swap).
        void NextOutput()
        {
            var outs = AudioDevices.Outputs();
            if (outs.Count < 2) { ReadSound(true); if (Switched != null) Switched(outs.Count == 1 ? "Only " + outs[0].Short : "No speakers", headphones); return; }
            int i = outs.FindIndex(o => o.Id == current);
            Choose(outs[(i + 1) % outs.Count]);
        }

        void Choose(AudioDevices.Output o)
        {
            AudioDevices.SetOutput(o.Id);
            ReadSound(true);
            if (Switched != null) Switched(o.Short, o.Headphones);
        }

        // ---- microphones ----
        bool? micMuted;
        public bool MicMuted { get { return micMuted == true; } }
        public bool MicLive { get { return micMuted == false; } }
        public bool HasMic { get { return micMuted.HasValue; } }
        public string MicTip
        {
            get
            {
                return !HasMic ? "No microphone found" : MicMuted ? "Microphones muted. Click to turn them back on." : "Microphone on. Click to mute every microphone.";
            }
        }
        public ICommand MicCommand { get; private set; }

        public void ReadMic()
        {
            if (!app.Settings.TaskbarMic) return;
            var m = AudioDevices.MicsMuted();
            if (m == micMuted) return;
            bool had = micMuted.HasValue;
            micMuted = m;
            Raise("MicMuted"); Raise("MicLive"); Raise("MicTip");
            if (had != m.HasValue) Raise("HasMic");
        }

        // --dev-taskbar on a PC with no microphone: the muted look, without touching anything.
        internal void PretendMic(bool? muted)
        {
            micMuted = muted;
            Raise("MicMuted"); Raise("MicLive"); Raise("MicTip"); Raise("HasMic");
        }

        void ToggleMic()
        {
            if (!HasMic) { ReadMic(); return; }
            AudioDevices.MuteMics(!MicMuted);
            micMuted = null; // read it back fresh
            ReadMic();
        }
    }

    // Nook's buttons on the taskbar. The strip belongs to the taskbar (it's "owned" by it), so it always sits on top
    // of it; it's clear except for its buttons, so clicks anywhere else go to the taskbar as usual. Nothing about the
    // taskbar itself is changed.
    class TaskbarStrip : Window
    {
        readonly App app;
        readonly TaskbarVM vm;
        readonly FrameworkElement root;
        readonly DispatcherTimer watch, sense, flashOff;
        IntPtr hwnd;
        bool placing;
        double scale = 1;
        public bool Leaving; // closed on purpose (not because Explorer restarted)

        public TaskbarStrip(App app)
        {
            this.app = app;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Title = "Nook on the taskbar";
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            Width = 10;
            Height = 48;
            vm = new TaskbarVM(app);
            DataContext = vm;
            root = (FrameworkElement)Xaml.Load("Taskbar.xaml");
            Content = root;
            ApplyGlass(root);
            AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
            vm.Switched = Flash;

            // The taskbar's icons come and go (and so does its free space), so its layout is looked at now and then.
            watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            watch.Tick += (s, e) => Place();
            // Speakers and microphones can change in Windows' own settings too.
            sense = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            sense.Tick += (s, e) => { vm.ReadSound(false); vm.ReadMic(); };
            flashOff = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            flashOff.Tick += (s, e) => { flashOff.Stop(); var f = root.FindName("Flash") as Popup; if (f != null) f.IsOpen = false; };

            SourceInitialized += (s, e) =>
            {
                hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) & ~Native.WS_EX_APPWINDOW;
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
                HwndSource.FromHwnd(hwnd).AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
                {
                    if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(Native.MA_NOACTIVATE); }
                    return IntPtr.Zero;
                });
                // Owned by the taskbar: always just above it, never under it.
                var tray = TaskbarSpot.Handle;
                if (tray != IntPtr.Zero) Native.SetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT, tray);
                Place();
            };
            Loaded += (s, e) =>
            {
                vm.ReadSound(true);
                vm.ReadMic();
                watch.Start();
                sense.Start();
                Place();
            };
            Closed += (s, e) => { watch.Stop(); sense.Stop(); flashOff.Stop(); };
        }

        internal TaskbarVM Model { get { return vm; } }
        internal Rect Placed { get; private set; } // for --dev-taskbar

        internal Rect ScreenRectOf(string name)
        {
            var e = root.FindName(name) as FrameworkElement;
            if (e == null || !e.IsVisible) return Rect.Empty;
            var tl = e.PointToScreen(new Point(0, 0));
            var br = e.PointToScreen(new Point(e.ActualWidth, e.ActualHeight));
            return new Rect(tl, br);
        }

        // Where the "now playing on" bubble is on screen (for --dev-taskbar).
        internal Rect FlashRect()
        {
            var f = root.FindName("Flash") as Popup;
            var src = f == null || f.Child == null ? null : PresentationSource.FromVisual(f.Child) as HwndSource;
            Native.RECT r;
            return src != null && Native.GetWindowRect(src.Handle, out r) ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : Rect.Empty;
        }

        public void Refresh()
        {
            vm.Refresh();
            ApplyGlass(root);
            Place();
        }

        public void SetBusy(string path, bool busy) { vm.SetBusy(path, busy); }

        // The same buttons on the small taskbar in Nook's Taskbar page (just to look at).
        internal static FrameworkElement Preview(App app, bool? light = null)
        {
            var vm = new TaskbarVM(app);
            vm.ReadSound(true);
            vm.ReadMic();
            vm.Fit(8);
            var root = (FrameworkElement)Xaml.Load("Taskbar.xaml");
            root.DataContext = vm;
            root.ContextMenu = null;
            root.IsHitTestVisible = false;
            root.Focusable = false;
            ApplyGlass(root, light);
            return root;
        }

        // The icons are always white. On a dark taskbar the glass is clear and pale; on a light one it's smoky, so white still reads.
        static void ApplyGlass(FrameworkElement root, bool? lightBar = null)
        {
            bool light = lightBar ?? TaskbarSpot.Light();
            root.Resources["GlassFill"] = Gradient(light ? Color.FromArgb(0x8C, 0x3A, 0x3A, 0x40) : Color.FromArgb(0x3D, 0xFF, 0xFF, 0xFF),
                                                   light ? Color.FromArgb(0xB3, 0x1E, 0x1E, 0x24) : Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
            root.Resources["GlassEdge"] = Gradient(light ? Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF),
                                                   light ? Color.FromArgb(0x26, 0, 0, 0) : Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            root.Resources["TaskLine"] = WidgetColors.Frozen(light ? Color.FromArgb(0x33, 0, 0, 0) : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        }

        static Brush Gradient(Color top, Color bottom)
        {
            var b = new LinearGradientBrush(top, bottom, 90);
            b.Freeze();
            return b;
        }

        // Finds the taskbar's free stretch (off the UI thread: it asks Explorer), then sits in it.
        void Place()
        {
            if (hwnd == IntPtr.Zero || placing) return;
            // A new taskbar (after Explorer restarts) takes the strip over.
            var tray = TaskbarSpot.Handle;
            if (tray != IntPtr.Zero && Native.GetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT) != tray) Native.SetWindowLongPtr(hwnd, Native.GWLP_HWNDPARENT, tray);
            placing = true;
            string side = app.Settings.TaskbarSide;
            System.Threading.Tasks.Task.Run(() => TaskbarSpot.Find(side)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                placing = false;
                Apply(t.IsFaulted ? null : t.Result);
            })));
        }

        void Apply(TaskbarSpot.Room room)
        {
            if (hwnd == IntPtr.Zero) return;
            // Out of the way when the taskbar hides itself, or a full-screen game, video or presentation is in front.
            if (room == null || room.AutoHide || FullScreen())
            {
                if (IsVisible) Native.ShowWindow(hwnd, Native.SW_HIDE);
                return;
            }
            scale = room.Scale;
            double barH = room.Bar.Bottom - room.Bar.Top;
            double space = (room.Right - room.Left) / scale;
            // Fixed pieces first, then as many group and workspace buttons as fit.
            double fixedW = 8 + (vm.ShowSound ? 42 : 0) + (vm.ShowMic ? 42 : 0) + 26;
            vm.Fit((int)Math.Max(0, Math.Floor((space - fixedW) / 42)));
            root.Measure(new Size(double.PositiveInfinity, barH / scale));
            double w = Math.Ceiling(root.DesiredSize.Width);
            if (w < 2 || space < 40) { if (IsVisible) Native.ShowWindow(hwnd, Native.SW_HIDE); return; }
            int pw = (int)Math.Ceiling(w * scale);
            int x = room.AtRight ? (int)(room.Right - pw) : (int)room.Left;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, room.Bar.Top, pw, (int)barH, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
            Placed = new Rect(x, room.Bar.Top, pw, barH);
            Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        }

        static bool FullScreen()
        {
            int state;
            // 2 = a full-screen app, 3 = a full-screen game, 4 = presentation mode
            return Native.SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4);
        }

        // A group pops open above its button; a workspace lays itself out.
        void OnClick(object sender, RoutedEventArgs e)
        {
            var b = e.OriginalSource as FrameworkElement;
            var item = b == null ? null : b.DataContext as TaskbarItemVM;
            if (item == null || item.Group == null) return;
            var tl = b.PointToScreen(new Point(0, 0));
            app.OpenFromTaskbar(item.Group, new Rect(tl.X, Placed.Top, b.ActualWidth * scale, Placed.Height));
        }

        // Where sound goes now, for a moment above the sound button.
        void Flash(string text, bool toHeadphones)
        {
            var f = root.FindName("Flash") as Popup;
            var label = root.FindName("FlashText") as TextBlock;
            var glyph = root.FindName("FlashGlyph") as TextBlock;
            if (f == null || label == null) return;
            label.Text = text;
            if (glyph != null) glyph.Text = toHeadphones ? "" : "";
            // Centered over the sound button and just clear of the taskbar. (The popup lines up the bubble itself,
            // not the margin its shadow is drawn in.)
            var bubble = f.Child as FrameworkElement;
            var target = f.PlacementTarget as FrameworkElement;
            if (bubble != null && target != null)
            {
                bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = bubble.DesiredSize.Width - bubble.Margin.Left - bubble.Margin.Right;
                f.HorizontalOffset = (target.ActualWidth - w) / 2;
                f.VerticalOffset = -12;
            }
            f.IsOpen = false;
            f.IsOpen = true;
            flashOff.Stop();
            flashOff.Start();
            var sound = root.FindName("SoundGlyph") as FrameworkElement;
            if (sound != null && Anim.Enabled)
            {
                var sc = new ScaleTransform(0.6, 0.6);
                sound.RenderTransformOrigin = new Point(0.5, 0.5);
                sound.RenderTransform = sc;
                var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(460)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } };
                sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }
        }
    }
}
