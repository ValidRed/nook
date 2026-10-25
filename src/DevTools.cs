using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinGroups
{
    // Developer commands for building and testing without changing anything on the PC:
    //   --dev-app-icon <out.ico>          write the app's own icon
    //   --dev-dump-icons <dir> [size]     save the icon of every app Nook can list (read-only)
    //   --dev-selftest <report.txt>       round-trip groups in a temp folder
    //   --dev-render <dir>                render each screen and widget size to PNG (light and dark)
    //   --dev-site <dir>                  app icons and a preview card for the website (site\assets)
    //   --dev-island <dir>                every island state, black, glass and notch, with made-up song details
    //   --dev-workspace <report.txt>      opens two test windows as a workspace and checks they land side by side, then closes them
    //   --dev-media <report.txt>          checks that what's playing and the level meter can be read (yes/no and numbers only)
    //   --dev-island-live <dir>           the real island over a test card at the top of the screen, screenshotting only that area
    //   --dev-popup-live <dir>            a real group pop-up over a test card, with its window and content sizes
    //   --dev-setup <dir>                 the first-run window, off and on, then closed: checks nothing was turned on
    //   --dev-timer <report.txt>          a short timer on the real island: counts down, ends, stops (no sound)
    //   --dev-shelf <dir>                 drags text, a test file and a picture onto the real island, then two off again
    //   --dev-taskbar <dir>               Nook's buttons on the real taskbar, on each side: checks they cover none of the taskbar's
    //                                     own buttons, opens a group above it, and mutes the microphones for a moment (then restores them)
    static class DevTools
    {
        public static int Run(List<string> args)
        {
            Settings.DevDataDir = Path.Combine(Path.GetTempPath(), "nook-dev-data");
            try
            {
                switch (args[0].ToLowerInvariant())
                {
                    case "--dev-app-icon":
                        File.WriteAllBytes(args[1], IconRenderer.BuildAppIco());
                        return 0;
                    case "--dev-dump-icons":
                        return DumpIcons(args[1], args.Count > 2 ? int.Parse(args[2]) : 96);
                    case "--dev-selftest":
                        return SelfTest(args[1]);
                    case "--dev-render":
                        return Render(args[1]);
                    case "--dev-site":
                        return SiteShots(args[1]);
                    case "--dev-island":
                        return IslandShots(args[1]);
                    case "--dev-drag":
                        return DragTest(args[1], args.Count > 2 ? args[2] : "");
                    case "--dev-workspace":
                        return WorkspaceTest(args[1]);
                    case "--dev-dummy":
                        return DummyWindow(args.Count > 1 ? args[1] : "", args.Count > 2 && args[2] == "move");
                    case "--dev-media":
                        return MediaReport(args[1]);
                    case "--dev-island-live":
                        return IslandLive(args[1]);
                    case "--dev-popup-live":
                        return PopupLive(args[1]);
                    case "--dev-title":
                        return TitleLive(args[1]);
                    case "--dev-home-live":
                        return HomeLive(args[1]);
                    case "--dev-clock-live":
                        return ClockLive(args[1]);
                    case "--dev-islands":
                        return IslandsLive(args[1]);
                    case "--dev-shelf":
                        return ShelfLive(args[1]);
                    case "--dev-timer":
                        return TimerLive(args[1]);
                    case "--dev-setup":
                        return SetupLive(args[1]);
                    case "--dev-taskbar":
                        return TaskbarLive(args[1]);
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "nook-dev-error.txt"), ex.ToString()); } catch { }
                return 2;
            }
            return 1;
        }

        static void SavePng(BitmapSource bmp, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
        }

        static int DumpIcons(string dir, int size)
        {
            Directory.CreateDirectory(dir);
            var apps = AppCatalog.LoadBlocking();
            var sb = new StringBuilder();
            int i = 0;
            foreach (var a in apps)
            {
                var bmp = ShellIcons.Get(a.IconPath, size);
                string file = bmp == null ? "" : (i.ToString("000") + ".png");
                if (bmp != null) SavePng(bmp, Path.Combine(dir, file));
                sb.AppendLine(string.Join("\t", file, a.Section, a.Name, a.Kind, a.Target));
                i++;
            }
            File.WriteAllText(Path.Combine(dir, "index.tsv"), sb.ToString(), Encoding.UTF8);
            return 0;
        }

        static int SelfTest(string report)
        {
            var log = new StringBuilder();
            Action<string> say = s => log.AppendLine(s);
            bool ok = true;

            DesktopGrid.Refresh();
            say("Desktop icon grid: " + DesktopGrid.CellW + " x " + DesktopGrid.CellH + "; widgets: " + DesktopGrid.WidthFor(1) + " x " + DesktopGrid.HeightFor(1) + " (1x1) to "
                + DesktopGrid.WidthFor(3) + " x " + DesktopGrid.HeightFor(3) + " (3x3)");

            // desktop layer, wallpaper and snapping on every screen
            say("Desktop window found: " + (DesktopGrid.DesktopHost() != IntPtr.Zero ? "yes" : "NO"));
            ok &= DesktopGrid.DesktopHost() != IntPtr.Zero;
            Wallpaper.Load(60);
            say("Wallpaper loaded: " + (Wallpaper.Loaded ? "yes" : "no (solid color or none)"));
            foreach (var scr in System.Windows.Forms.Screen.AllScreens)
            {
                var wa = scr.WorkingArea;
                var probe = new Rect(wa.Right - 300.4, wa.Bottom - 200.7, 150, 150); // deliberately off-grid, near the corner
                var t = DesktopGrid.SnapTarget(probe);
                bool inside = t.X >= wa.Left && t.Y >= wa.Top && t.X + 150 <= wa.Right + 1 && t.Y + 150 <= wa.Bottom + 1;
                say("Snap on " + scr.DeviceName + " (" + wa + "): (" + t.X + "," + t.Y + ") " + (inside ? "inside screen OK" : "OUTSIDE"));
                ok &= inside;
            }

            var catalog = AppCatalog.LoadBlocking();
            say("Catalog: " + catalog.Count + " apps (desktop " + catalog.Count(a => a.Section == "desktop")
                + ", installed " + catalog.Count(a => a.Section == "installed") + ", windows " + catalog.Count(a => a.Section == "windows") + ")");
            int icons = catalog.Take(40).Count(a => ShellIcons.Get(a.IconPath, 64) != null);
            say("Icons: " + icons + " of " + Math.Min(40, catalog.Count) + " extracted");
            if (catalog.Count == 0 || icons == 0) ok = false;

            string dir = Path.Combine(Path.GetTempPath(), "nook-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            GroupStore.Dir = dir; // never the real groups folder
            try
            {
                var store = new GroupStore();
                var picks = catalog.Where(a => a.Section == "desktop").Take(2).Concat(catalog.Where(a => a.Section != "desktop").Take(2)).ToList();
                var apps = picks.Select(GroupApp.FromCatalog).ToList();
                var g = store.Create(new GroupData { Name = "Test group", Apps = apps, Cols = 2, Rows = 3, Color = "#1F3A5F" });
                say("Created: \"" + g.Name + "\" " + g.Data.Cols + "x" + g.Data.Rows + " with " + string.Join(", ", g.Data.Apps.Select(a => a.Name + " [" + a.Kind + "]")));
                ok &= g.Data.Cols == 2 && g.Data.Rows == 3 && g.Data.Apps.Count == apps.Count;

                store.Update(g, new GroupData { Name = "Renamed", Apps = apps.Take(3).ToList(), Cols = 9, Rows = 0, Color = "accent", HideTitle = true, IconsOnly = true });
                var back = store.Find(g.Path);
                bool clamped = back.Data.Cols == 3 && back.Data.Rows == 1 && back.Name == "Renamed" && back.Data.Apps.Count == 3
                               && back.Data.Color == "accent" && back.Data.HideTitle && back.Data.IconsOnly;
                say("Update + size cap + color + options (asked 9x0, got " + back.Data.Cols + "x" + back.Data.Rows + "): " + (clamped ? "OK" : "WRONG"));
                ok &= clamped;

                // reorder within a group, then move an app to another group
                string first = back.Data.Apps[0].Name;
                store.MoveApp(back, 0, back, 3);
                back = store.Find(g.Path);
                bool reordered = back.Data.Apps[2].Name == first;
                var other = store.Create(new GroupData { Name = "Other", Apps = new List<GroupApp>(), Cols = 1, Rows = 1 });
                store.MoveApp(back, 0, other, 0);
                back = store.Find(g.Path);
                other = store.Find(other.Path);
                bool moved = back.Data.Apps.Count == 2 && other.Data.Apps.Count == 1;
                say("Reorder and move between groups: " + (reordered && moved ? "OK" : "WRONG"));
                ok &= reordered && moved;

                // delete with undo
                string trashed = store.Trash(other);
                bool gone = store.Find(other.Path) == null;
                store.Restore(trashed, other.Path);
                bool restored = store.Find(other.Path) != null;
                say("Delete then undo: " + (gone && restored ? "OK" : "WRONG"));
                ok &= gone && restored;

                // backup round-trip
                string backup = Path.Combine(dir, "backup.wgbackup");
                store.Export(backup);
                int imported = store.Import(backup);
                say("Export then import: " + imported + " groups (" + (imported == 2 && store.Groups.Count == 4 ? "OK" : "WRONG") + ")");
                ok &= imported == 2 && store.Groups.Count == 4;
                foreach (var extra in store.Groups.Where(x => x.Path != g.Path).ToList()) store.Delete(extra);

                store.SavePosition(back, 1234, 56);
                store.Reload();
                var placed = store.Find(g.Path);
                say("Position saved: " + (placed.Data.X == 1234 && placed.Data.Y == 56 ? "OK" : "WRONG"));
                ok &= placed.Data.X == 1234 && placed.Data.Y == 56;

                foreach (var a in placed.Data.Apps.Where(a => a.Kind == "link"))
                {
                    string copy = LinkCache.Materialize(a);
                    bool exists = File.Exists(copy) && File.Exists(a.Target);
                    say("Link copy for " + a.Name + ": " + (exists ? "OK (original untouched)" : "FAILED"));
                    ok &= exists;
                }

                store.Delete(placed);
                say("Delete: " + (store.Groups.Count == 0 ? "OK" : "WRONG"));
                ok &= store.Groups.Count == 0;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
            say(ok ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(report, log.ToString());
            return ok ? 0 : 1;
        }

        static int Render(string dir)
        {
            Directory.CreateDirectory(dir);
            Anim.Enabled = false;
            var app = new App();
            DesktopGrid.Refresh();
            var catalog = AppCatalog.LoadBlocking();
            foreach (var c in catalog) IconLoader.GetNow(c.Source, () => c.IconPath, 64);

            Func<string[], List<GroupApp>> pick = names =>
            {
                var list = new List<GroupApp>();
                foreach (var n in names)
                {
                    var c = catalog.FirstOrDefault(a => a.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (c == null) continue;
                    try { list.Add(GroupApp.FromCatalog(c)); } catch { }
                }
                return list;
            };
            Func<string, int, int, string[], Group> make = (name, c, r, names) => new Group
            {
                Path = "x:\\" + name + ".json",
                Data = new GroupData { Name = name, Cols = c, Rows = r, Apps = pick(names) }
            };
            var groups = new List<Group>
            {
                make("Games", 2, 2, new[] { "Steam", "Squad", "Buckshot", "CurseForge" }),
                make("Gear", 1, 1, new[] { "G HUB", "Stream Deck", "Glorious" }),
                make("Everyday", 3, 2, new[] { "Discord", "Edge", "Windhawk", "claude", "File Explorer", "Calculator", "Notepad" }),
            };
            var workspace = make("Coding", 1, 1, new[] { "Edge", "Terminal", "Notepad" });
            workspace.Data.Kind = "workspace";
            workspace.Data.Icon = WorkspaceIcons.All[1].Key;
            workspace.Data.Layout = "stack";
            var quiet = make("", 1, 1, new[] { "Spotify", "Discord" });
            quiet.Data.Kind = "workspace";
            quiet.Data.Icon = WorkspaceIcons.All[3].Key;
            quiet.Data.Layout = "halves";
            groups.Add(workspace);
            groups.Add(quiet);
            // 2x2 workspaces show a map of their layout; a tinted 1x1 for comparison.
            Func<string, int, string, string, string, string[], Group> space = (name, size, icon, layout, color, names) =>
            {
                var g = make(name, size, size, names);
                g.Data.Kind = "workspace";
                g.Data.Icon = icon;
                g.Data.Layout = layout;
                g.Data.Color = color;
                return g;
            };
            var bigSpace = space("Coding", 2, WorkspaceIcons.All[1].Key, "stack", "", new[] { "Edge", "Terminal", "Notepad" });
            var tintedSpace = space("Listening", 2, WorkspaceIcons.All[3].Key, "halves", "#6B2E73", new[] { "Spotify", "Discord" });
            var tintedSmall = space("Focus", 1, WorkspaceIcons.All[12].Key, "full", "#0E5E6F", new[] { "Notepad" });
            // and one across two screens: the music apps on the main one, a browser full screen on the other
            var otherScreen = Screens.All().FirstOrDefault(s => !s.Primary);
            tintedSpace.Data.MoreScreens.Add(new WorkspaceScreen { Screen = otherScreen != null ? otherScreen.Device : "\\\\.\\DISPLAY9", Layout = "full", Apps = make("", 1, 1, new[] { "Edge" }).Data.Apps });
            foreach (var g in new[] { bigSpace, tintedSpace, tintedSmall }) foreach (var a in g.Data.Apps) { AppIcons.GetNow(a, 64); AppIcons.GetNow(a, 96); }
            foreach (var g in groups) foreach (var a in g.Data.Apps) { AppIcons.GetNow(a, 64); AppIcons.GetNow(a, 96); }

            foreach (bool dark in new[] { true, false })
            {
                string tag = dark ? "dark" : "light";
                app.InitPreview(groups, catalog, dark);
                var vm = app.Main;
                var mica = (Brush)app.TryFindResource("AppBackground") ?? Brushes.Black; // Nook's own window background

                vm.Page = "home"; // the top of Home, then all of it (settings live there)
                Snap(LoadHome(vm), 1120, 760, mica, Path.Combine(dir, tag + "-home.png"));
                Snap(LoadHome(vm), 1120, 2700, mica, Path.Combine(dir, tag + "-home-full.png"));
                vm.Page = "groups";
                Snap(LoadHome(vm), 1120, 760, mica, Path.Combine(dir, tag + "-groups.png"));

                // A still from the opening splash: three squares in and the last one popping, the ring of light
                // spreading and the name fading in.
                var intro = LoadHome(vm);
                ((FrameworkElement)intro.FindName("Splash")).Visibility = Visibility.Visible;
                ((FrameworkElement)intro.FindName("Body")).Opacity = 0;
                ((FrameworkElement)intro.FindName("TitleBar")).Opacity = 0;
                ((FrameworkElement)intro.FindName("TabBar")).Opacity = 0;
                ((FrameworkElement)intro.FindName("SplashGlow")).Opacity = 0.9;
                var ring = (FrameworkElement)intro.FindName("SplashRing");
                ring.Opacity = 0.35;
                ((ScaleTransform)ring.RenderTransform).ScaleX = ((ScaleTransform)ring.RenderTransform).ScaleY = 1.4;
                ((FrameworkElement)intro.FindName("SplashName")).Opacity = 0.8;
                var lastSq = (FrameworkElement)intro.FindName("Sq2");
                var lastSg = (TransformGroup)lastSq.RenderTransform;
                ((ScaleTransform)lastSg.Children[0]).ScaleX = ((ScaleTransform)lastSg.Children[0]).ScaleY = 0.55;
                Snap(intro, 1120, 760, mica, Path.Combine(dir, tag + "-intro.png"));

                vm.StartEdit(groups[0]);
                Snap(LoadHome(vm), 1120, 760, mica, Path.Combine(dir, tag + "-editor.png"));

                vm.Page = "island";
                Snap(LoadHome(vm), 1120, 1500, mica, Path.Combine(dir, tag + "-island.png"));
                vm.Page = "workspaces";
                Snap(LoadHome(vm), 1120, 600, mica, Path.Combine(dir, tag + "-workspaces.png"));
                vm.StartEdit(tintedSpace);
                Snap(LoadHome(vm), 1120, 1300, mica, Path.Combine(dir, tag + "-workspace-editor.png"));
                vm.Page = "clock";
                Snap(LoadHome(vm), 1120, 1550, mica, Path.Combine(dir, tag + "-clock.png"));
                app.Settings.ClockLook = "glass"; // the preview in frosted glass
                vm.Page = "home";
                vm.Page = "clock";
                Snap(LoadHome(vm), 1120, 420, mica, Path.Combine(dir, tag + "-clock-glass.png"));
                app.Settings.ClockLook = "solid";
                vm.Page = "taskbar";
                Snap(LoadHome(vm), 1120, 1100, mica, Path.Combine(dir, tag + "-taskbar.png"));
                // Nook's taskbar buttons on a dark and a light taskbar
                foreach (bool lightBar in new[] { false, true })
                    Snap(TaskbarStrip.Preview(app, lightBar), 0, 0, new SolidColorBrush(lightBar ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x1C, 0x1C, 0x1C)),
                         Path.Combine(dir, tag + "-taskbar-strip-" + (lightBar ? "light" : "dark") + ".png"));

                var acrylic = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2C, 0x33) : Color.FromRgb(0xF7, 0xF8, 0xFA));
                var popup = (FrameworkElement)Xaml.Load("Popup.xaml");
                popup.DataContext = new PopupVM(app, groups[2]);
                Snap(popup, 0, 0, acrylic, Path.Combine(dir, tag + "-popup.png"));

                // All widget sizes side by side on a wallpaper-like background
                var wall = new LinearGradientBrush(dark ? Color.FromRgb(0x10, 0x1C, 0x3E) : Color.FromRgb(0x9F, 0xB9, 0xE6),
                                                   dark ? Color.FromRgb(0x2B, 0x4A, 0x92) : Color.FromRgb(0xD6, 0xE2, 0xF6), 30);
                // Widgets laid out on the real wallpaper (top-left of the main screen), each showing its frosted piece.
                var samples = new[] { groups[1], groups[0], groups[2], Colored(make("Tools", 1, 3, new[] { "Terminal", "Paint", "Snipping", "Photos" }), "#0E5E6F"), Colored(make("Paper", 2, 2, new[] { "Notepad", "Paint", "Photos" }), "#ECEAE4"), Colored(make("Wide", 3, 3, new[] { "Steam", "Discord", "Edge", "Calculator", "Notepad", "Paint", "Photos", "Terminal", "Settings", "Store", "Snipping" }), "accent"), Colored(make("Match", 2, 2, new[] { "Steam", "Discord", "Edge" }), "wallpaper"), workspace, quiet, tintedSmall, bigSpace, tintedSpace };
                var origin = new Point(200, 200);
                double x = 20, rowW = 0, rowH = 0;
                var canvas = new Canvas();
                foreach (var g in samples)
                {
                    var wvm = new WidgetVM(app, g);
                    var w = (FrameworkElement)Xaml.Load("Widget.xaml");
                    w.DataContext = wvm;
                    Canvas.SetLeft(w, x);
                    Canvas.SetTop(w, 20);
                    wvm.UpdateGlass(new Rect(origin.X + x, origin.Y + 20, wvm.Width, wvm.Height), 1.0);
                    canvas.Children.Add(w);
                    x += wvm.Width + 24;
                    rowH = Math.Max(rowH, wvm.Height);
                }
                rowW = x;
                canvas.Width = rowW;
                canvas.Height = rowH + 40;
                Brush back = Wallpaper.Loaded ? Wallpaper.GlassFor(new Rect(origin.X, origin.Y, rowW, rowH + 40), true) : wall;
                Snap(canvas, rowW, rowH + 40, back, Path.Combine(dir, tag + "-widgets.png"));

                var searchView = (FrameworkElement)Xaml.Load("Search.xaml");
                var svm = new SearchVM(app, groups) { Query = "e" };
                searchView.DataContext = svm;
                searchView.Width = 600;
                Snap(searchView, 0, 0, acrylic, Path.Combine(dir, tag + "-search.png"));
            }
            return 0;
        }

        // ---- website assets: built-in Windows apps only, over a generated wallpaper ----

        static BitmapSource BloomWallpaper(int w, int h)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x0B, 0x15, 0x33), Color.FromRgb(0x05, 0x09, 0x18), 60), null, new Rect(0, 0, w, h));
                Action<double, double, double, Color> bloom = (cx, cy, r, c) =>
                {
                    var b = new RadialGradientBrush(c, Color.FromArgb(0, c.R, c.G, c.B));
                    dc.DrawEllipse(b, null, new Point(cx * w, cy * h), r * w, r * w);
                };
                bloom(0.78, 0.30, 0.42, Color.FromArgb(0xE0, 0x2F, 0x6C, 0xE0));
                bloom(0.62, 0.72, 0.30, Color.FromArgb(0xC0, 0x1A, 0x4F, 0xB8));
                bloom(0.90, 0.80, 0.22, Color.FromArgb(0xB0, 0x7A, 0xB4, 0xFF));
                bloom(0.12, 0.95, 0.38, Color.FromArgb(0xA0, 0x16, 0x34, 0x86));
                bloom(0.35, 0.10, 0.25, Color.FromArgb(0x70, 0x3D, 0x8B, 0xF2));
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        static int SiteShots(string dir)
        {
            Directory.CreateDirectory(dir);
            Anim.Enabled = false;
            var app = new App();
            DesktopGrid.Refresh();
            var catalog = AppCatalog.LoadBlocking().Where(a => a.Section == "windows" || a.Name == "Microsoft Edge").ToList();
            foreach (var c in catalog) IconLoader.GetNow(c.Source, () => c.IconPath, 64);

            Func<string[], List<GroupApp>> pick = names =>
            {
                var list = new List<GroupApp>();
                foreach (var n in names)
                {
                    var c = catalog.FirstOrDefault(a => string.Equals(a.Name, n, StringComparison.OrdinalIgnoreCase))
                            ?? catalog.FirstOrDefault(a => a.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase));
                    if (c != null) list.Add(GroupApp.FromCatalog(c));
                }
                return list;
            };
            Func<string, int, int, string, string[], Group> make = (name, c, r, color, names) => new Group
            {
                Path = "x:\\" + name + ".json",
                Data = new GroupData { Name = name, Cols = c, Rows = r, Color = color, Apps = pick(names) }
            };
            var work = make("Work", 2, 2, "", new[] { "Microsoft Edge", "File Explorer", "Outlook", "Notepad" });
            var create = make("Create", 3, 2, "", new[] { "Paint", "Photos", "Snipping Tool", "Camera", "Sound Recorder", "Media Player" });
            var tools = make("Tools", 1, 1, "", new[] { "Terminal", "Settings", "Microsoft Store", "Windows Security" });
            var everyday = make("Everyday", 2, 3, "#0E5E6F", new[] { "Clock", "Weather", "Calculator", "Sticky Notes", "Phone Link", "Microsoft To Do" });
            var groups = new List<Group> { work, create, tools, everyday };
            foreach (var g in groups) foreach (var a in g.Data.Apps) { AppIcons.GetNow(a, 64); AppIcons.GetNow(a, 96); }

            // Each app's icon on its own, for the page's animated desktop
            string icons = Path.Combine(dir, "icons");
            Directory.CreateDirectory(icons);
            var extra = pick(new[] { "Control Panel" });
            foreach (var a in groups.SelectMany(g => g.Data.Apps).Concat(extra))
            {
                var bmp = AppIcons.GetNow(a, 96);
                string slug = new string(a.Name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
                if (bmp != null) SavePng(bmp, Path.Combine(icons, slug + ".png"));
            }

            app.InitPreview(groups, catalog, true);

            // Social preview card (800x420, saved at 1.5x): name and tagline beside widgets on a blue wallpaper
            const int W = 800, H = 420;
            var wallpaper = BloomWallpaper(W, H);
            Wallpaper.UseImage(wallpaper, new Rect(0, 0, W, H), 60);
            double u = DesktopGrid.Unit;
            var scene = new Canvas { Width = W, Height = H, Background = new ImageBrush(wallpaper) };
            Action<Group, double, double> put = (g, col, row) =>
            {
                var wvm = new WidgetVM(app, g);
                var el = (FrameworkElement)Xaml.Load("Widget.xaml");
                el.DataContext = wvm;
                double x = 392 + col * u, y = 60 + row * u;
                Canvas.SetLeft(el, x);
                Canvas.SetTop(el, y);
                wvm.UpdateGlass(new Rect(x, y, wvm.Width, wvm.Height), 1.0);
                scene.Children.Add(el);
            };
            put(work, 0, 0);
            put(tools, 2, 0);
            put(everyday, 3, 0);
            put(create, 0, 2);
            var logo = new Image { Source = IconRenderer.RenderAppIcon(256), Width = 64, Height = 64 };
            Canvas.SetLeft(logo, 52);
            Canvas.SetTop(logo, 112);
            scene.Children.Add(logo);
            var font = new FontFamily("Segoe UI Variable Display, Segoe UI");
            var title = new TextBlock { Text = "Nook", FontFamily = font, FontSize = 52, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            Canvas.SetLeft(title, 50);
            Canvas.SetTop(title, 188);
            scene.Children.Add(title);
            var tagline = new TextBlock
            {
                Text = "Everything on your Windows 11 desktop has its own place.", FontFamily = font, FontSize = 19, Width = 300, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 255, 255, 255))
            };
            Canvas.SetLeft(tagline, 52);
            Canvas.SetTop(tagline, 248);
            scene.Children.Add(tagline);
            Snap(scene, W, H, Brushes.Black, Path.Combine(dir, "og.png"));

            File.WriteAllBytes(Path.Combine(dir, "favicon.ico"), IconRenderer.BuildAppIco());
            SavePng(IconRenderer.RenderAppIcon(256), Path.Combine(dir, "icon-256.png"));
            SavePng(IconRenderer.RenderAppIcon(180), Path.Combine(dir, "apple-touch-icon.png"));
            return 0;
        }

        // ---- the island: every state, offscreen over a generated wallpaper, and live on screen ----

        static BitmapSource DemoCover()
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xF2, 0x6B, 0x3A), Color.FromRgb(0x8E, 0x2D, 0xE2), 45), null, new Rect(0, 0, 160, 160));
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0xD9, 0xFF, 0xD1, 0x66)), null, new Point(108, 58), 30, 30);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xA6, 0x1A, 0x10, 0x40)), null, new Rect(0, 112, 160, 48));
            }
            var rtb = new RenderTargetBitmap(160, 160, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // Made-up song details, so screenshots never show what's really playing.
        static IslandVM DemoIsland(App app, bool music)
        {
            var vm = new IslandVM(app);
            var art = DemoCover();
            vm.Title = "Northern Lights";
            vm.Artist = "The Weather Station";
            vm.Art = art;
            vm.SetTint(NowPlaying.TintOf(art));
            vm.SetPlaying(true);
            vm.HasMusic = music;
            vm.SetTimeline(TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(214));
            vm.SetVolume(0.62, false);
            vm.DemoBattery(82, true);
            vm.DemoStats("23%", "61%", "9.8 of 16 GB", "12%", "↓ 1.2 MB/s", "↑ 84 KB/s");
            vm.SetHeadphones("AirPods Pro", 76);
            return vm;
        }

        static int IslandShots(string dir)
        {
            Directory.CreateDirectory(dir);
            Anim.Enabled = false;
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            var wall = BloomWallpaper(460, 240);
            var levels = new[] { 0.55, 0.9, 0.4, 0.75, 0.6 };
            var states = new[]
            {
                Tuple.Create(IslandMode.Compact, false, false), Tuple.Create(IslandMode.Compact, true, false), Tuple.Create(IslandMode.Compact, true, true),
                Tuple.Create(IslandMode.Peek, true, false), Tuple.Create(IslandMode.Music, true, false), Tuple.Create(IslandMode.Clock, false, false),
                Tuple.Create(IslandMode.Volume, true, false), Tuple.Create(IslandMode.Charge, false, false),
                Tuple.Create(IslandMode.System, true, false), Tuple.Create(IslandMode.Headphones, false, false),
                // the timer: its page before it starts, then counting down (alone and with music), paused, and time's up
                Tuple.Create(IslandMode.Timer, false, false), Tuple.Create(IslandMode.Compact, false, false), Tuple.Create(IslandMode.Compact, true, false),
                Tuple.Create(IslandMode.Timer, false, false), Tuple.Create(IslandMode.Timer, false, false), Tuple.Create(IslandMode.TimerDone, false, false),
                // the shelf: resting with things on it, its page, and the drop zone
                Tuple.Create(IslandMode.Compact, false, false), Tuple.Create(IslandMode.Shelf, false, false), Tuple.Create(IslandMode.ShelfDrop, false, false)
            };
            var timers = new TimerState?[] { null, null, null, null, null, null, null, null, null, null,
                TimerState.Idle, TimerState.Running, TimerState.Running, TimerState.Running, TimerState.Paused, TimerState.Done, null, null, null };
            int shelfFrom = states.Length - 3;
            foreach (var look in new[] { "black", "glass", "notch", "tinted" })
            {
                var sheet = new StackPanel();
                for (int si = 0; si < states.Length; si++)
                {
                    var st = states[si];
                    app.Timer.Demo(timers[si] ?? TimerState.Idle, TimeSpan.FromMinutes(25), TimeSpan.FromSeconds(timers[si] == TimerState.Paused ? 734 : 1342));
                    if (si == 0) app.Shelf.Items.Clear();
                    if (si == shelfFrom)
                    {
                        // made-up things on the shelf: a note, a link, a picture and a file (pointed at, never changed)
                        foreach (var d in new[] { new DataObject(DataFormats.UnicodeText, "Call back about the venue on Friday"), new DataObject(DataFormats.UnicodeText, "https://www.example.com/trip-plans"),
                                                 new DataObject(DataFormats.FileDrop, new[] { @"C:\Windows\Web\Wallpaper\Windows\img0.jpg" }), new DataObject(DataFormats.FileDrop, new[] { @"C:\Windows\win.ini" }) })
                            app.Shelf.Take(d);
                    }
                    var vm = DemoIsland(app, st.Item2);
                    if (st.Item1 == IslandMode.ShelfDrop) vm.SetDrop(true);
                    IslandWindow.Look(vm, look == "glass", 0.6, true, look == "tinted" ? "#C42B3C" : "");
                    var view = new IslandView(vm);
                    view.SetShape(look == "notch");
                    view.ShowOnly(st.Item1);
                    Flush(); // let the bindings settle before measuring
                    var size = view.SizeOf(st.Item1);
                    double w = size.Width + (st.Item3 ? 8 : 0), h = size.Height + (st.Item3 ? 2 : 0);
                    view.SetSize(w, h);
                    view.CompactBars.SetLevels(levels);
                    view.PeekBars.SetLevels(levels);
                    view.MusicBars.SetLevels(levels);
                    var hist = new List<double>();
                    for (int k = 0; k < 40; k++) hist.Add(0.35 + 0.25 * Math.Sin(k / 3.0) + 0.1 * Math.Sin(k * 1.7));
                    view.CpuSpark.SetValues(hist);
                    view.MemorySpark.SetValues(hist.Select(v => 0.55 + v * 0.1).ToList());
                    view.GpuSpark.SetValues(hist.Select(v => v * 0.5).ToList());
                    view.NetSpark.SetValues(hist.Select(v => v * v * 3).ToList());
                    for (int k = 0; k < view.Dots.Length; k++) { view.Dots[k].Count = view.Dots.Length; view.Dots[k].Index = k; view.Dots[k].InvalidateMeasure(); }
                    view.Root.Height = view.Top + h + 18;
                    var cell = new Grid { Width = IslandView.Width, Height = view.Root.Height, ClipToBounds = true };
                    cell.Children.Add(new Image { Source = wall, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top });
                    if (look == "glass")
                    {
                        // Stands in for Windows' blur, which only exists on screen.
                        var b = view.Bounds(w, h);
                        cell.Children.Add(new Image
                        {
                            Source = wall, Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.6,
                            Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 30 },
                            Clip = new RectangleGeometry(b, IslandView.Radius(h), IslandView.Radius(h))
                        });
                    }
                    cell.Children.Add(view.Root);
                    sheet.Children.Add(cell);
                    sheet.Children.Add(new Border { Height = 6, Background = Brushes.Black });
                }
                Snap(sheet, 0, 0, Brushes.Black, Path.Combine(dir, "island-" + look + ".png"));
            }
            ClockShots(app, dir);
            return 0;
        }

        // Writes only yes/no answers and numbers, never song details.
        static string updateStatus = "(no answer yet)";

        static int MediaReport(string report)
        {
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            var media = new NowPlaying(app.Dispatcher);
            var audio = new AudioLevel();
            int changes = 0;
            media.Changed += () => changes++;
            media.Start();
            var phones = new HeadphoneWatcher(app.Dispatcher);
            phones.Start();
            app.Updater.Changed += () => { if (!app.Updater.Busy) updateStatus = app.Updater.Status; };
            app.Updater.Check(true);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var sb = new StringBuilder();
                sb.AppendLine("available: " + media.Available + ", playing: " + media.Playing + ", art: " + (media.Art != null) + ", tint: " + media.Tint + ", has duration: " + (media.Duration > TimeSpan.Zero) + ", changes: " + changes);
                float l, r;
                bool ok = audio.Read(out l, out r);
                sb.AppendLine("meter: " + ok + ", peaks " + l.ToString("0.000") + " " + r.ToString("0.000") + ", volume " + audio.Volume.ToString("0.00"));
                sb.AppendLine("headphone watcher running: " + phones.Running);
                var st = new SystemStats();
                st.Sample();
                System.Threading.Thread.Sleep(1000);
                st.Sample();
                sb.AppendLine("cpu " + (st.Cpu * 100).ToString("0") + "%, memory " + (st.Memory * 100).ToString("0") + "% of " + st.MemoryTotalGb.ToString("0") + " GB, gpu " +
                              (st.Gpu < 0 ? "n/a" : (st.Gpu * 100).ToString("0") + "%") + ", down " + SystemStats.Speed(st.Down) + ", up " + SystemStats.Speed(st.Up));
                phones.Stop();
                media.Stop();
                audio.Release();
                sb.AppendLine("update check: " + updateStatus);
                File.WriteAllText(report, sb.ToString());
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }
        // Shows the real island over a test card at the top of the main screen and screenshots just that area.
        static int IslandLive(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            app.Settings.IslandOn = true;

            var mon = Native.MonitorFromPoint(new Native.POINT(), Native.MONITOR_DEFAULTTOPRIMARY);
            var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            Native.GetMonitorInfo(mon, ref mi);
            int cw = 700, chh = 250, cx = mi.rcMonitor.Left + (mi.rcMonitor.Right - mi.rcMonitor.Left - cw) / 2, cy = mi.rcMonitor.Top;
            var card = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = cx, Top = cy, Width = cw, Height = chh
            };
            var canvas = new Canvas { Background = Brushes.White };
            for (int i = 0; i < 35; i++)
            {
                var r = new System.Windows.Shapes.Rectangle { Width = 20, Height = chh, Fill = i % 3 == 0 ? Brushes.OrangeRed : i % 3 == 1 ? Brushes.SeaGreen : Brushes.RoyalBlue };
                Canvas.SetLeft(r, i * 20);
                canvas.Children.Add(r);
            }
            var text = new TextBlock { Text = "NOOK ISLAND TEST 0123456789", FontSize = 30, FontWeight = FontWeights.Bold, Background = Brushes.Yellow };
            Canvas.SetLeft(text, 60);
            Canvas.SetTop(text, 22);
            canvas.Children.Add(text);
            card.Content = canvas;

            var island = new IslandWindow(app, DemoIsland(app, true));
            var steps = new List<Tuple<Action, string>>
            {
                Tuple.Create<Action, string>(() => { app.Settings.IslandShape = "notch"; island.ApplySettings(); island.ShowStateNow(IslandMode.Compact, false); }, "live-black-notch-compact.png"),
                Tuple.Create<Action, string>(() => { app.Settings.IslandShape = "float"; island.ApplySettings(); island.ShowStateNow(IslandMode.Compact, false); }, "live-black-compact.png"),
                Tuple.Create<Action, string>(() => island.ShowStateNow(IslandMode.Music, false), "live-black-music.png"),
                Tuple.Create<Action, string>(() => { app.Settings.IslandStyle = "glass"; app.Settings.IslandBlur = 100; island.ApplySettings(); island.ShowStateNow(IslandMode.Compact, false); }, "live-glass100-compact.png"),
                Tuple.Create<Action, string>(() => island.ShowStateNow(IslandMode.Music, false), "live-glass100-music.png"),
                Tuple.Create<Action, string>(() => { app.Settings.IslandBlur = 30; island.ApplySettings(); island.ShowStateNow(IslandMode.Music, false); }, "live-glass30-music.png"),
                Tuple.Create<Action, string>(() => { app.Settings.IslandShape = "notch"; island.ApplySettings(); island.ShowStateNow(IslandMode.Peek, false); }, "live-glass-notch-peek.png"),
            };
            int step = -1;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    if (step >= 0)
                    {
                        using (var bmp = new System.Drawing.Bitmap(cw, chh))
                        using (var g = System.Drawing.Graphics.FromImage(bmp))
                        {
                            g.CopyFromScreen(cx, cy, 0, 0, new System.Drawing.Size(cw, chh));
                            bmp.Save(Path.Combine(dir, steps[step].Item2));
                        }
                    }
                    step++;
                    if (step < steps.Count) steps[step].Item1();
                    else { timer.Stop(); island.Close(); card.Close(); app.Shutdown(); }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString());
                    timer.Stop();
                    app.Shutdown();
                }
            };
            card.Show();
            island.Show();
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- the page-title blues, live: two frames a couple of seconds apart should differ ----

        static int TitleLive(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            var text = new TextBlock { Text = "Nook", FontSize = 96, FontWeight = FontWeights.SemiBold, Margin = new Thickness(20) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TitleFill");
            var root = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x27)), Children = { text } };
            var w = new Window
            {
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = 40, Top = 40, SizeToContent = SizeToContent.WidthAndHeight, Content = root
            };
            w.Resources["TitleFill"] = TitleFill.Make(true, true); // as Nook's window does
            w.Show();
            TitleFill.Run(true);
            int shot = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    var rtb = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth), (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    SavePng(rtb, Path.Combine(dir, "title-" + shot + ".png"));
                    if (++shot < 2) return;
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                timer.Stop();
                TitleFill.Run(false);
                w.Close();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- the island on every screen, at a chosen width, then back to one screen ----

        static int IslandsLive(string report)
        {
            var sb = new StringBuilder();
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            app.Settings.IslandOn = true;
            app.Settings.IslandScreen = "*";
            app.Settings.IslandWidth = 300;
            app.SyncIsland();
            int screens = Screens.All().Count, step = 0;
            bool ok = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (s, e) =>
            {
                var isles = Application.Current.Windows.OfType<IslandWindow>().ToList();
                if (step == 0)
                {
                    bool each = isles.Count == screens && isles.Select(i => i.Device).Distinct().Count() == screens;
                    bool wide = isles.All(i => Math.Abs(i.PillWidth - 300) < 2);
                    ok &= each && wide;
                    sb.AppendLine("All screens: " + isles.Count + " islands on " + screens + " screens " + (each ? "OK" : "WRONG"));
                    sb.AppendLine("Width 300: " + string.Join(", ", isles.Select(i => Math.Round(i.PillWidth).ToString())) + (wide ? " OK" : " WRONG"));
                    app.Settings.IslandScreen = "";
                    app.Settings.IslandWidth = 0;
                    app.SyncIsland();
                    step++;
                    return;
                }
                bool one = isles.Count == 1, fits = isles.Count == 1 && isles[0].PillWidth < 200;
                ok &= one && fits;
                sb.AppendLine("Main screen only: " + isles.Count + " island " + (one ? "OK" : "WRONG"));
                sb.AppendLine("Width back to fitting the time: " + string.Join(", ", isles.Select(i => Math.Round(i.PillWidth).ToString())) + (fits ? " OK" : " WRONG"));
                sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
                File.WriteAllText(report, sb.ToString());
                timer.Stop();
                app.Settings.IslandOn = false;
                app.SyncIsland();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- the real desktop clock on the main screen, solid then frosted glass, drawn from its own window ----

        static int ClockLive(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            app.Settings.ClockOn = true;
            app.Settings.ClockTextSize = 168;
            app.SyncClock();
            var looks = new[] { "solid", "glass" };
            int step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    var clock = Application.Current.Windows.OfType<DesktopClock>().FirstOrDefault();
                    if (clock == null) throw new InvalidOperationException("the clock didn't open");
                    var root = (FrameworkElement)clock.Content;
                    var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    SavePng(rtb, Path.Combine(dir, "clock-" + looks[step] + ".png"));
                    if (++step < looks.Length)
                    {
                        app.Settings.ClockLook = looks[step];
                        app.SyncClock();
                        return;
                    }
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                timer.Stop();
                app.Settings.ClockOn = false;
                app.SyncClock();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- Nook's own window, opened exactly the way the app opens it, on each page; two frames per page ----

        static int HomeLive(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            app.Updater.PretendAvailable("9.9.9"); // the update note should greet the window
            app.ShowHome();
            var pages = new[] { "home", "groups", "workspaces", "island", "clock", "taskbar", "home" };
            int step = 0;
            var pills = new StringBuilder();
            bool pillsOk = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    var w = Application.Current.Windows.OfType<HomeWindow>().FirstOrDefault();
                    if (w == null) throw new InvalidOperationException("Nook's window isn't open");
                    if (step < pages.Length * 2)
                    {
                        if (step % 2 == 0) app.Main.Page = pages[step / 2];
                        else
                        {
                            var root = (FrameworkElement)w.Content;
                            var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            rtb.Render(root);
                            SavePng(rtb, Path.Combine(dir, pages[step / 2] + ".png"));
                            bool on = w.PillOnTab();
                            pillsOk &= on;
                            pills.AppendLine("Tab highlight on " + pages[step / 2] + ": " + (on ? "OK" : "WRONG"));
                        }
                        step++;
                        return;
                    }
                    var win = Application.Current.Windows.OfType<HomeWindow>().First();
                    var note = ((FrameworkElement)win.Content).FindName("UpdatePrompt") as FrameworkElement;
                    bool noted = note != null && note.IsVisible && note.Opacity > 0.9;
                    // Scrolling down over the tabs goes to the next one, and up goes back.
                    var bar = (FrameworkElement)((FrameworkElement)win.Content).FindName("TabBar");
                    Action<int> wheel = delta => bar.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent });
                    app.Main.Page = "home";
                    wheel(-120);
                    string down = app.Main.Page;
                    System.Threading.Thread.Sleep(300);
                    wheel(120);
                    string up = app.Main.Page;
                    bool scrolls = down == "groups" && up == "home";
                    File.WriteAllText(Path.Combine(dir, "home.txt"), "Opened every page: PASS\r\nTitle color patches moving: " + TitleFill.Moving + " of 4\r\n" + pills +
                        "Update note on open: " + (noted ? "OK" : "WRONG") + "\r\n" +
                        "Scrolling over the tabs (home -> " + down + " -> " + up + "): " + (scrolls ? "OK" : "WRONG") + "\r\n" +
                        "RESULT: " + (pillsOk && noted && scrolls && TitleFill.Moving == 4 ? "PASS" : "FAIL") + "\r\n");
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                timer.Stop();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- the shelf for real, with the real pointer: a test window drags text, a file and a picture onto the
        //      island, then two tiles are dragged off onto a test target. Only test files in the dev folder. ----

        static int ShelfLive(string dir)
        {
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            bool ok = true;
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            app.Settings.IslandOn = true;
            app.Settings.IslandScreen = "";
            app.SyncIsland();
            string file = Path.Combine(Settings.DataDir, "Shelf test.txt");
            Directory.CreateDirectory(Settings.DataDir);
            File.WriteAllText(file, "parked");

            var main = Screens.Primary(Screens.All());
            double scale = main.Scale;
            int cx = (main.Work.Left + main.Work.Right) / 2, cy = (main.Work.Top + main.Work.Bottom) / 2;
            Func<string, Brush, int, Window> card = (text, fill, x) =>
            {
                var w = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual, Width = 220, Height = 120, Left = (x - 110 * scale) / scale, Top = (cy - 60 * scale) / scale,
                    Background = fill, Content = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                w.Show();
                return w;
            };
            var source = card("Drag source", Brushes.SteelBlue, cx - (int)(200 * scale));
            var target = card("Drop target", Brushes.SeaGreen, cx + (int)(200 * scale));
            DataObject next = null;
            source.MouseLeftButtonDown += (s, e) => { if (next == null) return; var d = next; next = null; DragDrop.DoDragDrop(source, d, DragDropEffects.Copy | DragDropEffects.Link); };
            string received = null;
            target.AllowDrop = true;
            target.DragOver += (s, e) => { e.Effects = DragDropEffects.Copy; e.Handled = true; };
            target.Drop += (s, e) =>
            {
                received = e.Data.GetDataPresent(DataFormats.FileDrop) ? "file:" + ((string[])e.Data.GetData(DataFormats.FileDrop))[0]
                         : e.Data.GetDataPresent(DataFormats.UnicodeText) ? "text:" + e.Data.GetData(DataFormats.UnicodeText) : "?";
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            };

            var isle = Application.Current.Windows.OfType<IslandWindow>().First();
            var islandSpot = new System.Drawing.Point(main.Bounds.Left + main.PixelWidth / 2, main.Bounds.Top + (int)(24 * scale));
            string seen = "";
            Native.POINT original;
            Native.GetCursorPos(out original);

            Action<Exception> fail = null; // set once finish exists: a step that throws still writes the report
            // Presses at one point, glides to another and lets go, on another thread (the drag itself runs on this one).
            Action<System.Drawing.Point, System.Drawing.Point, string, Action> drive = (from, to, shot, done) =>
            {
                new System.Threading.Thread(() =>
                {
                    SetCursorPos(from.X, from.Y);
                    System.Threading.Thread.Sleep(200);
                    mouse_event(0x2, 0, 0, 0, IntPtr.Zero);
                    System.Threading.Thread.Sleep(150);
                    for (int k = 1; k <= 30; k++)
                    {
                        SetCursorPos(from.X + (to.X - from.X) * k / 30, from.Y + (to.Y - from.Y) * k / 30);
                        System.Threading.Thread.Sleep(20);
                    }
                    System.Threading.Thread.Sleep(600);
                    if (shot != null)
                    {
                        isle.Dispatcher.BeginInvoke(new Action(() => seen = isle.Mode.ToString()));
                        System.Threading.Thread.Sleep(150);
                        int w = (int)(460 * scale), h = (int)(130 * scale);
                        using (var bmp = new System.Drawing.Bitmap(w, h))
                        {
                            using (var g = System.Drawing.Graphics.FromImage(bmp)) g.CopyFromScreen(islandSpot.X - w / 2, main.Bounds.Top, 0, 0, new System.Drawing.Size(w, h));
                            bmp.Save(Path.Combine(dir, shot), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    mouse_event(0x4, 0, 0, 0, IntPtr.Zero);
                    System.Threading.Thread.Sleep(700);
                    isle.Dispatcher.BeginInvoke(new Action(() => { try { done(); } catch (Exception ex) { if (fail != null) fail(ex); } }));
                }) { IsBackground = true }.Start();
            };
            Func<Window, System.Drawing.Point> middle = w =>
            {
                Native.RECT r;
                Native.GetWindowRect(new System.Windows.Interop.WindowInteropHelper(w).Handle, out r);
                return new System.Drawing.Point((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            };
            Func<int, System.Drawing.Point> tile = i =>
            {
                var list = (ItemsControl)((FrameworkElement)isle.Content).FindName("ShelfItems");
                var el = (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(i);
                var p = el.PointToScreen(new Point(el.ActualWidth / 2, el.ActualHeight / 2 - 6));
                return new System.Drawing.Point((int)p.X, (int)p.Y);
            };
            Action finish = () =>
            {
                SetCursorPos(original.X, original.Y);
                sb.AppendLine("Your original file still there: " + (File.Exists(file) ? "OK" : "WRONG"));
                ok &= File.Exists(file);
                sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
                File.WriteAllText(Path.Combine(dir, "shelf.txt"), sb.ToString());
                source.Close();
                target.Close();
                app.Shelf.Clear();
                app.Settings.IslandOn = false;
                app.SyncIsland();
                app.Shutdown();
            };
            Action<string, bool> check = (what, pass) => { ok &= pass; sb.AppendLine(what + ": " + (pass ? "OK" : "WRONG")); };
            fail = ex => { ok = false; sb.AppendLine("Stopped: " + ex.GetType().Name + " (shelf has " + app.Shelf.Items.Count + ", island " + isle.Mode + ")"); finish(); };

            // 5: drag the text off onto the target
            Action dragText = () =>
            {
                isle.ShowPage(IslandMode.Shelf);
                Flush();
                received = null;
                drive(tile(0), middle(target), null, () =>
                {
                    check("Text dragged off lands as text and leaves the shelf", received == "text:Shelf test note" && app.Shelf.Items.Count == 1);
                    finish();
                });
            };
            // 4: drag the file off onto the target
            Action dragFile = () =>
            {
                isle.ShowPage(IslandMode.Shelf);
                Flush();
                var page = new System.Drawing.Bitmap((int)(460 * scale), (int)(260 * scale));
                using (var g = System.Drawing.Graphics.FromImage(page)) g.CopyFromScreen(islandSpot.X - page.Width / 2, main.Bounds.Top, 0, 0, page.Size);
                page.Save(Path.Combine(dir, "shelf-page.png"), System.Drawing.Imaging.ImageFormat.Png);
                page.Dispose();
                received = null;
                drive(tile(1), middle(target), null, () =>
                {
                    check("File dragged off hands over your file and leaves the shelf", received == "file:" + file && app.Shelf.Items.Count == 2);
                    dragText();
                });
            };
            // 3: a picture with no file behind it
            Action dropPicture = () =>
            {
                var pic = new RenderTargetBitmap(64, 48, 96, 96, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(new LinearGradientBrush(Colors.OrangeRed, Colors.Gold, 30), null, new Rect(0, 0, 64, 48));
                pic.Render(dv);
                var d = new DataObject();
                d.SetData(DataFormats.Bitmap, pic);
                next = d;
                drive(middle(source), islandSpot, null, () =>
                {
                    var last = app.Shelf.Items.LastOrDefault();
                    check("A picture gets a copy in Nook's shelf folder", app.Shelf.Items.Count == 3 && last != null && last.Own && File.Exists(last.Path) && last.Path.StartsWith(Shelf.Dir, StringComparison.OrdinalIgnoreCase));
                    dragFile();
                });
            };
            // 2: a file
            Action dropFile = () =>
            {
                next = new DataObject(DataFormats.FileDrop, new[] { file });
                drive(middle(source), islandSpot, null, () =>
                {
                    var last = app.Shelf.Items.LastOrDefault();
                    check("A file is kept by where it is (not copied)", app.Shelf.Items.Count == 2 && last != null && !last.Own && last.Path == file);
                    dropPicture();
                });
            };
            // 1: text, with a screenshot of the drop zone while it hovers
            var start = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            start.Tick += (s, e) =>
            {
                start.Stop();
                var d = new DataObject();
                d.SetData(DataFormats.UnicodeText, "Shelf test note");
                next = d;
                drive(middle(source), islandSpot, "shelf-drop.png", () =>
                {
                    check("Dragging over the island opens the drop zone (" + seen + ")", seen == "ShelfDrop");
                    check("Text dropped on the island is kept", app.Shelf.Items.Count == 1 && app.Shelf.Items[0].Kind == "text");
                    check("The shelf opens to show it (" + isle.Mode + ")", isle.Mode == IslandMode.Shelf);
                    dropFile();
                });
            };
            start.Start();
            app.Run();
            return 0;
        }

        // ---- the island's timer for real: a short one runs on the island, ends, and is stopped (no sound) ----

        static int TimerLive(string report)
        {
            var sb = new StringBuilder();
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            app.Settings.IslandOn = true;
            app.Settings.IslandScreen = "";
            app.Settings.IslandTimerSound = false;
            app.SyncIsland();
            bool ok = true;
            int step = 0;
            double restWidth = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    var isle = Application.Current.Windows.OfType<IslandWindow>().First();
                    if (step == 0)
                    {
                        restWidth = isle.PillWidth;
                        app.Timer.Start(TimeSpan.FromSeconds(4));
                    }
                    else if (step == 1)
                    {
                        bool live = isle.Model.TimerLive && isle.Mode == IslandMode.Compact && isle.PillWidth > restWidth + 20;
                        ok &= live;
                        sb.AppendLine("Counting down on the resting island (" + isle.Model.TimerText + ", " + Math.Round(restWidth) + " -> " + Math.Round(isle.PillWidth) + " wide): " + (live ? "OK" : "WRONG"));
                    }
                    else if (step == 3)
                    {
                        bool done = app.Timer.State == TimerState.Done && isle.Mode == IslandMode.TimerDone;
                        ok &= done;
                        sb.AppendLine("Time's up opens the island: " + (done ? "OK" : "WRONG (" + app.Timer.State + ", " + isle.Mode + ")"));
                        app.Timer.Cancel(); // what Stop does
                    }
                    else if (step == 4)
                    {
                        bool back = !isle.Model.TimerLive && isle.Mode == IslandMode.Compact && Math.Abs(isle.PillWidth - restWidth) < 2;
                        ok &= back;
                        sb.AppendLine("Stop puts it back to resting: " + (back ? "OK" : "WRONG (" + isle.Mode + ", " + Math.Round(isle.PillWidth) + " wide)"));
                        app.Timer.Start(TimeSpan.FromMinutes(5));
                        app.Timer.Pause();
                        string paused = app.Timer.Paused ? IslandTimer.Text(app.Timer.Remaining) : "?";
                        app.Timer.AddMinute();
                        bool added = app.Timer.Paused && IslandTimer.Text(app.Timer.Remaining) == "6:00" && paused == "5:00" && isle.Model.TimerText == "6:00";
                        ok &= added;
                        sb.AppendLine("Pause, then +1 min (" + paused + " -> " + IslandTimer.Text(app.Timer.Remaining) + "): " + (added ? "OK" : "WRONG"));
                        app.Timer.Cancel();
                        throw new OperationCanceledException();
                    }
                    step++;
                    return;
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { sb.AppendLine(ex.ToString()); ok = false; }
                sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
                File.WriteAllText(report, sb.ToString());
                timer.Stop();
                app.Settings.IslandOn = false;
                app.SyncIsland();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- the first-run window: screenshots with both choices off and on, then closed without "Get started" ----

        static int SetupLive(string dir)
        {
            Directory.CreateDirectory(dir);
            Func<string> runValue = () =>
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return k == null ? "" : (k.GetValue("Nook") as string ?? "");
            };
            string runBefore = runValue();
            bool shortcutBefore = Installer.HasDesktopShortcut;
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            app.ShowSetup();
            int step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            timer.Tick += (s, e) =>
            {
                try
                {
                    var w = Application.Current.Windows.OfType<SetupWindow>().FirstOrDefault();
                    if (w == null) throw new InvalidOperationException("The setup window isn't open");
                    var vm = (SetupVM)w.DataContext;
                    var root = (FrameworkElement)w.Content;
                    if (step < 2)
                    {
                        // the window draws its own background, so lay the content over it
                        var rtb = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        rtb.Render(root);
                        var dv = new DrawingVisual();
                        using (var dc = dv.RenderOpen())
                        {
                            dc.DrawRectangle(w.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                            dc.DrawImage(rtb, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                        }
                        var full = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        full.Render(dv);
                        SavePng(full, Path.Combine(dir, step == 0 ? "setup-off.png" : "setup-on.png"));
                        if (step == 0) { vm.Startup = true; vm.Shortcut = true; } // only the switches; nothing is applied
                        step++;
                        return;
                    }
                    w.Close(); // closing it counts as "not now"
                    bool homeOpen = Application.Current.Windows.OfType<HomeWindow>().Any();
                    bool runSame = runValue() == runBefore, shortcutSame = Installer.HasDesktopShortcut == shortcutBefore;
                    File.WriteAllText(Path.Combine(dir, "setup.txt"),
                        "Setup window: " + root.ActualWidth + " x " + root.ActualHeight + "\r\n" +
                        "Nook.exe in a passing folder: " + (vm.InPassingFolder ? "yes (heads-up shown)" : "no") + "\r\n" +
                        "Nook's window open behind it: " + (homeOpen ? "OK" : "WRONG") + "\r\n" +
                        "Closed without Get started marks setup done: " + (app.Settings.SetupDone ? "OK" : "WRONG") + "\r\n" +
                        "Startup entry unchanged: " + (runSame ? "OK" : "WRONG") + "\r\n" +
                        "Desktop shortcut unchanged: " + (shortcutSame ? "OK" : "WRONG") + "\r\n" +
                        "RESULT: " + (homeOpen && app.Settings.SetupDone && runSame && shortcutSame ? "PASS" : "FAIL") + "\r\n");
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                timer.Stop();
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- the group pop-up for real, over a striped test card: is the glass even on every side? ----

        static int PopupLive(string dir)
        {
            Directory.CreateDirectory(dir);
            var app = new App();
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var apps = new[] { "notepad.exe", "mspaint.exe", "calc.exe", "cmd.exe", "taskmgr.exe" }
                .Select(f => Path.Combine(sys, f)).Where(File.Exists).Select(f => GroupApp.FromFile(f)).ToList();
            var g = new Group { Path = Path.Combine(Path.GetTempPath(), "nook-popup-test.json"), Data = new GroupData { Name = "Popup test", Apps = apps, Cols = 1, Rows = 1 } };
            app.InitPreview(new List<Group> { g }, new List<CatalogApp>(), true);

            // The card is placed in DIPs; the screenshot and the window rect are in pixels.
            Native.RECT work;
            double scale;
            DesktopGrid.Monitor(new Native.POINT(), true, out work, out scale);
            int cw = 760, chh = 420;
            int px = work.Left + 40, py = work.Top + 40, pw = (int)(cw * scale), ph = (int)(chh * scale);
            var card = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Left = px / scale, Top = py / scale, Width = cw, Height = chh
            };
            var canvas = new Canvas { Background = Brushes.White };
            for (int i = 0; i < 40; i++)
            {
                var r = new System.Windows.Shapes.Rectangle { Width = 20, Height = chh, Fill = i % 3 == 0 ? Brushes.OrangeRed : i % 3 == 1 ? Brushes.SeaGreen : Brushes.RoyalBlue };
                Canvas.SetLeft(r, i * 20);
                canvas.Children.Add(r);
            }
            card.Content = canvas;
            card.Show();
            var popup = new PopupWindow(new PopupVM(app, g), g.Path, new Rect(px + 20 * scale, py + 60 * scale, 76 * scale, 76 * scale));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    Native.RECT wr;
                    Native.GetWindowRect(popup.Handle, out wr);
                    var content = (FrameworkElement)popup.Content;
                    File.WriteAllText(Path.Combine(dir, "popup.txt"),
                        "Window: " + (wr.Right - wr.Left) + " x " + (wr.Bottom - wr.Top) + " px\r\n" +
                        "Content: " + Math.Round(content.ActualWidth * scale) + " x " + Math.Round(content.ActualHeight * scale) + " px\r\n");
                    using (var bmp = new System.Drawing.Bitmap(pw, ph))
                    using (var gr = System.Drawing.Graphics.FromImage(bmp))
                    {
                        gr.CopyFromScreen(px, py, 0, 0, new System.Drawing.Size(pw, ph));
                        bmp.Save(Path.Combine(dir, "popup.png"));
                    }
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                popup.Close();
                card.Close();
                app.Shutdown();
            };
            popup.KeepOpenUntil = DateTime.Now.AddSeconds(10);
            popup.Show();
            popup.Activate();
            timer.Start();
            app.Run();
            return 0;
        }

        // ---- the taskbar ----

        static int TaskbarLive(string dir)
        {
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            bool ok = true;
            var app = new App();
            var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            Func<string[], List<GroupApp>> appsOf = names => names.Select(f => Path.Combine(sys, f)).Where(File.Exists).Select(f => GroupApp.FromFile(f)).ToList();
            var groups = new List<Group>
            {
                new Group { Path = Path.Combine(Path.GetTempPath(), "nook-tb-1.json"), Data = new GroupData { Name = "Tools", Apps = appsOf(new[] { "notepad.exe", "mspaint.exe", "calc.exe", "cmd.exe" }), Cols = 1, Rows = 1 } },
                new Group { Path = Path.Combine(Path.GetTempPath(), "nook-tb-2.json"), Data = new GroupData { Name = "System", Apps = appsOf(new[] { "taskmgr.exe", "control.exe" }), Cols = 1, Rows = 1 } },
                new Group { Path = Path.Combine(Path.GetTempPath(), "nook-tb-3.json"), Data = new GroupData { Kind = "workspace", Name = "Work", Icon = WorkspaceIcons.Default, Color = "#2E7DF6", Cols = 1, Rows = 1 } },
                new Group { Path = Path.Combine(Path.GetTempPath(), "nook-tb-4.json"), Data = new GroupData { Kind = "workspace", Name = "Music", Icon = WorkspaceIcons.All[3].Key, Color = "#F2C94C", Cols = 1, Rows = 1 } },
            };
            app.InitPreview(groups, new List<CatalogApp>(), true);
            app.Settings.TaskbarOn = true;
            var strip = new TaskbarStrip(app);
            var vm = strip.Model;
            bool? micBefore = AudioDevices.MicsMuted();

            // The taskbar's own buttons, to check Nook's strip covers none of them.
            Func<List<KeyValuePair<string, Rect>>> own = () =>
            {
                var list = new List<KeyValuePair<string, Rect>>();
                foreach (System.Windows.Automation.AutomationElement e in System.Windows.Automation.AutomationElement.FromHandle(TaskbarSpot.Handle)
                    .FindAll(System.Windows.Automation.TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition))
                {
                    var c = e.Current;
                    if (c.BoundingRectangle.IsEmpty || c.BoundingRectangle.Width <= 0) continue;
                    string cls = c.ClassName ?? "", id = c.AutomationId ?? "";
                    if (cls.StartsWith("SystemTray") || id == "StartButton" || id.StartsWith("Search") || id == "TaskViewButton" || id == "WidgetsButton" || cls.Contains("TaskListButton"))
                        list.Add(new KeyValuePair<string, Rect>(string.IsNullOrEmpty(id) ? cls : id, c.BoundingRectangle));
                }
                return list;
            };
            Action<string> check = side =>
            {
                var placed = strip.Placed;
                sb.AppendLine(side + ": strip at " + placed + (strip.IsVisible ? "" : " (hidden)"));
                if (!strip.IsVisible || placed.Width < 10) { ok = false; sb.AppendLine("  FAIL: not shown"); return; }
                foreach (var b in own())
                    if (b.Value.IntersectsWith(Rect.Inflate(placed, -1, -1))) { ok = false; sb.AppendLine("  FAIL: covers " + b.Key + " " + b.Value); }
                Native.RECT bar;
                Native.GetWindowRect(TaskbarSpot.Handle, out bar);
                using (var bmp = new System.Drawing.Bitmap(bar.Right - bar.Left, bar.Bottom - bar.Top))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bar.Left, bar.Top, 0, 0, bmp.Size);
                    bmp.Save(Path.Combine(dir, "taskbar-" + side + ".png"));
                }
                // The strip itself, twice as big.
                int x0 = (int)placed.Left - 8, w0 = (int)placed.Width + 16, h0 = bar.Bottom - bar.Top;
                using (var bmp = new System.Drawing.Bitmap(w0, h0))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                using (var big = new System.Drawing.Bitmap(w0 * 2, h0 * 2))
                using (var gb = System.Drawing.Graphics.FromImage(big))
                {
                    g.CopyFromScreen(x0, bar.Top, 0, 0, bmp.Size);
                    gb.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                    gb.DrawImage(bmp, 0, 0, w0 * 2, h0 * 2);
                    big.Save(Path.Combine(dir, "strip-" + side + ".png"));
                }
            };
            Action<string> shoot = name =>
            {
                var main = Screens.Primary(Screens.All());
                int h = (int)(420 * main.Scale);
                using (var bmp = new System.Drawing.Bitmap(main.Bounds.Right - main.Bounds.Left, h))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(main.Bounds.Left, main.Bounds.Bottom - h, 0, 0, bmp.Size);
                    bmp.Save(Path.Combine(dir, name + ".png"));
                }
            };

            var steps = new List<KeyValuePair<int, Action>>
            {
                new KeyValuePair<int, Action>(2500, () =>
                {
                    var room = TaskbarSpot.Find("auto");
                    sb.AppendLine("Taskbar " + room.Bar.Left + "," + room.Bar.Top + " to " + room.Bar.Right + "," + room.Bar.Bottom + ", scale " + room.Scale + ", icons " + (TaskbarSpot.Centered() ? "centered" : "on the left") + (room.AutoHide ? ", hides itself" : ""));
                    sb.AppendLine("Free space (auto): " + Math.Round(room.Left) + " to " + Math.Round(room.Right) + (room.AtRight ? " (by the tray)" : " (left end)"));
                    sb.AppendLine("Buttons shown: " + vm.Groups.Count + " groups, " + vm.Spaces.Count + " workspaces, sound " + vm.ShowSound + ", mic " + vm.ShowMic);
                    var outs = AudioDevices.Outputs();
                    string current = AudioDevices.CurrentOutput();
                    sb.AppendLine("Outputs: " + outs.Count);
                    foreach (var o in outs) sb.AppendLine("  " + o.Short + (o.Headphones ? " [headphones]" : "") + (o.Id == current ? " (in use)" : ""));
                    // Choosing the speakers already in use changes nothing, and shows the switch works.
                    if (current != null) { bool set = AudioDevices.SetOutput(current); sb.AppendLine("Re-select current output: " + (set ? "ok" : "FAIL")); if (!set) ok = false; }
                    sb.AppendLine("Microphones muted: " + (micBefore.HasValue ? micBefore.Value.ToString() : "no microphone"));
                    check("auto");
                }),
                new KeyValuePair<int, Action>(100, () =>
                {
                    // A group pops open above its button.
                    var first = vm.Groups.FirstOrDefault();
                    if (first == null) { ok = false; sb.AppendLine("FAIL: no group button"); return; }
                    var p = strip.Placed;
                    double k = p.Height / 48.0;
                    app.OpenFromTaskbar(first.Group, new Rect(p.Left + 4 * k, p.Top, 40 * k, p.Height));
                }),
                new KeyValuePair<int, Action>(1200, () =>
                {
                    var pop = app.Windows.OfType<PopupWindow>().FirstOrDefault();
                    if (pop == null) { ok = false; sb.AppendLine("FAIL: no pop-up"); return; }
                    Native.RECT r;
                    Native.GetWindowRect(pop.Handle, out r);
                    sb.AppendLine("Pop-up at " + r.Left + "," + r.Top + " to " + r.Right + "," + r.Bottom + "; taskbar top " + strip.Placed.Top);
                    if (r.Bottom > strip.Placed.Top) { ok = false; sb.AppendLine("  FAIL: pop-up overlaps the taskbar"); }
                    shoot("popup-above");
                    pop.Close();
                }),
                new KeyValuePair<int, Action>(300, () => { if (vm.Switched != null) vm.Switched("Headphones", true); }),
                new KeyValuePair<int, Action>(500, () =>
                {
                    // The bubble's own window has a 12 px clear margin around the bubble.
                    var f = strip.FlashRect();
                    sb.AppendLine("Sound bubble at " + f + "; bubble bottom " + (f.Bottom - 12) + ", taskbar top " + strip.Placed.Top + "; sound button " + strip.ScreenRectOf("SoundButton") + ", root " + strip.ScreenRectOf("Root"));
                    if (f.IsEmpty || f.Bottom - 12 > strip.Placed.Top) { ok = false; sb.AppendLine("  FAIL: the bubble covers the taskbar"); }
                    shoot("sound-flash");
                }),
                new KeyValuePair<int, Action>(1200, () =>
                {
                    // No microphone here: just the muted look, for the screenshot.
                    if (!micBefore.HasValue) { vm.PretendMic(true); return; }
                    AudioDevices.MuteMics(true);
                    vm.ReadMic();
                    bool muted = AudioDevices.MicsMuted() == true;
                    sb.AppendLine("Muted all: " + (muted ? "ok" : "FAIL"));
                    if (!muted) ok = false;
                }),
                new KeyValuePair<int, Action>(1100, () =>
                {
                    if (!micBefore.HasValue) { check("mic-muted"); vm.PretendMic(null); }
                    if (micBefore.HasValue)
                    {
                        check("mic-muted");
                        AudioDevices.MuteMics(micBefore.Value);
                        vm.ReadMic();
                        bool back = AudioDevices.MicsMuted() == micBefore;
                        sb.AppendLine("Microphones restored: " + (back ? "ok" : "FAIL"));
                        if (!back) ok = false;
                    }
                    app.Settings.TaskbarSide = "right";
                    strip.Refresh();
                }),
                new KeyValuePair<int, Action>(1500, () => { check("right"); app.Settings.TaskbarSide = "left"; strip.Refresh(); }),
                new KeyValuePair<int, Action>(1500, () => check("left")),
            };
            int step = 0;
            var timer = new DispatcherTimer();
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try { steps[step].Value(); }
                catch (Exception ex) { ok = false; sb.AppendLine("FAIL: " + ex); }
                step++;
                if (step < steps.Count) { timer.Interval = TimeSpan.FromMilliseconds(steps[step].Key); timer.Start(); return; }
                sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
                File.WriteAllText(Path.Combine(dir, "taskbar.txt"), sb.ToString());
                strip.Leaving = true;
                strip.Close();
                app.Shutdown();
            };
            timer.Interval = TimeSpan.FromMilliseconds(steps[0].Key);
            strip.Show();
            timer.Start();
            // Whatever happens, the microphones end up as they were.
            try { app.Run(); }
            finally { if (micBefore.HasValue && AudioDevices.MicsMuted() != micBefore) AudioDevices.MuteMics(micBefore.Value); }
            return ok ? 0 : 1;
        }

        // ---- workspaces: open two test windows (this exe, as a stand-in app) and check they land in their spaces ----

        // A plain resizable window that stands in for an app. "move" makes it jump somewhere else just after it
        // appears, the way some apps restore where they were last time.
        static int DummyWindow(string label, bool move)
        {
            var app = new Application();
            var w = new Window { Title = "Nook test window " + label, Width = 520, Height = 380, Content = new TextBlock { Text = "Test window " + label, Margin = new Thickness(20) } };
            if (move)
            {
                var jump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                jump.Tick += (s, e) => { jump.Stop(); w.Left = 120; w.Top = 90; w.Width = 640; w.Height = 420; };
                w.Loaded += (s, e) => jump.Start();
            }
            var quit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(40) };
            quit.Tick += (s, e) => app.Shutdown();
            quit.Start();
            app.Run(w);
            return 0;
        }

        static int WorkspaceTest(string report)
        {
            var sb = new StringBuilder();
            var app = new App();
            app.InitPreview(new List<Group>(), new List<CatalogApp>(), true);
            string exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            string tmp = Path.Combine(Path.GetTempPath(), "nook-workspace-test");
            Directory.CreateDirectory(tmp);
            var linksBefore = Directory.Exists(Path.Combine(Settings.DataDir, "links")) ? Directory.GetFiles(Path.Combine(Settings.DataDir, "links")).ToList() : new List<string>();
            var apps = new List<GroupApp>();
            // Three copies of a stand-in app in "Main and two"; C jumps somewhere else right after it appears. With a
            // second screen, D and E go side by side on it at the same time.
            var second = Screens.All().FirstOrDefault(s => !s.Primary);
            var names = second == null ? new[] { "A", "B", "C" } : new[] { "A", "B", "C", "D", "E" };
            foreach (var n in names)
            {
                // A shortcut with arguments, so each test window is its own "app".
                string lnk = Path.Combine(tmp, "Test " + n + ".lnk");
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe });
                sc.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "--dev-dummy " + n + (n == "C" ? " move" : "") });
                sc.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                apps.Add(GroupApp.FromFile(lnk));
            }
            var layout = LayoutPreset.Find("stack");
            var g = new Group { Path = Path.Combine(tmp, "test.json"), Data = new GroupData { Kind = "workspace", Name = "Test", Apps = apps.Take(3).ToList(), Layout = layout.Id, Screen = "" } };
            if (second != null) g.Data.MoreScreens.Add(new WorkspaceScreen { Screen = second.Device, Layout = "halves", Apps = apps.Skip(3).ToList() });
            var screen = Screens.Find("");
            var started = DateTime.UtcNow;
            new WorkspaceRun(g).Start(() =>
            {
                sb.AppendLine("All windows placed in " + (DateTime.UtcNow - started).TotalSeconds.ToString("0.0") + " s on " + screen.Label + (second != null ? " and " + second.Label : ""));
                // Checked once C has had time to jump away and be put back.
                var finish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
                finish.Tick += (s, e) =>
                {
                    finish.Stop();
                    Func<ScreenInfo, Rect, Rect> zoneOn = (scr, z) =>
                    {
                        double l = scr.Work.Left, tp = scr.Work.Top, ww = scr.Work.Right - l, hh = scr.Work.Bottom - tp;
                        double x1 = Math.Round(l + z.Left * ww), y1 = Math.Round(tp + z.Top * hh), x2 = Math.Round(l + z.Right * ww), y2 = Math.Round(tp + z.Bottom * hh);
                        return new Rect(x1, y1, x2 - x1, y2 - y1);
                    };
                    var expect = layout.Zones.Select(z => zoneOn(screen, z)).ToList();
                    if (second != null) expect.AddRange(LayoutPreset.Find("halves").Zones.Select(z => zoneOn(second, z)));
                    bool ok = true;
                    int seen = 0;
                    foreach (var w in OtherWindows.AppWindows())
                    {
                        var title = new StringBuilder(256);
                        GetWindowText(w, title, title.Capacity);
                        if (!title.ToString().StartsWith("Nook test window ")) continue;
                        int i = Array.IndexOf(names, title.ToString().Substring("Nook test window ".Length));
                        if (i < 0) continue;
                        seen++;
                        Native.RECT fr;
                        Native.DwmGetWindowAttribute(w, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out fr, 16);
                        var got = new Rect(fr.Left, fr.Top, fr.Right - fr.Left, fr.Bottom - fr.Top);
                        bool match = Math.Abs(got.X - expect[i].X) <= 2 && Math.Abs(got.Y - expect[i].Y) <= 2 && Math.Abs(got.Width - expect[i].Width) <= 2 && Math.Abs(got.Height - expect[i].Height) <= 2;
                        ok &= match;
                        sb.AppendLine("Window " + names[i] + ": " + got + " expected " + expect[i] + (match ? " OK" : " WRONG"));
                        uint pid;
                        Native.GetWindowThreadProcessId(w, out pid);
                        try { System.Diagnostics.Process.GetProcessById((int)pid).Kill(); } catch { }
                    }
                    if (seen != names.Length) { ok = false; sb.AppendLine("Found " + seen + " of " + names.Length + " test windows"); }
                    sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
                    File.WriteAllText(report, sb.ToString());
                    try { Directory.Delete(tmp, true); } catch { }
                    string links = Path.Combine(Settings.DataDir, "links");
                    if (Directory.Exists(links)) foreach (var f in Directory.GetFiles(links)) if (!linksBefore.Contains(f)) try { File.Delete(f); } catch { }
                    app.Shutdown();
                };
                finish.Start();
            });
            app.Run();
            return 0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);

        // The desktop clock at each size, over a generated wallpaper.
        static void ClockShots(App app, string dir)
        {
            var wall = BloomWallpaper(1000, 420);
            foreach (var size in new[] { 72, 112, 168 })
            {
                app.Settings.ClockTextSize = size;
                var clock = new DesktopClock(app, "");
                var content = (FrameworkElement)clock.Content;
                clock.Content = null;
                foreach (UIElement child in ((Panel)content).Children) child.Opacity = 1;
                Snap(content, 1000, 420, new ImageBrush(wall), Path.Combine(dir, "clock-" + size + ".png"));
                clock.Close();
            }
        }

        // ---- dragging a workspace button: a real press, move and release with the pointer, on a test button kept on top ----

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, IntPtr extra);

        // kind: "" = a 1x1 workspace button, "big" = a 2x2 workspace, "group" = a 1x1 group.
        static int DragTest(string report, string kind)
        {
            bool asGroup = kind == "group";
            int size = kind == "big" ? 2 : 1;
            var sb = new StringBuilder();
            var app = new App();
            string tmp = Path.Combine(Path.GetTempPath(), "nook-drag-test");
            Directory.CreateDirectory(tmp);
            var main = Screens.Primary(Screens.All());
            var g = new Group
            {
                Path = Path.Combine(tmp, "drag.json"),
                Data = new GroupData { Kind = asGroup ? "" : "workspace", Name = "Drag test", Icon = WorkspaceIcons.Default, Layout = "open", Cols = size, Rows = size, X = main.Work.Left + 300, Y = main.Work.Top + 300 }
            };
            app.InitPreview(new List<Group> { g }, new List<CatalogApp>(), true);
            var w = new WidgetWindow(app, g) { TestOnTop = true, Topmost = true };
            app.widgetsForTest[g.Path] = w;
            Native.POINT original;
            Native.GetCursorPos(out original);
            var steps = new List<Action>();
            Native.RECT before = new Native.RECT();
            steps.Add(() =>
            {
                Native.GetWindowRect(w.Handle, out before);
                SetCursorPos((before.Left + before.Right) / 2, before.Top + 25);
            });
            steps.Add(() => mouse_event(0x2 /*left down*/, 0, 0, 0, IntPtr.Zero));
            for (int i = 1; i <= 12; i++)
            {
                int k = i;
                steps.Add(() => SetCursorPos((before.Left + before.Right) / 2 + k * 20, before.Top + 25 + k * 4));
            }
            steps.Add(() => mouse_event(0x4 /*left up*/, 0, 0, 0, IntPtr.Zero));
            steps.Add(() => { });
            steps.Add(() => { });
            steps.Add(() =>
            {
                Native.RECT after;
                Native.GetWindowRect(w.Handle, out after);
                SetCursorPos(original.X, original.Y);
                int moved = after.Left - before.Left;
                bool ok = moved > 150 && app.WorkspaceRuns == 0;
                sb.AppendLine(asGroup ? "Kind: 1x1 group" : "Kind: workspace");
                sb.AppendLine("Before: " + before.Left + "," + before.Top + "  after: " + after.Left + "," + after.Top + "  (moved " + moved + " px; pointer moved 240 px)");
                sb.AppendLine("Workspace ran on release: " + (app.WorkspaceRuns > 0));
                sb.AppendLine("RESULT: " + (ok ? "PASS" : "FAIL"));
                File.WriteAllText(report, sb.ToString());
                w.Close();
                try { Directory.Delete(tmp, true); } catch { }
                app.Shutdown();
            });
            int step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            timer.Tick += (s, e) =>
            {
                if (step == 0) timer.Interval = TimeSpan.FromMilliseconds(40);
                if (step < steps.Count) steps[step++]();
                else timer.Stop();
            };
            w.Loaded += (s, e) =>
            {
                var start = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                start.Tick += (s2, e2) => { start.Stop(); timer.Start(); };
                start.Start();
            };
            w.Show();
            app.Run();
            return 0;
        }
        static Group Colored(Group g, string color)
        {
            g.Data.Color = color;
            return g;
        }

        static FrameworkElement LoadHome(MainVM vm)
        {
            var el = (FrameworkElement)Xaml.Load("Home.xaml");
            el.DataContext = vm;
            return el;
        }

        static void Flush()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        }

        static void Snap(FrameworkElement el, double w, double h, Brush bg, string file)
        {
            var host = new Border { Background = bg, Child = el };
            if (w > 0) { host.Width = w; host.Height = h; }
            var avail = new Size(w > 0 ? w : double.PositiveInfinity, h > 0 ? h : double.PositiveInfinity);
            host.Measure(avail);
            host.Arrange(new Rect(w > 0 ? new Size(w, h) : host.DesiredSize));
            Flush();
            host.Measure(avail);
            var size = w > 0 ? new Size(w, h) : host.DesiredSize;
            host.Arrange(new Rect(size));
            host.UpdateLayout();
            Flush();
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 1.5), (int)Math.Ceiling(size.Height * 1.5), 144, 144, PixelFormats.Pbgra32);
            rtb.Render(host);
            SavePng(rtb, file);
            host.Child = null;
        }
    }
}
