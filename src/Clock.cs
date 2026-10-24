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
        const double BoxW = 1000, BoxH = 420;

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
            string size = app.Settings.ClockSize;
            time.FontSize = size == "small" ? 72 : size == "large" ? 168 : 112;
            time.FontWeight = FontWeights.Light;
            date.FontSize = size == "small" ? 17 : size == "large" ? 30 : 22;
            date.Margin = new Thickness(0, size == "small" ? -4 : -10, 0, 0);
            date.Visibility = app.Settings.ClockDate ? Visibility.Visible : Visibility.Collapsed;
            UpdateText(true);
            Place();
        }

        // White on dark wallpapers, dark on light ones.
        public void ApplyColor(Rect screenRect)
        {
            var c = Wallpaper.AverageColor(screenRect);
            bool light = c.HasValue && (c.Value.R * 0.299 + c.Value.G * 0.587 + c.Value.B * 0.114) > 170;
            var brush = new SolidColorBrush(light ? Color.FromArgb(0xE6, 0x10, 0x12, 0x18) : Colors.White);
            brush.Freeze();
            time.Foreground = date.Foreground = brush;
            ((DropShadowEffect)stack.Effect).Color = light ? Colors.White : Colors.Black;
        }

        string shown = "";

        void UpdateText(bool force = false)
        {
            var now = DateTime.Now;
            string t = now.ToString("t", CultureInfo.CurrentCulture);
            // Drop AM/PM from the big digits; the date line below keeps the day readable.
            string digits = t.Replace(CultureInfo.CurrentCulture.DateTimeFormat.AMDesignator, "").Replace(CultureInfo.CurrentCulture.DateTimeFormat.PMDesignator, "").Trim();
            if (digits.Length == 0) digits = t;
            if (!force && digits == shown) return;
            shown = digits;
            time.Text = digits;
            date.Text = now.ToString("dddd", CultureInfo.CurrentCulture) + ", " + now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, CultureInfo.CurrentCulture);
        }
    }
}
