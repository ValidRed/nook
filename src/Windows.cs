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
        readonly Panel homePage, groupsPage, editorPage, settingsPage, islandPage, workspacesPage, clockPage;
        readonly FrameworkElement editorHeader, titleBar;
        readonly ItemsControl homeGrid;

        public HomeWindow(MainVM vm)
        {
            this.vm = vm;
            DataContext = vm;
            Title = "Nook";
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
            groupsPage = (Panel)root.FindName("GroupsPage");
            editorPage = (Panel)root.FindName("EditorPage");
            settingsPage = (Panel)root.FindName("SettingsPage");
            islandPage = (Panel)root.FindName("IslandPage");
            workspacesPage = (Panel)root.FindName("WorkspacesPage");
            clockPage = (Panel)root.FindName("ClockPage");
            editorHeader = (FrameworkElement)root.FindName("EditorHeader");
            titleBar = (FrameworkElement)root.FindName("TitleBar");
            homeGrid = (ItemsControl)root.FindName("HomeGrid");

            RestoreSavedBounds();
            SourceInitialized += (s, e) => ApplyEffects();
            vm.PropertyChanged += OnVm;
            vm.RemoveRequested += OnRemoveRequested;
            Closing += (s, e) => SaveBounds();
            Closed += (s, e) => { vm.PropertyChanged -= OnVm; vm.RemoveRequested -= OnRemoveRequested; vm.FinishToast(); };
            StateChanged += (s, e) => root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            scrim.MouseLeftButtonDown += (s, e) => vm.PaneOpen = false;
            PreviewKeyDown += OnKey;
            SmoothScroll.Attach(scroller);
            Loaded += (s, e) => PlayIntro();
        }

        // Opens where you left it last time, if that spot is still on a screen.
        void RestoreSavedBounds()
        {
            var s = App.Instance.Settings;
            if (s.WinW < MinWidth || s.WinH < MinHeight) return;
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                  SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!screen.Contains(new Point(s.WinX + 80, s.WinY + 20))) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WinX;
            Top = s.WinY;
            Width = s.WinW;
            Height = s.WinH;
            if (s.WinMax) WindowState = WindowState.Maximized;
        }

        void SaveBounds()
        {
            var s = App.Instance.Settings;
            var b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
            if (b.IsEmpty || b.Width < 100) return;
            s.WinX = b.X;
            s.WinY = b.Y;
            s.WinW = b.Width;
            s.WinH = b.Height;
            s.WinMax = WindowState == WindowState.Maximized;
            s.Save();
        }

        static bool introPlayed;
        internal static readonly Color FluidTop = Color.FromArgb(0xF7, 0x3D, 0x8B, 0xF2), FluidBottom = Color.FromArgb(0xFF, 0x1A, 0x55, 0xC4);
        internal static readonly Color FluidBackTop = Color.FromArgb(0x99, 0x7A, 0xB4, 0xFF), FluidBackBottom = Color.FromArgb(0x99, 0x3D, 0x8B, 0xF2);

        // The first time the window opens: blue fluid fills the window and drains away. As the surface passes the
        // logo, its squares splash up out of it with a few droplets; then the logo glides into the title bar while
        // the page rises in. Later opens just rise in.
        void PlayIntro()
        {
            if (introPlayed || !Anim.Enabled || !SystemParameters.ClientAreaAnimation)
            {
                Anim.Rise(titleBar, 0, 8);
                AnimatePage();
                return;
            }
            introPlayed = true;

            var splash = (FrameworkElement)root.FindName("Splash");
            var logo = (FrameworkElement)root.FindName("SplashLogo");
            var tile = (FrameworkElement)root.FindName("SplashTile");
            var fluid = (Canvas)root.FindName("Fluid");
            var drops = (Canvas)root.FindName("Drops");
            var titleIcon = (FrameworkElement)root.FindName("TitleIcon");
            var body = (FrameworkElement)root.FindName("Body");

            body.Opacity = 0;
            titleBar.Opacity = 0;
            titleIcon.Opacity = 0;
            splash.Visibility = Visibility.Visible;
            root.UpdateLayout(); // measure the logo now that it's visible

            double W = splash.ActualWidth, H = splash.ActualHeight;
            var iconCenter = titleIcon.TranslatePoint(new Point(titleIcon.ActualWidth / 2, titleIcon.ActualHeight / 2), root);
            var logoTop = logo.TranslatePoint(new Point(0, 0), splash);
            var logoCenter = logo.TranslatePoint(new Point(logo.ActualWidth / 2, logo.ActualHeight / 2), root);
            double landScale = logo.ActualWidth > 0 ? titleIcon.ActualWidth / logo.ActualWidth : 0.15;

            // Two layers of fluid with gently moving waves; the surface starts above the window and drains below it.
            const double period = 240, amp = 9, drainMs = 1050;
            double startY = -3 * amp, endY = H + 3 * amp;
            var back = Wave(W, H, period, amp * 0.75, FluidBackTop, FluidBackBottom);
            var front = Wave(W, H, period, amp, FluidTop, FluidBottom);
            fluid.Children.Add(back);
            fluid.Children.Add(front);
            var drain = new QuadraticEase { EasingMode = EasingMode.EaseIn }; // speeds up, like water going down a drain
            Flow(back, period, startY - 8, endY, drainMs + 90, 1500, drain);
            Flow(front, period, startY, endY, drainMs, 1100, drain);
            Func<double, double> passes = y => Math.Sqrt(Math.Max(0, Math.Min(1, (y - startY) / (endY - startY)))) * drainMs;

            // The squares pop up out of the fluid as the surface passes each one.
            var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };
            var settle = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
            double lastPop = 0;
            for (int i = 0; i < 4; i++)
            {
                var sq = (FrameworkElement)root.FindName("Sq" + i);
                double centerY = logoTop.Y + Canvas.GetTop(sq) + sq.Height / 2;
                double b = Math.Max(0, passes(centerY) - 30 + (i % 2) * 45);
                lastPop = Math.Max(lastPop, b);
                var sg = (TransformGroup)sq.RenderTransform;
                var grow = Track(K(0, 0.2), K(b, 0.2), K(b + 480, 1, pop));
                ((ScaleTransform)sg.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                ((ScaleTransform)sg.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                ((TranslateTransform)sg.Children[1]).BeginAnimation(TranslateTransform.YProperty, Track(K(0, 16), K(b, 16), K(b + 520, 0, settle)));
                sq.BeginAnimation(OpacityProperty, Track(K(0, 0), K(b, 0), K(b + 140, 1)));
            }

            // A few droplets leap from the surface as it uncovers the bottom of the logo.
            double splashAt = passes(logoTop.Y + logo.ActualHeight) - 20;
            var rng = new Random(7);
            for (int i = 0; i < 7; i++)
            {
                double size = 4 + rng.Next(4);
                var drop = new System.Windows.Shapes.Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(Color.FromRgb(0x8C, 0xC1, 0xFF)), Opacity = 0 };
                var tt = new TranslateTransform();
                drop.RenderTransform = tt;
                Canvas.SetLeft(drop, logoCenter.X - size / 2 + (i - 3) * 14);
                Canvas.SetTop(drop, logoTop.Y + logo.ActualHeight - 4);
                drops.Children.Add(drop);
                double t0 = splashAt + i * 18, rise = 26 + rng.Next(34), spread = (i - 3) * (6 + rng.Next(8));
                tt.BeginAnimation(TranslateTransform.XProperty, Track(K(0, 0), K(t0, 0), K(t0 + 620, spread)));
                tt.BeginAnimation(TranslateTransform.YProperty, Track(K(0, 0), K(t0, 0),
                    K(t0 + 300, -rise, new QuadraticEase { EasingMode = EasingMode.EaseOut }),
                    K(t0 + 620, 18, new QuadraticEase { EasingMode = EasingMode.EaseIn })));
                drop.BeginAnimation(OpacityProperty, Track(K(0, 0), K(t0, 0), K(t0 + 60, 0.9), K(t0 + 420, 0.9), K(t0 + 620, 0)));
            }

            // A soft pulse once everything has landed, then the logo glides into the title bar.
            var glide = new CubicEase { EasingMode = EasingMode.EaseInOut };
            double settled = lastPop + 520, glideAt = Math.Max(drainMs, settled) + 120, landAt = glideAt + 480;
            var tileScale = (ScaleTransform)tile.RenderTransform;
            var pulse = Track(K(0, 1), K(settled - 60, 1), K(settled + 40, 1.04, glide), K(settled + 160, 1, glide));
            tileScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            tileScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

            var g = (TransformGroup)logo.RenderTransform;
            var size2 = Track(K(0, 1), K(glideAt, 1), K(landAt, landScale, glide));
            ((ScaleTransform)g.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, size2);
            ((ScaleTransform)g.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, size2);
            var move = (TranslateTransform)g.Children[2];
            move.BeginAnimation(TranslateTransform.XProperty, Track(K(0, 0), K(glideAt, 0), K(landAt, iconCenter.X - logoCenter.X, glide)));
            move.BeginAnimation(TranslateTransform.YProperty, Track(K(0, 0), K(glideAt, 0), K(landAt, iconCenter.Y - logoCenter.Y, glide)));
            logo.BeginAnimation(OpacityProperty, Track(K(0, 1), K(landAt - 120, 1), K(landAt, 0)));

            After(glideAt + 120, () =>
            {
                body.Opacity = 1;
                Anim.To(titleBar, OpacityProperty, 1, 300);
                AnimatePage();
            });
            After(landAt - 80, () => Anim.To(titleIcon, OpacityProperty, 1, 120));
            After(landAt + 20, () =>
            {
                splash.Visibility = Visibility.Collapsed;
                fluid.Children.Clear();
                drops.Children.Clear();
            });
        }

        // A wide band of fluid with a sine-wave surface; it slides sideways (waves) and down (draining).
        internal static System.Windows.Shapes.Path Wave(double w, double h, double period, double amp, Color top, Color bottom)
        {
            var geo = new StreamGeometry();
            double width = w + period * 2;
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(0, amp), true, true);
                for (double x = 0; x <= width; x += 6)
                    ctx.LineTo(new Point(x, amp + Math.Sin(x / period * 2 * Math.PI) * amp), true, true);
                ctx.LineTo(new Point(width, h + amp * 6), true, false);
                ctx.LineTo(new Point(0, h + amp * 6), true, false);
            }
            geo.Freeze();
            var fill = new LinearGradientBrush(top, bottom, 90);
            fill.Freeze();
            return new System.Windows.Shapes.Path { Data = geo, Fill = fill, RenderTransform = new TranslateTransform() };
        }

        static void Flow(System.Windows.Shapes.Path wave, double period, double fromY, double toY, double drainMs, double waveMs, IEasingFunction ease)
        {
            var tt = (TranslateTransform)wave.RenderTransform;
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -period, new Duration(TimeSpan.FromMilliseconds(waveMs))) { RepeatBehavior = RepeatBehavior.Forever });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, toY, new Duration(TimeSpan.FromMilliseconds(drainMs))) { EasingFunction = ease });
        }

        static EasingDoubleKeyFrame K(double ms, double value, IEasingFunction ease = null)
        {
            return new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Math.Max(0, ms))), ease);
        }

        static DoubleAnimationUsingKeyFrames Track(params EasingDoubleKeyFrame[] frames)
        {
            var a = new DoubleAnimationUsingKeyFrames();
            foreach (var f in frames) a.KeyFrames.Add(f);
            return a;
        }

        void After(double ms, Action action)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); action(); };
            t.Start();
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
            else if (vm.IsEditor) vm.Page = vm.Editor != null && vm.Editor.IsWorkspace ? "workspaces" : "groups";
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
                case "ToastOpen":
                    AnimateToast(vm.ToastOpen);
                    break;
            }
        }

        Panel CurrentPage()
        {
            return vm.IsHome ? homePage : vm.IsGroups ? groupsPage : vm.IsEditor ? editorPage : vm.IsIsland ? islandPage : vm.IsWorkspaces ? workspacesPage : vm.IsClock ? clockPage : settingsPage;
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

        void AnimateToast(bool open)
        {
            var toast = (FrameworkElement)root.FindName("Toast");
            if (toast == null) return;
            var tt = toast.RenderTransform as TranslateTransform;
            if (tt == null || tt.IsFrozen) { tt = new TranslateTransform(); toast.RenderTransform = tt; }
            if (open)
            {
                toast.Visibility = Visibility.Visible;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, new Duration(TimeSpan.FromMilliseconds(420))) { EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 } });
                toast.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(200))));
            }
            else
            {
                var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)));
                fade.Completed += (s, e) => { if (!vm.ToastOpen) toast.Visibility = Visibility.Collapsed; };
                toast.BeginAnimation(OpacityProperty, fade);
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, new Duration(TimeSpan.FromMilliseconds(180))));
            }
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
        readonly Rect? anchor;
        bool placedLeft;

        public string GroupPath { get; private set; }
        public DateTime OpenedAt { get; private set; }
        public DateTime KeepOpenUntil { get; set; }
        public IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }

        public PopupWindow(PopupVM vm, string groupPath, Rect? anchor)
        {
            GroupPath = groupPath;
            this.anchor = anchor;
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
            ((Border)root).Background = App.Instance.GlassTint(); // the blur setting makes the glass more or less see-through
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

        // Opens beside the widget it came from, kept inside the screen's work area.
        void Place(IntPtr hwnd)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = root.DesiredSize;
            Native.POINT center;
            Native.GetCursorPos(out center);
            if (anchor.HasValue) center = new Native.POINT { X = (int)(anchor.Value.X + anchor.Value.Width / 2), Y = (int)(anchor.Value.Y + anchor.Value.Height / 2) };
            Native.RECT work;
            double scale;
            DesktopGrid.Monitor(center, true, out work, out scale);
            int w = (int)Math.Ceiling(desired.Width * scale), h = (int)Math.Ceiling(desired.Height * scale);
            int gap = (int)(8 * scale);
            double half = 38 * scale;
            var a = anchor.HasValue ? anchor.Value : new Rect(center.X - half, center.Y - half, half * 2, half * 2);

            int x = (int)a.Right + gap;
            placedLeft = false;
            if (x + w > work.Right - gap) { x = (int)a.Left - gap - w; placedLeft = true; }
            int y = (int)(a.Top + a.Height / 2 - 70 * scale);
            x = Math.Max(work.Left + gap, Math.Min(x, work.Right - w - gap));
            y = Math.Max(work.Top + gap, Math.Min(y, work.Bottom - h - gap));
            originY = h > 0 ? Math.Max(0, Math.Min(1, (a.Top + a.Height / 2 - y) / h)) : 0.3;
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        double originY = 0.3;

        // Unfolds out of the widget it came from: grows from the widget's side and slides away from it.
        void AnimateIn()
        {
            if (!Anim.Enabled) return;
            var group = new TransformGroup();
            var sc = new ScaleTransform(0.88, 0.88);
            var tr = new TranslateTransform(placedLeft ? 14 : -14, 0);
            group.Children.Add(sc);
            group.Children.Add(tr);
            root.RenderTransformOrigin = new Point(placedLeft ? 1 : 0, originY);
            root.RenderTransform = group;
            root.Opacity = 0;
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 420);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 420);
            Anim.To(tr, TranslateTransform.XProperty, 0, 420);
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

    // A group on the desktop. It sits right above the desktop (below your apps, and still visible after Win+D),
    // never takes focus, and opens with one click. Drag it to move it: it lifts, shows where it will land on the
    // grid, and glides into place. Drag an app to move it within the widget or to another widget.
    class WidgetWindow : Window
    {
        const string AppFormat = "WinGroups.App";

        readonly App app;
        readonly FrameworkElement root;
        Group group;
        WidgetVM vm;
        bool pressed, dragging;
        WidgetSlotVM pressedSlot;
        Native.POINT downCursor;
        Native.RECT downRect;
        SnapGhost ghost;
        EventHandler glide;

        public bool IsPlaced { get; private set; }
        public int AppearIndex { get; set; }
        internal bool TestOnTop;   // --dev-drag: stays above other windows so the test can't click anything else
        public IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }
        public string GroupPath { get { return group.Path; } }

        public Rect ScreenRect
        {
            get
            {
                Native.RECT r;
                return Native.GetWindowRect(Handle, out r) ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : Rect.Empty;
            }
        }

        double Scale
        {
            get { uint dpi = Native.GetDpiForWindow(Handle); return dpi > 0 ? dpi / 96.0 : 1.0; }
        }

        public WidgetWindow(App app, Group g)
        {
            this.app = app;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            UseLayoutRounding = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            AllowDrop = true;
            root = (FrameworkElement)Xaml.Load("Widget.xaml");
            Content = root;
            Apply(g);

            SourceInitialized += OnSourceInitialized;
            Loaded += (s, e) => AppearIn();
            PreviewMouseLeftButtonDown += OnDown;
            PreviewMouseMove += OnMove;
            PreviewMouseLeftButtonUp += OnUp;
            // Only the window losing the mouse ends a drag. (Starting a drag takes the mouse from the tile's button,
            // and that button's "lost capture" bubbles up here too; treating it as ours cancelled every 1x1 drag.)
            LostMouseCapture += (s, e) => { if (dragging && e.OriginalSource == this) EndDrag(); };
            DragEnter += OnDragOver;
            DragOver += OnDragOver;
            DragLeave += (s, e) => vm.IsDropTarget = false;
            Drop += OnDrop;
            Closed += (s, e) => { StopGlide(); if (ghost != null) ghost.Close(); };
        }

        void Apply(Group g)
        {
            group = g;
            Title = g.Name;
            vm = new WidgetVM(app, g);
            DataContext = vm;
            Width = vm.Width;
            Height = vm.Height;
        }

        public void Update(Group g)
        {
            bool resized = g.Data.Cols != group.Data.Cols || g.Data.Rows != group.Data.Rows;
            Apply(g);
            if (resized) Dispatcher.BeginInvoke(new Action(() => GlideTo(DesktopGrid.SnapTarget(ScreenRect), true)), System.Windows.Threading.DispatcherPriority.Loaded);
            else Dispatcher.BeginInvoke(new Action(RefreshGlass), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        public void SetBusy(bool on) { vm.IsBusy = on; }

        public void RefreshGlass()
        {
            if (IsPlaced) vm.UpdateGlass(ScreenRect, Scale);
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = Handle;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            ex = (ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE) & ~Native.WS_EX_APPWINDOW;
            Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            Place();
        }

        // Every move or restack lands the widget just above the desktop window, and it never activates.
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING && !TestOnTop)
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

        // Called by the app a few times a second; puts the widget back right above the desktop if something
        // changed the order (for example Win+D raising the desktop over your apps).
        public void KeepAboveDesktop()
        {
            if (dragging || !IsPlaced) return;
            if (!DesktopGrid.IsRightAboveDesktop(Handle))
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        void MoveTo(int x, int y)
        {
            Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            vm.UpdateGlass(new Rect(x, y, ScreenRect.Width, ScreenRect.Height), Scale);
        }

        void Place()
        {
            var d = group.Data;
            Native.RECT work;
            double scale;
            if (group.IsPlaced && DesktopGrid.Monitor(new Native.POINT { X = d.X + 4, Y = d.Y + 4 }, false, out work, out scale))
            {
                IsPlaced = true;
                MoveTo(d.X, d.Y);
            }
            else
            {
                var p = app.FreeSpot(this, d.Cols, d.Rows);
                IsPlaced = true;
                MoveTo(p.X, p.Y);
                app.SaveWidgetPosition(group, p.X, p.Y);
            }
        }

        public void EnsureOnScreen()
        {
            Native.RECT work;
            double scale;
            var r = ScreenRect;
            if (!DesktopGrid.Monitor(new Native.POINT { X = (int)r.X + 4, Y = (int)r.Y + 4 }, false, out work, out scale))
            {
                group.Data.X = int.MinValue;
                Place();
            }
            else RefreshGlass();
        }

        // ---- appearance ----

        void AppearIn()
        {
            if (!Anim.Enabled) return;
            var sc = new ScaleTransform(0.9, 0.9);
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            root.RenderTransform = sc;
            root.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(Math.Min(AppearIndex, 10) * 60);
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
            var grow = new DoubleAnimation(0.9, 1, new Duration(TimeSpan.FromMilliseconds(420))) { BeginTime = begin, EasingFunction = ease };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260))) { BeginTime = begin });
        }

        void Lift(bool up)
        {
            var sc = root.RenderTransform as ScaleTransform;
            if (sc == null || sc.IsFrozen) { sc = new ScaleTransform(1, 1); root.RenderTransform = sc; }
            root.RenderTransformOrigin = new Point(0.5, 0.5);
            Anim.To(sc, ScaleTransform.ScaleXProperty, up ? 1.04 : 1, 220);
            Anim.To(sc, ScaleTransform.ScaleYProperty, up ? 1.04 : 1, 220);
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(up ? 0.92 : 1, new Duration(TimeSpan.FromMilliseconds(160))));
        }

        // ---- moving the widget ----

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            pressed = true;
            pressedSlot = SlotAt(e.OriginalSource as DependencyObject);
            Native.GetCursorPos(out downCursor);
            Native.GetWindowRect(Handle, out downRect);
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!pressed || e.LeftButton != MouseButtonState.Pressed) return;
            Native.POINT p;
            Native.GetCursorPos(out p);
            int dx = p.X - downCursor.X, dy = p.Y - downCursor.Y;
            if (!dragging && (Math.Abs(dx) > 5 || Math.Abs(dy) > 5))
            {
                if (app.Settings.LockWidgets) { pressed = false; return; }
                if (pressedSlot != null && pressedSlot.IsApp)
                {
                    DragApp(pressedSlot);
                    return;
                }
                dragging = true;
                StopGlide();
                if (ghost == null) ghost = new SnapGhost();
                Lift(true);
                // Takes the press away from any button, so a drag never launches an app. (It can raise another
                // mouse move right away, which is why everything above is ready first.)
                CaptureMouse();
            }
            if (!dragging) return;
            MoveTo(downRect.Left + dx, downRect.Top + dy);
            var r = ScreenRect;
            var t = DesktopGrid.SnapTarget(r);
            ghost.ShowAt(t.X, t.Y, (int)r.Width, (int)r.Height, Handle);
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            pressed = false;
            pressedSlot = null;
            if (!dragging) return;
            EndDrag();
            e.Handled = true;
        }

        void EndDrag()
        {
            dragging = false;
            pressed = false;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (ghost != null) ghost.Hide();
            Lift(false);
            GlideTo(DesktopGrid.SnapTarget(ScreenRect), true);
        }

        // Eases the widget into its grid cell instead of jumping.
        void GlideTo(Native.POINT target, bool save)
        {
            StopGlide();
            var from = ScreenRect;
            var start = DateTime.Now;
            glide = (s, e) =>
            {
                double t = Math.Min(1, (DateTime.Now - start).TotalMilliseconds / 220.0);
                double k = 1 - Math.Pow(1 - t, 3);
                MoveTo((int)Math.Round(from.X + (target.X - from.X) * k), (int)Math.Round(from.Y + (target.Y - from.Y) * k));
                if (t < 1) return;
                StopGlide();
                if (save) app.SaveWidgetPosition(group, target.X, target.Y);
            };
            CompositionTarget.Rendering += glide;
        }

        void StopGlide()
        {
            if (glide != null) CompositionTarget.Rendering -= glide;
            glide = null;
        }

        // ---- moving apps between widgets ----

        static WidgetSlotVM SlotAt(DependencyObject d)
        {
            for (; d != null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.DataContext is WidgetSlotVM && fe is Button) return (WidgetSlotVM)fe.DataContext;
            }
            return null;
        }

        void DragApp(WidgetSlotVM slot)
        {
            pressed = false;
            pressedSlot = null;
            var data = new DataObject(AppFormat, group.Path + "\n" + slot.Index);
            try { DragDrop.DoDragDrop(this, data, DragDropEffects.Move); } catch { }
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(AppFormat)) e.Effects = DragDropEffects.Move;
            else e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; // copy: dropped files stay put
            vm.IsDropTarget = e.Effects != DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            vm.IsDropTarget = false;
            e.Handled = true;
            if (e.Data.GetDataPresent(AppFormat))
            {
                var parts = ((string)e.Data.GetData(AppFormat)).Split('\n');
                int index;
                if (parts.Length != 2 || !int.TryParse(parts[1], out index)) return;
                var hit = root.InputHitTest(e.GetPosition(root)) as DependencyObject;
                var target = SlotAt(hit);
                int insertAt = target != null && target.IsApp ? target.Index : group.Data.Apps.Count;
                app.MoveApp(parts[0], index, group.Path, insertAt);
                e.Effects = DragDropEffects.Move;
                return;
            }
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) app.AddToGroup(group.Path, new System.Collections.Generic.List<string>(files));
            e.Effects = DragDropEffects.Copy;
        }
    }

    // The dashed outline that shows where a dragged widget will land. Click-through and never focused.
    class SnapGhost : Window
    {
        public SnapGhost()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Left = -32000;
            Top = -32000;
            Width = 10;
            Height = 10;
            var rect = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 14, RadiusY = 14, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 3, 3 }, Margin = new Thickness(3)
            };
            rect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentFill");
            rect.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentTint");
            Content = rect;
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr(ex));
            };
        }

        // Shows the outline at a screen rectangle, just below the widget being dragged.
        public void ShowAt(int x, int y, int w, int h, IntPtr below)
        {
            if (!IsVisible) Show();
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, below, x, y, w, h, Native.SWP_NOACTIVATE);
        }
    }

    // Quick search (Ctrl+Alt+Space): type to find any app in your groups or on this PC, Enter to open.
    class SearchWindow : Window
    {
        readonly SearchVM vm;
        readonly FrameworkElement root;

        public SearchWindow(SearchVM vm)
        {
            this.vm = vm;
            DataContext = vm;
            Title = "Nook search";
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.Height;
            Width = 600;
            UseLayoutRounding = true;
            SetResourceReference(BackgroundProperty, "WindowFallback");
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 0,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
                CornerRadius = new CornerRadius(0)
            });
            root = (FrameworkElement)Xaml.Load("Search.xaml");
            Content = root;
            SourceInitialized += (s, e) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                long style = Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE).ToInt64();
                style &= ~(Native.WS_SYSMENU | Native.WS_MINIMIZEBOX | Native.WS_MAXIMIZEBOX);
                Native.SetWindowLongPtr(hwnd, Native.GWL_STYLE, new IntPtr(style));
                long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
                Native.SetWindowLongPtr(hwnd, Native.GWL_EXSTYLE, new IntPtr((ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW));
                Effects.Apply(this, Native.DWMSBT_TRANSIENTWINDOW, App.Instance.EffectiveDark);
                ((Border)root).Background = App.Instance.GlassTint();
                Center(hwnd);
            };
            Loaded += (s, e) =>
            {
                var box = root.FindName("Box") as TextBox;
                if (box != null) { box.Focus(); Keyboard.Focus(box); }
                var sc = new ScaleTransform(0.97, 0.97);
                root.RenderTransformOrigin = new Point(0.5, 0);
                root.RenderTransform = sc;
                Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 260);
                Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 260);
            };
            Deactivated += (s, e) => Close();
            PreviewKeyDown += OnKey;
        }

        // Upper third of the screen the pointer is on, like Start's search.
        void Center(IntPtr hwnd)
        {
            Native.POINT p;
            Native.GetCursorPos(out p);
            Native.RECT work;
            double scale;
            DesktopGrid.Monitor(p, true, out work, out scale);
            int w = (int)(Width * scale);
            int x = work.Left + (work.Right - work.Left - w) / 2;
            int y = work.Top + (int)((work.Bottom - work.Top) * 0.22);
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); e.Handled = true; break;
                case Key.Down: vm.Move(1); e.Handled = true; break;
                case Key.Up: vm.Move(-1); e.Handled = true; break;
                case Key.Enter: if (vm.OpenSelected()) Close(); e.Handled = true; break;
            }
        }
    }
}
