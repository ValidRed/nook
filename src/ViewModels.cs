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
        public bool IsWorkspace { get; private set; }
        public bool IsGroup { get { return !IsWorkspace; } }
        public string Glyph { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }

        public GroupVM(MainVM main, Group g, bool folder)
        {
            Model = g;
            IsWorkspace = g.IsWorkspace;
            Glyph = g.Data.Icon ?? "";
            Name = IsWorkspace && string.IsNullOrEmpty(g.Name) ? "No name" : g.Name;
            int n = g.Data.Apps.Count;
            CountText = (n == 1 ? "1 app" : n + " apps") + "  ·  " +
                        (IsWorkspace ? LayoutPreset.Find(g.Data.Layout).Label : g.Data.Cols + "×" + g.Data.Rows + " widget");
            Preview = IsWorkspace ? new ObservableCollection<IconSlot>() : IconSlot.PreviewOf(g.Data.Apps, 64);
            IsFolderStyle = folder && !IsWorkspace;
            EditCommand = new Relay(() => main.StartEdit(g));
            RemoveCommand = new Relay(() => main.AskRemove(this));
        }
    }

    // A box on the Home page: one per section of the app.
    public class SectionVM : Observable
    {
        string status;
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Status { get { return status; } set { Set(ref status, value); } }
        public ICommand Command { get; set; }
    }

    public class ScreenCheckVM : Observable
    {
        readonly Action<bool> changed;
        bool on;
        public string Label { get; private set; }
        public bool IsChecked { get { return on; } set { if (Set(ref on, value)) changed(value); } }
        public ScreenCheckVM(string label, bool isOn, Action<bool> onChange) { Label = label; on = isOn; changed = onChange; }
    }

    public class AddGroupVM
    {
        public ICommand Command { get; set; }
        public string Label { get; set; }
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

        // Workspaces: which space of the layout this app goes into ("" = it just opens).
        string zone = "";
        public string ZoneText { get { return zone; } set { if (Set(ref zone, value ?? "")) Raise("HasZone"); } }
        public bool HasZone { get { return zone.Length > 0; } }

        public GroupApp ToGroupApp()
        {
            return Existing ?? GroupApp.FromCatalog(App);
        }
    }

    public class EditorVM : Observable
    {
        readonly MainVM main;
        readonly List<PickVM> all = new List<PickVM>();
        readonly ObservableCollection<PickVM> order = new ObservableCollection<PickVM>();
        readonly List<PickVM> extras = new List<PickVM>();
        string name = "", search = "";
        bool loading = true;

        public Group Editing { get; private set; }
        public bool IsWorkspace { get; private set; }
        public bool IsGroup { get { return !IsWorkspace; } }
        public string Title { get { return Editing == null ? (IsWorkspace ? "New workspace" : "New group") : (IsWorkspace ? "Edit workspace" : "Edit group"); } }
        public string SaveLabel { get { return Editing == null ? (IsWorkspace ? "Create workspace" : "Create group") : "Save changes"; } }
        public string OrderTitle { get { return IsWorkspace ? "In this workspace" : "In this group"; } }
        public string NameLabel { get { return IsWorkspace ? "Name (optional)" : "Group name"; } }
        public string NamePlaceholder { get { return IsWorkspace ? "No name" : "Name your group"; } }
        public string NameHint { get { return IsWorkspace ? "Shows under the button on your desktop. Leave it empty for just the icon." : "This name shows on the group's widget on your desktop."; } }
        public string ColorLabel { get { return IsWorkspace ? "Tint" : "Widget color"; } }
        public string OrderHint { get { return IsWorkspace ? "  ·  drag to change the order; numbers are the spaces in the layout" : "  ·  drag to change the order"; } }

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

        public EditorVM(MainVM main, Group editing, bool workspace = false)
        {
            this.main = main;
            Editing = editing;
            IsWorkspace = editing != null ? editing.IsWorkspace : workspace;
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
                showTitle = !editing.Data.HideTitle;
                iconsOnly = editing.Data.IconsOnly;
            }
            InitColors(editing == null ? "" : editing.Data.Color);
            InitWorkspace(editing);
            order.CollectionChanged += (s, e) => { UpdateSelection(); Raise("HasOrder"); };
            // A group needs a name; a workspace needs at least one app (its name is optional).
            SaveCommand = new Relay(o => Save(), o => IsWorkspace ? order.Count > 0 : Name.Trim().Length > 0);
            CancelCommand = new Relay(() => main.Page = IsWorkspace ? "workspaces" : "groups");
            BrowseCommand = new Relay(Browse);
            SetSizeCommand = new Relay(o => { int n; if (int.TryParse(o as string, out n)) SetSize(n, n); }, null);
            SetSize(cols, rows);
        }

        // Apps in the group, in order; drag in the editor to reorder.
        public ObservableCollection<PickVM> Order { get { return order; } }
        public bool HasOrder { get { return order.Count > 0; } }

        // ---- widget options ----
        bool showTitle = true, iconsOnly;
        public bool ShowTitle { get { return showTitle; } set { Set(ref showTitle, value); } }
        public bool IconsOnly { get { return iconsOnly; } set { Set(ref iconsOnly, value); } }

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
            UpdateZones();
            CommandManager.InvalidateRequerySuggested();
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
            if (IsWorkspace)
            {
                main.SaveEditor(this, new GroupData
                {
                    Kind = "workspace", Name = Name.Trim(), Apps = apps, Cols = cols >= 2 ? 2 : 1, Rows = cols >= 2 ? 2 : 1, Color = color,
                    Icon = icon, Layout = layout, Screen = ScreenDevice
                });
                return;
            }
            main.SaveEditor(this, new GroupData
            {
                Name = Name.Trim(), Apps = apps, Cols = cols, Rows = rows, Color = color,
                HideTitle = !showTitle, IconsOnly = iconsOnly
            });
        }

        // ---- workspace: icon, layout and screen ----
        string icon = WorkspaceIcons.Default, layout = "halves";
        List<ScreenInfo> screens = new List<ScreenInfo>();
        int screenIndex;

        public ObservableCollection<SwatchVM> Icons { get; private set; }
        public ObservableCollection<LayoutChoiceVM> Layouts { get; private set; }
        public List<string> ScreenChoices { get; private set; }
        public string Glyph { get { return icon; } }

        public int ScreenIndex { get { return screenIndex; } set { Set(ref screenIndex, Math.Max(0, value)); } }
        string ScreenDevice { get { return screenIndex <= 0 || screenIndex > screens.Count ? "" : screens[screenIndex - 1].Device; } }

        void InitWorkspace(Group editing)
        {
            Icons = new ObservableCollection<SwatchVM>();
            Layouts = new ObservableCollection<LayoutChoiceVM>();
            ScreenChoices = new List<string>();
            if (!IsWorkspace) return;
            if (editing != null)
            {
                icon = string.IsNullOrEmpty(editing.Data.Icon) ? WorkspaceIcons.Default : editing.Data.Icon;
                layout = LayoutPreset.Find(editing.Data.Layout).Id;
            }
            foreach (var p in WorkspaceIcons.All)
            {
                var value = p.Key;
                Icons.Add(new SwatchVM { Value = value, Label = p.Value, Short = value, Command = new Relay(() => SelectIcon(value)) });
            }
            foreach (var l in LayoutPreset.All)
            {
                var id = l.Id;
                Layouts.Add(new LayoutChoiceVM(l, new Relay(() => SelectLayout(id))));
            }
            screens = Screens.All();
            ScreenChoices.Add("Main screen");
            foreach (var s in screens) ScreenChoices.Add(s.Label);
            string dev = editing == null ? "" : editing.Data.Screen ?? "";
            int i = screens.FindIndex(s => string.Equals(s.Device, dev, StringComparison.OrdinalIgnoreCase));
            screenIndex = i < 0 ? 0 : i + 1;
            SelectIcon(icon);
            SelectLayout(layout);
        }

        void SelectIcon(string value)
        {
            icon = value;
            foreach (var i in Icons) i.IsSelected = i.Value == value;
            Raise("Glyph");
        }

        void SelectLayout(string id)
        {
            layout = id;
            foreach (var l in Layouts) l.IsSelected = l.Id == id;
            UpdateZones();
        }

        // Numbers each app with the space it will fill, so the order in the editor matches the layout.
        void UpdateZones()
        {
            if (!IsWorkspace) return;
            var preset = LayoutPreset.Find(layout);
            for (int i = 0; i < order.Count; i++)
                order[i].ZoneText = preset.Maximize || preset.Zones.Length == 0 || i >= preset.Zones.Length ? "" : (i + 1).ToString();
        }

        // ---- widget color ----
        string color = "", colorText = "";

        public ObservableCollection<SwatchVM> Swatches { get; private set; }

        // Custom color as #RRGGBB; applied as soon as it's a valid color.
        public string ColorText
        {
            get { return colorText; }
            set
            {
                colorText = value ?? "";
                Raise();
                Color c;
                if (WidgetColors.TryParseHex(colorText, out c)) SelectColor(WidgetColors.ToHex(c), false);
            }
        }

        void InitColors(string current)
        {
            Swatches = new ObservableCollection<SwatchVM>();
            foreach (var p in WidgetColors.Presets)
            {
                var value = p.Key;
                Swatches.Add(new SwatchVM
                {
                    Value = value,
                    Label = p.Value,
                    Short = value == "" ? "A" : "",
                    Brush = WidgetColors.SwatchBrush(value),
                    Command = new Relay(() => SelectColor(value, true))
                });
            }
            SelectColor(current ?? "", true);
        }

        void SelectColor(string value, bool updateText)
        {
            color = value ?? "";
            foreach (var s in Swatches) s.IsSelected = string.Equals(s.Value, color, StringComparison.OrdinalIgnoreCase);
            if (updateText)
            {
                colorText = color.StartsWith("#") ? color : "";
                Raise("ColorText");
            }
            Raise("PreviewFill"); Raise("PreviewText"); Raise("HasTint");
        }

        // The preview tile at the top of the editor wears the chosen color.
        public bool HasTint { get { return color.Length > 0; } }
        public Brush PreviewFill { get { return HasTint ? WidgetColors.SwatchBrush(color) : null; } }
        public Brush PreviewText
        {
            get
            {
                var c = WidgetColors.Resolve(color);
                if (c.HasValue && WidgetColors.IsLight(c.Value)) return WidgetColors.Frozen(Color.FromArgb(0xE4, 0, 0, 0));
                return c.HasValue || color == "wallpaper" ? Brushes.White : (System.Windows.Application.Current.TryFindResource("TextFill") as Brush ?? Brushes.White);
            }
        }
    }

    public class SwatchVM : Observable
    {
        bool selected;
        Brush brush;
        public string Value { get; set; }
        public string Label { get; set; }
        public string Short { get; set; }
        public Brush Brush { get { return brush; } set { Set(ref brush, value); } }
        public ICommand Command { get; set; }
        public bool IsSelected { get { return selected; } set { Set(ref selected, value); } }
    }

    // Widget background colors: "" follows the theme, "accent" follows the Windows accent, or any #RRGGBB.
    // A layout in the workspace editor, drawn as a tiny screen with numbered spaces.
    public class LayoutChoiceVM : Observable
    {
        public const double BoxW = 64, BoxH = 40;
        bool selected;

        public string Id { get; private set; }
        public string Label { get; private set; }
        public List<ZoneVM> Zones { get; private set; }
        public bool IsJustOpen { get; private set; }
        public bool IsSelected { get { return selected; } set { Set(ref selected, value); } }
        public ICommand Command { get; private set; }

        internal LayoutChoiceVM(LayoutPreset p, ICommand command)
        {
            Id = p.Id;
            Label = p.Label;
            Command = command;
            IsJustOpen = p.Zones.Length == 0;
            Zones = new List<ZoneVM>();
            for (int i = 0; i < p.Zones.Length; i++)
            {
                var z = p.Zones[i];
                Zones.Add(new ZoneVM
                {
                    X = z.X * BoxW + 1.5, Y = z.Y * BoxH + 1.5, W = Math.Max(1, z.Width * BoxW - 3), H = Math.Max(1, z.Height * BoxH - 3),
                    Number = p.Maximize ? "" : (i + 1).ToString()
                });
            }
        }
    }

    public class ZoneVM
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public string Number { get; set; }
    }

    public static class WidgetColors
    {
        public static readonly List<KeyValuePair<string, string>> Presets = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("", "Default (follows your theme)"),
            new KeyValuePair<string, string>("accent", "Accent color"),
            new KeyValuePair<string, string>("wallpaper", "Match the wallpaper behind the widget"),
            new KeyValuePair<string, string>("#2B2D36", "Graphite"),
            new KeyValuePair<string, string>("#1F3A5F", "Navy"),
            new KeyValuePair<string, string>("#0E5E6F", "Teal"),
            new KeyValuePair<string, string>("#2E6B3F", "Green"),
            new KeyValuePair<string, string>("#8A5A12", "Amber"),
            new KeyValuePair<string, string>("#8E2E3A", "Red"),
            new KeyValuePair<string, string>("#6B2E73", "Purple"),
            new KeyValuePair<string, string>("#ECEAE4", "Paper")
        };

        public static bool TryParseHex(string s, out Color c)
        {
            c = Colors.Transparent;
            s = (s ?? "").Trim().TrimStart('#');
            int v;
            if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
            c = Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        public static string ToHex(Color c) { return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B); }

        static Brush Res(string key) { return System.Windows.Application.Current.TryFindResource(key) as Brush; }

        // null = use the theme's default look
        public static Color? Resolve(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            if (value == "accent")
            {
                var b = Res("AccentFill") as SolidColorBrush;
                return b == null ? (Color?)null : b.Color;
            }
            Color c;
            return TryParseHex(value, out c) ? c : (Color?)null;
        }

        public static bool IsLight(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0 > 0.6; }

        public static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public static Brush SwatchBrush(string value)
        {
            if (value == "wallpaper")
            {
                var g = new LinearGradientBrush(Color.FromRgb(0x4F, 0x8D, 0xF0), Color.FromRgb(0xF2, 0xA6, 0x8C), 45);
                g.Freeze();
                return g;
            }
            var c = Resolve(value);
            return c.HasValue ? Frozen(c.Value) : (Res("WidgetFill") ?? Brushes.Gray);
        }
    }

    public class SizeCell : Observable
    {
        bool on;
        public bool IsOn { get { return on; } set { Set(ref on, value); } }
    }

    // One space on a 2x2 workspace's layout map, with the icon of the app that goes there.
    public class WorkspaceSpotVM : Observable
    {
        ImageSource icon;
        public WidgetVM Owner { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public double IconSize { get; set; }
        public ImageSource Icon { get { return icon; } set { Set(ref icon, value); } }
    }

    // One slot inside a larger widget: an app, or "+N" when more apps than fit.
    public class WidgetSlotVM : Observable
    {
        ImageSource icon;
        public WidgetVM Owner { get; set; }      // colors come from the widget, so they can change live
        public int Index { get; set; }           // the app's position in its group, for drag and drop
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
        // Must match Widget.xaml: panel margin, border, padding and title height.
        const double Margin = 3, Border = 1, Padding = 5, TitleHeight = 24;

        readonly App app;
        readonly Group group;
        readonly double title;
        Brush glass, tileGlass, panel, tile, stroke, tileStroke, text, hover;
        bool dropTarget;

        public string Path { get { return group.Path; } }
        public string Name { get; private set; }
        public bool IsCompact { get; private set; }
        public bool IsWorkspace { get; private set; }
        public bool IsGroupTile { get { return IsCompact && !IsWorkspace; } }
        public string Glyph { get; private set; }
        public string OpenLabel { get { return IsWorkspace ? "Open workspace" : "Open"; } }
        public string EditLabel { get { return IsWorkspace ? "Edit workspace" : "Edit group"; } }
        bool busy;
        public bool IsBusy { get { return busy; } set { Set(ref busy, value); } }
        public bool HasUpdate { get { return app.Updater != null && app.Updater.Available; } }
        public string UpdateLabel { get { return HasUpdate ? "Update Nook to " + app.Updater.NewVersion : ""; } }
        public ICommand UpdateCommand { get { return new Relay(() => app.Updater.Install()); } }
        public bool IsPanel { get { return !IsCompact && !IsWorkspace; } }
        public bool IsWorkspacePanel { get { return !IsCompact && IsWorkspace; } }
        public List<WorkspaceSpotVM> Spots { get; private set; }
        public bool ShowTitle { get; private set; }
        // The name opens the whole group only when the widget can't show every app.
        public bool HasMore { get; private set; }
        public bool TitleOpens { get { return ShowTitle && HasMore; } }
        public bool TitleIsText { get { return ShowTitle && !HasMore; } }
        public ObservableCollection<IconSlot> Preview { get; private set; }
        public List<WidgetSlotVM> Slots { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }
        public ICommand OpenCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand OpenHubCommand { get; private set; }
        public ICommand CloseAllCommand { get; private set; }
        public ICommand ToggleLockCommand { get; private set; }
        public string LockLabel { get { return app.Settings.LockWidgets ? "Unlock widgets" : "Lock widgets"; } }
        public string LockGlyph { get { return app.Settings.LockWidgets ? "" : ""; } }

        // Frosted wallpaper behind the panel (or the 1x1 tile), and the tint and text colors on top of it.
        public Brush GlassBrush { get { return glass; } private set { Set(ref glass, value); } }
        public Brush TileGlassBrush { get { return tileGlass; } private set { Set(ref tileGlass, value); } }
        public Brush PanelBrush { get { return panel; } private set { Set(ref panel, value); } }
        public Brush TileBrush { get { return tile; } private set { Set(ref tile, value); } }
        public Brush StrokeBrush { get { return stroke; } private set { Set(ref stroke, value); } }
        public Brush TileStroke { get { return tileStroke; } private set { Set(ref tileStroke, value); } }
        public Brush TextBrush { get { return text; } private set { Set(ref text, value); } }
        public Brush HoverBrush { get { return hover; } private set { Set(ref hover, value); } }
        public System.Windows.CornerRadius TileRadius { get; private set; }
        public bool IsDropTarget { get { return dropTarget; } set { Set(ref dropTarget, value); } }

        public WidgetVM(App app, Group g)
        {
            this.app = app;
            group = g;
            var d = g.Data;
            Name = g.Name;
            IsWorkspace = d.IsWorkspace;
            Glyph = d.Icon ?? "";
            IsCompact = d.Cols == 1 && d.Rows == 1;
            ShowTitle = !d.HideTitle && !string.IsNullOrEmpty(Name);
            title = d.HideTitle ? 0 : TitleHeight;
            Width = DesktopGrid.WidthFor(d.Cols);
            Height = DesktopGrid.HeightFor(d.Rows);
            OpenCommand = new Relay(() => app.OpenFromWidget(g.Path));
            EditCommand = new Relay(() => app.EditGroup(g.Path));
            OpenHubCommand = new Relay(() => app.ShowHome());
            CloseAllCommand = new Relay(() => app.Quit());
            ToggleLockCommand = new Relay(() => app.ToggleLock());
            SetColors(d.Color);
            Preview = IsCompact && !IsWorkspace ? IconSlot.PreviewOf(d.Apps, 64) : new ObservableCollection<IconSlot>();
            Slots = IsCompact || IsWorkspace ? new List<WidgetSlotVM>() : Layout(g);
            Spots = IsWorkspacePanel ? WorkspaceSpots(g) : new List<WorkspaceSpotVM>();
        }

        // A 2x2 workspace shows a small map of its layout, each app's icon in the space it fills.
        // Must match Widget.xaml: the workspace panel's padding and header height.
        List<WorkspaceSpotVM> WorkspaceSpots(Group g)
        {
            const double Pad = 9, Header = 30;
            double w = Width - 2 * (Margin + Border + Pad), h = Height - 2 * (Margin + Border + Pad) - Header;
            var preset = LayoutPreset.Find(g.Data.Layout);
            var apps = g.Data.Apps;
            var list = new List<WorkspaceSpotVM>();
            if (preset.Zones.Length > 0 && !preset.Maximize)
            {
                for (int i = 0; i < preset.Zones.Length; i++)
                {
                    var z = preset.Zones[i];
                    list.Add(Spot(z.X * w + 2, z.Y * h + 2, z.Width * w - 4, z.Height * h - 4, i < apps.Count ? apps[i] : null));
                }
                return list;
            }
            // "Just open" and "Full screen": the apps side by side, up to two rows of three.
            int n = Math.Min(apps.Count, 6), cols = Math.Max(1, Math.Min(3, n)), rows = n > 3 ? 2 : 1;
            double cw = w / cols, ch = h / rows;
            for (int i = 0; i < n; i++) list.Add(Spot((i % cols) * cw + 2, (i / cols) * ch + 2, cw - 4, ch - 4, apps[i]));
            return list;
        }

        WorkspaceSpotVM Spot(double x, double y, double w, double h, GroupApp a)
        {
            var s = new WorkspaceSpotVM { Owner = this, X = x, Y = y, W = Math.Max(1, w), H = Math.Max(1, h), IconSize = Math.Max(12, Math.Min(26, Math.Min(w, h) - 8)) };
            if (a != null) AppIcons.Request(a, 64, b => s.Icon = b);
            return s;
        }

        Brush Res(string key) { return app.TryFindResource(key) as Brush ?? Brushes.Gray; }

        void SetColors(string value)
        {
            var custom = WidgetColors.Resolve(value);
            if (custom.HasValue) { SetCustom(custom.Value, 0xB8); return; }
            bool folder = app.Settings.IconStyle == "folder";
            PanelBrush = Res("WidgetTint");
            StrokeBrush = Res("WidgetStroke");
            TextBrush = Res("TextFill");
            HoverBrush = Res("SubtleFill");
            TileBrush = folder ? Res("FolderFill") : Res("WidgetTileTint");
            TileStroke = folder ? WidgetColors.Frozen(Color.FromArgb(0x73, 0xA8, 0x6E, 0x12)) : Res("WidgetTileStroke");
            TileRadius = new System.Windows.CornerRadius(folder ? 8 : 12);
        }

        void SetCustom(Color c, byte alpha)
        {
            bool light = WidgetColors.IsLight(c);
            PanelBrush = WidgetColors.Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));
            TileBrush = WidgetColors.Frozen(Color.FromArgb((byte)Math.Min(255, alpha + 0x20), c.R, c.G, c.B));
            StrokeBrush = TileStroke = WidgetColors.Frozen(light ? Color.FromArgb(0x29, 0, 0, 0) : Color.FromArgb(0x33, 255, 255, 255));
            TextBrush = WidgetColors.Frozen(light ? Color.FromArgb(0xE4, 0, 0, 0) : Colors.White);
            HoverBrush = WidgetColors.Frozen(light ? Color.FromArgb(0x17, 0, 0, 0) : Color.FromArgb(0x1F, 255, 255, 255));
            TileRadius = new System.Windows.CornerRadius(12);
        }

        // Called whenever the widget moves: picks up the piece of wallpaper behind it.
        public void UpdateGlass(System.Windows.Rect windowPx, double scale)
        {
            var panelRect = new System.Windows.Rect(windowPx.X + Margin * scale, windowPx.Y + Margin * scale,
                Math.Max(1, windowPx.Width - 2 * Margin * scale), Math.Max(1, windowPx.Height - 2 * Margin * scale));
            var tileRect = new System.Windows.Rect(windowPx.X + (Width - 44) / 2 * scale, windowPx.Y + 5 * scale, 44 * scale, 44 * scale);
            if (IsCompact) TileGlassBrush = Wallpaper.GlassFor(tileRect);
            else GlassBrush = Wallpaper.GlassFor(panelRect);
            if (group.Data.Color == "wallpaper")
            {
                var c = Wallpaper.AverageColor(IsCompact ? tileRect : panelRect);
                if (c.HasValue) SetCustom(c.Value, 0x8C);
            }
        }

        // Fits as many app slots as the widget has room for. Names show when every app still fits with them.
        List<WidgetSlotVM> Layout(Group g)
        {
            var apps = g.Data.Apps;
            double innerW = Width - 2 * (Margin + Border + Padding);
            double innerH = Height - 2 * (Margin + Border + Padding) - title;
            int colsFit = Math.Max(1, (int)Math.Floor(innerW / 56));
            int rowsNamed = Math.Max(0, (int)Math.Floor(innerH / 60));
            int rowsPlain = Math.Max(1, (int)Math.Floor(innerH / 46));
            bool names = app.Settings.ShowNames && !g.Data.IconsOnly && rowsNamed > 0
                         && rowsNamed * colsFit >= Math.Min(apps.Count, rowsPlain * colsFit);
            int rowsFit = names ? rowsNamed : rowsPlain;
            double slotW = Math.Floor(innerW / colsFit), slotH = Math.Floor(innerH / rowsFit);
            double iconSize = Math.Max(16, Math.Min(32, Math.Min(slotW - 14, slotH - (names ? 28 : 8))));

            int capacity = colsFit * rowsFit;
            HasMore = apps.Count > capacity;
            int shown = HasMore ? capacity - 1 : apps.Count;
            var list = new List<WidgetSlotVM>();
            for (int i = 0; i < shown; i++)
            {
                var a = apps[i];
                var slot = new WidgetSlotVM
                {
                    Owner = this, Index = i, Name = a.Name, IsApp = true, ShowName = names,
                    Width = slotW, Height = slotH, IconSize = iconSize,
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
                    Owner = this, Index = -1, IsApp = false, MoreText = "+" + (apps.Count - shown), Name = "Show all apps",
                    ShowName = false, Width = slotW, Height = slotH, IconSize = iconSize, Command = OpenCommand
                });
            }
            return list;
        }
    }

    public class SearchResultVM : Observable
    {
        ImageSource icon;
        bool selected;
        public string Name { get; set; }
        public string Subtitle { get; set; }
        public ImageSource Icon { get { return icon; } set { Set(ref icon, value); } }
        public bool IsSelected { get { return selected; } set { Set(ref selected, value); } }
        public Action Open { get; set; }
        public ICommand OpenCommand { get; set; }
    }

    // Quick search over the apps in your groups first, then everything else installed.
    public class SearchVM : Observable
    {
        class Entry { public string Name, Subtitle, Key; public GroupApp App; public CatalogApp Catalog; public bool InGroup; }

        readonly App app;
        readonly List<Entry> pool = new List<Entry>();
        readonly Dictionary<string, SearchResultVM> made = new Dictionary<string, SearchResultVM>();
        string query = "";
        int selected;

        public ObservableCollection<SearchResultVM> Results { get; private set; }
        public bool NoResults { get { return Results.Count == 0; } }
        public Action Done { get; set; }

        public SearchVM(App app, IEnumerable<Group> groups)
        {
            this.app = app;
            Results = new ObservableCollection<SearchResultVM>();
            var keys = new HashSet<string>();
            var names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var g in groups)
                foreach (var a in g.Data.Apps)
                {
                    string key = a.Source ?? AppIcons.Key(a);
                    if (!keys.Add(key)) continue;
                    names.Add(a.Name);
                    pool.Add(new Entry { Name = a.Name, Subtitle = "In " + (string.IsNullOrEmpty(g.Name) ? "a workspace" : g.Name), Key = AppIcons.Key(a), App = a, InGroup = true });
                }
            app.GetCatalog(list =>
            {
                foreach (var c in list)
                {
                    // the same app can be both a desktop shortcut and a Start entry; list it once
                    if (keys.Contains(c.Source) || !names.Add(c.Name)) continue;
                    keys.Add(c.Source);
                    string where = c.Section == "windows" ? "Windows app" : c.Section == "desktop" ? "On your desktop" : "Installed app";
                    pool.Add(new Entry { Name = c.Name, Subtitle = where, Key = c.Source, Catalog = c });
                }
                Filter();
            });
            Filter();
        }

        public string Query
        {
            get { return query; }
            set { if (Set(ref query, value ?? "")) Filter(); }
        }

        static double Rank(string name, string q)
        {
            if (q.Length == 0) return 1;
            int i = name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase);
            if (i < 0) return 0;
            if (i == 0) return 3;
            return char.IsWhiteSpace(name[i - 1]) || name[i - 1] == '-' ? 2 : 1;
        }

        void Filter()
        {
            string q = query.Trim();
            var top = pool
                .Select(e => new { e, s = Rank(e.Name, q) + (e.InGroup ? 0.5 : 0) })
                .Where(x => x.s >= (q.Length == 0 ? 1.5 : 1)) // empty search: just your groups' apps
                .OrderByDescending(x => x.s).ThenBy(x => x.e.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(8).Select(x => x.e).ToList();
            Results.Clear();
            foreach (var e in top) Results.Add(Result(e));
            selected = 0;
            for (int i = 0; i < Results.Count; i++) Results[i].IsSelected = i == 0;
            Raise("NoResults");
        }

        SearchResultVM Result(Entry e)
        {
            SearchResultVM r;
            if (made.TryGetValue(e.Key, out r)) return r;
            r = new SearchResultVM { Name = e.Name, Subtitle = e.Subtitle };
            var res = r;
            if (e.App != null)
            {
                AppIcons.Request(e.App, 64, b => res.Icon = b);
                r.Open = () => Launcher.Launch(e.App, false);
            }
            else
            {
                IconLoader.Request(e.Catalog.Source, () => e.Catalog.IconPath, 64, b => res.Icon = b);
                r.Open = () => { try { Launcher.Launch(GroupApp.FromCatalog(e.Catalog), false); } catch { } };
            }
            r.OpenCommand = new Relay(() => { res.Open(); if (Done != null) Done(); });
            made[e.Key] = r;
            return r;
        }

        public void Move(int delta)
        {
            if (Results.Count == 0) return;
            selected = (selected + delta + Results.Count) % Results.Count;
            for (int i = 0; i < Results.Count; i++) Results[i].IsSelected = i == selected;
        }

        public bool OpenSelected()
        {
            if (selected < 0 || selected >= Results.Count) return false;
            Results[selected].Open();
            return true;
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
            WorkspaceItems = new ObservableCollection<object>();
            AppIcon = IconRenderer.RenderAppIcon(32);
            TogglePaneCommand = new Relay(() => PaneOpen = !PaneOpen);
            GoHomeCommand = new Relay(() => { Page = "home"; PaneOpen = false; });
            GoGroupsCommand = new Relay(() => { Page = "groups"; PaneOpen = false; });
            GoSettingsCommand = new Relay(() => { Page = "settings"; PaneOpen = false; });
            GoIslandCommand = new Relay(() => { Page = "island"; PaneOpen = false; });
            GoWorkspacesCommand = new Relay(() => { Page = "workspaces"; PaneOpen = false; });
            GoClockCommand = new Relay(() => { Page = "clock"; PaneOpen = false; });
            // The Home boxes take the commands above, so they're made after them.
            Sections = new ObservableCollection<SectionVM>
            {
                new SectionVM { Glyph = "", Title = "Groups", Command = GoGroupsCommand },
                new SectionVM { Glyph = "", Title = "Workspaces", Command = GoWorkspacesCommand },
                new SectionVM { Glyph = "", Title = "Island", Command = GoIslandCommand },
                new SectionVM { Glyph = "", Title = "Clock", Command = GoClockCommand },
                new SectionVM { Glyph = "", Title = "Settings", Command = GoSettingsCommand },
            };
            NewWorkspaceCommand = new Relay(() => StartEdit(null, true));
            NewGroupCommand = new Relay(() => StartEdit(null));
            DialogConfirmCommand = new Relay(() => { DialogOpen = false; var a = dialogAction; dialogAction = null; if (a != null) a(); });
            DialogCancelCommand = new Relay(() => { DialogOpen = false; dialogAction = null; });
            OpenReleasesCommand = new Relay(() => { try { Process.Start(Program.ReleasesUrl); } catch { } });
            RemoveFromPcCommand = new Relay(() => Ask(
                "Remove Nook from this PC?",
                "This deletes your groups and their widgets, turns off Start with Windows and removes Nook's settings. Your desktop icons and apps aren't touched.",
                "Remove", () => app.RemoveFromPc()));
        }

        public ImageSource AppIcon { get; private set; }
        public ObservableCollection<object> HomeItems { get; private set; }
        public ObservableCollection<object> WorkspaceItems { get; private set; }
        public ObservableCollection<SectionVM> Sections { get; private set; }
        public string AppName { get { return "Nook"; } }

        // What each box on Home says: a quick status for its section.
        public void RefreshSections()
        {
            if (Sections == null || app.Store == null) return;
            int groups = app.Store.Groups.Count(g => !g.IsWorkspace), spaces = app.Store.Groups.Count(g => g.IsWorkspace);
            var s = app.Settings;
            Sections[0].Status = groups == 0 ? "Make your first group" : groups + (groups == 1 ? " group" : " groups") + " on your desktop";
            Sections[1].Status = spaces == 0 ? "One-click app layouts" : spaces + (spaces == 1 ? " workspace" : " workspaces");
            Sections[2].Status = s.IslandOn ? "On · " + (s.IslandStyle == "glass" ? "Glass" : "Black") : "Off";
            int screens = Math.Max(1, s.ClockScreens.Count);
            Sections[3].Status = s.ClockOn ? "On · " + screens + (screens == 1 ? " screen" : " screens") : "Off";
            Sections[4].Status = app.Updater != null && app.Updater.Available ? "Version " + app.Updater.NewVersion + " is ready to install" : "Version " + Program.Version;
        }
        public string VersionText { get { return "Version " + Program.Version; } }

        public string Page
        {
            get { return page; }
            set
            {
                if (!Set(ref page, value)) return;
                Raise("IsHome"); Raise("IsGroups"); Raise("IsEditor"); Raise("IsSettings"); Raise("IsHomeNav"); Raise("IsGroupsNav"); Raise("IsSettingsNav");
                if (page == "home") RefreshSections();
                Raise("IsIsland"); Raise("IsIslandNav"); Raise("IsWorkspaces"); Raise("IsWorkspacesNav"); Raise("IsClock"); Raise("IsClockNav");
                if (page == "clock") RaiseClock();
                if (page == "island") IslandPreviewTick();
            }
        }
        public bool IsHome { get { return page == "home"; } }
        public bool IsEditor { get { return page == "editor"; } }
        public bool IsSettings { get { return page == "settings"; } }
        public bool IsIsland { get { return page == "island"; } }
        public bool IsWorkspaces { get { return page == "workspaces"; } }
        public bool IsClock { get { return page == "clock"; } }
        public bool IsGroups { get { return page == "groups"; } }
        public bool IsHomeNav { get { return page == "home"; } }
        public bool IsGroupsNav { get { return page == "groups" || (page == "editor" && (editor == null || !editor.IsWorkspace)); } }
        public bool IsWorkspacesNav { get { return page == "workspaces" || (page == "editor" && editor != null && editor.IsWorkspace); } }
        public bool IsClockNav { get { return page == "clock"; } }
        public bool IsSettingsNav { get { return page == "settings"; } }
        public bool IsIslandNav { get { return page == "island"; } }

        public bool PaneOpen { get { return paneOpen; } set { Set(ref paneOpen, value); } }
        public EditorVM Editor { get { return editor; } private set { Set(ref editor, value); } }

        public ICommand TogglePaneCommand { get; private set; }
        public ICommand GoHomeCommand { get; private set; }
        public ICommand GoGroupsCommand { get; private set; }
        public ICommand GoSettingsCommand { get; private set; }
        public ICommand GoIslandCommand { get; private set; }
        public ICommand GoWorkspacesCommand { get; private set; }
        public ICommand GoClockCommand { get; private set; }
        public ICommand NewWorkspaceCommand { get; private set; }
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
            WorkspaceItems.Clear();
            bool folder = app.Settings.IconStyle == "folder";
            foreach (var g in app.Store.Groups) (g.IsWorkspace ? WorkspaceItems : HomeItems).Add(new GroupVM(this, g, folder));
            HomeItems.Add(new AddGroupVM { Command = NewGroupCommand, Label = "Add Group" });
            RefreshSections();
            WorkspaceItems.Add(new AddGroupVM { Command = NewWorkspaceCommand, Label = "Add workspace" });
        }

        public void StartEdit(Group g) { StartEdit(g, false); }

        public void StartEdit(Group g, bool workspace)
        {
            var ed = new EditorVM(this, g, workspace);
            Editor = ed;
            Page = "editor";
            PaneOpen = false;
            app.GetCatalog(list => { if (Editor == ed) ed.SetCatalog(list); });
        }

        public void SaveEditor(EditorVM ed, GroupData look)
        {
            app.SaveGroup(ed.Editing, look);
            Page = ed.IsWorkspace ? "workspaces" : "groups";
        }

        public void AskRemove(GroupVM g)
        {
            Ask(g.IsWorkspace ? "Delete this workspace?" : "Are you sure you want to delete this group?",
                g.IsWorkspace ? "Its button will be removed from your desktop. The apps stay installed."
                              : "“" + g.Name + "” will be removed from Nook and your desktop. The apps inside stay installed, and your other desktop icons aren't touched.",
                g.IsWorkspace ? "Delete workspace" : "Delete group",
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

        public void ThemeChanged()
        {
            Raise("ModeLabel");
            if (AccentSwatches != null && AccentSwatches.Count > 0) AccentSwatches[0].Brush = WidgetColors.Frozen(Theme.AccentPalette()[3]);
        }

        // ---- accent color ----
        static readonly List<KeyValuePair<string, string>> AccentPresets = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("", "Match Windows"),
            new KeyValuePair<string, string>("#0078D4", "Blue"),
            new KeyValuePair<string, string>("#0099BC", "Teal blue"),
            new KeyValuePair<string, string>("#00B294", "Seafoam"),
            new KeyValuePair<string, string>("#107C10", "Green"),
            new KeyValuePair<string, string>("#8764B8", "Lavender"),
            new KeyValuePair<string, string>("#C239B3", "Orchid"),
            new KeyValuePair<string, string>("#E81123", "Red"),
            new KeyValuePair<string, string>("#F7630C", "Orange"),
            new KeyValuePair<string, string>("#FFB900", "Gold")
        };

        ObservableCollection<SwatchVM> accentSwatches;
        string accentText = "";

        public ObservableCollection<SwatchVM> AccentSwatches
        {
            get
            {
                if (accentSwatches != null) return accentSwatches;
                accentSwatches = new ObservableCollection<SwatchVM>();
                foreach (var p in AccentPresets)
                {
                    var value = p.Key;
                    accentSwatches.Add(new SwatchVM
                    {
                        Value = value,
                        Label = p.Value,
                        Short = value == "" ? "A" : "",
                        Brush = WidgetColors.Frozen(value == "" ? Theme.AccentPalette()[3] : Theme.AccentPalette(value)[3]),
                        Command = new Relay(() => SetAccent(value, true))
                    });
                }
                MarkAccent(true);
                return accentSwatches;
            }
        }

        public string AccentText
        {
            get { return accentText; }
            set
            {
                accentText = value ?? "";
                Raise();
                Color c;
                if (WidgetColors.TryParseHex(accentText, out c)) SetAccent(WidgetColors.ToHex(c), false);
            }
        }

        void SetAccent(string value, bool updateText)
        {
            if (app.Settings.Accent != value)
            {
                app.Settings.Accent = value;
                app.Settings.Save();
                app.ApplyTheme();
            }
            MarkAccent(updateText);
        }

        void MarkAccent(bool updateText)
        {
            string current = app.Settings.Accent ?? "";
            foreach (var s in accentSwatches) s.IsSelected = string.Equals(s.Value, current, StringComparison.OrdinalIgnoreCase);
            if (updateText && !accentSwatches.Any(s => s.IsSelected && s.Value != "")) { accentText = current; Raise("AccentText"); }
            else if (updateText) { accentText = ""; Raise("AccentText"); }
        }

        // ---- glass ----
        public double Blur
        {
            get { return app.Settings.Blur; }
            set
            {
                int v = (int)Math.Round(Math.Max(0, Math.Min(100, value)));
                if (v == app.Settings.Blur) return;
                app.Settings.Blur = v;
                app.Settings.Save();
                app.ApplyBlurSoon();
                Raise();
                Raise("BlurText");
            }
        }

        public string BlurText { get { return app.Settings.Blur == 0 ? "Clear" : app.Settings.Blur + "%"; } }

        // ---- island ----
        public bool IslandOn
        {
            get { return app.Settings.IslandOn; }
            set { app.SetIslandOn(value); }
        }

        public void IslandChanged() { Raise("IslandOn"); RefreshSections(); }

        void SaveIsland(string name)
        {
            app.Settings.Save();
            app.SyncIsland();
            Raise(name);
            Raise("IsGlass");
            Raise("IslandPreviewFill");
            Raise("IslandPreviewFrost");
            Raise("IslandPreviewStroke");
            Raise("IslandPreviewMargin");
            Raise("IslandPreviewCorners");
        }

        public int IslandStyleIndex
        {
            get { return app.Settings.IslandStyle == "glass" ? 1 : 0; }
            set { string v = value == 1 ? "glass" : "black"; if (v == app.Settings.IslandStyle) return; app.Settings.IslandStyle = v; SaveIsland("IslandStyleIndex"); }
        }

        public bool IsGlass { get { return app.Settings.IslandStyle == "glass"; } }

        public double IslandBlur
        {
            get { return app.Settings.IslandBlur; }
            set
            {
                int v = (int)Math.Round(Math.Max(0, Math.Min(100, value)));
                if (v == app.Settings.IslandBlur) return;
                app.Settings.IslandBlur = v;
                SaveIsland("IslandBlur");
                Raise("IslandBlurText");
            }
        }

        public string IslandBlurText { get { return app.Settings.IslandBlur == 0 ? "Clear" : app.Settings.IslandBlur + "%"; } }

        public int IslandShapeIndex
        {
            get { return app.Settings.IslandShape == "notch" ? 1 : 0; }
            set { string v = value == 1 ? "notch" : "float"; if (v == app.Settings.IslandShape) return; app.Settings.IslandShape = v; SaveIsland("IslandShapeIndex"); }
        }

        public bool IslandMusic
        {
            get { return app.Settings.IslandMusic; }
            set { app.Settings.IslandMusic = value; SaveIsland("IslandMusic"); }
        }

        public bool IslandScrollVolume
        {
            get { return app.Settings.IslandScrollVolume; }
            set { app.Settings.IslandScrollVolume = value; SaveIsland("IslandScrollVolume"); }
        }

        public bool IslandCharging
        {
            get { return app.Settings.IslandCharging; }
            set { app.Settings.IslandCharging = value; SaveIsland("IslandCharging"); }
        }

        public bool IslandHideFullScreen
        {
            get { return app.Settings.IslandHideFullScreen; }
            set { app.Settings.IslandHideFullScreen = value; SaveIsland("IslandHideFullScreen"); }
        }

        public bool IslandHeadphones
        {
            get { return app.Settings.IslandHeadphones; }
            set { app.Settings.IslandHeadphones = value; SaveIsland("IslandHeadphones"); }
        }

        public bool IslandSystem
        {
            get { return app.Settings.IslandSystem; }
            set { app.Settings.IslandSystem = value; SaveIsland("IslandSystem"); }
        }

        // Which screen the island sits on: "Main screen", then each screen by number.
        List<ScreenInfo> islandScreens;
        public List<string> IslandScreenChoices
        {
            get
            {
                islandScreens = Screens.All();
                var list = new List<string> { "Main screen" };
                list.AddRange(islandScreens.Select(x => x.Label));
                return list;
            }
        }

        public int IslandScreenIndex
        {
            get
            {
                var list = islandScreens ?? Screens.All();
                int i = list.FindIndex(x => string.Equals(x.Device, app.Settings.IslandScreen, StringComparison.OrdinalIgnoreCase));
                return string.IsNullOrEmpty(app.Settings.IslandScreen) || i < 0 ? 0 : i + 1;
            }
            set
            {
                var list = islandScreens ?? Screens.All();
                string dev = value <= 0 || value > list.Count ? "" : list[value - 1].Device;
                if (dev == app.Settings.IslandScreen) return;
                app.Settings.IslandScreen = dev;
                SaveIsland("IslandScreenIndex");
            }
        }

        // ---- desktop clock ----
        public bool ClockOn
        {
            get { return app.Settings.ClockOn; }
            set { app.Settings.ClockOn = value; app.Settings.Save(); app.SyncClock(); Raise(); }
        }

        public bool ClockDate
        {
            get { return app.Settings.ClockDate; }
            set { app.Settings.ClockDate = value; app.Settings.Save(); app.SyncClock(); Raise(); }
        }

        public int ClockSizeIndex
        {
            get { return app.Settings.ClockSize == "small" ? 0 : app.Settings.ClockSize == "large" ? 2 : 1; }
            set
            {
                string v = value == 0 ? "small" : value == 2 ? "large" : "medium";
                if (v == app.Settings.ClockSize) return;
                app.Settings.ClockSize = v;
                app.Settings.Save();
                app.SyncClock();
                Raise();
            }
        }

        void SaveClock(string name)
        {
            app.Settings.Save();
            app.SyncClock();
            Raise(name);
            Raise("ClockPreviewText"); Raise("ClockPreviewFont"); Raise("ClockPreviewWeight"); Raise("ClockPreviewBrush");
        }

        // Fonts: Segoe UI Variable first, then every font installed on this PC, each shown in its own typeface.
        List<string> clockFonts;
        public List<string> ClockFonts
        {
            get
            {
                if (clockFonts != null) return clockFonts;
                clockFonts = new List<string> { "Segoe UI Variable (default)" };
                try { clockFonts.AddRange(System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)); }
                catch { }
                return clockFonts;
            }
        }

        public int ClockFontIndex
        {
            get { var f = app.Settings.ClockFont; return string.IsNullOrEmpty(f) ? 0 : Math.Max(0, ClockFonts.FindIndex(n => string.Equals(n, f, StringComparison.OrdinalIgnoreCase))); }
            set
            {
                string v = value <= 0 || value >= ClockFonts.Count ? "" : ClockFonts[value];
                if (v == app.Settings.ClockFont) return;
                app.Settings.ClockFont = v;
                SaveClock("ClockFontIndex");
            }
        }

        static readonly string[] ClockWeights = { "thin", "light", "regular", "semibold", "bold" };
        public int ClockWeightIndex
        {
            get { return Math.Max(0, Array.IndexOf(ClockWeights, app.Settings.ClockWeight)); }
            set
            {
                string v = ClockWeights[Math.Max(0, Math.Min(ClockWeights.Length - 1, value))];
                if (v == app.Settings.ClockWeight) return;
                app.Settings.ClockWeight = v;
                SaveClock("ClockWeightIndex");
            }
        }

        public int ClockFormatIndex
        {
            get { return app.Settings.ClockFormat == "12" ? 1 : app.Settings.ClockFormat == "24" ? 2 : 0; }
            set
            {
                string v = value == 1 ? "12" : value == 2 ? "24" : "";
                if (v == app.Settings.ClockFormat) return;
                app.Settings.ClockFormat = v;
                SaveClock("ClockFormatIndex");
            }
        }

        public bool ClockSeconds
        {
            get { return app.Settings.ClockSeconds; }
            set { app.Settings.ClockSeconds = value; SaveClock("ClockSeconds"); }
        }

        // Color: "" suits the wallpaper (white or dark), or your accent, a preset or any #RRGGBB.
        static readonly List<KeyValuePair<string, string>> ClockColorPresets = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("", "Automatic: white or dark to suit your wallpaper"),
            new KeyValuePair<string, string>("#FFFFFF", "White"),
            new KeyValuePair<string, string>("#15171D", "Dark"),
            new KeyValuePair<string, string>("accent", "Accent color"),
            new KeyValuePair<string, string>("#9CC8FF", "Sky"),
            new KeyValuePair<string, string>("#FFD166", "Gold"),
            new KeyValuePair<string, string>("#FF9AA2", "Rose"),
            new KeyValuePair<string, string>("#B9F2C9", "Mint")
        };

        ObservableCollection<SwatchVM> clockColors;
        string clockColorText = "";

        public ObservableCollection<SwatchVM> ClockColorSwatches
        {
            get
            {
                if (clockColors != null) return clockColors;
                clockColors = new ObservableCollection<SwatchVM>();
                foreach (var p in ClockColorPresets)
                {
                    var value = p.Key;
                    Brush brush;
                    if (value == "")
                    {
                        var half = new LinearGradientBrush(Colors.White, Color.FromRgb(0x15, 0x17, 0x1D), 45);
                        half.GradientStops.Clear();
                        half.GradientStops.Add(new GradientStop(Colors.White, 0.5));
                        half.GradientStops.Add(new GradientStop(Color.FromRgb(0x15, 0x17, 0x1D), 0.5));
                        half.Freeze();
                        brush = half;
                    }
                    else brush = WidgetColors.SwatchBrush(value);
                    clockColors.Add(new SwatchVM
                    {
                        Value = value, Label = p.Value, Short = value == "accent" ? "A" : "", Brush = brush,
                        Command = new Relay(() => SetClockColor(value, true))
                    });
                }
                MarkClockColor(true);
                return clockColors;
            }
        }

        public string ClockColorText
        {
            get { return clockColorText; }
            set
            {
                clockColorText = value ?? "";
                Raise();
                Color c;
                if (WidgetColors.TryParseHex(clockColorText, out c)) SetClockColor(WidgetColors.ToHex(c), false);
            }
        }

        void SetClockColor(string value, bool updateText)
        {
            if (app.Settings.ClockColor != value) { app.Settings.ClockColor = value; SaveClock("ClockColor"); }
            MarkClockColor(updateText);
        }

        void MarkClockColor(bool updateText)
        {
            string current = app.Settings.ClockColor ?? "";
            foreach (var s in clockColors) s.IsSelected = string.Equals(s.Value, current, StringComparison.OrdinalIgnoreCase);
            if (!updateText) return;
            clockColorText = current.StartsWith("#") && !clockColors.Any(s => s.IsSelected) ? current : "";
            Raise("ClockColorText");
        }

        // The preview at the top of the Clock page: the middle of your wallpaper with the clock on it.
        static System.Windows.Rect ClockPreviewArea
        {
            get
            {
                var main = Screens.Primary(Screens.All());
                if (main == null) return new System.Windows.Rect(0, 0, 900, 240);
                double w = 900 * main.Scale, h = 240 * main.Scale;
                double cx = (main.Work.Left + main.Work.Right) / 2.0, cy = (main.Work.Top + main.Work.Bottom) / 2.0;
                return new System.Windows.Rect(cx - w / 2, cy - h / 2, w, h); // screen pixels, like the wallpaper
            }
        }

        public Brush ClockPreviewWallpaper { get { return Wallpaper.GlassFor(ClockPreviewArea, true) ?? new SolidColorBrush(Color.FromRgb(0x1C, 0x2B, 0x4A)); } }
        public string ClockPreviewText { get { return DesktopClock.TimeText(DateTime.Now, app.Settings); } }
        public FontFamily ClockPreviewFont { get { return DesktopClock.FontOf(app, app.Settings.ClockFont); } }
        public System.Windows.FontWeight ClockPreviewWeight { get { return DesktopClock.WeightOf(app.Settings.ClockWeight); } }
        public Brush ClockPreviewBrush
        {
            get
            {
                var c = WidgetColors.Resolve(app.Settings.ClockColor);
                if (c.HasValue) return WidgetColors.Frozen(c.Value);
                var wall = Wallpaper.AverageColor(ClockPreviewArea);
                bool lightWall = wall.HasValue && (wall.Value.R * 0.299 + wall.Value.G * 0.587 + wall.Value.B * 0.114) > 170;
                return lightWall ? WidgetColors.Frozen(Color.FromArgb(0xE6, 0x10, 0x12, 0x18)) : Brushes.White;
            }
        }

        public ObservableCollection<ScreenCheckVM> ClockScreenChoices { get; private set; }

        System.Windows.Threading.DispatcherTimer clockPreviewTimer;

        void RaiseClock()
        {
            Raise("ClockPreviewWallpaper"); Raise("ClockPreviewText"); Raise("ClockPreviewBrush");
            // The preview keeps time while the Clock page is open.
            if (clockPreviewTimer == null)
            {
                clockPreviewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                clockPreviewTimer.Tick += (s, e) => { if (page == "clock") Raise("ClockPreviewText"); else clockPreviewTimer.Stop(); };
            }
            clockPreviewTimer.Start();
            var all = Screens.All();
            var chosen = app.Settings.ClockScreens;
            bool none = !all.Any(x => chosen.Contains(x.Device));
            var list = new ObservableCollection<ScreenCheckVM>();
            foreach (var x in all)
            {
                var device = x.Device;
                list.Add(new ScreenCheckVM(x.Label, none ? x.Primary : chosen.Contains(device), on =>
                {
                    var set = new List<string>(app.Settings.ClockScreens.Where(d => all.Any(a => a.Device == d)));
                    if (set.Count == 0) { var main = Screens.Primary(all); if (main != null) set.Add(main.Device); }
                    set.Remove(device);
                    if (on) set.Add(device);
                    app.Settings.ClockScreens = set;
                    app.Settings.Save();
                    app.SyncClock();
                }));
            }
            ClockScreenChoices = list;
            Raise("ClockScreenChoices");
        }

        // ---- updates ----
        public bool CheckUpdates
        {
            get { return app.Settings.CheckUpdates; }
            set { app.Settings.CheckUpdates = value; app.Settings.Save(); Raise(); }
        }

        public string UpdateStatus { get { return app.Updater.Status; } }
        public bool HasUpdate { get { return app.Updater.Available; } }
        public string UpdateButtonLabel { get { return app.Updater.Available ? "Update to " + app.Updater.NewVersion : "Check now"; } }
        public bool UpdateIdle { get { return !app.Updater.Busy; } }

        public ICommand UpdateCommand
        {
            get { return new Relay(() => { if (app.Updater.Available) app.Updater.Install(); else app.Updater.Check(true); }); }
        }

        public void UpdateChanged()
        {
            RefreshSections();
            Raise("UpdateStatus"); Raise("HasUpdate"); Raise("UpdateButtonLabel"); Raise("UpdateIdle");
        }

        public bool HasBattery
        {
            get
            {
                var st = System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus;
                return (st & System.Windows.Forms.BatteryChargeStatus.NoSystemBattery) == 0 && st != System.Windows.Forms.BatteryChargeStatus.Unknown;
            }
        }

        // The preview at the top of the Island page: your wallpaper's top edge with the island on it.
        string previewTime = "";
        System.Windows.Threading.DispatcherTimer previewTimer;
        public string IslandPreviewTime { get { return previewTime; } private set { Set(ref previewTime, value); } }

        void IslandPreviewTick()
        {
            IslandPreviewTime = DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);
            if (previewTimer == null)
            {
                previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                previewTimer.Tick += (s, e) => { if (page == "island") IslandPreviewTime = DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture); else previewTimer.Stop(); };
            }
            previewTimer.Start();
            Raise("IslandPreviewWallpaper");
            Raise("IslandPreviewBlurred");
        }

        static System.Windows.Rect PreviewArea
        {
            get
            {
                double w = System.Windows.SystemParameters.PrimaryScreenWidth;
                return new System.Windows.Rect((w - 640) / 2, 0, 640, 120);
            }
        }

        public Brush IslandPreviewWallpaper { get { return Wallpaper.GlassFor(PreviewArea, true) ?? new SolidColorBrush(Color.FromRgb(0x1C, 0x2B, 0x4A)); } }
        public Brush IslandPreviewBlurred { get { return Wallpaper.GlassFor(PreviewArea) ?? new SolidColorBrush(Color.FromRgb(0x1C, 0x2B, 0x4A)); } }
        public double IslandPreviewFrost { get { return IsGlass ? Math.Sqrt(app.Settings.IslandBlur / 100.0) : 0; } }

        public Brush IslandPreviewFill { get { return IslandWindow.Fill(IsGlass, app.Settings.IslandBlur / 100.0, true, app.Settings.IslandTint); } }

        public Brush IslandPreviewStroke { get { return IsGlass ? new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)) : null; } }

        // The island's tint: none, your accent, a preset or any #RRGGBB. Swatches show the color as the island will wear it.
        static readonly List<KeyValuePair<string, string>> IslandTintPresets = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("", "No tint"),
            new KeyValuePair<string, string>("accent", "Accent color"),
            new KeyValuePair<string, string>("#3D8BF2", "Blue"),
            new KeyValuePair<string, string>("#0E7C86", "Teal"),
            new KeyValuePair<string, string>("#2E8B57", "Green"),
            new KeyValuePair<string, string>("#C27C0E", "Amber"),
            new KeyValuePair<string, string>("#C42B3C", "Red"),
            new KeyValuePair<string, string>("#8E44AD", "Purple"),
            new KeyValuePair<string, string>("#D6409F", "Pink")
        };

        ObservableCollection<SwatchVM> islandTints;
        string islandTintText = "";

        public ObservableCollection<SwatchVM> IslandTintSwatches
        {
            get
            {
                if (islandTints != null) return islandTints;
                islandTints = new ObservableCollection<SwatchVM>();
                foreach (var p in IslandTintPresets)
                {
                    var value = p.Key;
                    var c = IslandWindow.TintColor(value);
                    islandTints.Add(new SwatchVM
                    {
                        Value = value, Label = p.Value, Short = value == "accent" ? "A" : "",
                        Brush = WidgetColors.Frozen(c ?? Colors.Black),
                        Command = new Relay(() => SetIslandTint(value, true))
                    });
                }
                MarkIslandTint(true);
                return islandTints;
            }
        }

        public string IslandTintText
        {
            get { return islandTintText; }
            set
            {
                islandTintText = value ?? "";
                Raise();
                Color c;
                if (WidgetColors.TryParseHex(islandTintText, out c)) SetIslandTint(WidgetColors.ToHex(c), false);
            }
        }

        void SetIslandTint(string value, bool updateText)
        {
            if (app.Settings.IslandTint != value) { app.Settings.IslandTint = value; SaveIsland("IslandTint"); }
            MarkIslandTint(updateText);
        }

        void MarkIslandTint(bool updateText)
        {
            string current = app.Settings.IslandTint ?? "";
            foreach (var s in islandTints) s.IsSelected = string.Equals(s.Value, current, StringComparison.OrdinalIgnoreCase);
            if (!updateText) return;
            islandTintText = current.StartsWith("#") && !islandTints.Any(s => s.IsSelected) ? current : "";
            Raise("IslandTintText");
        }
        public System.Windows.Thickness IslandPreviewMargin { get { return new System.Windows.Thickness(0, app.Settings.IslandShape == "notch" ? 0 : 8, 0, 0); } }
        public System.Windows.CornerRadius IslandPreviewCorners { get { return app.Settings.IslandShape == "notch" ? new System.Windows.CornerRadius(0, 0, 16, 16) : new System.Windows.CornerRadius(16); } }

        // ---- widgets ----
        public bool LockWidgets
        {
            get { return app.Settings.LockWidgets; }
            set { if (value != app.Settings.LockWidgets) app.ToggleLock(); }
        }

        public void LockChanged() { Raise("LockWidgets"); }

        public bool QuickSearch
        {
            get { return app.Settings.QuickSearch; }
            set
            {
                app.Settings.QuickSearch = value;
                app.Settings.Save();
                app.ApplyHotkey();
                Raise();
                Raise("QuickSearchStatus");
            }
        }

        public string QuickSearchStatus { get { return app.HotkeyStatus; } }

        public ICommand ExportCommand { get { return new Relay(() => app.ExportGroups()); } }
        public ICommand ImportCommand { get { return new Relay(() => app.ImportGroups()); } }

        // ---- undo toast ----
        string toastText;
        bool toastOpen;
        Action undoAction, expireAction;
        System.Windows.Threading.DispatcherTimer toastTimer;

        public string ToastText { get { return toastText; } private set { Set(ref toastText, value); } }
        public bool ToastOpen { get { return toastOpen; } private set { Set(ref toastOpen, value); } }
        public bool HasUndo { get { return undoAction != null; } }
        public ICommand UndoCommand
        {
            get
            {
                return new Relay(() =>
                {
                    var undo = undoAction;
                    undoAction = null;
                    expireAction = null;
                    CloseToast();
                    if (undo != null) undo();
                });
            }
        }

        public void ShowUndo(string text, Action undo, Action expired)
        {
            FinishToast();
            ToastText = text;
            undoAction = undo;
            expireAction = expired;
            Raise("HasUndo");
            ToastOpen = true;
            if (toastTimer == null)
            {
                toastTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
                toastTimer.Tick += (s, e) => FinishToast();
            }
            toastTimer.Stop();
            toastTimer.Start();
        }

        // The undo window closed: make the delete final.
        public void FinishToast()
        {
            var expire = expireAction;
            expireAction = null;
            undoAction = null;
            CloseToast();
            if (expire != null) expire();
        }

        void CloseToast()
        {
            if (toastTimer != null) toastTimer.Stop();
            ToastOpen = false;
        }
    }
}
