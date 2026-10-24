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
                        return DragTest(args[1], args.Count > 2 && args[2] == "group");
                    case "--dev-workspace":
                        return WorkspaceTest(args[1]);
                    case "--dev-dummy":
                        return DummyWindow(args.Count > 1 ? args[1] : "");
                    case "--dev-media":
                        return MediaReport(args[1]);
                    case "--dev-island-live":
                        return IslandLive(args[1]);
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
            foreach (var g in groups) foreach (var a in g.Data.Apps) { AppIcons.GetNow(a, 64); AppIcons.GetNow(a, 96); }

            foreach (bool dark in new[] { true, false })
            {
                string tag = dark ? "dark" : "light";
                app.InitPreview(groups, catalog, dark);
                var vm = app.Main;
                var mica = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x21, 0x27) : Color.FromRgb(0xF1, 0xF3, 0xF7));

                vm.Page = "home";
                Snap(LoadHome(vm), 1120, 760, mica, Path.Combine(dir, tag + "-home.png"));
                vm.Page = "groups";
                Snap(LoadHome(vm), 1120, 760, mica, Path.Combine(dir, tag + "-groups.png"));

                // A still from the opening animation: fluid half drained, the top squares out, the bottom ones popping.
                var intro = LoadHome(vm);
                ((FrameworkElement)intro.FindName("Splash")).Visibility = Visibility.Visible;
                ((FrameworkElement)intro.FindName("Body")).Opacity = 0;
                ((FrameworkElement)intro.FindName("TitleBar")).Opacity = 0;
                var fluid = (Canvas)intro.FindName("Fluid");
                var backWave = HomeWindow.Wave(1120, 760, 240, 6.75, HomeWindow.FluidBackTop, HomeWindow.FluidBackBottom);
                var frontWave = HomeWindow.Wave(1120, 760, 240, 9, HomeWindow.FluidTop, HomeWindow.FluidBottom);
                ((TranslateTransform)backWave.RenderTransform).Y = 414;
                ((TranslateTransform)backWave.RenderTransform).X = -90;
                ((TranslateTransform)frontWave.RenderTransform).Y = 424;
                ((TranslateTransform)frontWave.RenderTransform).X = -30;
                fluid.Children.Add(backWave);
                fluid.Children.Add(frontWave);
                for (int i = 2; i < 4; i++)
                {
                    var sq = (FrameworkElement)intro.FindName("Sq" + i);
                    var sg = (TransformGroup)sq.RenderTransform;
                    ((ScaleTransform)sg.Children[0]).ScaleX = ((ScaleTransform)sg.Children[0]).ScaleY = 0.6;
                    ((TranslateTransform)sg.Children[1]).Y = 8;
                }
                Snap(intro, 1120, 760, mica, Path.Combine(dir, tag + "-intro.png"));

                vm.StartEdit(groups[0]);
                Snap(LoadHome(vm), 1120, 760, mica, Path.Combine(dir, tag + "-editor.png"));

                vm.Page = "settings";
                Snap(LoadHome(vm), 1120, 1900, mica, Path.Combine(dir, tag + "-settings.png"));

                vm.Page = "island";
                Snap(LoadHome(vm), 1120, 1300, mica, Path.Combine(dir, tag + "-island.png"));
                vm.Page = "workspaces";
                Snap(LoadHome(vm), 1120, 600, mica, Path.Combine(dir, tag + "-workspaces.png"));
                vm.StartEdit(workspace);
                Snap(LoadHome(vm), 1120, 900, mica, Path.Combine(dir, tag + "-workspace-editor.png"));
                vm.Page = "clock";
                Snap(LoadHome(vm), 1120, 700, mica, Path.Combine(dir, tag + "-clock.png"));

                var acrylic = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2C, 0x33) : Color.FromRgb(0xF7, 0xF8, 0xFA));
                var popup = (FrameworkElement)Xaml.Load("Popup.xaml");
                popup.DataContext = new PopupVM(app, groups[2]);
                Snap(popup, 0, 0, acrylic, Path.Combine(dir, tag + "-popup.png"));

                // All widget sizes side by side on a wallpaper-like background
                var wall = new LinearGradientBrush(dark ? Color.FromRgb(0x10, 0x1C, 0x3E) : Color.FromRgb(0x9F, 0xB9, 0xE6),
                                                   dark ? Color.FromRgb(0x2B, 0x4A, 0x92) : Color.FromRgb(0xD6, 0xE2, 0xF6), 30);
                // Widgets laid out on the real wallpaper (top-left of the main screen), each showing its frosted piece.
                var samples = new[] { groups[1], groups[0], groups[2], Colored(make("Tools", 1, 3, new[] { "Terminal", "Paint", "Snipping", "Photos" }), "#0E5E6F"), Colored(make("Paper", 2, 2, new[] { "Notepad", "Paint", "Photos" }), "#ECEAE4"), Colored(make("Wide", 3, 3, new[] { "Steam", "Discord", "Edge", "Calculator", "Notepad", "Paint", "Photos", "Terminal", "Settings", "Store", "Snipping" }), "accent"), Colored(make("Match", 2, 2, new[] { "Steam", "Discord", "Edge" }), "wallpaper"), workspace, quiet };
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
                Tuple.Create(IslandMode.System, true, false), Tuple.Create(IslandMode.Headphones, false, false)
            };
            foreach (var look in new[] { "black", "glass", "notch" })
            {
                var sheet = new StackPanel();
                foreach (var st in states)
                {
                    var vm = DemoIsland(app, st.Item2);
                    IslandWindow.Look(vm, look == "glass", 0.6, true);
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
                    for (int k = 0; k < 3; k++) { view.Dots[k].Count = 3; view.Dots[k].Index = k; view.Dots[k].InvalidateMeasure(); }
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
                Tuple.Create<Action, string>(() => island.ShowStateNow(IslandMode.Compact, false), "live-black-compact.png"),
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

        // ---- workspaces: open two test windows (this exe, as a stand-in app) and check they land in their spaces ----

        // A plain resizable window that stands in for an app.
        static int DummyWindow(string label)
        {
            var app = new Application();
            var w = new Window { Title = "Nook test window " + label, Width = 520, Height = 380, Content = new TextBlock { Text = "Test window " + label, Margin = new Thickness(20) } };
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
            foreach (var n in new[] { "A", "B" })
            {
                // A shortcut with arguments, so each test window is its own "app".
                string lnk = Path.Combine(tmp, "Test " + n + ".lnk");
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                sc.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { exe });
                sc.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.SetProperty, null, sc, new object[] { "--dev-dummy " + n });
                sc.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, sc, null);
                apps.Add(GroupApp.FromFile(lnk));
            }
            var g = new Group { Path = Path.Combine(tmp, "test.json"), Data = new GroupData { Kind = "workspace", Name = "Test", Apps = apps, Layout = "halves", Screen = "" } };
            var screen = Screens.Find("");
            var started = DateTime.UtcNow;
            new WorkspaceRun(g).Start(() =>
            {
                var finish = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                finish.Tick += (s, e) =>
                {
                    finish.Stop();
                    sb.AppendLine("Finished in " + (DateTime.UtcNow - started).TotalSeconds.ToString("0.0") + " s on " + screen.Label);
                    double ww = screen.Work.Right - screen.Work.Left;
                    var expect = new[] { new Rect(screen.Work.Left, screen.Work.Top, Math.Round(ww / 2), screen.Work.Bottom - screen.Work.Top),
                                         new Rect(screen.Work.Left + Math.Round(ww / 2), screen.Work.Top, ww - Math.Round(ww / 2), screen.Work.Bottom - screen.Work.Top) };
                    bool ok = true;
                    foreach (var w in OtherWindows.AppWindows())
                    {
                        var title = new StringBuilder(256);
                        GetWindowText(w, title, title.Capacity);
                        if (!title.ToString().StartsWith("Nook test window ")) continue;
                        int i = title.ToString().EndsWith("A") ? 0 : 1;
                        Native.RECT fr;
                        Native.DwmGetWindowAttribute(w, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out fr, 16);
                        var got = new Rect(fr.Left, fr.Top, fr.Right - fr.Left, fr.Bottom - fr.Top);
                        bool match = Math.Abs(got.X - expect[i].X) <= 2 && Math.Abs(got.Y - expect[i].Y) <= 2 && Math.Abs(got.Width - expect[i].Width) <= 2 && Math.Abs(got.Height - expect[i].Height) <= 2;
                        ok &= match;
                        sb.AppendLine("Window " + (i == 0 ? "A" : "B") + ": " + got + " expected " + expect[i] + (match ? " OK" : " WRONG"));
                        uint pid;
                        Native.GetWindowThreadProcessId(w, out pid);
                        try { System.Diagnostics.Process.GetProcessById((int)pid).Kill(); } catch { }
                    }
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
            foreach (var size in new[] { "small", "medium", "large" })
            {
                app.Settings.ClockSize = size;
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

        static int DragTest(string report, bool asGroup)
        {
            var sb = new StringBuilder();
            var app = new App();
            string tmp = Path.Combine(Path.GetTempPath(), "nook-drag-test");
            Directory.CreateDirectory(tmp);
            var main = Screens.Primary(Screens.All());
            var g = new Group
            {
                Path = Path.Combine(tmp, "drag.json"),
                Data = new GroupData { Kind = asGroup ? "" : "workspace", Name = "Drag test", Icon = WorkspaceIcons.Default, Layout = "open", X = main.Work.Left + 300, Y = main.Work.Top + 300 }
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
