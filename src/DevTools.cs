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
    //   --dev-dump-icons <dir> [size]     save the icon of every app WinGroups can list (read-only)
    //   --dev-selftest <report.txt>       round-trip groups in a temp folder
    //   --dev-render <dir>                render each screen and widget size to PNG (light and dark)
    static class DevTools
    {
        public static int Run(List<string> args)
        {
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
                }
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "wingroups-dev-error.txt"), ex.ToString()); } catch { }
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

            string dir = Path.Combine(Path.GetTempPath(), "wingroups-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            foreach (var g in groups) foreach (var a in g.Data.Apps) { AppIcons.GetNow(a, 64); AppIcons.GetNow(a, 96); }

            foreach (bool dark in new[] { true, false })
            {
                string tag = dark ? "dark" : "light";
                app.InitPreview(groups, catalog, dark);
                var vm = app.Main;
                var mica = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x21, 0x27) : Color.FromRgb(0xF1, 0xF3, 0xF7));

                vm.Page = "home";
                Snap(LoadHome(vm), 1120, 760, mica, Path.Combine(dir, tag + "-home.png"));

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
                Snap(LoadHome(vm), 1120, 980, mica, Path.Combine(dir, tag + "-settings.png"));

                var acrylic = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2C, 0x33) : Color.FromRgb(0xF7, 0xF8, 0xFA));
                var popup = (FrameworkElement)Xaml.Load("Popup.xaml");
                popup.DataContext = new PopupVM(app, groups[2]);
                Snap(popup, 0, 0, acrylic, Path.Combine(dir, tag + "-popup.png"));

                // All widget sizes side by side on a wallpaper-like background
                var wall = new LinearGradientBrush(dark ? Color.FromRgb(0x10, 0x1C, 0x3E) : Color.FromRgb(0x9F, 0xB9, 0xE6),
                                                   dark ? Color.FromRgb(0x2B, 0x4A, 0x92) : Color.FromRgb(0xD6, 0xE2, 0xF6), 30);
                // Widgets laid out on the real wallpaper (top-left of the main screen), each showing its frosted piece.
                var samples = new[] { groups[1], groups[0], groups[2], Colored(make("Tools", 1, 3, new[] { "Terminal", "Paint", "Snipping", "Photos" }), "#0E5E6F"), Colored(make("Paper", 2, 2, new[] { "Notepad", "Paint", "Photos" }), "#ECEAE4"), Colored(make("Wide", 3, 3, new[] { "Steam", "Discord", "Edge", "Calculator", "Notepad", "Paint", "Photos", "Terminal", "Settings", "Store", "Snipping" }), "accent"), Colored(make("Match", 2, 2, new[] { "Steam", "Discord", "Edge" }), "wallpaper") };
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
