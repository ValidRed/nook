using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace WinGroups
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void Raise([CallerMemberName] string name = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }
    }

    public class Relay : ICommand
    {
        readonly Action<object> run;
        readonly Func<object, bool> can;

        public Relay(Action run) : this(o => run(), null) { }
        public Relay(Action<object> run, Func<object, bool> can) { this.run = run; this.can = can; }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object p) { return can == null || can(p); }
        public void Execute(object p) { run(p); }
    }

    public class IconSlot : Observable
    {
        ImageSource icon;
        public ImageSource Icon { get { return icon; } set { Set(ref icon, value); } }
        public bool IsEmpty { get; set; }

        public static ObservableCollection<IconSlot> PreviewOf(IList<GroupApp> apps, int size)
        {
            var list = new ObservableCollection<IconSlot>();
            for (int i = 0; i < 4; i++)
            {
                var slot = new IconSlot { IsEmpty = i >= apps.Count };
                if (i < apps.Count)
                {
                    var s = slot;
                    AppIcons.Request(apps[i], size, b => s.Icon = b);
                }
                list.Add(slot);
            }
            return list;
        }
    }

    public class GroupVM : Observable
    {
        public Group Model { get; private set; }
        public string Name { get; private set; }
        public string CountText { get; private set; }
        public ObservableCollection<IconSlot> Preview { get; private set; }
        public bool IsFolderStyle { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }

        public GroupVM(MainVM main, Group g, bool folder)
        {
            Model = g;
            Name = g.Name;
            int n = g.Data.Apps.Count;
            CountText = (n == 1 ? "1 app" : n + " apps") + "  ·  " + g.Data.Cols + "×" + g.Data.Rows + " widget";
            Preview = IconSlot.PreviewOf(g.Data.Apps, 64);
            IsFolderStyle = folder;
            EditCommand = new Relay(() => main.StartEdit(g));
            RemoveCommand = new Relay(() => main.AskRemove(this));
        }
    }

    public class AddGroupVM
    {
        public ICommand Command { get; set; }
    }

    public class PickVM : Observable
    {
        readonly EditorVM owner;
        bool selected;
        ImageSource icon;

        public CatalogApp App { get; private set; }
        public GroupApp Existing { get; private set; }
        public string Name { get; private set; }
        public string Source { get; private set; }

        public ImageSource Icon { get { return icon; } set { Set(ref icon, value); } }

        public bool IsSelected
        {
            get { return selected; }
            set { if (Set(ref selected, value)) owner.SelectionChanged(this); }
        }

        public PickVM(EditorVM owner, CatalogApp app)
        {
            this.owner = owner;
            App = app;
            Name = app.Name;
            Source = app.Source;
            IconLoader.Request(app.Source, () => app.IconPath, 64, b => Icon = b);
        }

        public PickVM(EditorVM owner, GroupApp existing)
        {
            this.owner = owner;
            Existing = existing;
            Name = existing.Name;
            Source = existing.Source ?? AppIcons.Key(existing);
            AppIcons.Request(existing, 64, b => Icon = b);
        }

        public void SetSelectedQuietly(bool value) { selected = value; Raise("IsSelected"); }

        public GroupApp ToGroupApp()
        {
            return Existing ?? GroupApp.FromCatalog(App);
        }
    }

    public class EditorVM : Observable
    {
        readonly MainVM main;
        readonly List<PickVM> all = new List<PickVM>();
        readonly List<PickVM> order = new List<PickVM>();
        readonly List<PickVM> extras = new List<PickVM>();
        string name = "", search = "";
        bool loading = true;

        public Group Editing { get; private set; }
        public string Title { get { return Editing == null ? "New group" : "Edit group"; } }
        public string SaveLabel { get { return Editing == null ? "Create group" : "Save changes"; } }

        public ObservableCollection<PickVM> CurrentApps { get; private set; }
        public ObservableCollection<PickVM> DesktopApps { get; private set; }
        public ObservableCollection<PickVM> InstalledApps { get; private set; }
        public ObservableCollection<PickVM> WindowsApps { get; private set; }
        public ObservableCollection<IconSlot> Preview { get; private set; }

        public bool HasCurrent { get { return CurrentApps.Count > 0; } }
        public bool HasDesktop { get { return DesktopApps.Count > 0; } }
        public bool HasInstalled { get { return InstalledApps.Count > 0; } }
        public bool HasWindows { get { return WindowsApps.Count > 0; } }
        public bool NoMatches { get { return !loading && !HasCurrent && !HasDesktop && !HasInstalled && !HasWindows; } }
        public bool IsLoading { get { return loading; } }
        public string SelectedText { get { return order.Count + " selected"; } }

        public string Name
        {
            get { return name; }
            set { if (Set(ref name, value ?? "")) CommandManager.InvalidateRequerySuggested(); }
        }

        public string Search
        {
            get { return search; }
            set { if (Set(ref search, value ?? "")) Filter(); }
        }

        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }
        public ICommand BrowseCommand { get; private set; }

        public EditorVM(MainVM main, Group editing)
        {
            this.main = main;
            Editing = editing;
            CurrentApps = new ObservableCollection<PickVM>();
            DesktopApps = new ObservableCollection<PickVM>();
            InstalledApps = new ObservableCollection<PickVM>();
            WindowsApps = new ObservableCollection<PickVM>();
            Preview = IconSlot.PreviewOf(new List<GroupApp>(), 64);
            SizeCells = new ObservableCollection<SizeCell>();
            for (int i = 0; i < GroupData.MaxSize * GroupData.MaxSize; i++) SizeCells.Add(new SizeCell());
            if (editing != null)
            {
                name = editing.Name;
                cols = editing.Data.Cols;
                rows = editing.Data.Rows;
            }
            SaveCommand = new Relay(o => Save(), o => Name.Trim().Length > 0);
            CancelCommand = new Relay(() => main.Page = "home");
            BrowseCommand = new Relay(Browse);
            SetSizeCommand = new Relay(o => { int n; if (int.TryParse(o as string, out n)) SetSize(n, n); }, null);
            SetSize(cols, rows);
        }

        // ---- widget size, in desktop icon spaces ----
        int cols = 1, rows = 1;
        string widthText = "1", lengthText = "1";

        public int Cols { get { return cols; } }
        public int Rows { get { return rows; } }
        public ObservableCollection<SizeCell> SizeCells { get; private set; }
        public ICommand SetSizeCommand { get; private set; }
        public bool Is1 { get { return cols == 1 && rows == 1; } }
        public bool Is2 { get { return cols == 2 && rows == 2; } }
        public bool Is3 { get { return cols == 3 && rows == 3; } }
        public string SizeText { get { return cols + " × " + rows; } }

        // Text fields accept 1-3; anything bigger is capped at 3.
        public string WidthText
        {
            get { return widthText; }
            set
            {
                int n;
                if (int.TryParse((value ?? "").Trim(), out n)) SetSize(n, rows);
                else { widthText = value; Raise(); }
            }
        }

        public string LengthText
        {
            get { return lengthText; }
            set
            {
                int n;
                if (int.TryParse((value ?? "").Trim(), out n)) SetSize(cols, n);
                else { lengthText = value; Raise(); }
            }
        }

        void SetSize(int c, int r)
        {
            cols = GroupData.ClampSize(c);
            rows = GroupData.ClampSize(r);
            widthText = cols.ToString();
            lengthText = rows.ToString();
            for (int i = 0; i < SizeCells.Count; i++)
                SizeCells[i].IsOn = (i % GroupData.MaxSize) < cols && (i / GroupData.MaxSize) < rows;
            Raise("WidthText"); Raise("LengthText"); Raise("Is1"); Raise("Is2"); Raise("Is3"); Raise("SizeText");
        }

        public void SetCatalog(List<CatalogApp> catalog)
        {
            var keep = new HashSet<string>(order.Select(p => p.Source));
            bool first = all.Count == 0 && extras.Count == 0;
            all.Clear();
            order.Clear();
            foreach (var c in catalog) all.Add(new PickVM(this, c));

            if (first && Editing != null)
            {
                foreach (var a in Editing.Data.Apps)
                {
                    var match = a.Source == null ? null : all.FirstOrDefault(p => p.Source == a.Source && !order.Contains(p));
                    if (match == null)
                    {
                        match = new PickVM(this, a);
                        extras.Add(match);
                    }
                    match.SetSelectedQuietly(true);
                    order.Add(match);
                }
            }
            else
            {
                foreach (var e in extras) if (e.IsSelected) { order.Add(e); keep.Remove(e.Source); }
                foreach (var p in all) if (keep.Contains(p.Source)) { p.SetSelectedQuietly(true); order.Add(p); }
            }
            loading = false;
            Raise("IsLoading");
            Filter();
            UpdateSelection();
        }

        void Filter()
        {
            string q = search.Trim();
            Func<PickVM, bool> ok = p => q.Length == 0 || p.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
            Fill(CurrentApps, extras.Where(ok));
            Fill(DesktopApps, all.Where(p => p.App.Section == "desktop" && ok(p)));
            Fill(InstalledApps, all.Where(p => p.App.Section == "installed" && ok(p)));
            Fill(WindowsApps, all.Where(p => p.App.Section == "windows" && ok(p)));
            Raise("HasCurrent"); Raise("HasDesktop"); Raise("HasInstalled"); Raise("HasWindows"); Raise("NoMatches");
        }

        static void Fill(ObservableCollection<PickVM> target, IEnumerable<PickVM> items)
        {
            target.Clear();
            foreach (var i in items) target.Add(i);
        }

        internal void SelectionChanged(PickVM p)
        {
            if (p.IsSelected) { if (!order.Contains(p)) order.Add(p); }
            else order.Remove(p);
            UpdateSelection();
        }

        void UpdateSelection()
        {
            Raise("SelectedText");
            for (int i = 0; i < 4; i++)
            {
                var slot = Preview[i];
                slot.IsEmpty = i >= order.Count;
                slot.Icon = i < order.Count ? order[i].Icon : null;
            }
            // icons may still be loading; refresh the preview once they arrive
            foreach (var p in order.Take(4))
            {
                if (p.Icon == null)
                {
                    var pick = p;
                    PropertyChangedEventHandler h = null;
                    h = (s, e) =>
                    {
                        if (e.PropertyName != "Icon") return;
                        pick.PropertyChanged -= h;
                        int idx = order.IndexOf(pick);
                        if (idx >= 0 && idx < 4) Preview[idx].Icon = pick.Icon;
                    };
                    pick.PropertyChanged += h;
                }
            }
        }

        void Browse()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add an app",
                Filter = "Apps and shortcuts|*.exe;*.lnk;*.url;*.appref-ms|All files|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            foreach (var f in dlg.FileNames)
            {
                try
                {
                    var p = new PickVM(this, GroupApp.FromFile(f));
                    extras.Add(p);
                    p.IsSelected = true;
                }
                catch { }
            }
            Filter();
        }

        void Save()
        {
            var apps = new List<GroupApp>();
            foreach (var p in order)
            {
                try { apps.Add(p.ToGroupApp()); } catch { }
            }
            main.SaveEditor(this, Name.Trim(), apps, cols, rows);
        }
    }

    public class SizeCell : Observable
    {
        bool on;
        public bool IsOn { get { return on; } set { Set(ref on, value); } }
    }

    // One slot inside a larger widget: an app, or "+N" when more apps than fit.
    public class WidgetSlotVM : Observable
    {
        ImageSource icon;
        public string Name { get; set; }
        public ImageSource Icon { get { return icon; } set { Set(ref icon, value); } }
        public bool IsApp { get; set; }
        public bool IsMore { get { return !IsApp; } }
        public string MoreText { get; set; }
        public bool ShowName { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double IconSize { get; set; }
        public ICommand Command { get; set; }
        public ICommand AdminCommand { get; set; }
        public ICommand RemoveCommand { get; set; }
    }

    // A group shown on the desktop. 1x1 looks like a desktop icon and opens the pop-up;
    // anything bigger shows the apps themselves, one click to launch.
    public class WidgetVM : Observable
    {
        // Must match Widget.xaml: panel margin, border, padding and header height.
        const double Margin = 4, Border = 1, Padding = 6, Header = 28;

        public string Name { get; private set; }
        public bool IsCompact { get; private set; }
        public bool IsPanel { get { return !IsCompact; } }
        public bool IsFolderStyle { get; private set; }
        public ObservableCollection<IconSlot> Preview { get; private set; }
        public List<WidgetSlotVM> Slots { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand OpenHubCommand { get; private set; }
        public ICommand CloseAllCommand { get; private set; }

        public WidgetVM(App app, Group g)
        {
            var d = g.Data;
            Name = g.Name;
            IsCompact = d.Cols == 1 && d.Rows == 1;
            IsFolderStyle = app.Settings.IconStyle == "folder";
            Width = DesktopGrid.WidthFor(d.Cols);
            Height = DesktopGrid.HeightFor(d.Rows);
            OpenCommand = new Relay(() => app.OpenFromWidget(g.Path));
            EditCommand = new Relay(() => app.EditGroup(g.Path));
            OpenHubCommand = new Relay(() => app.ShowHome());
            CloseAllCommand = new Relay(() => app.Quit());
            Preview = IsCompact ? IconSlot.PreviewOf(d.Apps, 64) : new ObservableCollection<IconSlot>();
            Slots = IsCompact ? new List<WidgetSlotVM>() : Layout(app, g);
        }

        List<WidgetSlotVM> Layout(App app, Group g)
        {
            double innerW = Width - 2 * (Margin + Border + Padding);
            double innerH = Height - 2 * (Margin + Border + Padding) - Header;
            bool names = app.Settings.ShowNames;
            int colsFit = Math.Max(1, (int)Math.Floor(innerW / 60));
            int rowsFit = Math.Max(1, (int)Math.Floor(innerH / (names ? 66 : 50)));
            double slotW = Math.Floor(innerW / colsFit), slotH = Math.Floor(innerH / rowsFit);
            if (slotH < 62) names = false;
            double iconSize = Math.Max(16, Math.Min(32, Math.Min(slotW - 14, slotH - (names ? 30 : 10))));

            var apps = g.Data.Apps;
            int capacity = colsFit * rowsFit;
            int shown = apps.Count > capacity ? capacity - 1 : apps.Count;
            var list = new List<WidgetSlotVM>();
            for (int i = 0; i < shown; i++)
            {
                var a = apps[i];
                var slot = new WidgetSlotVM
                {
                    Name = a.Name, IsApp = true, ShowName = names, Width = slotW, Height = slotH, IconSize = iconSize,
                    Command = new Relay(() => app.Launch(a, false)),
                    AdminCommand = new Relay(() => app.Launch(a, true)),
                    RemoveCommand = new Relay(() => app.RemoveFromGroup(g, a))
                };
                AppIcons.Request(a, 64, b => slot.Icon = b);
                list.Add(slot);
            }
            if (apps.Count > shown)
            {
                list.Add(new WidgetSlotVM
                {
                    IsApp = false, MoreText = "+" + (apps.Count - shown), Name = "Show all apps", ShowName = false,
                    Width = slotW, Height = slotH, IconSize = iconSize, Command = OpenCommand
                });
            }
            return list;
        }
    }

    public class PopupAppVM : Observable
    {
        ImageSource icon;
        public string Name { get; private set; }
        public ImageSource Icon { get { return icon; } set { Set(ref icon, value); } }
        public ICommand LaunchCommand { get; private set; }
        public ICommand AdminCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }

        public PopupAppVM(App app, Group g, GroupApp a)
        {
            Name = a.Name;
            AppIcons.Request(a, 96, b => Icon = b);
            LaunchCommand = new Relay(() => app.Launch(a, false));
            AdminCommand = new Relay(() => app.Launch(a, true));
            RemoveCommand = new Relay(() => app.RemoveFromGroup(g, a));
        }
    }

    public class PopupVM : Observable
    {
        public string Name { get; private set; }
        public ObservableCollection<PopupAppVM> Apps { get; private set; }
        public int Columns { get; private set; }
        public double PanelWidth { get { return Columns * 100; } }
        public bool ShowNames { get; private set; }
        public bool IsEmpty { get { return Apps.Count == 0; } }
        public bool HasApps { get { return Apps.Count > 0; } }
        public ICommand EditCommand { get; private set; }

        public PopupVM(App app, Group g)
        {
            Name = g.Name;
            Apps = new ObservableCollection<PopupAppVM>(g.Data.Apps.Select(a => new PopupAppVM(app, g, a)));
            Columns = Math.Max(1, Math.Min(4, Apps.Count));
            ShowNames = app.Settings.ShowNames;
            EditCommand = new Relay(() => app.EditFromPopup(g));
        }
    }

    public class MainVM : Observable
    {
        readonly App app;
        string page = "home";
        bool paneOpen, dialogOpen;
        EditorVM editor;
        Action dialogAction;

        public MainVM(App app)
        {
            this.app = app;
            HomeItems = new ObservableCollection<object>();
            AppIcon = IconRenderer.RenderAppIcon(32);
            TogglePaneCommand = new Relay(() => PaneOpen = !PaneOpen);
            GoHomeCommand = new Relay(() => { Page = "home"; PaneOpen = false; });
            GoSettingsCommand = new Relay(() => { Page = "settings"; PaneOpen = false; });
            NewGroupCommand = new Relay(() => StartEdit(null));
            DialogConfirmCommand = new Relay(() => { DialogOpen = false; var a = dialogAction; dialogAction = null; if (a != null) a(); });
            DialogCancelCommand = new Relay(() => { DialogOpen = false; dialogAction = null; });
            OpenReleasesCommand = new Relay(() => { try { Process.Start(Program.ReleasesUrl); } catch { } });
            RemoveFromPcCommand = new Relay(() => Ask(
                "Remove WinGroups from this PC?",
                "This deletes your groups and their widgets, turns off Start with Windows and removes WinGroups' settings. Your desktop icons and apps aren't touched.",
                "Remove", () => app.RemoveFromPc()));
        }

        public ImageSource AppIcon { get; private set; }
        public ObservableCollection<object> HomeItems { get; private set; }
        public string VersionText { get { return "Version " + Program.Version; } }

        public string Page
        {
            get { return page; }
            set
            {
                if (!Set(ref page, value)) return;
                Raise("IsHome"); Raise("IsEditor"); Raise("IsSettings"); Raise("IsHomeNav"); Raise("IsSettingsNav");
            }
        }
        public bool IsHome { get { return page == "home"; } }
        public bool IsEditor { get { return page == "editor"; } }
        public bool IsSettings { get { return page == "settings"; } }
        public bool IsHomeNav { get { return page != "settings"; } }
        public bool IsSettingsNav { get { return page == "settings"; } }

        public bool PaneOpen { get { return paneOpen; } set { Set(ref paneOpen, value); } }
        public EditorVM Editor { get { return editor; } private set { Set(ref editor, value); } }

        public ICommand TogglePaneCommand { get; private set; }
        public ICommand GoHomeCommand { get; private set; }
        public ICommand GoSettingsCommand { get; private set; }
        public ICommand NewGroupCommand { get; private set; }
        public ICommand DialogConfirmCommand { get; private set; }
        public ICommand DialogCancelCommand { get; private set; }
        public ICommand OpenReleasesCommand { get; private set; }
        public ICommand RemoveFromPcCommand { get; private set; }

        // Raised so the window can play the card's exit animation before the group is deleted.
        public event Action<GroupVM, Action> RemoveRequested;

        public void RefreshGroups()
        {
            HomeItems.Clear();
            bool folder = app.Settings.IconStyle == "folder";
            foreach (var g in app.Store.Groups) HomeItems.Add(new GroupVM(this, g, folder));
            HomeItems.Add(new AddGroupVM { Command = NewGroupCommand });
        }

        public void StartEdit(Group g)
        {
            var ed = new EditorVM(this, g);
            Editor = ed;
            Page = "editor";
            PaneOpen = false;
            app.GetCatalog(list => { if (Editor == ed) ed.SetCatalog(list); });
        }

        public void SaveEditor(EditorVM ed, string name, List<GroupApp> apps, int cols, int rows)
        {
            app.SaveGroup(ed.Editing, name, apps, cols, rows);
            Page = "home";
        }

        public void AskRemove(GroupVM g)
        {
            Ask("Are you sure you want to delete this group?",
                "“" + g.Name + "” will be removed from WinGroups and your desktop. The apps inside stay installed, and your other desktop icons aren't touched.",
                "Delete group",
                () =>
                {
                    var h = RemoveRequested;
                    if (h != null) h(g, () => app.DeleteGroup(g.Model));
                    else app.DeleteGroup(g.Model);
                });
        }

        // ---- dialog ----
        string dialogTitle, dialogMessage, dialogPrimary;
        public string DialogTitle { get { return dialogTitle; } private set { Set(ref dialogTitle, value); } }
        public string DialogMessage { get { return dialogMessage; } private set { Set(ref dialogMessage, value); } }
        public string DialogPrimary { get { return dialogPrimary; } private set { Set(ref dialogPrimary, value); } }
        public bool DialogOpen { get { return dialogOpen; } set { Set(ref dialogOpen, value); } }

        void Ask(string title, string message, string primary, Action onConfirm)
        {
            DialogTitle = title;
            DialogMessage = message;
            DialogPrimary = primary;
            dialogAction = onConfirm;
            DialogOpen = true;
        }

        // ---- settings ----
        public bool StartWithWindows
        {
            get { return app.Settings.StartWithWindows; }
            set { app.Settings.StartWithWindows = value; app.Settings.Save(); Installer.SetStartup(value); Raise(); }
        }

        public bool CloseAfterLaunch
        {
            get { return app.Settings.CloseAfterLaunch; }
            set { app.Settings.CloseAfterLaunch = value; app.Settings.Save(); Raise(); }
        }

        public bool ShowNames
        {
            get { return app.Settings.ShowNames; }
            set { app.Settings.ShowNames = value; app.Settings.Save(); app.SyncWidgets(); Raise(); }
        }

        public int ThemeIndex
        {
            get { return app.Settings.Theme == "light" ? 1 : app.Settings.Theme == "dark" ? 2 : 0; }
            set
            {
                string t = value == 1 ? "light" : value == 2 ? "dark" : "windows";
                if (t == app.Settings.Theme) return;
                app.Settings.Theme = t;
                app.Settings.Save();
                app.ApplyTheme();
                Raise();
            }
        }

        public int IconStyleIndex
        {
            get { return app.Settings.IconStyle == "folder" ? 1 : 0; }
            set
            {
                string s = value == 1 ? "folder" : "grid";
                if (s == app.Settings.IconStyle) return;
                app.Settings.IconStyle = s;
                app.Settings.Save();
                app.RewriteIcons();
                Raise();
            }
        }

        public string ModeLabel { get { return app.EffectiveDark ? "Dark mode" : "Light mode"; } }

        public void ThemeChanged() { Raise("ModeLabel"); }
    }
}
