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
            say("Desktop grid: " + DesktopGrid.CellW + " x " + DesktopGrid.CellH + " (1x1 widget) to "
                + DesktopGrid.WidthFor(3) + " x " + DesktopGrid.HeightFor(3) + " (3x3)");

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
                var g = store.Create("Test group", apps, 2, 3);
                say("Created: \"" + g.Name + "\" " + g.Data.Cols + "x" + g.Data.Rows + " with " + string.Join(", ", g.Data.Apps.Select(a => a.Name + " [" + a.Kind + "]")));
                ok &= g.Data.Cols == 2 && g.Data.Rows == 3 && g.Data.Apps.Count == apps.Count;

                store.Update(g, "Renamed", apps.Take(3).ToList(), 9, 0);
                var back = store.Find(g.Path);
                bool clamped = back.Data.Cols == 3 && back.Data.Rows == 1 && back.Name == "Renamed" && back.Data.Apps.Count == 3;
                say("Update + size cap (asked 9x0, got " + back.Data.Cols + "x" + back.Data.Rows + "): " + (clamped ? "OK" : "WRONG"));
                ok &= clamped;

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
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20) };
                foreach (var g in new[] { groups[1], groups[0], groups[2], make("Tools", 1, 3, new[] { "Terminal", "Paint", "Snipping", "Photos" }), make("Wide", 3, 3, new[] { "Steam", "Discord", "Edge", "Calculator", "Notepad", "Paint", "Photos", "Terminal", "Settings", "Store", "Snipping" }) })
                {
                    var w = (FrameworkElement)Xaml.Load("Widget.xaml");
                    w.DataContext = new WidgetVM(app, g);
                    w.VerticalAlignment = VerticalAlignment.Top;
                    w.Margin = new Thickness(0, 0, 24, 0);
                    row.Children.Add(w);
                }
                Snap(row, 0, 0, wall, Path.Combine(dir, tag + "-widgets.png"));
            }
            return 0;
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
