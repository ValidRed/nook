using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace WinGroups
{
    // Frosted glass for widgets: the wallpaper is blurred once per screen, and each widget shows the piece
    // that's behind it. Windows only blurs behind active windows, and widgets never take focus, so this
    // is how they stay frosted. Reads the wallpaper; never changes it.
    static class Wallpaper
    {
        class Screen
        {
            public Rect Bounds;          // screen pixels
            public BitmapSource Image;   // decoded at a modest size; blur doesn't need detail
            public BitmapSource Blurred;
            public BitmapSource Thumb;   // tiny copy for averaging colors
            public Color Background;
        }

        const int Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5;
        static List<Screen> screens = new List<Screen>();
        static int position = Fill;
        static double blurred = -1;
        public static string Signature { get; private set; }

        public static void Load(int blurStrength)
        {
            var list = new List<Screen>();
            var sig = new System.Text.StringBuilder();
            try
            {
                var dw = (Native.IDesktopWallpaper)new Native.DesktopWallpaperClass();
                try
                {
                    position = dw.GetPosition();
                    uint bg = dw.GetBackgroundColor(); // 0x00BBGGRR
                    var background = Color.FromRgb((byte)bg, (byte)(bg >> 8), (byte)(bg >> 16));
                    uint n = dw.GetMonitorDevicePathCount();
                    for (uint i = 0; i < n; i++)
                    {
                        try
                        {
                            string id = dw.GetMonitorDevicePathAt(i);
                            var r = dw.GetMonitorRECT(id);
                            if (r.Right <= r.Left || r.Bottom <= r.Top) continue;
                            string path = dw.GetWallpaper(id) ?? "";
                            sig.Append(path).Append('|');
                            if (File.Exists(path)) sig.Append(File.GetLastWriteTimeUtc(path).Ticks);
                            sig.Append(';');
                            list.Add(new Screen
                            {
                                Bounds = new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                                Image = Decode(path, 1280),
                                Thumb = Decode(path, 64),
                                Background = background
                            });
                        }
                        catch { }
                    }
                    sig.Append(position);
                }
                finally { Marshal.ReleaseComObject(dw); }
            }
            catch { }
            screens = list;
            Signature = sig.ToString();
            blurred = -1;
            SetBlur(blurStrength);
        }

        // Cheap check for "did the wallpaper change?": the same signature Load builds, without decoding images.
        public static string Probe()
        {
            var sig = new System.Text.StringBuilder();
            try
            {
                var dw = (Native.IDesktopWallpaper)new Native.DesktopWallpaperClass();
                try
                {
                    int pos = dw.GetPosition();
                    uint n = dw.GetMonitorDevicePathCount();
                    for (uint i = 0; i < n; i++)
                    {
                        try
                        {
                            string id = dw.GetMonitorDevicePathAt(i);
                            var r = dw.GetMonitorRECT(id);
                            if (r.Right <= r.Left || r.Bottom <= r.Top) continue;
                            string path = dw.GetWallpaper(id) ?? "";
                            sig.Append(path).Append('|');
                            if (File.Exists(path)) sig.Append(File.GetLastWriteTimeUtc(path).Ticks);
                            sig.Append(';');
                        }
                        catch { }
                    }
                    sig.Append(pos);
                }
                finally { Marshal.ReleaseComObject(dw); }
            }
            catch { }
            return sig.ToString();
        }

        static BitmapSource Decode(string path, int width)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = width;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // Blur strength 0-100 maps to up to 48 screen pixels of blur.
        public static void SetBlur(int strength)
        {
            double radius = Math.Max(0, Math.Min(100, strength)) / 100.0 * 48;
            if (radius == blurred) return;
            blurred = radius;
            foreach (var s in screens)
            {
                if (s.Image == null) { s.Blurred = null; continue; }
                double scale = DisplayScale(s);
                s.Blurred = radius < 0.5 ? s.Image : Blur(s.Image, radius / scale);
            }
        }

        // A sharp copy sits under the blurred one so the image edges don't fade to transparent.
        static BitmapSource Blur(BitmapSource img, double radius)
        {
            int w = img.PixelWidth, h = img.PixelHeight;
            var grid = new Grid { Width = w, Height = h };
            grid.Children.Add(new Image { Source = img, Stretch = System.Windows.Media.Stretch.Fill });
            grid.Children.Add(new Image
            {
                Source = img,
                Stretch = System.Windows.Media.Stretch.Fill,
                Effect = new BlurEffect { Radius = radius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance }
            });
            grid.Measure(new Size(w, h));
            grid.Arrange(new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(grid);
            rtb.Freeze();
            return rtb;
        }

        static Rect Area(Screen s)
        {
            if (position != Span || screens.Count < 2) return s.Bounds;
            var all = screens[0].Bounds;
            foreach (var o in screens) all.Union(o.Bounds);
            return all;
        }

        // Screen pixels per image pixel, as Windows draws it.
        static double DisplayScale(Screen s)
        {
            var a = Area(s);
            double iw = s.Image.PixelWidth, ih = s.Image.PixelHeight;
            if (position == Fit) return Math.Min(a.Width / iw, a.Height / ih);
            if (position == Stretch) return a.Width / iw;
            return Math.Max(a.Width / iw, a.Height / ih); // fill, span, and an approximation for center/tile
        }

        static Screen At(Rect r)
        {
            var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            Screen best = null;
            double bestDist = double.MaxValue;
            foreach (var s in screens)
            {
                if (s.Bounds.Contains(c)) return s;
                double dx = Math.Max(0, Math.Max(s.Bounds.Left - c.X, c.X - s.Bounds.Right));
                double dy = Math.Max(0, Math.Max(s.Bounds.Top - c.Y, c.Y - s.Bounds.Bottom));
                double d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        // Where a screen rectangle falls on the wallpaper image, as fractions of the image (0-1).
        static Rect Viewbox(Screen s, Rect r)
        {
            var a = Area(s);
            double iw = s.Image.PixelWidth, ih = s.Image.PixelHeight;
            double sx, sy;
            if (position == Stretch) { sx = a.Width / iw; sy = a.Height / ih; }
            else sx = sy = DisplayScale(s);
            double ox = (a.Width - iw * sx) / 2, oy = (a.Height - ih * sy) / 2;
            double x = (r.X - a.X - ox) / sx, y = (r.Y - a.Y - oy) / sy;
            return new Rect(x / iw, y / ih, r.Width / sx / iw, r.Height / sy / ih);
        }

        public static bool Loaded { get { return screens.Count > 0; } }

        // The frosted piece of wallpaper behind a screen rectangle (screen pixels). sharp = unblurred (for previews).
        public static Brush GlassFor(Rect screenRect, bool sharp = false)
        {
            var s = At(screenRect);
            if (s == null) return null;
            var img = sharp ? s.Image : s.Blurred;
            if (img == null)
            {
                var solid = new SolidColorBrush(s.Background);
                solid.Freeze();
                return solid;
            }
            var b = new ImageBrush(img)
            {
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Viewbox = Viewbox(s, screenRect),
                Stretch = System.Windows.Media.Stretch.Fill
            };
            b.Freeze();
            return b;
        }

        // Average wallpaper color behind a rectangle, for the "match wallpaper" widget color.
        public static Color? AverageColor(Rect screenRect)
        {
            var s = At(screenRect);
            if (s == null) return null;
            if (s.Thumb == null) return s.Background;
            try
            {
                var src = new FormatConvertedBitmap(s.Thumb, PixelFormats.Bgra32, null, 0);
                int w = src.PixelWidth, h = src.PixelHeight;
                var px = new byte[w * h * 4];
                src.CopyPixels(px, w * 4, 0);
                var v = Viewbox(s, screenRect);
                int x0 = Clamp((int)(v.X * w), 0, w - 1), x1 = Clamp((int)Math.Ceiling(v.Right * w), x0 + 1, w);
                int y0 = Clamp((int)(v.Y * h), 0, h - 1), y1 = Clamp((int)Math.Ceiling(v.Bottom * h), y0 + 1, h);
                double r = 0, g = 0, b = 0;
                int n = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = (y * w + x) * 4;
                        b += px[i]; g += px[i + 1]; r += px[i + 2]; n++;
                    }
                if (n == 0) return null;
                return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
            catch { return null; }
        }

        static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }
    }
}
