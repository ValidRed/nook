using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WinGroups
{
    public class App : Application
    {
        public static App Instance { get; private set; }

        public Settings Settings { get; private set; }
        internal GroupStore Store { get; private set; }
        public MainVM Main { get; private set; }
        internal Updater Updater { get; private set; }

        HomeWindow home;
        PopupWindow popup;
        SearchWindow search;
        IslandWindow island;
        readonly List<DesktopClock> clocks = new List<DesktopClock>();
        readonly Dictionary<string, WidgetWindow> widgets = new Dictionary<string, WidgetWindow>(StringComparer.OrdinalIgnoreCase);
        List<CatalogApp> catalog;
        DateTime catalogTime;
        bool catalogLoading;
        readonly List<Action<List<CatalogApp>>> catalogWaiters = new List<Action<List<CatalogApp>>>();
        string themeSignature;
        DispatcherTimer keeper, wallpaperWatch, blurDebounce;
        HwndSource hotkeyWindow;
        bool hotkeyOn;
        const int HotkeyId = 0x5747;

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
            Theme.Apply(Resources, EffectiveDark, Settings.Accent);
            themeSignature = Theme.Signature(Settings.Accent);
            DesktopGrid.Refresh();
            Wallpaper.Load(Settings.Blur);
            GroupStore.EmptyTrash();
            Store = new GroupStore();
            Updater = new Updater(this);
            Updater.Changed += () => Main.UpdateChanged();
            Updater.StartAutomatic();
            Main = new MainVM(this);
            Store.MigrateLegacy();
            if (Installer.HasLegacyFileType) Installer.RemoveLegacyFileType();
            Store.Changed += () => { Main.RefreshGroups(); SyncWidgets(); };
            Store.Reload();
            Store.StartWatching();
            Installer.RefreshStartup(Settings.StartWithWindows);
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            ApplyHotkey();

            // Widgets stay directly above the desktop, including after Win+D raises it over your apps.
            keeper = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            keeper.Tick += (s, e) =>
            {
                foreach (var w in widgets.Values) w.KeepAboveDesktop();
                foreach (var c in clocks) c.KeepAboveDesktop();
            };
            keeper.Start();

            // Picks up wallpaper changes (including slideshows) for the widgets' frosted glass.
            wallpaperWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            wallpaperWatch.Tick += (s, e) => CheckWallpaper();
            wallpaperWatch.Start();
        }

        // Used by --dev-render: in-memory groups, no files, timers or shortcuts.
        internal void InitPreview(List<Group> groups, List<CatalogApp> apps, bool dark)
        {
            Settings = new Settings { Theme = dark ? "dark" : "light" };
            Updater = new Updater(this);
            Theme.Apply(Resources, dark, Settings.Accent);
            Wallpaper.Load(Settings.Blur);
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

        // The tint laid over Windows' frosted glass in pop-ups: weaker blur setting = more solid.
        public Brush GlassTint()
        {
            var fallback = TryFindResource("WindowFallback") as SolidColorBrush;
            var c = fallback == null ? Colors.Black : fallback.Color;
            byte alpha = (byte)Math.Round((1 - Settings.Blur / 100.0) * 0.7 * 255);
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        // "--widgets" (used at sign-in) shows only the widgets; anything else also opens this window.
        public void HandleCommand(List<string> args)
        {
            string cmd = args.Count == 0 ? "show" : args[0];
            SyncWidgets();
            SyncIsland();
            SyncClock();
            if (cmd != "--widgets") ShowHome();
            if (cmd == "--updated") Main.ShowUndo("Updated to version " + Program.Version, null, null);
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
            int fresh = 0;
            foreach (var g in Store.Groups)
            {
                WidgetWindow w;
                if (widgets.TryGetValue(g.Path, out w)) w.Update(g);
                else
                {
                    w = new WidgetWindow(this, g) { AppearIndex = fresh++ };
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

        // A newer version turned up: say so in WinGroups and add "Update" to the widget and island menus.
        public void UpdateFound()
        {
            SyncWidgets(); // widgets rebuild their menus
            if (island != null) island.Model.UpdateChanged();
            if (home != null) Main.ShowUndo("WinGroups " + Updater.NewVersion + " is available (Settings › Updates)", null, null);
        }

        readonly HashSet<string> runningWorkspaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Opens the workspace's apps and lays them out. A second click while it's busy does nothing.
        public void RunWorkspace(Group g)
        {
            if (!runningWorkspaces.Add(g.Path)) return;
            WidgetWindow w;
            if (widgets.TryGetValue(g.Path, out w)) w.SetBusy(true);
            new WorkspaceRun(g).Start(() =>
            {
                runningWorkspaces.Remove(g.Path);
                WidgetWindow ww;
                if (widgets.TryGetValue(g.Path, out ww)) ww.SetBusy(false);
            });
        }

        public void OpenFromWidget(string path)
        {
            var g = Store.Find(path);
            if (g == null) return;
            if (g.IsWorkspace) { RunWorkspace(g); return; }
            WidgetWindow w;
            Rect? anchor = widgets.TryGetValue(path, out w) ? w.ScreenRect : (Rect?)null;
            ShowPopup(g, anchor);
        }

        internal void AddToGroup(string path, List<string> files)
        {
            var g = Store.Find(path);
            if (g != null && files.Count > 0) Store.AddFiles(g, files);
        }

        internal void MoveApp(string fromPath, int index, string toPath, int insertAt)
        {
            var from = Store.Find(fromPath);
            var to = Store.Find(toPath);
            if (from != null && to != null) Store.MoveApp(from, index, to, insertAt);
        }

        public void ToggleLock()
        {
            Settings.LockWidgets = !Settings.LockWidgets;
            Settings.Save();
            SyncWidgets(); // updates the "Lock widgets" menu label
            Main.LockChanged();
        }

        // ---- island ----

        public void SyncIsland()
        {
            if (Settings.IslandOn && island == null)
            {
                var w = new IslandWindow(this);
                w.Closed += (s, e) => { if (island == w) island = null; };
                island = w;
                w.Show();
            }
            else if (!Settings.IslandOn && island != null)
            {
                var w = island;
                island = null;
                w.Close();
            }
            else if (island != null) island.ApplySettings();
        }

        // ---- desktop clock ----

        // One clock per chosen screen. New ones open before old ones close, so WinGroups never runs out of windows.
        public void SyncClock()
        {
            var all = Screens.All();
            var want = new List<string>();
            if (Settings.ClockOn)
            {
                foreach (var s in all) if (Settings.ClockScreens.Contains(s.Device)) want.Add(s.Device);
                if (want.Count == 0) { var main = Screens.Primary(all); if (main != null) want.Add(main.Device); }
            }
            var old = clocks.Where(c => !want.Contains(c.Device)).ToList();
            foreach (var d in want)
            {
                var c = clocks.FirstOrDefault(x => x.Device == d);
                if (c != null) { c.ApplySettings(); continue; }
                c = new DesktopClock(this, d);
                clocks.Add(c);
                c.Show();
            }
            foreach (var c in old) { clocks.Remove(c); c.Close(); }
        }

        public void SetIslandOn(bool on)
        {
            if (Settings.IslandOn == on) return;
            Settings.IslandOn = on;
            Settings.Save();
            SyncIsland();
            Main.IslandChanged();
        }

        public void OpenIslandSettings()
        {
            ShowHome();
            Main.Page = "island";
            Main.PaneOpen = false;
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

        public void ShowSearch()
        {
            if (search != null) { search.Activate(); return; }
            var vm = new SearchVM(this, Store.Groups);
            var w = new SearchWindow(vm);
            vm.Done = () => w.Close();
            w.Closed += (s, e) => { if (search == w) search = null; };
            search = w;
            w.Show();
            w.Activate();
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

        public void SaveGroup(Group editing, GroupData look)
        {
            try
            {
                if (editing == null) Store.Create(look);
                else Store.Update(Store.Find(editing.Path) ?? editing, look);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't save the group.\n\n" + ex.Message, "WinGroups", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Deleted groups can be brought back for a few seconds with Undo.
        public void DeleteGroup(Group g)
        {
            if (popup != null && string.Equals(popup.GroupPath, g.Path, StringComparison.OrdinalIgnoreCase)) ClosePopup();
            string original = g.Path, name = g.Name;
            string trashed = Store.Trash(g);
            if (trashed == null) return;
            Main.ShowUndo("“" + name + "” deleted",
                () => Store.Restore(trashed, original),
                () => { try { File.Delete(trashed); } catch { } });
        }

        public void RewriteIcons()
        {
            Main.RefreshGroups();
            SyncWidgets();
        }

        public void ExportGroups()
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export groups",
                FileName = "WinGroups backup " + DateTime.Now.ToString("yyyy-MM-dd"),
                DefaultExt = ".wgbackup",
                Filter = "WinGroups backup|*.wgbackup"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                Store.Export(dlg.FileName);
                Main.ShowUndo("Exported " + Store.Groups.Count + (Store.Groups.Count == 1 ? " group" : " groups"), null, null);
            }
            catch (Exception ex) { MessageBox.Show("Couldn't export.\n\n" + ex.Message, "WinGroups", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        public void ImportGroups()
        {
            var dlg = new OpenFileDialog { Title = "Import groups", Filter = "WinGroups backup|*.wgbackup|All files|*.*" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                int n = Store.Import(dlg.FileName);
                Main.ShowUndo("Imported " + n + (n == 1 ? " group" : " groups"), null, null);
            }
            catch (Exception ex) { MessageBox.Show("Couldn't import that file.\n\n" + ex.Message, "WinGroups", MessageBoxButton.OK, MessageBoxImage.Warning); }
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
            Theme.Apply(Resources, EffectiveDark, Settings.Accent);
            themeSignature = Theme.Signature(Settings.Accent);
            Main.ThemeChanged();
            if (home != null) home.ApplyEffects();
            if (popup != null) popup.ApplyEffects();
            SyncWidgets(); // widget colors come from the theme and accent
        }

        // Re-blurring the wallpaper takes a moment, so wait until the slider settles.
        public void ApplyBlurSoon()
        {
            if (blurDebounce == null)
            {
                blurDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                blurDebounce.Tick += (s, e) =>
                {
                    blurDebounce.Stop();
                    Wallpaper.SetBlur(Settings.Blur);
                    foreach (var w in widgets.Values) w.RefreshGlass();
                    if (popup != null) popup.ApplyEffects();
                };
            }
            blurDebounce.Stop();
            blurDebounce.Start();
        }

        void CheckWallpaper()
        {
            if (Wallpaper.Probe() == Wallpaper.Signature) return;
            Wallpaper.Load(Settings.Blur);
            foreach (var w in widgets.Values) w.RefreshGlass();
            foreach (var c in clocks) c.Place(); // picks a readable color for the new wallpaper
        }

        public string HotkeyStatus { get; private set; }

        // Ctrl+Alt+Space opens quick search (a standard Windows shortcut registration; nothing watches the keyboard).
        public void ApplyHotkey()
        {
            if (hotkeyWindow == null)
            {
                var p = new HwndSourceParameters("WinGroupsShortcut") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
                hotkeyWindow = new HwndSource(p);
                hotkeyWindow.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId) { ShowSearch(); handled = true; }
                    return IntPtr.Zero;
                });
            }
            if (hotkeyOn) { Native.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId); hotkeyOn = false; }
            if (Settings.QuickSearch)
            {
                hotkeyOn = Native.RegisterHotKey(hotkeyWindow.Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, Native.VK_SPACE);
                HotkeyStatus = hotkeyOn ? "Press Ctrl + Alt + Space to search your groups and apps"
                                        : "Ctrl + Alt + Space is already used by another app";
            }
            else HotkeyStatus = "Search your groups and apps with Ctrl + Alt + Space";
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Theme.Signature(Settings.Accent) != themeSignature) ApplyTheme();
                double w = DesktopGrid.CellW, h = DesktopGrid.CellH;
                DesktopGrid.Refresh();
                if (w != DesktopGrid.CellW || h != DesktopGrid.CellH) SyncWidgets();
                if (e.Category == UserPreferenceCategory.Desktop) CheckWallpaper();
            }));
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Wallpaper.Load(Settings.Blur);
                foreach (var w in widgets.Values) w.EnsureOnScreen();
                if (island != null) island.Place();
                SyncClock();
            }));
        }

        public void Quit()
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            if (keeper != null) keeper.Stop();
            if (wallpaperWatch != null) wallpaperWatch.Stop();
            if (hotkeyWindow != null)
            {
                if (hotkeyOn) Native.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
                hotkeyWindow.Dispose();
                hotkeyWindow = null;
            }
            Main.FinishToast();
            Shutdown();
        }

        public void RemoveFromPc()
        {
            ClosePopup();
            foreach (var w in widgets.Values.ToList()) w.Close();
            widgets.Clear();
            if (island != null) { island.Close(); island = null; }
            foreach (var c in clocks.ToList()) c.Close();
            clocks.Clear();
            Installer.RemoveFromPc();
            MessageBox.Show("WinGroups has been removed from this PC. You can delete WinGroups.exe now.",
                "WinGroups", MessageBoxButton.OK, MessageBoxImage.Information);
            Quit();
        }
    }
}
