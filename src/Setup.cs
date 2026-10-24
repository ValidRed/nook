using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace WinGroups
{
    // The first time Nook opens: open it when Windows starts? Add it to the desktop? Both start off, and both can
    // be changed later on Home. Nook never copies itself anywhere; these point at Nook.exe wherever it is.
    public class SetupVM : Observable
    {
        readonly App app;
        bool startup, shortcut;

        public SetupVM(App app)
        {
            this.app = app;
            AppIcon = IconRenderer.RenderAppIcon(96);
            StartCommand = new Relay(() => { if (Done != null) Done(); });
            ShowFileCommand = new Relay(() =>
            {
                try { Process.Start("explorer.exe", "/select,\"" + Installer.CurrentExe + "\""); } catch { }
            });
        }

        public ImageSource AppIcon { get; private set; }
        public bool Startup { get { return startup; } set { Set(ref startup, value); } }
        public bool Shortcut { get { return shortcut; } set { Set(ref shortcut, value); } }
        public bool InPassingFolder { get { return Installer.InPassingFolder; } }
        public string WhereText
        {
            get
            {
                string folder = Path.GetFileName(Path.GetDirectoryName(Installer.CurrentExe) ?? "") ?? "";
                return "Nook.exe is in " + (folder.Length > 0 ? folder : "a temporary folder") + ". If you'll move it somewhere it can stay (like Documents), do that first; the shortcut and startup point at where it is now.";
            }
        }
        public ICommand StartCommand { get; private set; }
        public ICommand ShowFileCommand { get; private set; }
        public Action Done;

        // Get started: turn on what was picked.
        public void Apply()
        {
            var s = app.Settings;
            if (startup) { s.StartWithWindows = true; Installer.SetStartup(true); }
            if (shortcut) Installer.AddDesktopShortcut();
            s.SetupDone = true;
            s.Save();
            app.Main.StartupChanged();
        }
    }

    class SetupWindow : Window
    {
        readonly FrameworkElement root;
        bool applied;

        public SetupWindow(App app)
        {
            var vm = new SetupVM(app);
            DataContext = vm;
            Title = "Welcome to Nook";
            Width = 600;
            Height = 660;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SetResourceReference(BackgroundProperty, "AppBackground");
            WindowChrome.SetWindowChrome(this, new WindowChrome
            {
                CaptionHeight = 40,
                GlassFrameThickness = new Thickness(-1),
                ResizeBorderThickness = new Thickness(0),
                UseAeroCaptionButtons = true,
                CornerRadius = new CornerRadius(0)
            });
            root = (FrameworkElement)Xaml.Load("Setup.xaml");
            Content = root;
            SourceInitialized += (s, e) =>
            {
                // Nook's own deep blue, with Windows' rounded corners and dark title buttons.
                Effects.Apply(this, Native.DWMSBT_NONE, app.EffectiveDark);
                SetResourceReference(BackgroundProperty, "AppBackground");
            };
            vm.Done = () => { Finish(vm); Close(); };
            // Closing it any other way counts as "not now": nothing is turned on, and it won't ask again.
            Closing += (s, e) => Finish(null);
            Loaded += (s, e) => AnimateIn();
        }

        void Finish(SetupVM vm)
        {
            if (applied) return;
            applied = true;
            if (vm != null) vm.Apply();
            else { App.Instance.Settings.SetupDone = true; App.Instance.Settings.Save(); }
        }

        // The logo pops in, then everything rises into place.
        void AnimateIn()
        {
            if (!Anim.Enabled) return;
            var logo = (FrameworkElement)root.FindName("Logo");
            var sc = (ScaleTransform)logo.RenderTransform;
            var pop = new DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 } };
            sc.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            sc.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            logo.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            int i = 0;
            foreach (var name in new[] { "Title", "Lede", "Choices", "Where", "Go" })
            {
                var el = root.FindName(name) as UIElement;
                if (el != null && el.Visibility == Visibility.Visible) Anim.Rise(el, 120 + i++ * 70, 14);
            }
        }
    }
}
