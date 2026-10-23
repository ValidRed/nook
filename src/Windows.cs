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
            SmoothScroll.Attach(scroller);
            Loaded += (s, e) => PlayIntro();
        }

        static bool introPlayed;

        // The first time the window opens: the logo's squares gather into the tile with a slight spin,
        // then the logo glides into the title bar while the page rises in. Later opens just rise in.
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
            var titleIcon = (FrameworkElement)root.FindName("TitleIcon");
            var body = (FrameworkElement)root.FindName("Body");

            body.Opacity = 0;
            titleBar.Opacity = 0;
            titleIcon.Opacity = 0;
            splash.Visibility = Visibility.Visible;
            root.UpdateLayout(); // measure the logo now that it's visible

            var iconCenter = titleIcon.TranslatePoint(new Point(titleIcon.ActualWidth / 2, titleIcon.ActualHeight / 2), root);
            var logoCenter = logo.TranslatePoint(new Point(logo.ActualWidth / 2, logo.ActualHeight / 2), root);
            double landScale = logo.ActualWidth > 0 ? titleIcon.ActualWidth / logo.ActualWidth : 0.15;

            var outBack = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            var settle = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
            var glide = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var into = new CubicEase { EasingMode = EasingMode.EaseIn };

            var g = (TransformGroup)logo.RenderTransform;
            var scale = (ScaleTransform)g.Children[0];
            var rotate = (RotateTransform)g.Children[1];
            var move = (TranslateTransform)g.Children[2];

            logo.BeginAnimation(OpacityProperty, Track(K(0, 0), K(220, 1), K(1180, 1), K(1460, 0, into)));
            var size = Track(K(0, 0.6), K(620, 1, outBack), K(1000, 1), K(1460, landScale, glide));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, size);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, size);
            rotate.BeginAnimation(RotateTransform.AngleProperty, Track(K(0, -45), K(760, 0, settle)));
            move.BeginAnimation(TranslateTransform.XProperty, Track(K(0, 0), K(1000, 0), K(1460, iconCenter.X - logoCenter.X, glide)));
            move.BeginAnimation(TranslateTransform.YProperty, Track(K(0, 0), K(1000, 0), K(1460, iconCenter.Y - logoCenter.Y, glide)));

            // a soft pulse as the squares click into place
            var tileScale = (ScaleTransform)tile.RenderTransform;
            var pulse = Track(K(0, 0.92), K(560, 1, settle), K(820, 1), K(900, 1.045, glide), K(1000, 1, glide));
            tileScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            tileScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

            double[][] outward = { new[] { -1.0, -1.0 }, new[] { 1.0, -1.0 }, new[] { -1.0, 1.0 }, new[] { 1.0, 1.0 } };
            for (int i = 0; i < 4; i++)
            {
                var sq = (FrameworkElement)root.FindName("Sq" + i);
                var sg = (TransformGroup)sq.RenderTransform;
                var sqScale = (ScaleTransform)sg.Children[0];
                var sqMove = (TranslateTransform)sg.Children[1];
                double b = 120 + i * 80;
                var grow = Track(K(0, 0), K(b, 0), K(b + 520, 1, pop));
                sqScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                sqScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                sqMove.BeginAnimation(TranslateTransform.XProperty, Track(K(0, outward[i][0] * 34), K(b, outward[i][0] * 34), K(b + 600, 0, settle)));
                sqMove.BeginAnimation(TranslateTransform.YProperty, Track(K(0, outward[i][1] * 34), K(b, outward[i][1] * 34), K(b + 600, 0, settle)));
                sq.BeginAnimation(OpacityProperty, Track(K(0, 0), K(b, 0), K(b + 200, 1)));
            }

            After(1150, () =>
            {
                body.Opacity = 1;
                Anim.To(titleBar, OpacityProperty, 1, 300);
                AnimatePage();
            });
            After(1400, () => Anim.To(titleIcon, OpacityProperty, 1, 120));
            After(1480, () => splash.Visibility = Visibility.Collapsed);
        }

        static EasingDoubleKeyFrame K(double ms, double value, IEasingFunction ease = null)
        {
            return new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), ease);
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

    // A group on the desktop. It sits behind every other window, never takes focus, opens with one click,
    // and snaps to the desktop icon grid when dragged. Files dropped on it are added as copies.
    class WidgetWindow : Window
    {
        readonly App app;
        readonly FrameworkElement root;
        Group group;
        bool pressed, dragging;
        Native.POINT downCursor;
        Native.RECT downRect;

        public bool IsPlaced { get; private set; }
        public IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }

        public Rect ScreenRect
        {
            get
            {
                Native.RECT r;
                return Native.GetWindowRect(Handle, out r) ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : Rect.Empty;
            }
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
            PreviewMouseLeftButtonDown += OnDown;
            PreviewMouseMove += OnMove;
            PreviewMouseLeftButtonUp += OnUp;
            LostMouseCapture += (s, e) => { if (dragging) { dragging = false; pressed = false; Snap(); } };
            DragEnter += OnDragOver;
            DragOver += OnDragOver;
            Drop += OnDrop;
        }

        void Apply(Group g)
        {
            group = g;
            Title = g.Name;
            var vm = new WidgetVM(app, g);
            DataContext = vm;
            Width = vm.Width;
            Height = vm.Height;
        }

        public void Update(Group g)
        {
            bool resized = g.Data.Cols != group.Data.Cols || g.Data.Rows != group.Data.Rows;
            Apply(g);
            if (resized) Dispatcher.BeginInvoke(new Action(Snap), System.Windows.Threading.DispatcherPriority.Loaded);
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

        // Keep the widget at the bottom of the window stack (above the desktop, below apps) and never activate it.
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_WINDOWPOSCHANGING)
            {
                var wp = (Native.WINDOWPOS)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(Native.WINDOWPOS));
                wp.hwndInsertAfter = Native.HWND_BOTTOM;
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

        void MoveTo(int x, int y)
        {
            Native.SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        void Place()
        {
            var d = group.Data;
            Native.RECT work;
            double scale;
            if (group.IsPlaced && DesktopGrid.Monitor(new Native.POINT { X = d.X + 4, Y = d.Y + 4 }, false, out work, out scale))
            {
                MoveTo(d.X, d.Y);
            }
            else
            {
                var p = app.FreeSpot(this, d.Cols, d.Rows);
                MoveTo(p.X, p.Y);
                app.SaveWidgetPosition(group, p.X, p.Y);
            }
            IsPlaced = true;
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
        }

        // Lines the widget up with the desktop icon grid of whichever screen it's on, inside that screen.
        void Snap()
        {
            var r = ScreenRect;
            if (r.IsEmpty) return;
            var c = new Native.POINT { X = (int)(r.X + r.Width / 2), Y = (int)(r.Y + r.Height / 2) };
            Native.RECT work;
            double scale;
            DesktopGrid.Monitor(c, true, out work, out scale);
            double cw = DesktopGrid.Unit * scale, ch = DesktopGrid.Unit * scale;
            int maxCol = Math.Max(0, (int)Math.Floor((work.Right - work.Left - r.Width) / cw + 0.01));
            int maxRow = Math.Max(0, (int)Math.Floor((work.Bottom - work.Top - r.Height) / ch + 0.01));
            int col = Math.Max(0, Math.Min(maxCol, (int)Math.Round((r.X - work.Left) / cw)));
            int row = Math.Max(0, Math.Min(maxRow, (int)Math.Round((r.Y - work.Top) / ch)));
            int x = work.Left + (int)Math.Round(col * cw), y = work.Top + (int)Math.Round(row * ch);
            MoveTo(x, y);
            app.SaveWidgetPosition(group, x, y);
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            pressed = true;
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
                dragging = true;
                CaptureMouse(); // takes the press away from any button, so a drag never launches an app
            }
            if (dragging) MoveTo(downRect.Left + dx, downRect.Top + dy);
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            pressed = false;
            if (!dragging) return;
            dragging = false;
            ReleaseMouseCapture();
            Snap();
            e.Handled = true;
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            // Copy, never move: whatever you drop stays where it was.
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) app.AddToGroup(group.Path, new System.Collections.Generic.List<string>(files));
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }
}
