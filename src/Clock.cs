using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace WinGroups
{
    // A big, quiet clock in the middle of the desktop, on the screens you pick. It sits on the desktop like the
    // widgets (above your wallpaper and icons, below your windows) and clicks pass straight through it.
    class DesktopClock : Window
    {
        const double BoxW = 1400, BoxH = 480; // room for wide fonts and seconds at the largest size

        readonly App app;
        readonly TextBlock time, date;
        readonly StackPanel stack;
        readonly DispatcherTimer tick;
        IntPtr hwnd;

        public string Device { get; private set; }

        public DesktopClock(App app, string device)
        {
            this.app = app;
            Device = device;
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

            var font = (FontFamily)app.FindResource("DisplayFont");
            time = new TextBlock { FontFamily = font, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
            date = new TextBlock { FontFamily = font, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.85 };
            stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
            time.Foreground = date.Foreground = Brushes.White;
            stack.Children.Add(time);
            stack.Children.Add(date);
            stack.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.35, Color = Colors.Black };
            Content = new Grid { Children = { stack } };

            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tick.Tick += (s, e) => UpdateText();
            SourceInitialized += OnSourceInitialized;
            Loaded += (s, e) =>
            {
                tick.Start();
                stack.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(600)));
            };
            Closed += (s, e) => tick.Stop();
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

        // Centered in the screen's work area (the space above the taskbar).
        public void Place()
        {
            if (hwnd == IntPtr.Zero) return;
            var screen = Screens.Find(Device);
            if (screen == null) return;
            int w = (int)Math.Round(BoxW * screen.Scale), h = (int)Math.Round(BoxH * screen.Scale);
            int x = screen.Work.Left + (screen.Work.Right - screen.Work.Left - w) / 2;
            int y = screen.Work.Top + (screen.Work.Bottom - screen.Work.Top - h) / 2;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_NOACTIVATE);
            ApplyColor(new Rect(x, y, w, h));
        }

        public void ApplySettings()
        {
            var s = app.Settings;
            string size = s.ClockSize;
            time.FontFamily = date.FontFamily = FontOf(app, s.ClockFont);
            time.FontSize = size == "small" ? 72 : size == "large" ? 168 : 112;
            time.FontWeight = WeightOf(s.ClockWeight);
            date.FontSize = size == "small" ? 17 : size == "large" ? 30 : 22;
            date.Margin = new Thickness(0, size == "small" ? -4 : -10, 0, 0);
            date.Visibility = s.ClockDate ? Visibility.Visible : Visibility.Collapsed;
            UpdateText(true);
            Place();
        }

        public static FontFamily FontOf(Application app, string name)
        {
            if (!string.IsNullOrWhiteSpace(name)) return new FontFamily(name);
            return (FontFamily)app.FindResource("DisplayFont");
        }

        public static FontWeight WeightOf(string w)
        {
            return w == "thin" ? FontWeights.Thin : w == "regular" ? FontWeights.Normal : w == "semibold" ? FontWeights.SemiBold
                 : w == "bold" ? FontWeights.Bold : FontWeights.Light;
        }

        // Your color if you picked one; otherwise white on dark wallpapers and dark on light ones.
        public void ApplyColor(Rect screenRect)
        {
            var picked = WidgetColors.Resolve(app.Settings.ClockColor);
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
            time.Foreground = date.Foreground = WidgetColors.Frozen(c);
            // A soft glow in the opposite shade keeps the digits readable on busy wallpapers.
            ((DropShadowEffect)stack.Effect).Color = lightText ? Colors.Black : Colors.White;
        }

        string shown = "";

        void UpdateText(bool force = false)
        {
            var now = DateTime.Now;
            string digits = TimeText(now, app.Settings);
            if (!force && digits == shown) return;
            shown = digits;
            time.Text = digits;
            date.Text = now.ToString("dddd", CultureInfo.CurrentCulture) + ", " + now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, CultureInfo.CurrentCulture);
        }

        // 12- or 24-hour (or like Windows), with or without seconds. AM/PM is left off the big digits;
        // the date line below keeps the day readable.
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
    }
}
