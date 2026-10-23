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
    // Developer commands for building and testing without installing anything:
    //   --dev-app-icon <out.ico>          write the app's own icon
    //   --dev-dump-icons <dir> [size]     save the icon of every app WinGroups can list (read-only)
    //   --dev-selftest <report.txt>       round-trip a group file in a temp folder
    //   --dev-render <dir>                render each screen to PNG (light and dark)
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

            var catalog = AppCatalog.LoadBlocking();
            say("Catalog: " + catalog.Count + " apps (desktop " + catalog.Count(a => a.Section == "desktop")
                + ", installed " + catalog.Count(a => a.Section == "installed") + ", windows " + catalog.Count(a => a.Section == "windows") + ")");
            int icons = catalog.Take(40).Count(a => ShellIcons.Get(a.IconPath, 64) != null);
            say("Icons: " + icons + " of " + Math.Min(40, catalog.Count) + " extracted");
            if (catalog.Count == 0 || icons == 0) ok = false;

            string dir = Path.Combine(Path.GetTempPath(), "wingroups-selftest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                var picks = catalog.Where(a => a.Section == "desktop").Take(2).Concat(catalog.Where(a => a.Section != "desktop").Take(2)).ToList();
                var data = new GroupData();
                foreach (var p in picks) data.Apps.Add(GroupApp.FromCatalog(p));
                say("Picked: " + string.Join(", ", data.Apps.Select(a => a.Name + " [" + a.Kind + "]")));

                var imgs = data.Apps.Take(4).Select(a => (ImageSource)AppIcons.GetNow(a, 128)).ToList();
                say("Group icons resolved: " + imgs.Count(x => x != null) + " of " + imgs.Count);
                byte[] ico = IconRenderer.BuildIco(imgs, true, false);
                string path = Path.Combine(dir, "Test group" + GroupFile.Ext);
                GroupFile.Write(path, ico, data);
                say("Wrote " + path + " (" + new FileInfo(path).Length / 1024 + " KB)");

                var back = GroupFile.Read(path);
                bool same = back.Apps.Count == data.Apps.Count && back.Apps.Zip(data.Apps, (x, y) => x.Name == y.Name && x.Kind == y.Kind && x.Target == y.Target && x.LinkData == y.LinkData).All(b => b);
                say("Round-trip data: " + (same ? "OK" : "MISMATCH"));
                ok &= same;

                using (var fs = File.OpenRead(path))
                {
                    var dec = new IconBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    say("Icon frames: " + string.Join(", ", dec.Frames.Select(f => f.PixelWidth.ToString())));
                    ok &= dec.Frames.Count == 11;
                }

                foreach (var a in back.Apps.Where(a => a.Kind == "link"))
                {
                    string copy = LinkCache.Materialize(a);
                    bool exists = File.Exists(copy) && File.Exists(a.Target);
                    say("Link copy for " + a.Name + ": " + (exists ? "OK (original untouched)" : "FAILED"));
                    ok &= exists;
                }

                var g = GroupStore.Load(path);
                say("Display name from file: \"" + g.Name + "\"");

                SavePng(IconRenderer.Render(imgs, null, 256, true, false), Path.Combine(Path.GetDirectoryName(report), "group-icon-dark.png"));
                SavePng(IconRenderer.Render(imgs, null, 256, false, false), Path.Combine(Path.GetDirectoryName(report), "group-icon-light.png"));
                SavePng(IconRenderer.Render(imgs, null, 256, false, true), Path.Combine(Path.GetDirectoryName(report), "group-icon-folder.png"));
                SavePng(IconRenderer.Render(imgs, null, 48, true, false), Path.Combine(Path.GetDirectoryName(report), "group-icon-48.png"));
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
            var groups = new List<Group>
            {
                new Group { Name = "Games", Path = "x:\\Games.wingroup", Data = new GroupData { Apps = pick(new[] { "Steam", "Squad", "Buckshot", "CurseForge" }) } },
                new Group { Name = "Gear", Path = "x:\\Gear.wingroup", Data = new GroupData { Apps = pick(new[] { "G HUB", "Stream Deck", "Glorious" }) } },
                new Group { Name = "Everyday", Path = "x:\\Everyday.wingroup", Data = new GroupData { Apps = pick(new[] { "Discord", "Edge", "Windhawk", "Claude" }) } },
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
                Snap(LoadHome(vm), 1120, 900, mica, Path.Combine(dir, tag + "-settings.png"));

                vm.Page = "home";
                var withPane = LoadHome(vm);
                ((TranslateTransform)((Border)withPane.FindName("Pane")).RenderTransform).X = 0;
                ((Border)withPane.FindName("Scrim")).Opacity = 1;
                Snap(withPane, 1120, 760, mica, Path.Combine(dir, tag + "-menu.png"));

                vm.AskRemove(new GroupVM(vm, groups[1], false));
                var withDialog = LoadHome(vm);
                ((Grid)withDialog.FindName("DialogLayer")).Visibility = Visibility.Visible;
                Snap(withDialog, 1120, 760, mica, Path.Combine(dir, tag + "-confirm.png"));
                vm.DialogOpen = false;

                var acrylic = new SolidColorBrush(dark ? Color.FromRgb(0x2B, 0x2C, 0x33) : Color.FromRgb(0xF7, 0xF8, 0xFA));
                var popup = (FrameworkElement)Xaml.Load("Popup.xaml");
                popup.DataContext = new PopupVM(app, groups[0]);
                Snap(popup, 0, 0, acrylic, Path.Combine(dir, tag + "-popup.png"));
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
