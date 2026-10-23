using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace WinGroups
{
    public class App : Application
    {
        public static App Instance { get; private set; }

        public Settings Settings { get; private set; }
        internal GroupStore Store { get; private set; }
        public MainVM Main { get; private set; }

        HomeWindow home;
        PopupWindow popup;
        List<CatalogApp> catalog;
        DateTime catalogTime;
        bool catalogLoading;
        readonly List<Action<List<CatalogApp>>> catalogWaiters = new List<Action<List<CatalogApp>>>();
        string themeSignature;

        public App()
        {
            Instance = this;
            // Nothing stays running in the background: WinGroups exits when its last window closes.
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            Resources.MergedDictionaries.Add((ResourceDictionary)Xaml.Load("Styles.xaml"));
        }

        public void Init()
        {
            Settings = Settings.Load();
            Theme.Apply(Resources, EffectiveDark);
            themeSignature = Theme.Signature();
            Store = new GroupStore(Settings);
            Main = new MainVM(this);
            Store.Changed += () => Main.RefreshGroups();
            Store.Reload();
            Store.StartWatching();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            // Redraw desktop icons if Windows switched between light and dark since they were drawn.
            string iconTheme = Theme.SystemDark ? "dark" : "light";
            if (Settings.IconTheme != iconTheme)
            {
                if (Store.Groups.Count > 0) Store.RewriteAll();
                Settings.IconTheme = iconTheme;
                Settings.Save();
            }
        }

        // Used by --dev-render: in-memory groups, no files or watchers.
        internal void InitPreview(List<Group> groups, List<CatalogApp> apps, bool dark)
        {
            Settings = new Settings { Theme = dark ? "dark" : "light" };
            Theme.Apply(Resources, dark);
            Store = new GroupStore(Settings);
            Store.Groups = groups;
            Main = new MainVM(this);
            Main.RefreshGroups();
            catalog = apps;
            catalogTime = DateTime.Now;
        }

        public bool EffectiveDark
        {
            get { return Settings.Theme == "dark" || (Settings.Theme == "windows" && Theme.SystemDark); }
        }

        public void HandleCommand(List<string> args)
        {
            string cmd = args.Count == 0 ? "show" : args[0];
            if (cmd == "open" && args.Count > 1) OpenGroupFile(args[1], args.Skip(2).ToList());
            else ShowHome();
            if (Windows.Count == 0) Shutdown();
        }

        // ---- windows ----

        public void ShowHome()
        {
            if (home == null)
            {
                home = new HomeWindow(Main);
                home.Closed += (s, e) => { home = null; Main.PaneOpen = false; Main.DialogOpen = false; };
                home.Show();
            }
            else if (home.WindowState == WindowState.Minimized)
            {
                home.WindowState = WindowState.Normal;
            }
            home.Activate();
        }

        public void OpenGroupFile(string path, List<string> dropped)
        {
            path = Native.LongPath(path);
            var g = Store.Find(path);
            if (g == null)
            {
                try { g = GroupStore.Load(path); } catch { return; }
            }
            if (dropped != null && dropped.Count > 0)
            {
                Store.AddFiles(g, dropped);
                g = Store.Find(path) ?? g;
            }
            ShowPopup(g);
        }

        void ShowPopup(Group g)
        {
            var old = popup;
            var p = new PopupWindow(new PopupVM(this, g), g.Path);
            p.Closed += (s, e) => { if (popup == p) popup = null; };
            popup = p;
            p.Show();
            p.Activate();
            if (old != null) old.Close();
        }

        public void ClosePopup()
        {
            if (popup != null)
            {
                var p = popup;
                popup = null;
                p.Close();
            }
        }

        public void Launch(GroupApp a, bool admin)
        {
            if (!Settings.CloseAfterLaunch && popup != null) popup.KeepOpenUntil = DateTime.Now.AddSeconds(3);
            Launcher.Launch(a, admin);
            if (Settings.CloseAfterLaunch) ClosePopup();
        }

        public void EditFromPopup(Group g)
        {
            ShowHome(); // open first so the app doesn't exit when the pop-up closes
            ClosePopup();
            Main.StartEdit(Store.Find(g.Path) ?? g);
        }

        public void RemoveFromGroup(Group g, GroupApp a)
        {
            g.Data.Apps.Remove(a);
            Store.Save(g);
            if (popup != null && string.Equals(popup.GroupPath, g.Path, StringComparison.OrdinalIgnoreCase))
                popup.DataContext = new PopupVM(this, Store.Find(g.Path) ?? g);
        }

        // ---- groups ----

        public void SaveGroup(Group editing, string name, List<GroupApp> apps)
        {
            try
            {
                if (editing == null) Store.Create(name, apps);
                else Store.Update(Store.Find(editing.Path) ?? editing, name, apps);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't save the group.\n\n" + ex.Message, "WinGroups", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void DeleteGroup(Group g)
        {
            if (popup != null && string.Equals(popup.GroupPath, g.Path, StringComparison.OrdinalIgnoreCase)) ClosePopup();
            Store.Delete(g);
        }

        public void RewriteIcons()
        {
            Store.RewriteAll();
            Main.RefreshGroups();
        }

        // ---- catalog ----

        public void GetCatalog(Action<List<CatalogApp>> done)
        {
            if (catalog != null && done != null) done(catalog);
            bool stale = catalog == null || (DateTime.Now - catalogTime).TotalSeconds > 30;
            if (!stale) return;
            if (done != null) catalogWaiters.Add(done); // a fresh list replaces the cached one when it arrives
            if (catalogLoading) return;
            catalogLoading = true;
            Task.Run(() => AppCatalog.LoadBlocking()).ContinueWith(t =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    catalogLoading = false;
                    if (t.Status == TaskStatus.RanToCompletion)
                    {
                        catalog = t.Result;
                        catalogTime = DateTime.Now;
                    }
                    var waiters = catalogWaiters.ToList();
                    catalogWaiters.Clear();
                    if (catalog != null) foreach (var w in waiters) w(catalog);
                }));
            });
        }

        // ---- settings ----

        public void ApplyTheme()
        {
            Theme.Apply(Resources, EffectiveDark);
            Main.ThemeChanged();
            if (home != null) home.ApplyEffects();
            if (popup != null) popup.ApplyEffects();
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string sig = Theme.Signature();
                if (sig == themeSignature) return;
                bool desktopThemeChanged = sig[0] != themeSignature[0];
                themeSignature = sig;
                ApplyTheme();
                if (desktopThemeChanged)
                {
                    Store.RewriteAll();
                    Settings.IconTheme = Theme.SystemDark ? "dark" : "light";
                    Settings.Save();
                }
            }));
        }

        public void RemoveFromPc()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            ClosePopup();
            Installer.RemoveFromPc();
            MessageBox.Show("WinGroups has been removed from this PC. You can delete WinGroups.exe now.",
                "WinGroups", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
        }
    }
}
