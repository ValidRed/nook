using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

namespace WinGroups
{
    // Windows 11 materials: Mica for the main window, acrylic (frosted glass) for group pop-ups.
    static class Effects
    {
        public static bool Supported
        {
            get { return Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22621; }
        }

        public static void Apply(Window w, int backdrop, bool dark)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int d = dark ? 1 : 0;
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, 4);
            int corner = 2; // round
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, 4);
            bool ok = false;
            if (Supported)
            {
                var src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
                ok = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4) == 0;
            }
            if (ok) w.Background = Brushes.Transparent;
            else w.SetResourceReference(Control.BackgroundProperty, "WindowFallback");
        }
    }

    static class Anim
    {
        public static bool Enabled = true;
        static readonly IEasingFunction Ease = MakeEase();

        static IEasingFunction MakeEase()
        {
            var e = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
            e.Freeze();
            return e;
        }

        static Duration Ms(double ms) { return new Duration(TimeSpan.FromMilliseconds(ms)); }

        // Rise and fade in.
        public static void Rise(UIElement el, double delayMs, double distance = 16)
        {
            if (el == null || !Enabled) return;
            var tt = new TranslateTransform(0, distance);
            el.RenderTransform = tt;
            el.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(delayMs);
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, Ms(600)) { BeginTime = begin, EasingFunction = Ease });
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(320)) { BeginTime = begin });
        }

        public static void To(Animatable target, DependencyProperty prop, double to, double ms, EventHandler done = null)
        {
            var a = new DoubleAnimation(to, Ms(ms)) { EasingFunction = Ease };
            if (done != null) a.Completed += done;
            target.BeginAnimation(prop, a);
        }

        public static void To(UIElement target, DependencyProperty prop, double to, double ms, EventHandler done = null)
        {
            var a = new DoubleAnimation(to, Ms(ms)) { EasingFunction = Ease };
            if (done != null) a.Completed += done;
            target.BeginAnimation(prop, a);
        }
    }

    class HomeWindow : Window
    {
        readonly MainVM vm;
        readonly FrameworkElement root;
        readonly Border pane, scrim;
        readonly Grid dialogLayer;
        readonly Border dialogCard;
        readonly ScrollViewer scroller;
        readonly Panel homePage, editorPage, settingsPage;
        readonly FrameworkElement editorHeader, titleBar;
        readonly ItemsControl homeGrid;

        public HomeWindow(MainVM vm)
        {
            this.vm = vm;
            DataContext = vm;
            Title = "WinGroups";
            Width = 1120;
            Height = 760;
            MinWidth = 760;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SetResourceReference(BackgroundProperty, "WindowFallback");
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 40,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(6),
                UseAeroCaptionButtons = true,
                CornerRadius = new CornerRadius(0)
            });

            root = (FrameworkElement)Xaml.Load("Home.xaml");
            Content = root;
            pane = (Border)root.FindName("Pane");
            scrim = (Border)root.FindName("Scrim");
            dialogLayer = (Grid)root.FindName("DialogLayer");
            dialogCard = (Border)root.FindName("DialogCard");
            scroller = (ScrollViewer)root.FindName("Scroller");
            homePage = (Panel)root.FindName("HomePage");
            editorPage = (Panel)root.FindName("EditorPage");
            settingsPage = (Panel)root.FindName("SettingsPage");
            editorHeader = (FrameworkElement)root.FindName("EditorHeader");
            titleBar = (FrameworkElement)root.FindName("TitleBar");
            homeGrid = (ItemsControl)root.FindName("HomeGrid");

            SourceInitialized += (s, e) => ApplyEffects();
            vm.PropertyChanged += OnVm;
            vm.RemoveRequested += OnRemoveRequested;
            Closed += (s, e) => { vm.PropertyChanged -= OnVm; vm.RemoveRequested -= OnRemoveRequested; };
            StateChanged += (s, e) => root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            scrim.MouseLeftButtonDown += (s, e) => vm.PaneOpen = false;
            PreviewKeyDown += OnKey;
            Loaded += (s, e) => { Anim.Rise(titleBar, 0, 8); AnimatePage(); };
        }

        public void ApplyEffects()
        {
            Effects.Apply(this, Native.DWMSBT_MAINWINDOW, App.Instance.EffectiveDark);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (vm.DialogOpen) vm.DialogOpen = false;
            else if (vm.PaneOpen) vm.PaneOpen = false;
            else if (vm.IsEditor) vm.Page = "home";
            else return;
            e.Handled = true;
        }

        void OnVm(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "Page":
                    var page = CurrentPage();
                    if (Anim.Enabled && page != null) page.Opacity = 0;
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AnimatePage));
                    break;
                case "PaneOpen":
                    AnimatePane(vm.PaneOpen);
                    break;
                case "DialogOpen":
                    AnimateDialog(vm.DialogOpen);
                    break;
            }
        }

        Panel CurrentPage()
        {
            return vm.IsHome ? homePage : vm.IsEditor ? editorPage : settingsPage;
        }

        void AnimatePage()
        {
            var page = CurrentPage();
            if (page == null) return;
            page.Opacity = 1;
            scroller.ScrollToVerticalOffset(0);
            if (vm.IsEditor) Anim.Rise(editorHeader, 0, 10);
            int i = 0;
            foreach (UIElement child in page.Children)
            {
                if (child.Visibility != Visibility.Visible) continue;
                var ic = child as ItemsControl;
                if (ic != null)
                {
                    for (int k = 0; k < ic.Items.Count; k++)
                        Anim.Rise(ic.ItemContainerGenerator.ContainerFromIndex(k) as UIElement, 80 + i * 50 + Math.Min(k, 14) * 40);
                }
                else
                {
                    Anim.Rise(child, 60 + i * 60);
                }
                i++;
            }
        }

        void AnimatePane(bool open)
        {
            var tt = (TranslateTransform)pane.RenderTransform;
            Anim.To(tt, TranslateTransform.XProperty, open ? 0 : -300, open ? 420 : 280);
            scrim.IsHitTestVisible = open;
            Anim.To(scrim, OpacityProperty, open ? 1 : 0, 300);
        }

        void AnimateDialog(bool open)
        {
            if (!open)
            {
                dialogLayer.Visibility = Visibility.Collapsed;
                return;
            }
            dialogLayer.Visibility = Visibility.Visible;
            dialogLayer.Opacity = 0;
            Anim.To(dialogLayer, OpacityProperty, 1, 220);
            var group = new TransformGroup();
            var sc = new ScaleTransform(0.96, 0.96);
            var tr = new TranslateTransform(0, 12);
            group.Children.Add(sc);
            group.Children.Add(tr);
            dialogCard.RenderTransformOrigin = new Point(0.5, 0.5);
            dialogCard.RenderTransform = group;
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 420);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 420);
            Anim.To(tr, TranslateTransform.YProperty, 0, 420);
        }

        void OnRemoveRequested(GroupVM g, Action done)
        {
            var container = homeGrid.ItemContainerGenerator.ContainerFromItem(g) as FrameworkElement;
            if (container == null || !Anim.Enabled) { done(); return; }
            var st = new ScaleTransform(1, 1);
            container.RenderTransformOrigin = new Point(0.5, 0.5);
            container.RenderTransform = st;
            var shrink = new DoubleAnimation(0.92, new Duration(TimeSpan.FromMilliseconds(230)));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(230)));
            fade.Completed += (s, e) => done();
            container.BeginAnimation(OpacityProperty, fade);
        }
    }

    class PopupWindow : Window
    {
        readonly FrameworkElement root;
        bool placedLeft;

        public string GroupPath { get; private set; }
        public DateTime OpenedAt { get; private set; }
        public DateTime KeepOpenUntil { get; set; }
        public IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }

        public PopupWindow(PopupVM vm, string groupPath)
        {
            GroupPath = groupPath;
            DataContext = vm;
            Title = vm.Name;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            OpenedAt = DateTime.Now;
            SetResourceReference(BackgroundProperty, "WindowFallback");
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
                CornerRadius = new CornerRadius(0)
            });
            root = (FrameworkElement)Xaml.Load("Popup.xaml");
            Content = root;
            SourceInitialized += OnSourceInitialized;
            Deactivated += OnDeactivated;
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Loaded += (s, e) => AnimateIn();
        }

        public void ApplyEffects()
        {
            Effects.Apply(this, Native.DWMSBT_TRANSIENTWINDOW, App.Instance.EffectiveDark);
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = Handle;
            long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
            style &= ~(Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX);
            Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style));
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            ApplyEffects();
            Place(hwnd);
        }

        // Opens beside the group you double-clicked (the pointer is on its icon), kept inside the screen's work area.
        void Place(IntPtr hwnd)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = root.DesiredSize;
            Native.POINT center;
            Native.GetCursorPos(out center);
            IntPtr mon = Native.MonitorFromPoint(center, 2);
            var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            Native.GetMonitorInfo(mon, ref mi);
            uint dx, dy;
            double scale = Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 ? dx / 96.0 : 1.0;
            int w = (int)Math.Ceiling(desired.Width * scale), h = (int)Math.Ceiling(desired.Height * scale);
            int gap = (int)(8 * scale);
            var work = mi.rcWork;
            double half = 38 * scale; // about half a desktop icon cell
            var a = new Rect(center.X - half, center.Y - half, half * 2, half * 2);

            int x = (int)a.Right + gap;
            placedLeft = false;
            if (x + w > work.Right - gap) { x = (int)a.Left - gap - w; placedLeft = true; }
            int y = (int)(a.Top + a.Height / 2 - 70 * scale);
            x = Math.Max(work.Left + gap, Math.Min(x, work.Right - w - gap));
            y = Math.Max(work.Top + gap, Math.Min(y, work.Bottom - h - gap));
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        void AnimateIn()
        {
            if (!Anim.Enabled) return;
            var sc = new ScaleTransform(0.94, 0.94);
            root.RenderTransformOrigin = new Point(placedLeft ? 1 : 0, 0.3);
            root.RenderTransform = sc;
            root.Opacity = 0;
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 380);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 380);
            Anim.To(root, OpacityProperty, 1, 160);
            var list = root.FindName("AppsList") as ItemsControl;
            if (list != null)
                for (int k = 0; k < list.Items.Count; k++)
                    Anim.Rise(list.ItemContainerGenerator.ContainerFromIndex(k) as UIElement, 60 + Math.Min(k, 12) * 28, 10);
        }

        // Closes when you click anywhere else, like the Start menu. When "close after opening an app" is off,
        // the app that just launched may take focus; the group stays open for that.
        void OnDeactivated(object sender, EventArgs e)
        {
            if (DateTime.Now < KeepOpenUntil) return;
            Close();
        }
    }
}
