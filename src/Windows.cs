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

        // Springy: overshoots a touch and settles, for things that pop into place.
        static readonly IEasingFunction Bounce = MakeBounce();

        static IEasingFunction MakeBounce()
        {
            var e = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 5 };
            e.Freeze();
            return e;
        }

        // Elements Pop has given a transform (so Cascade knows it may animate them again).
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, object> popped = new System.Runtime.CompilerServices.ConditionalWeakTable<UIElement, object>();

        // Rises in with a little bounce, growing from just under full size.
        public static void Pop(UIElement el, double delayMs, double distance = 18)
        {
            if (el == null || !Enabled) return;
            var group = new TransformGroup();
            var sc = new ScaleTransform(0.97, 0.97);
            var tt = new TranslateTransform(0, distance);
            group.Children.Add(sc);
            group.Children.Add(tt);
            var fe = el as FrameworkElement;
            if (fe != null) fe.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = group;
            popped.Remove(el);
            popped.Add(el, null);
            el.Opacity = 0;
            var begin = TimeSpan.FromMilliseconds(delayMs);
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, Ms(700)) { BeginTime = begin, EasingFunction = Bounce });
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, Ms(700)) { BeginTime = begin, EasingFunction = Bounce });
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, Ms(700)) { BeginTime = begin, EasingFunction = Bounce });
            el.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Ms(260)) { BeginTime = begin });
        }

        // A panel's parts pop in one after another. Only parts without a transform of their own (the rest just show),
        // and only the first several: anything further down is out of sight anyway.
        public static void Cascade(Panel panel, double startMs = 0, double stepMs = 45, int max = 10)
        {
            if (panel == null || !Enabled) return;
            int k = 0;
            object mark;
            foreach (UIElement child in panel.Children)
            {
                if (child.Visibility != Visibility.Visible) continue;
                bool free = child.RenderTransform == null || child.RenderTransform == Transform.Identity || popped.TryGetValue(child, out mark);
                if (k >= max || !free) { child.BeginAnimation(UIElement.OpacityProperty, null); child.Opacity = 1; continue; }
                Pop(child, startMs + k * stepMs, 14 + Math.Min(k, 4) * 2);
                k++;
            }
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

    // Every button in Nook dips a little when pressed and springs back with a small bounce when let go: in Nook's
    // window, the widgets, pop-ups and the island alike. Big buttons dip less than small ones.
    static class Springy
    {
        static bool installed;
        static UIElement pressed;
        static readonly IEasingFunction Down = new CubicEase { EasingMode = EasingMode.EaseOut };
        static readonly IEasingFunction Back = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 };

        public static void Install()
        {
            if (installed) return;
            installed = true;
            var type = typeof(System.Windows.Controls.Primitives.ButtonBase);
            EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) => Press(s as FrameworkElement, true)), true);
            EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonUpEvent, new System.Windows.Input.MouseButtonEventHandler((s, e) => Press(s as FrameworkElement, false)), true);
            EventManager.RegisterClassHandler(type, UIElement.MouseLeaveEvent, new System.Windows.Input.MouseEventHandler((s, e) => { if (s == pressed) Press(s as FrameworkElement, false); }), true);
        }

        static void Press(FrameworkElement b, bool down)
        {
            if (b == null || !Anim.Enabled || !b.IsEnabled) return;
            var st = b.RenderTransform as ScaleTransform;
            if (st == null)
            {
                // Leave anything that's already moved or scaled by something else alone.
                if (b.RenderTransform != null && b.RenderTransform != Transform.Identity) return;
                st = new ScaleTransform(1, 1);
                b.RenderTransform = st;
                b.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            if (st.IsFrozen) return;
            if (down)
            {
                pressed = b;
                double size = Math.Max(1, Math.Max(b.ActualWidth, b.ActualHeight));
                double to = 1 - Math.Max(0.015, Math.Min(0.06, 7 / size));
                var a = new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(90))) { EasingFunction = Down };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                if (pressed == b) pressed = null;
                var a = new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(560))) { EasingFunction = Back };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
        }
    }

    class HomeWindow : Window
    {
        readonly MainVM vm;
        readonly FrameworkElement root;
        readonly Border tabPill;
        readonly Panel tabs;
        readonly Grid dialogLayer;
        readonly Border dialogCard;
        readonly ScrollViewer scroller;
        readonly Panel homePage, groupsPage, editorPage, islandPage, workspacesPage, clockPage, taskbarPage;
        int shownTab = -1;
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
            SetResourceReference(BackgroundProperty, "AppBackground");
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
            ApplyTitleFill();
            tabPill = (Border)root.FindName("TabPill");
            tabs = (Panel)root.FindName("Tabs");
            dialogLayer = (Grid)root.FindName("DialogLayer");
            dialogCard = (Border)root.FindName("DialogCard");
            scroller = (ScrollViewer)root.FindName("Scroller");
            homePage = (Panel)root.FindName("HomePage");
            groupsPage = (Panel)root.FindName("GroupsPage");
            editorPage = (Panel)root.FindName("EditorPage");
            islandPage = (Panel)root.FindName("IslandPage");
            workspacesPage = (Panel)root.FindName("WorkspacesPage");
            clockPage = (Panel)root.FindName("ClockPage");
            taskbarPage = (Panel)root.FindName("TaskbarPage");
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
            PreviewKeyDown += OnKey;
            SmoothScroll.Attach(scroller);
            Loaded += (s, e) => { MovePill(false); PlayIntro(); if (vm.UpdatePromptOpen) AnimateUpdatePrompt(true); };
            tabs.SizeChanged += (s, e) => MovePill(false); // tab widths settle once fonts load
            ((FrameworkElement)root.FindName("TabBar")).PreviewMouseWheel += OnTabWheel;
        }

        // ---- tabs ----

        // Which tab a page belongs to (the editor belongs to Groups or Workspaces).
        int TabOf()
        {
            if (vm.IsGroupsNav) return 1;
            if (vm.IsWorkspacesNav) return 2;
            if (vm.IsIsland) return 3;
            if (vm.IsClock) return 4;
            if (vm.IsTaskbar) return 5;
            return 0;
        }

        static readonly IEasingFunction Snap = MakeSnap();

        static IEasingFunction MakeSnap()
        {
            var e = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
            e.Freeze();
            return e;
        }

        // For --dev-home-live: does the highlight sit exactly on the picked tab?
        internal bool PillOnTab()
        {
            var tab = (FrameworkElement)tabs.Children[TabOf()];
            double x = tab.TranslatePoint(new Point(0, 0), tabs).X;
            var tt = (TranslateTransform)tabPill.RenderTransform;
            return Math.Abs(tt.X - x) < 1 && Math.Abs(tabPill.ActualWidth - tab.ActualWidth) < 1;
        }

        // The accent highlight springs to the picked tab: the edge in front leads and the one behind catches up, so it
        // stretches on the way and settles with a little bounce.
        readonly Spring pillLeft = new Spring(), pillRight = new Spring();
        bool pillMoving;
        TimeSpan pillFrame;

        void MovePill(bool animate)
        {
            int i = TabOf();
            if (i >= tabs.Children.Count) return;
            var tab = (FrameworkElement)tabs.Children[i];
            if (tab.ActualWidth <= 0) return;
            double x = tab.TranslatePoint(new Point(0, 0), tabs).X, w = tab.ActualWidth;
            tabPill.Height = tab.ActualHeight;
            if (!animate || !Anim.Enabled || pillRight.Value <= pillLeft.Value)
            {
                pillLeft.Value = pillLeft.Target = x;
                pillRight.Value = pillRight.Target = x + w;
                pillLeft.Velocity = pillRight.Velocity = 0;
                ShowPill();
                return;
            }
            bool right = x > pillLeft.Value;
            var lead = right ? pillRight : pillLeft;
            var trail = right ? pillLeft : pillRight;
            lead.Stiffness = 560; lead.Damping = 30;
            trail.Stiffness = 240; trail.Damping = 22;
            pillLeft.Target = x;
            pillRight.Target = x + w;
            if (!pillMoving) { pillMoving = true; pillFrame = TimeSpan.Zero; CompositionTarget.Rendering += StepPill; }
        }

        void StepPill(object sender, EventArgs e)
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            double dt = pillFrame == TimeSpan.Zero ? 1 / 60.0 : (t - pillFrame).TotalSeconds;
            pillFrame = t;
            if (dt <= 0) return;
            bool moving = pillLeft.Step(dt) | pillRight.Step(dt);
            ShowPill();
            if (!moving) { CompositionTarget.Rendering -= StepPill; pillMoving = false; }
        }

        void ShowPill()
        {
            ((TranslateTransform)tabPill.RenderTransform).X = pillLeft.Value;
            tabPill.Width = Math.Max(8, pillRight.Value - pillLeft.Value);
        }

        // Scrolling over the tabs moves to the next or previous one (a touchpad's many small scrolls add up to one).
        static readonly string[] TabPages = { "home", "groups", "workspaces", "island", "clock", "taskbar" };
        double tabWheel;
        DateTime lastTabWheel, lastTabFlip;

        void OnTabWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            e.Handled = true;
            if ((DateTime.UtcNow - lastTabWheel).TotalMilliseconds > 400) tabWheel = 0;
            lastTabWheel = DateTime.UtcNow;
            tabWheel += e.Delta;
            if (Math.Abs(tabWheel) < 100 || (DateTime.UtcNow - lastTabFlip).TotalMilliseconds < 260) return;
            int next = Math.Max(0, Math.Min(TabPages.Length - 1, TabOf() + (tabWheel < 0 ? 1 : -1)));
            tabWheel = 0;
            if (next == TabOf()) return;
            lastTabFlip = DateTime.UtcNow;
            vm.Page = TabPages[next];
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

        // The first time the window opens: a quiet splash. The logo's tile settles in, its four squares pop in one
        // after another, a soft ring of light breathes out behind it and the name fades in underneath; then the logo
        // glides into the title bar while the page rises in. Later opens just rise in.
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
            var ring = (FrameworkElement)root.FindName("SplashRing");
            var glow = (FrameworkElement)root.FindName("SplashGlow");
            var name = (FrameworkElement)root.FindName("SplashName");
            var titleIcon = (FrameworkElement)root.FindName("TitleIcon");
            var body = (FrameworkElement)root.FindName("Body");
            var tabs = (FrameworkElement)root.FindName("TabBar");

            body.Opacity = 0;
            tabs.Opacity = 0;
            titleBar.Opacity = 0;
            titleIcon.Opacity = 0;
            splash.Visibility = Visibility.Visible;
            root.UpdateLayout(); // measure the logo now that it's visible

            var iconCenter = titleIcon.TranslatePoint(new Point(titleIcon.ActualWidth / 2, titleIcon.ActualHeight / 2), root);
            var logoCenter = logo.TranslatePoint(new Point(logo.ActualWidth / 2, logo.ActualHeight / 2), root);
            double landScale = logo.ActualWidth > 0 ? titleIcon.ActualWidth / logo.ActualWidth : 0.15;
            var spring = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 };
            var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            var soft = new CubicEase { EasingMode = EasingMode.EaseOut };
            var glide = new CubicEase { EasingMode = EasingMode.EaseInOut };
            const double glideAt = 1150, landAt = glideAt + 520;

            // The tile settles in.
            var tileScale = (ScaleTransform)tile.RenderTransform;
            var settle = Track(K(0, 0.82), K(620, 1, spring));
            tileScale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
            tileScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);
            tile.BeginAnimation(OpacityProperty, Track(K(0, 0), K(240, 1, soft)));

            // The squares pop in, clockwise from the top left.
            var clockwise = new[] { 0, 1, 3, 2 };
            for (int k = 0; k < 4; k++)
            {
                var sq = (FrameworkElement)root.FindName("Sq" + clockwise[k]);
                double b = 170 + k * 80;
                var sg = (TransformGroup)sq.RenderTransform;
                var grow = Track(K(0, 0), K(b, 0), K(b + 440, 1, pop));
                ((ScaleTransform)sg.Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                ((ScaleTransform)sg.Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                sq.BeginAnimation(OpacityProperty, Track(K(0, 0), K(b, 0), K(b + 120, 1)));
            }

            // A ring of light breathes out from behind the logo, over a soft glow.
            var ringScale = (ScaleTransform)ring.RenderTransform;
            var widen = Track(K(0, 0.9), K(420, 0.9), K(1250, 1.9, soft));
            ringScale.BeginAnimation(ScaleTransform.ScaleXProperty, widen);
            ringScale.BeginAnimation(ScaleTransform.ScaleYProperty, widen);
            ring.BeginAnimation(OpacityProperty, Track(K(0, 0), K(420, 0), K(560, 0.6), K(1250, 0, soft)));
            glow.BeginAnimation(OpacityProperty, Track(K(0, 0), K(300, 0), K(700, 1, soft), K(glideAt, 1), K(glideAt + 300, 0, soft)));

            // The name, under the logo, then out of the way as the logo leaves.
            var nameMove = (TranslateTransform)name.RenderTransform;
            nameMove.BeginAnimation(TranslateTransform.YProperty, Track(K(0, 10), K(420, 10), K(820, 0, soft), K(glideAt, 0), K(glideAt + 260, 6, soft)));
            name.BeginAnimation(OpacityProperty, Track(K(0, 0), K(420, 0), K(820, 1, soft), K(glideAt, 1), K(glideAt + 220, 0, soft)));

            // Then the logo glides into the title bar.
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
                // the tabs drop into place with a little bounce
                var drop = new TranslateTransform(0, -16);
                tabs.RenderTransform = drop;
                Anim.To(tabs, OpacityProperty, 1, 260);
                drop.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(650)) { EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 4 } });
                Anim.To(titleBar, OpacityProperty, 1, 300);
                AnimatePage();
            });
            After(landAt - 80, () => Anim.To(titleIcon, OpacityProperty, 1, 120));
            After(landAt + 20, () => splash.Visibility = Visibility.Collapsed);
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

        // Solid is Nook's own deep blue background (like the website); frosted glass is Windows' acrylic, which blurs
        // whatever is behind the window, with the blur setting laying more or less of Nook's navy over it.
        public void ApplyEffects()
        {
            var app = App.Instance;
            bool glass = app.Settings.AppLook == "glass";
            Effects.Apply(this, glass ? Native.DWMSBT_TRANSIENTWINDOW : Native.DWMSBT_NONE, app.EffectiveDark);
            bool frosted = glass && Background == Brushes.Transparent;
            ((Panel)root).Background = frosted ? app.AppGlassTint() : null;
            if (!frosted) SetResourceReference(BackgroundProperty, "AppBackground");
            ApplyTitleFill();
        }

        // Page titles get a moving copy of the title blues, set on each title itself (see TitleFill).
        void ApplyTitleFill()
        {
            var style = TryFindResource("PageTitle") as Style;
            if (style != null) SetTitleFill(root, style, TitleFill.Make(App.Instance.EffectiveDark, true));
        }

        static void SetTitleFill(object node, Style style, Brush brush)
        {
            var t = node as TextBlock;
            if (t != null && t.Style == style) t.Foreground = brush;
            var d = node as DependencyObject;
            if (d == null) return;
            foreach (var child in LogicalTreeHelper.GetChildren(d)) SetTitleFill(child, style, brush);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            if (vm.DialogOpen) vm.DialogOpen = false;
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
                    MovePill(true);
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AnimatePage));
                    break;
                case "DialogOpen":
                    AnimateDialog(vm.DialogOpen);
                    break;
                case "ToastOpen":
                    AnimateToast(vm.ToastOpen);
                    break;
                case "UpdatePromptOpen":
                    AnimateUpdatePrompt(vm.UpdatePromptOpen);
                    break;
            }
        }

        Panel CurrentPage()
        {
            return vm.IsGroups ? groupsPage : vm.IsEditor ? editorPage : vm.IsIsland ? islandPage : vm.IsWorkspaces ? workspacesPage : vm.IsClock ? clockPage : vm.IsTaskbar ? taskbarPage : homePage;
        }

        // Switching tabs: the new page slides in from the side of the tab you picked and fades up, quickly.
        // Opening an editor (same tab) rises in instead.
        void AnimatePage()
        {
            var page = CurrentPage();
            if (page == null) return;
            page.Opacity = 1;
            scroller.ScrollToVerticalOffset(0);
            int tab = TabOf(), from = shownTab;
            shownTab = tab;
            if (!Anim.Enabled) return;
            if (vm.IsEditor) Anim.Pop(editorHeader, 0, 10);
            // The page swings in from the side of the tab you picked, settling with a little bounce, and its
            // sections flow in after it, one by one.
            var tt = new TranslateTransform();
            page.RenderTransform = tt;
            var swing = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 };
            var d = new Duration(TimeSpan.FromMilliseconds(560));
            if (from >= 0 && from != tab)
                tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tab > from ? 56 : -56, 0, d) { EasingFunction = swing });
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160))));
            Anim.Cascade(page, 20, 45, 9);
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

        // The update note drops in from above and fades; it leaves the same way.
        void AnimateUpdatePrompt(bool open)
        {
            var note = (FrameworkElement)root.FindName("UpdatePrompt");
            if (note == null) return;
            var tt = (TranslateTransform)note.RenderTransform;
            if (!Anim.Enabled) { note.Visibility = open ? Visibility.Visible : Visibility.Collapsed; return; }
            if (open)
            {
                note.Visibility = Visibility.Visible;
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-16, 0, new Duration(TimeSpan.FromMilliseconds(420))) { EasingFunction = Snap });
                note.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220))));
            }
            else
            {
                var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(180)));
                fade.Completed += (s, e) => { if (!vm.UpdatePromptOpen) note.Visibility = Visibility.Collapsed; };
                note.BeginAnimation(OpacityProperty, fade);
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-10, new Duration(TimeSpan.FromMilliseconds(180))));
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
        readonly bool above; // from a taskbar button: opens above it
        bool placedLeft, closing;

        public string GroupPath { get; private set; }
        public DateTime OpenedAt { get; private set; }
        public DateTime KeepOpenUntil { get; set; }
        public IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }

        public PopupWindow(PopupVM vm, string groupPath, Rect? anchor, bool above = false)
        {
            GroupPath = groupPath;
            this.anchor = anchor;
            this.above = above;
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
            // An app removed from the open group: shrink to the new content.
            DataContextChanged += (s, e) => { if (IsLoaded) Dispatcher.BeginInvoke(new Action(() => Fit())); };
            Deactivated += OnDeactivated;
            Closing += (s, e) => closing = true; // closing deactivates it too; it's already on its way
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

        // Exactly as big as the pop-up's content. (Sizing to content would also leave room for a frame and title bar
        // that are never drawn, a strip of bare glass along the right and bottom.)
        Size Fit()
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var desired = root.DesiredSize;
            SizeToContent = SizeToContent.Manual;
            Width = desired.Width;
            Height = desired.Height;
            return desired;
        }

        // Opens beside the widget it came from (or above the taskbar button), kept inside the screen's work area.
        void Place(IntPtr hwnd)
        {
            var desired = Fit();
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

            int x, y;
            if (above)
            {
                x = (int)(a.Left + a.Width / 2 - w / 2.0);
                y = (int)a.Top - gap - h;
                x = Math.Max(work.Left + gap, Math.Min(x, work.Right - w - gap));
                y = Math.Max(work.Top + gap, y);
                originX = w > 0 ? Math.Max(0, Math.Min(1, (a.Left + a.Width / 2 - x) / w)) : 0.5;
                originY = 1;
            }
            else
            {
                x = (int)a.Right + gap;
                placedLeft = false;
                if (x + w > work.Right - gap) { x = (int)a.Left - gap - w; placedLeft = true; }
                y = (int)(a.Top + a.Height / 2 - 70 * scale);
                x = Math.Max(work.Left + gap, Math.Min(x, work.Right - w - gap));
                y = Math.Max(work.Top + gap, Math.Min(y, work.Bottom - h - gap));
                originX = placedLeft ? 1 : 0;
                originY = h > 0 ? Math.Max(0, Math.Min(1, (a.Top + a.Height / 2 - y) / h)) : 0.3;
            }
            Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
        }

        double originX, originY = 0.3;

        // Unfolds out of the widget it came from: grows from the widget's side (or the taskbar) and slides away from it.
        void AnimateIn()
        {
            if (!Anim.Enabled) return;
            var group = new TransformGroup();
            var sc = new ScaleTransform(0.88, 0.88);
            var tr = above ? new TranslateTransform(0, 14) : new TranslateTransform(placedLeft ? 14 : -14, 0);
            group.Children.Add(sc);
            group.Children.Add(tr);
            root.RenderTransformOrigin = new Point(originX, originY);
            root.RenderTransform = group;
            root.Opacity = 0;
            Anim.To(sc, ScaleTransform.ScaleXProperty, 1, 420);
            Anim.To(sc, ScaleTransform.ScaleYProperty, 1, 420);
            Anim.To(tr, above ? TranslateTransform.YProperty : TranslateTransform.XProperty, 0, 420);
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
            if (closing || DateTime.Now < KeepOpenUntil) return;
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
