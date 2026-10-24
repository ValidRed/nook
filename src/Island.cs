using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;
using Comp = Windows.UI.Composition;

namespace WinGroups
{
    enum IslandMode { Compact, Peek, Music, Clock, System, Volume, Charge, Headphones }

    // A small line chart of recent values, for the System page.
    public class Spark : FrameworkElement
    {
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(Spark),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke { get { return (Brush)GetValue(StrokeProperty); } set { SetValue(StrokeProperty, value); } }
        public double Max { get; set; }   // 0 = scale to the biggest value shown
        IList<double> values = new double[0];

        public void SetValues(IList<double> v)
        {
            values = new List<double>(v);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            int n = values.Count;
            if (n < 2 || w <= 0 || h <= 0) return;
            double max = Max > 0 ? Max : 0;
            foreach (var v in values) max = Math.Max(max, v);
            if (max <= 0) max = 1;
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            Func<int, Point> at = i => new Point(w * i / (n - 1), h - 1 - (h - 2) * Math.Max(0, Math.Min(1, values[i] / max)));
            using (var c = line.Open())
            {
                c.BeginFigure(at(0), false, false);
                for (int i = 1; i < n; i++) c.LineTo(at(i), true, true);
            }
            using (var c = area.Open())
            {
                c.BeginFigure(new Point(0, h), true, true);
                for (int i = 0; i < n; i++) c.LineTo(at(i), false, false);
                c.LineTo(new Point(w, h), false, false);
            }
            var solid = Stroke as SolidColorBrush;
            if (solid != null)
            {
                var fill = new LinearGradientBrush(Color.FromArgb(0x4D, solid.Color.R, solid.Color.G, solid.Color.B),
                                                   Color.FromArgb(0x00, solid.Color.R, solid.Color.G, solid.Color.B), 90);
                dc.DrawGeometry(fill, null, area);
            }
            dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    // Which page of the opened island you're on, as a column of dots on its right edge.
    public class PageDots : FrameworkElement
    {
        public int Count { get; set; }
        public int Index { get; set; }

        protected override Size MeasureOverride(Size available) { return new Size(6, Math.Max(0, Count * 10 - 4)); }

        protected override void OnRender(DrawingContext dc)
        {
            if (Count < 2) return;
            var on = new SolidColorBrush(Color.FromArgb(0xE6, 255, 255, 255));
            var off = new SolidColorBrush(Color.FromArgb(0x4D, 255, 255, 255));
            for (int i = 0; i < Count; i++)
            {
                double h = i == Index ? 10 : 6;
                double y = i * 10 + (i > Index ? 4 : 0);
                dc.DrawRoundedRectangle(i == Index ? on : off, null, new Rect(0, y, 6, h), 3, 3);
            }
        }
    }

    // A tiny audio visualizer: rounded bars whose heights the island sets many times a second.
    public class Bars : FrameworkElement
    {
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(Bars),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public int Count { get; set; }
        double[] levels = new double[0];

        public void SetLevels(double[] values)
        {
            if (levels.Length != Count) levels = new double[Count];
            for (int i = 0; i < Count; i++) levels[i] = values[i % values.Length];
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            int n = Math.Max(1, Count);
            double gap = ActualWidth / n * 0.4, w = (ActualWidth - gap * (n - 1)) / n, h = ActualHeight;
            for (int i = 0; i < n; i++)
            {
                double v = i < levels.Length ? levels[i] : 0;
                double bh = Math.Max(w, h * Math.Max(0, Math.Min(1, v)));
                dc.DrawRoundedRectangle(Fill, null, new Rect(i * (w + gap), (h - bh) / 2, w, bh), w / 2, w / 2);
            }
        }
    }

    public class IslandVM : Observable
    {
        readonly App app;
        static readonly Geometry PlayIcon = Geometry.Parse("M0,0 L13,8 L0,16 Z");
        static readonly Geometry PauseIcon = Geometry.Parse("M0,0 L4.5,0 L4.5,16 L0,16 Z M8.5,0 L13,0 L13,16 L8.5,16 Z");

        public IslandVM(App app)
        {
            this.app = app;
            TintBrush = Frozen(NowPlaying.DefaultTint);
            Title = Artist = TimeText = DateText = "";
            SettingsCommand = new Relay(() => app.OpenIslandSettings());
            HideCommand = new Relay(() => app.SetIslandOn(false));
            OpenHubCommand = new Relay(() => app.ShowHome());
            QuitCommand = new Relay(() => app.Quit());
            PlayPauseCommand = new Relay(() => { if (PlayPause != null) PlayPause(); });
            NextCommand = new Relay(() => { if (Next != null) Next(); });
            PreviousCommand = new Relay(() => { if (Previous != null) Previous(); });
            OpenPlayerCommand = new Relay(() => { if (OpenPlayer != null) OpenPlayer(); });
            Tick();
        }

        public Action PlayPause, Next, Previous, OpenPlayer;

        public bool HasUpdate { get { return app.Updater != null && app.Updater.Available; } }
        public string UpdateLabel { get { return HasUpdate ? "Update Nook to " + app.Updater.NewVersion : ""; } }
        public ICommand UpdateCommand { get { return new Relay(() => app.Updater.Install()); } }
        public void UpdateChanged() { Raise("HasUpdate"); Raise("UpdateLabel"); }
        public ICommand SettingsCommand { get; private set; }
        public ICommand HideCommand { get; private set; }
        public ICommand OpenHubCommand { get; private set; }
        public ICommand QuitCommand { get; private set; }
        public ICommand PlayPauseCommand { get; private set; }
        public ICommand NextCommand { get; private set; }
        public ICommand PreviousCommand { get; private set; }
        public ICommand OpenPlayerCommand { get; private set; }

        static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        // ---- look ----
        Brush pillFill = Brushes.Black, pillStroke = Brushes.Transparent;
        public Brush PillFill { get { return pillFill; } set { Set(ref pillFill, value); } }
        public Brush PillStroke { get { return pillStroke; } set { Set(ref pillStroke, value); } }

        // ---- time ----
        string time, date;
        public string TimeText { get { return time; } private set { if (Set(ref time, value)) Raise("TimeMargin"); } }
        public string DateText { get { return date; } private set { Set(ref date, value); } }

        // Returns true when the minute changed.
        public bool Tick()
        {
            var now = DateTime.Now;
            string t = now.ToString("t", CultureInfo.CurrentCulture);
            DateText = now.ToString("dddd", CultureInfo.CurrentCulture) + ", " + now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, CultureInfo.CurrentCulture);
            if (t == time) return false;
            TimeText = t;
            return true;
        }

        public Thickness TimeMargin { get { return hasMusic ? new Thickness(11, 0, 11, 0) : new Thickness(18, 0, 18, 0); } }

        // ---- music ----
        bool hasMusic, hasTimeline;
        string title, artist, elapsed, remaining;
        ImageSource art;
        Brush tint;
        double progress;
        Geometry playPauseIcon = PlayIcon;

        public bool HasMusic { get { return hasMusic; } set { if (Set(ref hasMusic, value)) Raise("TimeMargin"); } }
        public string Title { get { return title; } set { Set(ref title, value); } }
        public string Artist { get { return artist; } set { Set(ref artist, value); } }
        public ImageSource Art { get { return art; } set { Set(ref art, value); } }
        public Brush TintBrush { get { return tint; } private set { Set(ref tint, value); } }
        public Geometry PlayPauseIcon { get { return playPauseIcon; } private set { Set(ref playPauseIcon, value); } }
        public bool HasTimeline { get { return hasTimeline; } private set { Set(ref hasTimeline, value); } }
        public double Progress { get { return progress; } private set { Set(ref progress, value); } }
        public string ElapsedText { get { return elapsed; } private set { Set(ref elapsed, value); } }
        public string RemainingText { get { return remaining; } private set { Set(ref remaining, value); } }

        Color tintColor;
        public void SetTint(Color c) { if (c != tintColor) { tintColor = c; TintBrush = Frozen(c); } }
        public void SetPlaying(bool playing) { PlayPauseIcon = playing ? PauseIcon : PlayIcon; }

        public void SetTimeline(TimeSpan position, TimeSpan duration)
        {
            HasTimeline = duration.TotalSeconds >= 1;
            if (!HasTimeline) return;
            Progress = Math.Max(0, Math.Min(1, position.TotalSeconds / duration.TotalSeconds));
            ElapsedText = Clock(position);
            RemainingText = "-" + Clock(duration - position);
        }

        static string Clock(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                                     : string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
        }

        // ---- volume ----
        double volume;
        bool muted;
        public double VolumeLevel { get { return muted ? 0 : volume; } }
        public string VolumeText { get { return muted ? "Off" : Math.Round(volume * 100) + ""; } }
        public string VolumeGlyph
        {
            get { return muted || volume <= 0 ? "" : volume < 0.34 ? "" : volume < 0.67 ? "" : ""; }
        }

        public void SetVolume(double v, bool isMuted)
        {
            volume = v;
            muted = isMuted;
            Raise("VolumeLevel"); Raise("VolumeText"); Raise("VolumeGlyph");
        }

        // ---- battery ----
        bool hasBattery, charging;
        int percent;
        public bool HasBattery { get { return hasBattery; } }
        public string BatteryText { get { return percent + "%" + (charging ? " · Charging" : ""); } }
        public string BatteryPercent { get { return percent + "%"; } }
        public string ChargeText { get { return charging ? "Charging" : "On battery"; } }
        public double BatteryFillWidth { get { return Math.Max(1, 16 * percent / 100.0); } }
        public Brush BatteryBrush
        {
            get { return charging ? Frozen(Color.FromRgb(0x4C, 0xD9, 0x64)) : percent <= 20 ? Frozen(Color.FromRgb(0xFF, 0x45, 0x3A)) : Brushes.White; }
        }

        // ---- headphones ----
        string headphones = "", headphoneBattery = "";
        public string HeadphoneName { get { return headphones; } }
        public string HeadphoneBattery { get { return headphoneBattery; } }
        public bool HasHeadphoneBattery { get { return headphoneBattery.Length > 0; } }

        public void SetHeadphones(string name, int battery)
        {
            headphones = string.IsNullOrWhiteSpace(name) ? "Headphones" : name.Trim();
            headphoneBattery = battery >= 0 ? battery + "%" : "";
            Raise("HeadphoneName"); Raise("HeadphoneBattery"); Raise("HasHeadphoneBattery");
        }

        // ---- system ----
        string cpu = "", memory = "", memoryDetail = "", gpu = "", down = "", up = "";
        public string CpuText { get { return cpu; } }
        public string MemoryText { get { return memory; } }
        public string MemoryDetail { get { return memoryDetail; } }
        public string GpuText { get { return gpu; } }
        public string DownText { get { return down; } }
        public string UpText { get { return up; } }

        internal void SetStats(SystemStats s)
        {
            cpu = Math.Round(s.Cpu * 100) + "%";
            memory = Math.Round(s.Memory * 100) + "%";
            memoryDetail = s.MemoryUsedGb.ToString("0.0") + " of " + Math.Round(s.MemoryTotalGb) + " GB";
            gpu = s.Gpu < 0 ? "—" : Math.Round(s.Gpu * 100) + "%";
            down = "↓ " + SystemStats.Speed(s.Down);
            up = "↑ " + SystemStats.Speed(s.Up);
            foreach (var p in new[] { "CpuText", "MemoryText", "MemoryDetail", "GpuText", "DownText", "UpText" }) Raise(p);
        }

        internal void DemoStats(string cpuText, string memoryText, string detail, string gpuText, string downText, string upText)
        {
            cpu = cpuText; memory = memoryText; memoryDetail = detail; gpu = gpuText; down = downText; up = upText;
            foreach (var p in new[] { "CpuText", "MemoryText", "MemoryDetail", "GpuText", "DownText", "UpText" }) Raise(p);
        }

        internal void DemoBattery(int pct, bool isCharging)
        {
            hasBattery = true;
            percent = pct;
            charging = isCharging;
            foreach (var p in new[] { "HasBattery", "BatteryText", "BatteryPercent", "ChargeText", "BatteryFillWidth", "BatteryBrush" }) Raise(p);
        }

        // Returns true if the charger was just plugged in or unplugged.
        public bool ReadBattery()
        {
            var ps = WinForms.SystemInformation.PowerStatus;
            bool has = (ps.BatteryChargeStatus & WinForms.BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != WinForms.BatteryChargeStatus.Unknown;
            bool plugged = ps.PowerLineStatus == WinForms.PowerLineStatus.Online;
            int pct = (int)Math.Round(Math.Max(0, Math.Min(1, ps.BatteryLifePercent)) * 100);
            bool changed = has && hasBattery && plugged != charging;
            hasBattery = has;
            charging = has && plugged;
            percent = pct;
            foreach (var p in new[] { "HasBattery", "BatteryText", "BatteryPercent", "ChargeText", "BatteryFillWidth", "BatteryBrush" }) Raise(p);
            return changed;
        }
    }

    // The island's content and its shape. Used by the island window and to render previews.
    class IslandView
    {
        public const double Width = 460, Height = 240, MaxRadius = 26;

        public readonly FrameworkElement Root;
        public readonly Border Pill;
        public readonly Bars CompactBars, PeekBars, MusicBars;
        public readonly Spark CpuSpark, MemorySpark, GpuSpark, NetSpark;
        public readonly PageDots[] Dots;
        readonly Grid content;
        readonly Dictionary<IslandMode, FrameworkElement> panels = new Dictionary<IslandMode, FrameworkElement>();
        double top;
        IslandMode current = IslandMode.Compact;

        public IslandView(IslandVM vm)
        {
            Root = (FrameworkElement)Xaml.Load("Island.xaml");
            Root.DataContext = vm;
            Pill = (Border)Root.FindName("Pill");
            content = (Grid)Root.FindName("Content");
            CompactBars = (Bars)Root.FindName("CompactBars");
            PeekBars = (Bars)Root.FindName("PeekBars");
            MusicBars = (Bars)Root.FindName("MusicBars");
            CpuSpark = (Spark)Root.FindName("CpuSpark");
            MemorySpark = (Spark)Root.FindName("MemorySpark");
            GpuSpark = (Spark)Root.FindName("GpuSpark");
            NetSpark = (Spark)Root.FindName("NetSpark");
            Dots = new[] { (PageDots)Root.FindName("MusicDots"), (PageDots)Root.FindName("ClockDots"), (PageDots)Root.FindName("SystemDots") };
            panels[IslandMode.Compact] = (FrameworkElement)Root.FindName("Compact");
            panels[IslandMode.Peek] = (FrameworkElement)Root.FindName("Peek");
            panels[IslandMode.Music] = (FrameworkElement)Root.FindName("Music");
            panels[IslandMode.Clock] = (FrameworkElement)Root.FindName("Clock");
            panels[IslandMode.Volume] = (FrameworkElement)Root.FindName("Volume");
            panels[IslandMode.Charge] = (FrameworkElement)Root.FindName("Charge");
            panels[IslandMode.System] = (FrameworkElement)Root.FindName("System");
            panels[IslandMode.Headphones] = (FrameworkElement)Root.FindName("Headphones");
        }

        public bool Notch { get; private set; }
        public double Top { get { return top; } }

        // How wide the resting island is (DIPs). It never gets narrower than the time needs.
        public const double MinRestingWidth = 112, MaxRestingWidth = 420;
        public double RestingWidth { get; set; }

        // A notch's pill starts a little above the screen, so it always meets the top edge.
        const double NotchLift = 3;

        // Floating sits just under the top of the screen; a notch hangs from the edge.
        public void SetShape(bool notch)
        {
            Notch = notch;
            top = notch ? 0 : 8;
            Pill.Margin = new Thickness(0, notch ? -NotchLift : top, 0, 0);
            foreach (var p in panels.Values) p.Margin = new Thickness(0, top, 0, 0);
        }

        public FrameworkElement Panel(IslandMode m) { return panels[m]; }

        public Size SizeOf(IslandMode m)
        {
            var p = panels[m];
            p.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var s = new Size(p.DesiredSize.Width - p.Margin.Left - p.Margin.Right, p.DesiredSize.Height - p.Margin.Top - p.Margin.Bottom);
            // A little room either side of the time, or as wide as you set it.
            if (m == IslandMode.Compact) s.Width = Math.Max(s.Width + 16, Math.Max(MinRestingWidth, RestingWidth));
            return s;
        }

        public static double Radius(double h) { return Math.Min(h / 2, MaxRadius); }

        // The pill's outline in the view's coordinates. A notch's top corners run off the top of the screen.
        public Rect Bounds(double w, double h)
        {
            double r = Radius(h);
            return Notch ? new Rect((Width - w) / 2, top - r, w, h + r) : new Rect((Width - w) / 2, top, w, h);
        }

        public void SetSize(double w, double h)
        {
            double r = Radius(h);
            Pill.Width = Math.Max(1, w);
            Pill.Height = Math.Max(1, h + (Notch ? NotchLift : 0));
            Pill.CornerRadius = Notch ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
            var clip = new RectangleGeometry(Bounds(w, h), r, r);
            clip.Freeze();
            content.Clip = clip;
        }

        public void ShowOnly(IslandMode m)
        {
            current = m;
            foreach (var kv in panels)
            {
                kv.Value.BeginAnimation(UIElement.OpacityProperty, null);
                kv.Value.Opacity = kv.Key == m ? 1 : 0;
                kv.Value.Visibility = kv.Key == m ? Visibility.Visible : Visibility.Hidden;
            }
        }

        // Fades the old content out and the new in while the pill changes size.
        public void CrossFade(IslandMode from, IslandMode to)
        {
            current = to;
            var a = panels[from];
            var b = panels[to];
            var outAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
            outAnim.Completed += (s, e) => { if (panels[current] != a) a.Visibility = Visibility.Hidden; };
            a.BeginAnimation(UIElement.OpacityProperty, outAnim);
            b.Visibility = Visibility.Visible;
            b.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            {
                BeginTime = TimeSpan.FromMilliseconds(90),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        public bool IsShowing(IslandMode m) { return panels[m].Visibility == Visibility.Visible; }
    }

    // A springy value: moves toward its target with a little bounce, like the island on iPhone.
    class Spring
    {
        public double Value, Velocity, Target;
        const double Stiffness = 340, Damping = 25;

        public bool Step(double dt)
        {
            dt = Math.Min(dt, 1 / 30.0);
            double a = (Target - Value) * Stiffness - Velocity * Damping;
            Velocity += a * dt;
            Value += Velocity * dt;
            if (Math.Abs(Target - Value) < 0.25 && Math.Abs(Velocity) < 0.5) { Value = Target; Velocity = 0; return false; }
            return true;
        }
    }

    // The island: a pill at the top of the main screen with the time, and what's playing when music is on.
    // Click it for more; scroll over it for the volume. It never takes focus from your apps.
    class IslandWindow : Window
    {
        readonly App app;
        readonly IslandVM vm;
        readonly IslandView view;
        readonly NowPlaying media;
        readonly AudioLevel audio = new AudioLevel();
        readonly Spring w = new Spring(), h = new Spring();
        readonly DispatcherTimer clock, watch, back, leave;
        readonly double[] levels = new double[5];
        readonly SystemStats stats = new SystemStats();
        readonly HeadphoneWatcher headphones;
        IslandGlass glass;
        bool sampling;
        double wheelSum;
        DateTime lastFlip, lastWheel;
        IslandMode mode = IslandMode.Compact;
        bool hovering, hiddenForFullScreen, attached, sizing;
        TimeSpan lastFrame;
        double phase, barClock;
        IntPtr hwnd;
        public bool Demo;   // --dev-island: no real media, volume or power events

        // device: the screen this island sits on (null = the one picked in settings).
        public IslandWindow(App app, IslandVM model = null, string device = null)
        {
            this.app = app;
            Device = device;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Title = "Island";
            Width = IslandView.Width;
            Height = IslandView.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;

            vm = model ?? new IslandVM(app);
            Demo = model != null;
            view = new IslandView(vm);
            Content = view.Root;
            media = new NowPlaying(Dispatcher);
            vm.PlayPause = () => media.PlayPause();
            vm.Next = () => media.Next();
            vm.Previous = () => media.Previous();
            vm.OpenPlayer = OpenPlayer;
            media.Changed += OnMedia;
            media.TrackChanged += () => { if (mode == IslandMode.Compact && vm.HasMusic) Flash(IslandMode.Peek, 3500); };
            headphones = new HeadphoneWatcher(Dispatcher);
            headphones.Connected += OnHeadphones;

            view.SetShape(app.Settings.IslandShape == "notch");
            view.RestingWidth = app.Settings.IslandWidth;
            view.ShowOnly(IslandMode.Compact);
            ApplyLook();

            view.Root.MouseLeftButtonUp += OnClick;
            view.Root.MouseEnter += (s, e) => { hovering = true; leave.Stop(); Resize(); };
            view.Root.MouseLeave += (s, e) => { hovering = false; Resize(); if (mode != IslandMode.Compact) leave.Start(); };
            view.Root.MouseWheel += OnWheel;

            clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clock.Tick += (s, e) =>
            {
                if (vm.Tick() && mode == IslandMode.Compact) Resize();
                if (mode == IslandMode.Music && !Demo) vm.SetTimeline(media.Position, media.Duration);
                if (mode == IslandMode.System) SampleStats();
                UpdateMusic();
            };
            watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            watch.Tick += (s, e) => CheckFullScreen();
            back = new DispatcherTimer();
            back.Tick += (s, e) => { back.Stop(); if (!hovering) SetMode(IslandMode.Compact); };
            leave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            leave.Tick += (s, e) => { leave.Stop(); if (!hovering) SetMode(IslandMode.Compact); };

            SourceInitialized += OnSourceInitialized;
            Loaded += (s, e) =>
            {
                if (!Demo) { media.Start(); headphones.Start(); }
                clock.Start();
                watch.Start();
                Appear();
            };
            Closed += (s, e) =>
            {
                clock.Stop(); watch.Stop(); back.Stop(); leave.Stop();
                media.Stop();
                headphones.Stop();
                audio.Release();
                Animate(false);
                SystemEvents.PowerModeChanged -= OnPower;
                if (glass != null) { glass.Dispose(); glass = null; }
            };
            vm.ReadBattery();
            SystemEvents.PowerModeChanged += OnPower;
        }

        public IslandVM Model { get { return vm; } }
        public string Device { get; private set; }
        internal double PillWidth { get { return view.Pill.Width; } } // for --dev-islands

        void OnSourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) & ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(hwnd).AddHook((IntPtr h2, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(Native.MA_NOACTIVATE); }
                return IntPtr.Zero;
            });
            ApplyGlass();
            Place();
        }

        double Scale
        {
            get { uint dpi = hwnd == IntPtr.Zero ? 96 : Native.GetDpiForWindow(hwnd); return dpi > 0 ? dpi / 96.0 : 1.0; }
        }

        // Top center of the screen you picked (the main screen unless you chose another).
        public void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            var screen = Screens.Find(Device ?? app.Settings.IslandScreen);
            if (screen == null) return;
            double scale = screen.Scale;
            int pw = (int)Math.Round(IslandView.Width * scale), ph = (int)Math.Round(IslandView.Height * scale);
            int x = screen.Bounds.Left + (screen.PixelWidth - pw) / 2, y = screen.Bounds.Top;
            if (glass != null) glass.Place(x, y, pw, ph);
            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, pw, ph, Native.SWP_NOACTIVATE);
            UpdateGlassShape();
        }

        // ---- look ----

        public void ApplySettings()
        {
            view.SetShape(app.Settings.IslandShape == "notch");
            view.RestingWidth = app.Settings.IslandWidth;
            if (mode == IslandMode.System && !app.Settings.IslandSystem) SetMode(IslandMode.Clock);
            UpdateDots();
            Place();
            ApplyLook();
            ApplyGlass();
            UpdateMusic();
            Resize();
            UpdateGlassShape();
        }

        double Frost { get { return app.Settings.IslandBlur / 100.0; } }

        void ApplyLook()
        {
            Look(vm, app.Settings.IslandStyle == "glass", Frost, glass != null || hwnd == IntPtr.Zero, app.Settings.IslandTint);
        }

        public static double TintAlpha(double frost) { return 0.55 - 0.27 * frost; }

        // Your tint, if you picked one, kept deep enough that the island's white text stays easy to read.
        public static Color? TintColor(string value)
        {
            var c = WidgetColors.Resolve(value);
            if (!c.HasValue) return null;
            double h, s, l;
            Theme.ToHsl(c.Value, out h, out s, out l);
            return l > 0.42 ? Theme.FromHsl(h, s, 0.42) : c.Value;
        }

        // Black is solid (or solid in your tint). Glass is a dark tint over Windows' blur; clearer glass gets a little more
        // tint so the text stays readable, and a color of your own a little more again so it shows. Without the blur
        // (older Windows) the tint is darker instead.
        public static Brush Fill(bool isGlass, double frost, bool blurred, string tint)
        {
            var t = TintColor(tint);
            if (!isGlass) return WidgetColors.Frozen(t ?? Colors.Black);
            double alpha = blurred ? TintAlpha(frost) : 0.6;
            if (t.HasValue) alpha = Math.Min(0.85, alpha + 0.2);
            var c = t ?? Color.FromRgb(0x0A, 0x0A, 0x10);
            return WidgetColors.Frozen(Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B));
        }

        internal static void Look(IslandVM vm, bool isGlass, double frost, bool blurred, string tint)
        {
            vm.PillFill = Fill(isGlass, frost, blurred, tint);
            // Solid black has no outline at all, so the fill reaches every edge (a notch meets the top of the screen).
            vm.PillStroke = isGlass ? WidgetColors.Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)) : null;
        }

        void ApplyGlass()
        {
            if (hwnd == IntPtr.Zero) return;
            bool want = app.Settings.IslandStyle == "glass";
            if (want && glass == null)
            {
                glass = IslandGlass.TryCreate();
                if (glass != null)
                {
                    // The glass sits right under the island (the island is owned by it, so it stays above).
                    new WindowInteropHelper(this).Owner = glass.Handle;
                    Place();
                }
                ApplyLook();
            }
            else if (!want && glass != null)
            {
                new WindowInteropHelper(this).Owner = IntPtr.Zero;
                glass.Dispose();
                glass = null;
            }
            if (glass != null)
            {
                glass.Frost = Frost;
                glass.Visible = !hiddenForFullScreen;
            }
        }

        void UpdateGlassShape()
        {
            if (glass == null) return;
            double s = Scale;
            var b = view.Bounds(w.Value, h.Value);
            glass.Shape(b.X * s, b.Y * s, b.Width * s, b.Height * s, IslandView.Radius(h.Value) * s);
        }

        // ---- size and mode ----

        void Appear()
        {
            var size = view.SizeOf(IslandMode.Compact);
            w.Value = Math.Min(40, size.Width);
            h.Value = 10;
            Resize();
        }

        void Resize()
        {
            var size = view.SizeOf(mode);
            double grow = hovering && mode == IslandMode.Compact ? 1 : 0; // breathes a little under the pointer
            w.Target = size.Width + grow * 8;
            h.Target = size.Height + grow * 2;
            sizing = true;
            Animate(true);
        }

        void SetMode(IslandMode m)
        {
            if (m == mode) return;
            var old = mode;
            mode = m;
            if (m == IslandMode.Music && !Demo) vm.SetTimeline(media.Position, media.Duration);
            if (m == IslandMode.System) SampleStats();
            UpdateDots();
            view.CrossFade(old, m);
            Resize();
        }

        // ---- pages: open the island and scroll through music, time and system ----

        static bool IsPage(IslandMode m) { return m == IslandMode.Music || m == IslandMode.Clock || m == IslandMode.System; }

        List<IslandMode> Pages()
        {
            var pages = new List<IslandMode>();
            if (vm.HasMusic) pages.Add(IslandMode.Music);
            pages.Add(IslandMode.Clock);
            if (app.Settings.IslandSystem) pages.Add(IslandMode.System);
            return pages;
        }

        void UpdateDots()
        {
            var pages = Pages();
            var order = new[] { IslandMode.Music, IslandMode.Clock, IslandMode.System };
            for (int k = 0; k < order.Length; k++)
            {
                var d = view.Dots[k];
                if (d == null) continue;
                d.Count = pages.Count;
                d.Index = pages.IndexOf(order[k]);
                d.InvalidateMeasure();
                d.InvalidateVisual();
            }
        }

        void FlipPage(int delta)
        {
            // Touchpads send lots of small scrolls; add them up and turn one page at a time.
            if ((DateTime.UtcNow - lastWheel).TotalMilliseconds > 400) wheelSum = 0;
            lastWheel = DateTime.UtcNow;
            wheelSum += delta;
            if (Math.Abs(wheelSum) < 100 || (DateTime.UtcNow - lastFlip).TotalMilliseconds < 350) return;
            int dir = wheelSum < 0 ? 1 : -1;
            wheelSum = 0;
            var pages = Pages();
            int i = pages.IndexOf(mode), j = Math.Max(0, Math.Min(pages.Count - 1, i + dir));
            if (i < 0 || j == i) return;
            lastFlip = DateTime.UtcNow;
            SetMode(pages[j]);
        }

        // CPU, memory, GPU and network are read only while the System page is open.
        void SampleStats()
        {
            if (sampling || Demo) return;
            sampling = true;
            System.Threading.Tasks.Task.Run(() => stats.Sample()).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                sampling = false;
                vm.SetStats(stats);
                view.CpuSpark.SetValues(stats.CpuHistory);
                view.MemorySpark.SetValues(stats.MemoryHistory);
                view.GpuSpark.SetValues(stats.GpuHistory);
                view.NetSpark.SetValues(stats.NetHistory);
            })));
        }

        void OnHeadphones(string name, int battery)
        {
            if (!app.Settings.IslandHeadphones) return;
            vm.SetHeadphones(name, battery);
            if (mode == IslandMode.Compact || mode == IslandMode.Peek || mode == IslandMode.Volume) Flash(IslandMode.Headphones, 3200);
        }

        // Shows something for a moment, then goes back to resting.
        void Flash(IslandMode m, int ms)
        {
            SetMode(m);
            back.Stop();
            back.Interval = TimeSpan.FromMilliseconds(ms);
            back.Start();
        }

        void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled) return;
            back.Stop();
            if (IsPage(mode)) SetMode(IslandMode.Compact);
            else SetMode(vm.HasMusic ? IslandMode.Music : IslandMode.Clock);
            e.Handled = true;
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            if (IsPage(mode)) { FlipPage(e.Delta); return; }
            if (!app.Settings.IslandScrollVolume || Demo) return;
            double v = audio.Volume;
            if (v < 0) return;
            v = Math.Max(0, Math.Min(1, Math.Round((v + e.Delta / 120.0 * 0.02) * 50) / 50));
            audio.Volume = v;
            vm.SetVolume(v, audio.Muted && v == 0);
            if (mode != IslandMode.Music) Flash(IslandMode.Volume, 1400);
        }

        void OnPower(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.StatusChange) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool plugged = vm.ReadBattery();
                if (plugged && app.Settings.IslandCharging && (mode == IslandMode.Compact || mode == IslandMode.Peek)) Flash(IslandMode.Charge, 2600);
            }));
        }

        void OnMedia()
        {
            vm.Title = media.Title;
            vm.Artist = media.Artist;
            vm.Art = media.Art;
            vm.SetTint(media.Tint);
            vm.SetPlaying(media.Playing);
            UpdateMusic();
        }

        // Music shows while something plays, and for a minute after it's paused.
        void UpdateMusic()
        {
            if (Demo) { Animate(true); return; }
            bool on = app.Settings.IslandMusic && media.Available && (media.Playing || (DateTime.UtcNow - media.LastPlayed).TotalSeconds < 60);
            if (on != vm.HasMusic)
            {
                vm.HasMusic = on;
                if (!on && (mode == IslandMode.Music || mode == IslandMode.Peek)) SetMode(IslandMode.Compact);
                else if (mode == IslandMode.Compact) Resize();
                UpdateDots();
            }
            if (on) Animate(true);
        }

        void OpenPlayer()
        {
            string id = media.AppId;
            if (string.IsNullOrEmpty(id)) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "shell:AppsFolder\\" + id); } catch { }
        }

        // ---- full screen ----

        void CheckFullScreen()
        {
            bool hide = app.Settings.IslandHideFullScreen && FullScreenAppOnTop();
            if (hide == hiddenForFullScreen) return;
            hiddenForFullScreen = hide;
            if (hide) { back.Stop(); SetMode(IslandMode.Compact); }
            Native.ShowWindow(hwnd, hide ? Native.SW_HIDE : Native.SW_SHOWNOACTIVATE);
            if (glass != null) glass.Visible = !hide;
            if (!hide) Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        bool FullScreenAppOnTop()
        {
            int state;
            // 2 = a full-screen app, 3 = a full-screen game, 4 = presentation mode
            if (Native.SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4)) return true;
            var fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == hwnd || fg == Native.GetShellWindow()) return false;
            var cls = new StringBuilder(64);
            Native.GetClassName(fg, cls, cls.Capacity);
            string c = cls.ToString();
            if (c == "WorkerW" || c == "Progman" || c == "Shell_TrayWnd") return false;
            Native.RECT r;
            if (!Native.GetWindowRect(fg, out r)) return false;
            var mon = Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
            if (mon != Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST)) return false; // only the island's screen matters
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetMonitorInfo(mon, ref mi)) return false;
            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }

        // ---- frames: springs and the visualizer ----

        void Animate(bool on)
        {
            if (on && !attached) { CompositionTarget.Rendering += OnFrame; attached = true; lastFrame = TimeSpan.Zero; }
            else if (!on && attached) { CompositionTarget.Rendering -= OnFrame; attached = false; }
        }

        void OnFrame(object sender, EventArgs e)
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            double dt = lastFrame == TimeSpan.Zero ? 1 / 60.0 : (t - lastFrame).TotalSeconds;
            lastFrame = t;
            if (dt <= 0) return;

            bool moving = w.Step(dt) | h.Step(dt);
            if (moving || sizing)
            {
                view.SetSize(w.Value, h.Value);
                UpdateGlassShape();
            }
            sizing = moving;

            bool visualizing = vm.HasMusic && !hiddenForFullScreen && IsVisible;
            if (visualizing) StepBars(dt);
            if (!moving && !visualizing) Animate(false);
        }

        static readonly double[] Speed1 = { 7.3, 9.1, 6.2, 8.4, 10.6 }, Speed2 = { 2.1, 3.3, 2.7, 1.9, 3.8 }, Phase = { 0, 1.7, 3.1, 4.4, 5.6 };

        void StepBars(double dt)
        {
            barClock += dt;
            if (barClock < 1 / 40.0) return; // about 40 updates a second is plenty for a few bars
            double step = barClock;
            barClock = 0;
            phase += step;
            float left = 0, right = 0;
            bool live = !Demo && audio.Read(out left, out right);
            bool playing = Demo || media.Playing;
            for (int i = 0; i < levels.Length; i++)
            {
                double ch = !live ? 0.55 : i < 2 ? left : i > 2 ? right : (left + right) / 2;
                double loud = Math.Pow(Math.Max(0, Math.Min(1, ch)), 0.55);
                double wobble = Math.Abs(Math.Sin(phase * Speed1[i] + Phase[i]) * (0.6 + 0.4 * Math.Sin(phase * Speed2[i])));
                double target = playing ? loud * (0.3 + 0.7 * wobble) : 0;
                double k = target > levels[i] ? 0.6 : 0.22;
                levels[i] += (target - levels[i]) * Math.Min(1, k * step * 60);
            }
            if (view.IsShowing(IslandMode.Compact)) view.CompactBars.SetLevels(levels);
            if (view.IsShowing(IslandMode.Peek)) view.PeekBars.SetLevels(levels);
            if (view.IsShowing(IslandMode.Music)) view.MusicBars.SetLevels(levels);
        }

        // --dev-island: jump straight to a state for screenshots.
        internal void ShowStateNow(IslandMode m, bool hover)
        {
            mode = m;
            hovering = hover;
            view.ShowOnly(m);
            var size = view.SizeOf(m);
            w.Value = w.Target = size.Width + (hover && m == IslandMode.Compact ? 8 : 0);
            h.Value = h.Target = size.Height + (hover && m == IslandMode.Compact ? 2 : 0);
            view.SetSize(w.Value, h.Value);
            UpdateGlassShape();
            UpdateDots();
            for (int i = 0; i < levels.Length; i++) levels[i] = new[] { 0.55, 0.9, 0.4, 0.75, 0.6 }[i];
            view.CompactBars.SetLevels(levels);
            view.PeekBars.SetLevels(levels);
            view.MusicBars.SetLevels(levels);
        }
    }

    // Frosted glass behind the island: Windows' own backdrop blur, drawn by the compositor in a window just under
    // the island and cut to its shape. (The simpler "blur behind" styles always blur the whole window rectangle.)
    sealed class IslandGlass : IDisposable
    {
        static IntPtr dispatcherQueue;
        HwndSource window;
        Comp.Compositor compositor;
        object target;
        Comp.SpriteVisual blur;
        Comp.CompositionRoundedRectangleGeometry shape;
        bool visible = true;

        public IntPtr Handle { get { return window == null ? IntPtr.Zero : window.Handle; } }

        public static IslandGlass TryCreate()
        {
            var g = new IslandGlass();
            try { g.Create(); return g; }
            catch { g.Dispose(); return null; } // no compositor (older Windows): the island uses a darker tint instead
        }

        void Create()
        {
            if (dispatcherQueue == IntPtr.Zero)
            {
                // The compositor needs a dispatcher queue on this thread; it lives as long as the app.
                var opts = new Native.DispatcherQueueOptions { dwSize = Marshal.SizeOf(typeof(Native.DispatcherQueueOptions)), threadType = 2, apartmentType = 2 };
                Marshal.ThrowExceptionForHR(Native.CreateDispatcherQueueController(opts, out dispatcherQueue));
            }
            var p = new HwndSourceParameters("Nook island glass")
            {
                WindowStyle = unchecked((int)Native.WS_POPUP),
                ExtendedWindowStyle = (int)(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST | Native.WS_EX_NOREDIRECTIONBITMAP),
                PositionX = -32000, PositionY = -32000, Width = 10, Height = 10
            };
            window = new HwndSource(p);
            window.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == Native.WM_MOUSEACTIVATE) { handled = true; return new IntPtr(Native.MA_NOACTIVATE); }
                return IntPtr.Zero;
            });
            int one = 1, none = unchecked((int)0xFFFFFFFE), square = 1;
            Native.DwmSetWindowAttribute(window.Handle, Native.DWMWA_USE_HOSTBACKDROPBRUSH, ref one, 4);
            Native.DwmSetWindowAttribute(window.Handle, Native.DWMWA_BORDER_COLOR, ref none, 4);
            Native.DwmSetWindowAttribute(window.Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref square, 4);

            compositor = new Comp.Compositor();
            IntPtr raw;
            Marshal.ThrowExceptionForHR(((Native.ICompositorDesktopInterop)(object)compositor).CreateDesktopWindowTarget(window.Handle, false, out raw));
            var t = (Comp.CompositionTarget)Marshal.GetObjectForIUnknown(raw);
            Marshal.Release(raw);
            target = t;
            blur = compositor.CreateSpriteVisual();
            blur.Brush = compositor.CreateHostBackdropBrush();
            shape = compositor.CreateRoundedRectangleGeometry();
            blur.Clip = compositor.CreateGeometricClip(shape);
            t.Root = blur;
            Native.ShowWindow(window.Handle, Native.SW_SHOWNOACTIVATE);
        }

        public void Place(int x, int y, int w, int h)
        {
            if (window != null) Native.SetWindowPos(window.Handle, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
        }

        // Pixels, in the window. The window's own shape matches, so clicks around the island reach your apps.
        public void Shape(double x, double y, double w, double h, double r)
        {
            if (blur == null) return;
            blur.Offset = new Vector3((float)x, (float)y, 0);
            blur.Size = new Vector2((float)w, (float)h);
            shape.Size = new Vector2((float)w, (float)h);
            shape.CornerRadius = new Vector2((float)r, (float)r);
            int ri = (int)Math.Round(r * 2);
            Native.SetWindowRgn(window.Handle, Native.CreateRoundRectRgn((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(x + w) + 1, (int)Math.Round(y + h) + 1, ri, ri), false);
        }

        // 0 = clear glass, 1 = fully frosted.
        public double Frost { set { if (blur != null) blur.Opacity = (float)Math.Sqrt(Math.Max(0, Math.Min(1, value))); } }

        public bool Visible
        {
            set
            {
                if (value == visible || window == null) return;
                visible = value;
                Native.ShowWindow(window.Handle, value ? Native.SW_SHOWNOACTIVATE : Native.SW_HIDE);
            }
        }

        public void Dispose()
        {
            try { if (blur != null) blur.Dispose(); } catch { }
            try { var d = target as IDisposable; if (d != null) d.Dispose(); } catch { }
            try { if (compositor != null) compositor.Dispose(); } catch { }
            blur = null;
            shape = null;
            target = null;
            compositor = null;
            if (window != null) { window.Dispose(); window = null; }
        }
    }
}
