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
        readonly Dictionary<string, WidgetWindow> widgets = new Dictionary<string, WidgetWindow>(StringComparer.OrdinalIgnoreCase);
        List<CatalogApp> catalog;
        DateTime catalogTime;
        bool catalogLoading;
        readonly List<Action<List<CatalogApp>>> catalogWaiters = new List<Action<List<CatalogApp>>>();
        string themeSignature;

        public App()
        {
            Instance = this;
            // WinGroups keeps running while it has widgets on the desktop, and exits when its last window closes.
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            Resources.MergedDictionaries.Add((ResourceDictionary)Xaml.Load("Styles.xaml"));
        }

        public void Init()
        {
            Settings = Settings.Load();
            Theme.Apply(Resources, EffectiveDark);
            themeSignature = Theme.Signature();
            DesktopGrid.Refresh();
            Store = new GroupStore();
            Main = new MainVM(this);
            Store.MigrateLegacy();
            if (Installer.HasLegacyFileType) Installer.RemoveLegacyFileType();
            Store.Changed += () => { Main.RefreshGroups(); SyncWidgets(); };
            Store.Reload();
            Store.StartWatching();
            Installer.RefreshStartup(Settings.StartWithWindows);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        }

        // Used by --dev-render: in-memory groups, no files or watchers.
        internal void InitPreview(List<Group> groups, List<CatalogApp> apps, bool dark)
        {
            Settings = new Settings { Theme = dark ? "dark" : "light" };
            Theme.Apply(Resources, dark);
            Store = new GroupStore();
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

        // "--widgets" (used at sign-in) shows only the widgets; anything else also opens this window.
        public void HandleCommand(List<string> args)
        {
            string cmd = args.Count == 0 ? "show" : args[0];
            SyncWidgets();
            if (cmd != "--widgets") ShowHome();
            if (Windows.Count == 0) Shutdown();
        }

        // ---- widgets ----

        public void SyncWidgets()
        {
            if (Store == null) return;
            var keep = new HashSet<string>(Store.Groups.Select(g => g.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var path in widgets.Keys.Where(p => !keep.Contains(p)).ToList())
            {
                var w = widgets[path];
                widgets.Remove(path);
                w.Close();
            }
            foreach (var g in Store.Groups)
            {
                WidgetWindow w;
                if (widgets.TryGetValue(g.Path, out w)) w.Update(g);
                else
                {
                    w = new WidgetWindow(this, g);
                    widgets[g.Path] = w;
                    w.Show();
                }
            }
        }

        internal void SaveWidgetPosition(Group g, int x, int y)
        {
            Store.SavePosition(g, x, y);
        }

        // First free spot on the main screen, filling columns from the right (desktop icons fill from the left).
        internal Native.POINT FreeSpot(WidgetWindow me, int cols, int rows)
        {
            Native.RECT work;
            double scale;
            DesktopGrid.Monitor(new Native.POINT(), true, out work, out scale);
            double cw = DesktopGrid.Unit * scale, ch = DesktopGrid.Unit * scale;
            int maxCol = Math.Max(0, (int)((work.Right - work.Left) / cw) - cols);
            int maxRow = Math.Max(0, (int)((work.Bottom - work.Top) / ch) - rows);
            var taken = widgets.Values.Where(w => w != me && w.IsPlaced).Select(w => w.ScreenRect).ToList();
            for (int col = maxCol; col >= 0; col--)
            {
                for (int row = 0; row <= maxRow; row++)
                {
                    int x = work.Left + (int)Math.Round(col * cw), y = work.Top + (int)Math.Round(row * ch);
                    var r = new Rect(x, y, cols * cw, rows * ch);
                    if (!taken.Any(t => t.IntersectsWith(Rect.Inflate(r, -2, -2)))) return new Native.POINT { X = x, Y = y };
                }
            }
            return new Native.POINT { X = work.Left + (int)Math.Round(maxCol * cw), Y = work.Top };
        }

        public void OpenFromWidget(string path)
        {
            var g = Store.Find(path);
            if (g == null) return;
            WidgetWindow w;
            Rect? anchor = widgets.TryGetValue(path, out w) ? w.ScreenRect : (Rect?)null;
            ShowPopup(g, anchor);
        }

        internal void AddToGroup(string path, List<string> files)
        {
            var g = Store.Find(path);
            if (g != null && files.Count > 0) Store.AddFiles(g, files);
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

        void ShowPopup(Group g, Rect? anchor)
        {
            var old = popup;
            var p = new PopupWindow(new PopupVM(this, g), g.Path, anchor);
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

        public void EditGroup(string path)
        {
            var g = Store.Find(path);
            if (g == null) return;
            ShowHome();
            ClosePopup();
            Main.StartEdit(g);
        }

        public void EditFromPopup(Group g)
        {
            EditGroup(g.Path);
        }

        public void RemoveFromGroup(Group g, GroupApp a)
        {
            g.Data.Apps.Remove(a);
            Store.Save(g);
            if (popup != null && string.Equals(popup.GroupPath, g.Path, StringComparison.OrdinalIgnoreCase))
                popup.DataContext = new PopupVM(this, Store.Find(g.Path) ?? g);
        }

        // ---- groups ----

        public void SaveGroup(Group editing, string name, List<GroupApp> apps, int cols, int rows, string color)
        {
            try
            {
                if (editing == null) Store.Create(name, apps, cols, rows, color);
                else Store.Update(Store.Find(editing.Path) ?? editing, name, apps, cols, rows, color);
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
            Main.RefreshGroups();
            SyncWidgets();
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
            SyncWidgets(); // widget colors come from the theme and accent
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                string sig = Theme.Signature();
                if (sig != themeSignature)
                {
                    themeSignature = sig;
                    ApplyTheme();
                }
                double w = DesktopGrid.CellW, h = DesktopGrid.CellH;
                DesktopGrid.Refresh();
                if (w != DesktopGrid.CellW || h != DesktopGrid.CellH) SyncWidgets();
            }));
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var w in widgets.Values) w.EnsureOnScreen();
            }));
        }

        public void Quit()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            Shutdown();
        }

        public void RemoveFromPc()
        {
            ClosePopup();
            foreach (var w in widgets.Values.ToList()) w.Close();
            widgets.Clear();
            Installer.RemoveFromPc();
            MessageBox.Show("WinGroups has been removed from this PC. You can delete WinGroups.exe now.",
                "WinGroups", MessageBoxButton.OK, MessageBoxImage.Information);
            Quit();
        }
    }
}
