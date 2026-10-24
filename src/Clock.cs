using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinGroups
{
    // A big, quiet clock in the middle of the desktop, on the screens you pick. It sits on the desktop like the
    // widgets (above your wallpaper and icons, below your windows) and clicks pass straight through it.
    // Its digits are solid, or frosted glass that shows a blur of the wallpaper behind them.
    class DesktopClock : Window
    {
        // The window grows with the clock: room for wide fonts, seconds, taller digits, the date and the glance row.
        double BoxW = 1400, BoxH = 560;

        readonly App app;
        readonly TextBlock time, date, glanceText;
        readonly Image glanceArt;
        readonly Border glanceArtBox;
        readonly GlassText glassTime;
        readonly StackPanel stack, glance;
        readonly Border depth;
        readonly Grid host;
        readonly DispatcherTimer tick, shine;
        NowPlaying media;
        int ticks;
        Rect placed;
        IntPtr hwnd;

        public string Device { get; private set; }
        // Why the depth effect is off on this screen, if it is ("" when it's on or not asked for).
        public string DepthNote { get; private set; }
        internal bool DepthShown { get { return depth.Visibility == Visibility.Visible; } }

        public DesktopClock(App app, string device)
        {
            this.app = app;
            Device = device;
            DepthNote = "";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Title = "Clock";
            Width = BoxW;
            Height = BoxH;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            IsHitTestVisible = false;

            time = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
            glassTime = new GlassText { HorizontalAlignment = HorizontalAlignment.Center };
            date = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.85, FontWeight = FontWeights.Bold };
            // The glance row: what's playing (with its cover) and, on laptops, the battery, small and quiet under the time.
            glanceText = new TextBlock { FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 900 };
            glanceArt = new Image { Stretch = Stretch.UniformToFill };
            glanceArtBox = new Border { CornerRadius = new CornerRadius(4), ClipToBounds = true, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Child = glanceArt };
            glance = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.85 };
            glance.Children.Add(glanceArtBox);
            glance.Children.Add(glanceText);
            stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
            time.Foreground = date.Foreground = glanceText.Foreground = Brushes.White;
            stack.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.35, Color = Colors.Black };
            // The depth effect: your wallpaper's subject, cut out, laid over the time exactly where it is on the wallpaper.
            depth = new Border { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            host = new Grid { Children = { stack, depth } };
            date.SizeChanged += (s, e) => ClipDepth();
            glance.SizeChanged += (s, e) => ClipDepth();
            stack.SizeChanged += (s, e) => ClipDepth();
            glassTime.Host = host; // the blurred wallpaper covers the whole clock window
            Content = host;

            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tick.Tick += (s, e) => { UpdateText(); if (++ticks % 20 == 0) UpdateGlance(); };
            // Now and then a light sweeps across glass digits. A timer starts each sweep, so nothing animates in between.
            shine = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
            shine.Tick += (s, e) => { if (Glass && app.Settings.ClockShine && glassTime.IsVisible) glassTime.Sweep(); };
            SourceInitialized += OnSourceInitialized;
            Loaded += (s, e) =>
            {
                tick.Start();
                shine.Start();
                stack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)));
            };
            Closed += (s, e) => { tick.Stop(); shine.Stop(); StopMedia(); };
            ApplySettings();
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT) & ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            Place();
        }

        // Every move or restack lands the clock just above the desktop, like the widgets.
        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var wp = (Native.WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = DesktopGrid.InsertAfterForWidgets();
                wp.flags &= ~Native.SWP_NOZORDER;
                System.Runtime.InteropServices.Marshal.StructureToPtr(wp, lParam, false);
            }
            else if (msg == Native.WM_MOUSEACTIVATE)
            {
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);
            }
            return IntPtr.Zero;
        }

        public void KeepAboveDesktop()
        {
            if (hwnd != IntPtr.Zero && !DesktopGrid.IsRightAboveDesktop(hwnd))
                Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // In the upper third of the screen's work area (like a phone's lock screen), or centered in it.
        public void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            var screen = Screens.Find(Device);
            if (screen == null) return;
            int w = (int)Math.Round(BoxW * screen.Scale), h = (int)Math.Round(BoxH * screen.Scale);
            int workH = screen.Work.Bottom - screen.Work.Top;
            int x = screen.Work.Left + (screen.Work.Right - screen.Work.Left - w) / 2;
            int y = app.Settings.ClockPosition == "center"
                ? screen.Work.Top + (workH - h) / 2
                : Math.Max(screen.Work.Top, screen.Work.Top + (int)(workH * 0.3) - h / 2);
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_NOACTIVATE);
            placed = new Rect(x, y, w, h);
            ApplyColor(placed);
            // Frosted glass shows the wallpaper behind the clock, blurred as much as you like.
            glassTime.Backdrop = Glass ? BlurredWallpaper(placed, app.Settings.ClockGlassBlur, screen.Scale) : null;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyDepth));
        }

        bool Glass { get { return app.Settings.ClockLook == "glass"; } }

        public void ApplySettings()
        {
            var s = app.Settings;

            var font = FontOf(app, s.ClockFont);
            double big = s.ClockTextSize;
            time.FontFamily = date.FontFamily = glassTime.FontFamily = glanceText.FontFamily = font;
            time.FontSize = glassTime.FontSize = big;
            time.FontWeight = glassTime.FontWeight = WeightOf(s.ClockWeight);
            // Taller numbers: the digits stretch up, the rest of the clock makes room.
            var tall = new ScaleTransform(1, s.ClockStretch / 100.0);
            tall.Freeze();
            time.LayoutTransform = glassTime.LayoutTransform = tall;
            time.Visibility = Glass ? Visibility.Collapsed : Visibility.Visible;
            glassTime.Visibility = Glass ? Visibility.Visible : Visibility.Collapsed;
            double small = Math.Max(12, Math.Round(big * 0.196)); // the date, in step with the time
            BoxW = Math.Max(1400, big * 9);
            BoxH = Math.Max(560, big * 1.15 * s.ClockStretch / 100.0 + small * 3 + 160);
            Width = BoxW;
            Height = BoxH;
            date.FontSize = small;
            glanceText.FontSize = small * 0.8;
            date.Visibility = s.ClockDate ? Visibility.Visible : Visibility.Collapsed;
            // The date above the time, like a phone's lock screen, or under it.
            stack.Children.Clear();
            if (s.ClockDateAbove)
            {
                date.Margin = new Thickness(0, 0, 0, Math.Round(big * 0.03));
                stack.Children.Add(date);
                stack.Children.Add(time);
                stack.Children.Add(glassTime);
            }
            else
            {
                date.Margin = new Thickness(0, -Math.Round(big * 0.08), 0, 0);
                stack.Children.Add(time);
                stack.Children.Add(glassTime);
                stack.Children.Add(date);
            }
            glance.Margin = new Thickness(0, small * 0.5, 0, 0);
            stack.Children.Add(glance);
            if (s.ClockGlance) StartMedia(); else StopMedia();
            UpdateGlance();
            UpdateText(true);
            Place();
        }

        // ---- the glance row ----

        void StartMedia()
        {
            if (media != null) return;
            media = new NowPlaying(Dispatcher);
            media.Changed += UpdateGlance;
            media.Start();
        }

        void StopMedia()
        {
            if (media == null) return;
            media.Changed -= UpdateGlance;
            media.Stop();
            media = null;
        }

        // "♪ song — artist" (or "Paused · song", or "Nothing playing"), then the battery on laptops.
        // The row always shows while it's on, even with no music and no battery.
        void UpdateGlance()
        {
            bool on = app.Settings.ClockGlance;
            glance.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) return;
            var parts = new List<string>();
            bool song = media != null && media.Available && !string.IsNullOrEmpty(media.Title);
            if (!song) parts.Add("♪ Nothing playing");
            else if (media.Playing) parts.Add("♪ " + media.Title + (string.IsNullOrEmpty(media.Artist) ? "" : " — " + media.Artist));
            else parts.Add("Paused · " + media.Title);
            var ps = System.Windows.Forms.SystemInformation.PowerStatus;
            bool battery = (ps.BatteryChargeStatus & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == 0 && ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown;
            if (battery)
                parts.Add((ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "Charging " : "Battery ") + (int)Math.Round(ps.BatteryLifePercent * 100) + "%");
            glanceText.Text = string.Join("   ·   ", parts);
            // The song's cover, small, beside it.
            glanceArt.Source = song ? media.Art : null;
            double art = glanceText.FontSize * 1.5;
            glanceArtBox.Width = glanceArtBox.Height = art;
            glanceArtBox.Visibility = glanceArt.Source != null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- the depth effect ----

        // Like a phone's lock screen: the subject sits in front of the time, unless it would hide too much of it;
        // then it steps aside on that screen so the time stays readable. The subject is found in the wallpaper on
        // its own, or comes from a cutout you chose.
        void ApplyDepth()
        {
            int run = ++depthRun;
            if (!app.Settings.ClockDepth || hwnd == IntPtr.Zero || placed.Width <= 0) { HideDepth(""); return; }
            Brush brush;
            Func<Rect, double> cover;
            string file = app.Settings.ClockDepthFile;
            var own = DepthCutout.Picture(file);
            if (own != null)
            {
                brush = Wallpaper.CutoutFor(own, placed);
                if (brush == null) { HideDepth("Off on " + ScreenName() + ": your cutout isn't the same shape as the wallpaper there."); return; }
                cover = r => Wallpaper.Coverage(DepthCutout.Small(file), r);
            }
            else
            {
                // the first look at a wallpaper takes a moment, so it happens in the background
                var subject = Wallpaper.SubjectFor(placed, ScreenName(), () => { if (run == depthRun) ApplyDepth(); });
                if (subject == null) { HideDepth(""); return; }
                if (subject.Cutout == null) { HideDepth(subject.Why); return; }
                var b = new ImageBrush(subject.Cutout) { Stretch = Stretch.Fill };
                b.Freeze();
                brush = b;
                cover = subject.Coverage;
            }
            var digits = (FrameworkElement)(Glass ? (FrameworkElement)glassTime : time);
            if (digits.ActualWidth > 0)
            {
                var box = digits.TransformToAncestor(host).TransformBounds(new Rect(0, 0, digits.ActualWidth, digits.ActualHeight));
                double k = placed.Width / Math.Max(1, host.ActualWidth);
                var onScreen = new Rect(placed.X + box.X * k, placed.Y + box.Y * k, box.Width * k, box.Height * k);
                double worst, covered = Hidden(cover, onScreen, (shown ?? "").Length, out worst);
                if (TooHidden(covered, worst)) { HideDepth("Off on " + ScreenName() + ": the subject would hide too much of the time there."); return; }
            }
            DepthNote = "";
            depth.Background = brush;
            depth.Visibility = Visibility.Visible;
            ClipDepth();
            app.ClockDepthChanged();
        }

        int depthRun;

        // Like a phone, only the digits go behind the subject; the date and the glance row stay in front. The cutout
        // is the wallpaper's own pixels, so leaving those rows out of it changes nothing but what's on top.
        void ClipDepth()
        {
            if (depth.Visibility != Visibility.Visible || host.ActualWidth <= 0) return;
            var keep = new GeometryGroup();
            foreach (var row in new FrameworkElement[] { date, glance })
            {
                if (!row.IsVisible || row.ActualHeight <= 0) continue;
                var r = row.TransformToAncestor(host).TransformBounds(new Rect(0, 0, row.ActualWidth, row.ActualHeight));
                keep.Children.Add(new RectangleGeometry(new Rect(0, r.Y - 6, host.ActualWidth, r.Height + 12)));
            }
            if (keep.Children.Count == 0) { depth.Clip = null; return; }
            var clip = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, host.ActualWidth, host.ActualHeight)), keep);
            clip.Freeze();
            depth.Clip = clip;
        }

        void HideDepth(string note)
        {
            DepthNote = note;
            depth.Visibility = Visibility.Collapsed;
            app.ClockDepthChanged();
        }

        // How much of the time a subject hides: overall, and of the worst-hit character (each gets an equal slice).
        internal static double Hidden(Func<Rect, double> cover, Rect digits, int chars, out double worst)
        {
            worst = 0;
            chars = Math.Max(1, chars);
            double w = digits.Width / chars;
            for (int i = 0; i < chars; i++) worst = Math.Max(worst, cover(new Rect(digits.X + i * w, digits.Y, w, digits.Height)));
            return cover(digits);
        }

        // Steps aside when it would hide more than 30% of the time, or of any one digit, so every digit stays readable.
        internal static bool TooHidden(double overall, double worst) { return overall > 0.3 || worst > 0.3; }

        string ScreenName()
        {
            var s = Screens.Find(Device);
            return s == null ? "this screen" : s.Label.Split('·')[0].Trim();
        }

        // ---- text and color ----

        // The font you picked from the clock's list (or Helvetica, if the list is empty).
        public static FontFamily FontOf(App app, string key)
        {
            var list = app.Settings.ClockFontList ?? new List<ClockFontChoice>();
            var c = list.FirstOrDefault(f => f.Key == key) ?? list.FirstOrDefault();
            return ClockFonts.Family(c);
        }

        public static FontWeight WeightOf(string w)
        {
            return w == "thin" ? FontWeights.Thin : w == "regular" ? FontWeights.Normal : w == "semibold" ? FontWeights.SemiBold
                 : w == "bold" ? FontWeights.Bold : FontWeights.Light;
        }

        // A color taken from the wallpaper behind the clock, made vivid enough to stand out: light on dark
        // wallpapers, deep on light ones.
        public static Color WallpaperTint(Rect screenRect)
        {
            var avg = Wallpaper.AverageColor(screenRect) ?? Color.FromRgb(0x3D, 0x8B, 0xF2);
            double h, s, l;
            Theme.ToHsl(avg, out h, out s, out l);
            bool lightWall = (avg.R * 0.299 + avg.G * 0.587 + avg.B * 0.114) > 170;
            return Theme.FromHsl(h, s < 0.08 ? s : Math.Max(s, 0.55), lightWall ? 0.3 : 0.8);
        }

        // Your color if you picked one (or one from the wallpaper); otherwise white on dark wallpapers and dark on
        // light ones. On frosted glass the color is the glass's tint.
        public void ApplyColor(Rect screenRect)
        {
            var picked = app.Settings.ClockColor == "wallpaper" ? (Color?)WallpaperTint(screenRect) : WidgetColors.Resolve(app.Settings.ClockColor);
            bool lightText;
            Color c;
            if (picked.HasValue)
            {
                c = picked.Value;
                lightText = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0 >= 0.25; // only very dark colors get a light glow
            }
            else
            {
                var wall = Wallpaper.AverageColor(screenRect);
                bool lightWall = wall.HasValue && (wall.Value.R * 0.299 + wall.Value.G * 0.587 + wall.Value.B * 0.114) > 170;
                c = lightWall ? Color.FromArgb(0xE6, 0x10, 0x12, 0x18) : Colors.White;
                lightText = !lightWall;
            }
            time.Foreground = date.Foreground = glanceText.Foreground = WidgetColors.Frozen(c);
            glassTime.Tint = picked.HasValue ? c : Colors.White;
            // A soft glow in the opposite shade keeps the digits readable on busy wallpapers.
            ((DropShadowEffect)stack.Effect).Color = lightText || Glass ? Colors.Black : Colors.White;
        }

        string shown = "";

        void UpdateText(bool force = false)
        {
            var now = DateTime.Now;
            string digits = TimeText(now, app.Settings);
            if (!force && digits == shown) return;
            bool changed = digits != shown;
            shown = digits;
            time.Text = glassTime.Text = digits;
            date.Text = now.ToString("dddd", CultureInfo.CurrentCulture) + ", " + now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, CultureInfo.CurrentCulture);
            // New digits are a different shape: check again how much of them the subject would hide.
            if (changed && app.Settings.ClockDepth && !force) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyDepth));
        }

        // 12- or 24-hour (or like Windows), with or without seconds. AM/PM is left off the big digits;
        // the date line keeps the day readable.
        public static string TimeText(DateTime now, Settings s)
        {
            var f = CultureInfo.CurrentCulture.DateTimeFormat;
            bool secs = s.ClockSeconds;
            string pattern = s.ClockFormat == "24" ? (secs ? "HH:mm:ss" : "HH:mm")
                           : s.ClockFormat == "12" ? (secs ? "h:mm:ss" : "h:mm")
                           : (secs ? f.LongTimePattern : f.ShortTimePattern);
            pattern = pattern.Replace("tt", "").Replace("t", "").Trim();
            if (pattern.Length < 2) pattern = secs ? "H:mm:ss" : "H:mm";
            string text = now.ToString(pattern, CultureInfo.CurrentCulture).Trim();
            return text.Length > 0 ? text : now.ToString("t", CultureInfo.CurrentCulture);
        }

        // The wallpaper behind a screen rectangle (pixels), blurred: blur 0-100 is up to 60 screen pixels.
        // Worked out at half size, which the blur hides and which is four times quicker. Null if there's no wallpaper.
        internal static BitmapSource BlurredWallpaper(Rect screenPx, int blur, double scale)
        {
            const double k = 0.5;
            double r = Math.Max(0, Math.Min(100, blur)) / 100.0 * 60 * scale;
            double pad = r * 2 + 2;
            var area = new Rect(screenPx.X - pad, screenPx.Y - pad, screenPx.Width + 2 * pad, screenPx.Height + 2 * pad);
            var wall = Wallpaper.GlassFor(area, true);
            if (wall == null) return null;
            int w = (int)Math.Ceiling(area.Width * k), h = (int)Math.Ceiling(area.Height * k);
            var rect = new System.Windows.Shapes.Rectangle { Width = w, Height = h, Fill = wall };
            if (r > 0.5) rect.Effect = new BlurEffect { Radius = r * k, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
            rect.Measure(new Size(w, h));
            rect.Arrange(new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(rect);
            int cx = (int)Math.Round(pad * k);
            int cw = Math.Max(1, Math.Min(w - cx, (int)Math.Round(screenPx.Width * k)));
            int ch = Math.Max(1, Math.Min(h - cx, (int)Math.Round(screenPx.Height * k)));
            var crop = new CroppedBitmap(rtb, new Int32Rect(cx, cx, cw, ch));
            crop.Freeze();
            return crop;
        }
    }

    // Text drawn as frosted glass, like the iPhone lock screen clock: each letter shows a blurred copy of what's
    // behind it (Backdrop, which covers Host exactly), lightened with a tint, with a bright edge along the top.
    // Without a Host it uses the nearest Border it sits in.
    public class GlassText : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(GlassText),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontFamilyProperty = System.Windows.Documents.TextElement.FontFamilyProperty.AddOwner(typeof(GlassText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontWeightProperty = System.Windows.Documents.TextElement.FontWeightProperty.AddOwner(typeof(GlassText),
            new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FontSizeProperty = System.Windows.Documents.TextElement.FontSizeProperty.AddOwner(typeof(GlassText),
            new FrameworkPropertyMetadata(48.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TintProperty = DependencyProperty.Register("Tint", typeof(Color), typeof(GlassText),
            new FrameworkPropertyMetadata(Colors.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register("Backdrop", typeof(ImageSource), typeof(GlassText),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        // Where the light sweep is, from before the text (-0.4) to past it (1.4); outside that, no sweep.
        public static readonly DependencyProperty ShineProperty = DependencyProperty.Register("Shine", typeof(double), typeof(GlassText),
            new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public FontFamily FontFamily { get { return (FontFamily)GetValue(FontFamilyProperty); } set { SetValue(FontFamilyProperty, value); } }
        public FontWeight FontWeight { get { return (FontWeight)GetValue(FontWeightProperty); } set { SetValue(FontWeightProperty, value); } }
        public double FontSize { get { return (double)GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }
        public Color Tint { get { return (Color)GetValue(TintProperty); } set { SetValue(TintProperty, value); } }
        public ImageSource Backdrop { get { return (ImageSource)GetValue(BackdropProperty); } set { SetValue(BackdropProperty, value); } }
        public double Shine { get { return (double)GetValue(ShineProperty); } set { SetValue(ShineProperty, value); } }
        public FrameworkElement Host { get; set; }

        Matrix lastPlace;
        Geometry shape;
        string shapeKey;

        public GlassText()
        {
            // If layout moves (or stretches) the text, the piece of backdrop it shows has to follow.
            LayoutUpdated += (s, e) =>
            {
                var h = HostOrBorder();
                if (h == null || !IsVisible) return;
                var place = ToHost(h);
                if (place != lastPlace) { lastPlace = place; InvalidateVisual(); }
            };
        }

        Matrix ToHost(FrameworkElement h)
        {
            try { var t = TransformToVisual(h) as Transform; return t == null ? Matrix.Identity : t.Value; }
            catch (InvalidOperationException) { return Matrix.Identity; } // not in the same tree (yet)
        }

        // A light sweeps across the glass once, in about a second and a half.
        public void Sweep()
        {
            var a = new DoubleAnimation(-0.4, 1.4, TimeSpan.FromMilliseconds(1500))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            Timeline.SetDesiredFrameRate(a, 40);
            BeginAnimation(ShineProperty, a);
        }

        FrameworkElement HostOrBorder()
        {
            if (Host != null) return Host;
            for (DependencyObject d = VisualTreeHelper.GetParent(this); d != null; d = VisualTreeHelper.GetParent(d))
                if (d is Border) return (FrameworkElement)d;
            return null;
        }

        FormattedText Format()
        {
            if (string.IsNullOrEmpty(Text)) return null;
            double dip = 1;
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null) dip = src.CompositionTarget.TransformToDevice.M11;
            return new FormattedText(Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal), FontSize, Brushes.White, dip);
        }

        protected override Size MeasureOverride(Size available)
        {
            var ft = Format();
            return ft == null ? new Size() : new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
        }

        // The letters' outline, built once per text, font and size (the sweep redraws often).
        Geometry Shape()
        {
            string key = Text + "|" + FontFamily + "|" + FontWeight + "|" + FontSize;
            if (key == shapeKey) return shape;
            var ft = Format();
            shape = ft == null ? null : ft.BuildGeometry(new Point());
            if (shape != null) shape.Freeze();
            shapeKey = key;
            return shape;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var geo = Shape();
            if (geo == null) return;
            var h = HostOrBorder();
            if (Backdrop != null && h != null && h.ActualWidth > 0)
            {
                // The backdrop covers the host; mapped back through however this text is placed and stretched.
                var toLocal = ToHost(h);
                if (toLocal.HasInverse)
                {
                    toLocal.Invert();
                    var glass = new ImageBrush(Backdrop)
                    {
                        ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, h.ActualWidth, h.ActualHeight),
                        Stretch = Stretch.Fill, Transform = new MatrixTransform(toLocal)
                    };
                    dc.DrawGeometry(glass, null, geo);
                }
            }
            var c = Tint;
            var tint = new SolidColorBrush(Color.FromArgb(Backdrop == null ? (byte)120 : (byte)84, c.R, c.G, c.B));
            var edge = new LinearGradientBrush(Color.FromArgb(235, 255, 255, 255), Color.FromArgb(70, 255, 255, 255), 90);
            dc.DrawGeometry(tint, new Pen(edge, Math.Max(1, FontSize / 80)), geo);
            double p = Shine;
            if (p > -0.4 && p < 1.4)
            {
                // A soft diagonal band of light, inside the letters only.
                var band = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0.35) };
                band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), p - 0.14));
                band.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 255, 255), p));
                band.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), p + 0.14));
                dc.DrawGeometry(band, null, geo);
            }
        }
    }

    // The depth effect's picture: a copy of your wallpaper with everything but the subject removed (Paint ›
    // Remove background makes one). It's copied into Nook's folder; the original is never touched.
    static class DepthCutout
    {
        public static string Dir { get { return Path.Combine(Settings.DataDir, "depth"); } }
        static string loaded;
        static BitmapSource picture, small;

        public static BitmapSource Picture(string file) { Load(file); return picture; }
        public static BitmapSource Small(string file) { Load(file); return small; } // for measuring how much of the time it covers

        static void Load(string file)
        {
            if (file == loaded) return;
            loaded = file;
            picture = small = null;
            if (string.IsNullOrEmpty(file)) return;
            string path = Path.Combine(Dir, file);
            picture = Decode(path, 2560);
            small = Decode(path, 480);
        }

        static BitmapSource Decode(string path, int width)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad; // so the file isn't kept open
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = width;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // Back to the automatic subject: Nook's copy of your cutout goes (your own file stays where it is).
        public static void Clear()
        {
            loaded = null;
            picture = small = null;
            try { if (Directory.Exists(Dir)) foreach (var f in Directory.GetFiles(Dir)) System.IO.File.Delete(f); } catch { }
        }

        // Copies the picture into Nook's folder and returns its new name, or null if it isn't a picture with
        // see-through parts (a cutout has to let the clock show around the subject).
        public static string Add(string file)
        {
            try
            {
                var test = Decode(file, 64);
                if (test == null) return null;
                var px = new FormatConvertedBitmap(test, PixelFormats.Bgra32, null, 0);
                var bytes = new byte[px.PixelWidth * px.PixelHeight * 4];
                px.CopyPixels(bytes, px.PixelWidth * 4, 0);
                bool clear = false;
                for (int i = 3; i < bytes.Length; i += 4) if (bytes[i] < 128) { clear = true; break; }
                if (!clear) return null;
                Directory.CreateDirectory(Dir);
                foreach (var old in Directory.GetFiles(Dir)) try { System.IO.File.Delete(old); } catch { }
                string name = "cutout-" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(file).ToLowerInvariant();
                System.IO.File.Copy(file, Path.Combine(Dir, name));
                return name;
            }
            catch { return null; }
        }
    }

    // One font in the clock's list: a font installed on this PC, or one you added (copied into Nook's folder).
    public class ClockFontChoice
    {
        public string Name { get; set; }
        public string File { get; set; }  // "" = installed on this PC
        [System.Web.Script.Serialization.ScriptIgnore]
        public string Key { get { return string.IsNullOrEmpty(File) ? Name : "file:" + File; } }
    }

    // The clock's font list starts with Arial, Times New Roman and Helvetica. Fonts you add are copied into Nook's
    // folder, so they work without being installed; you can remove any font from the list.
    static class ClockFonts
    {
        public static string Dir { get { return Path.Combine(Settings.DataDir, "fonts"); } }

        public static List<ClockFontChoice> Defaults()
        {
            return new List<ClockFontChoice>
            {
                new ClockFontChoice { Name = "Arial", File = "" },
                new ClockFontChoice { Name = "Times New Roman", File = "" },
                new ClockFontChoice { Name = "Helvetica", File = "" },
            };
        }

        // Helvetica isn't part of Windows, so Arial (its close cousin) stands in until it's installed or added.
        public static FontFamily Family(ClockFontChoice c)
        {
            if (c != null && !string.IsNullOrEmpty(c.File))
            {
                try
                {
                    var f = Fonts.GetFontFamilies(Path.Combine(Dir, c.File)).FirstOrDefault();
                    if (f != null) return f;
                }
                catch { }
            }
            return new FontFamily((c == null ? "Helvetica" : c.Name) + ", Arial");
        }

        // Copies a font file into Nook's folder. Null if Windows can't read it as a font.
        public static ClockFontChoice Add(string file)
        {
            try
            {
                var found = Fonts.GetFontFamilies(file).FirstOrDefault();
                if (found == null) return null;
                Directory.CreateDirectory(Dir);
                string name = Path.GetFileName(file), dest = Path.Combine(Dir, name);
                for (int n = 2; System.IO.File.Exists(dest); n++)
                    dest = Path.Combine(Dir, Path.GetFileNameWithoutExtension(name) + " (" + n + ")" + Path.GetExtension(name));
                System.IO.File.Copy(file, dest);
                string family = found.FamilyNames.Where(kv => kv.Key.IetfLanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).FirstOrDefault()
                             ?? found.FamilyNames.Values.FirstOrDefault() ?? Path.GetFileNameWithoutExtension(name);
                return new ClockFontChoice { Name = family, File = Path.GetFileName(dest) };
            }
            catch { return null; }
        }

        // A removed font's file goes now if it can; Windows keeps a font file open while it's in use, so anything
        // left over is cleared the next time Nook starts.
        public static void Tidy(List<ClockFontChoice> list)
        {
            try
            {
                if (!Directory.Exists(Dir)) return;
                var keep = new HashSet<string>((list ?? new List<ClockFontChoice>()).Where(f => !string.IsNullOrEmpty(f.File)).Select(f => f.File), StringComparer.OrdinalIgnoreCase);
                foreach (var f in Directory.GetFiles(Dir))
                    if (!keep.Contains(Path.GetFileName(f))) try { System.IO.File.Delete(f); } catch { }
            }
            catch { }
        }
    }
}
