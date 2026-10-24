using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinGroups
{
    // Reads the Windows light/dark setting and accent palette and turns them into the app's brushes.
    static class Theme
    {
        public static bool SystemDark
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                        return v is int && (int)v == 0;
                    }
                }
                catch { return false; }
            }
        }

        // A custom accent (#RRGGBB) expanded into the same seven shades Windows uses; "" uses the Windows accent.
        public static Color[] AccentPalette(string custom)
        {
            Color c;
            if (!WidgetColors.TryParseHex(custom, out c)) return AccentPalette();
            double h, s, l;
            ToHsl(c, out h, out s, out l);
            return new[]
            {
                FromHsl(h, s, l + (1 - l) * 0.75), FromHsl(h, s, l + (1 - l) * 0.5), FromHsl(h, s, l + (1 - l) * 0.25),
                c,
                FromHsl(h, s, l * 0.82), FromHsl(h, s, l * 0.6), FromHsl(h, s, l * 0.38)
            };
        }

        internal static void ToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            h = s = 0;
            if (max == min) return;
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }

        internal static Color FromHsl(double h, double s, double l)
        {
            l = Math.Max(0, Math.Min(1, l));
            Func<double, double, double, double> hue = (p, q, t) =>
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            };
            double r, g, b;
            if (s == 0) r = g = b = l;
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
                r = hue(p, q, h + 1.0 / 3);
                g = hue(p, q, h);
                b = hue(p, q, h - 1.0 / 3);
            }
            return Color.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
        }

        // Windows' own accent shades: 0 Light3, 1 Light2, 2 Light1, 3 Base, 4 Dark1, 5 Dark2, 6 Dark3.
        public static Color[] AccentPalette()
        {
            var result = new Color[7];
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var b = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (b != null && b.Length >= 28)
                    {
                        for (int i = 0; i < 7; i++) result[i] = Color.FromRgb(b[i * 4], b[i * 4 + 1], b[i * 4 + 2]);
                        return result;
                    }
                }
            }
            catch { }
            // Windows default blue
            string[] hex = { "#99EBFF", "#4CC2FF", "#0091F8", "#0078D4", "#0067C0", "#003E92", "#001A68" };
            for (int i = 0; i < 7; i++) result[i] = (Color)ColorConverter.ConvertFromString(hex[i]);
            return result;
        }

        public static string Signature(string customAccent)
        {
            var p = AccentPalette(customAccent);
            return (SystemDark ? "d" : "l") + p[3].ToString();
        }

        static Color C(uint argb)
        {
            return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }

        static void Set(ResourceDictionary res, string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            res[key] = b;
        }

        public static void Apply(ResourceDictionary res, bool dark, string customAccent)
        {
            var p = AccentPalette(customAccent);
            Color accent = dark ? p[1] : p[4];
            Color accentText = dark ? p[0] : p[5];

            Set(res, "AccentFill", accent);
            Set(res, "AccentHoverFill", Color.FromArgb(0xE6, accent.R, accent.G, accent.B));
            Set(res, "AccentText", accentText);
            Set(res, "AccentTint", Color.FromArgb(0x26, accent.R, accent.G, accent.B));
            Set(res, "OnAccent", dark ? Colors.Black : Colors.White);
            res["TitleFill"] = TitleFill.Make(dark, false); // a still copy; Nook's window has its own that moves

            if (dark)
            {
                Set(res, "TextFill", C(0xFFFFFFFF));
                Set(res, "TextFill2", C(0xC8FFFFFF));
                Set(res, "TextFill3", C(0x8CFFFFFF));
                Set(res, "CardFill", C(0x0DFFFFFF));
                Set(res, "CardHoverFill", C(0x15FFFFFF));
                Set(res, "CardStroke", C(0x12FFFFFF));
                Set(res, "SubtleFill", C(0x0FFFFFFF));
                Set(res, "SubtlePressedFill", C(0x0AFFFFFF));
                Set(res, "ControlFill", C(0x0FFFFFFF));
                Set(res, "ControlHoverFill", C(0x16FFFFFF));
                Set(res, "ControlStroke", C(0x14FFFFFF));
                Set(res, "TextBoxFill", C(0x0FFFFFFF));
                Set(res, "TextBoxFocusFill", C(0xFF1E1E1E));
                Set(res, "TextBoxBottom", C(0x8CFFFFFF));
                Set(res, "Divider", C(0x12FFFFFF));
                Set(res, "Outline", C(0x45FFFFFF));
                Set(res, "TileFill", C(0x12FFFFFF));
                Set(res, "TileStroke", C(0x14FFFFFF));
                Set(res, "PaneFill", C(0xFF262832));
                Set(res, "Scrim", C(0x40000000));
                Set(res, "Smoke", C(0x73000000));
                Set(res, "DialogFill", C(0xFF2B2C33));
                Set(res, "DialogFooter", C(0xFF23242B));
                Set(res, "MenuFill", C(0xFA2C2D34));
                Set(res, "MenuStroke", C(0x1AFFFFFF));
                Set(res, "Danger", C(0xFFFF99A4));
                Set(res, "ToggleOffStroke", C(0x99FFFFFF));
                Set(res, "ToggleOffKnob", C(0xC8FFFFFF));
                Set(res, "WindowFallback", C(0xFF202020));
                Set(res, "WidgetFill", C(0xC81E2029));
                Set(res, "WidgetTint", C(0x8C1A1C24));
                Set(res, "WidgetTileTint", C(0xA62A2C36));
                Set(res, "WidgetStroke", C(0x24FFFFFF));
                Set(res, "WidgetTileFill", C(0xD92A2C36));
                Set(res, "WidgetTileStroke", C(0x38FFFFFF));
            }
            else
            {
                Set(res, "TextFill", C(0xE4000000));
                Set(res, "TextFill2", C(0x9E000000));
                Set(res, "TextFill3", C(0x72000000));
                Set(res, "CardFill", C(0xB3FFFFFF));
                Set(res, "CardHoverFill", C(0xE6FFFFFF));
                Set(res, "CardStroke", C(0x14000000));
                Set(res, "SubtleFill", C(0x0B000000));
                Set(res, "SubtlePressedFill", C(0x06000000));
                Set(res, "ControlFill", C(0xC8FFFFFF));
                Set(res, "ControlHoverFill", C(0x99F9F9F9));
                Set(res, "ControlStroke", C(0x1A000000));
                Set(res, "TextBoxFill", C(0xC8FFFFFF));
                Set(res, "TextBoxFocusFill", C(0xFFFFFFFF));
                Set(res, "TextBoxBottom", C(0x72000000));
                Set(res, "Divider", C(0x14000000));
                Set(res, "Outline", C(0x40000000));
                Set(res, "TileFill", C(0x0D000000));
                Set(res, "TileStroke", C(0x12000000));
                Set(res, "PaneFill", C(0xFFF6F7FA));
                Set(res, "Scrim", C(0x1F000000));
                Set(res, "Smoke", C(0x4D000000));
                Set(res, "DialogFill", C(0xFFFFFFFF));
                Set(res, "DialogFooter", C(0xFFF3F4F7));
                Set(res, "MenuFill", C(0xFAFAFAFC));
                Set(res, "MenuStroke", C(0x1A000000));
                Set(res, "Danger", C(0xFFC42B1C));
                Set(res, "ToggleOffStroke", C(0x99000000));
                Set(res, "ToggleOffKnob", C(0x9E000000));
                Set(res, "WindowFallback", C(0xFFF3F3F3));
                Set(res, "WidgetFill", C(0xD6F7F8FA));
                Set(res, "WidgetTint", C(0xA6F7F8FA));
                Set(res, "WidgetTileTint", C(0xBFFAFAFC));
                Set(res, "WidgetStroke", C(0x1F000000));
                Set(res, "WidgetTileFill", C(0xE6FAFAFC));
                Set(res, "WidgetTileStroke", C(0x2E000000));
            }
        }
    }

    // The color of page titles: soft patches of the logo's blues that drift slowly around each title,
    // instead of a gradient sweeping from one side to the other. It only moves while Nook's window is open.
    // (WPF freezes brushes in app-wide resources and ones it finds through a style, and a frozen brush can't move,
    // so the resource is a still copy and Nook's window sets the moving one on each title itself.)
    static class TitleFill
    {
        static readonly System.Collections.Generic.List<System.Windows.Media.Animation.AnimationClock> clocks =
            new System.Collections.Generic.List<System.Windows.Media.Animation.AnimationClock>();
        static readonly System.Collections.Generic.List<Tuple<EllipseGeometry, System.Windows.Media.Animation.PointAnimation>> drifts =
            new System.Collections.Generic.List<Tuple<EllipseGeometry, System.Windows.Media.Animation.PointAnimation>>();
        static bool running;

        static Color Hex(string s) { return (Color)ColorConverter.ConvertFromString(s); }

        public static Brush Make(bool dark, bool moving)
        {
            if (moving)
            {
                foreach (var c in clocks) c.Controller.Stop();
                clocks.Clear();
                drifts.Clear();
            }
            // Brighter blues on dark, deeper ones on light, so titles stay easy to read on either.
            var blues = dark ? new[] { "#5A9EF8", "#C2DEFF", "#2F7BE6", "#8CC0FF", "#3D8BF2" }
                             : new[] { "#1F5FD0", "#4A95F5", "#0F3A92", "#2A74E4", "#1A4FB8" };
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Hex(blues[0])), null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            // Where each patch starts and where it drifts to (fractions of the title's box).
            var from = new[] { new Point(0.12, 0.25), new Point(0.62, 0.85), new Point(0.9, 0.2), new Point(0.38, 0.6) };
            var to = new[] { new Point(0.45, 0.8), new Point(0.25, 0.15), new Point(0.6, 0.7), new Point(0.85, 0.9) };
            var seconds = new[] { 7.0, 9.5, 6.0, 8.5 };
            for (int i = 0; i < 4; i++)
            {
                var c = Hex(blues[i + 1]);
                var fill = new RadialGradientBrush();
                fill.GradientStops.Add(new GradientStop(c, 0));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(150, c.R, c.G, c.B), 0.45));
                fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                var spot = new EllipseGeometry(from[i], 0.34, 0.95);
                group.Children.Add(new GeometryDrawing(fill, null, spot));
                var drift = new System.Windows.Media.Animation.PointAnimation(from[i], to[i], TimeSpan.FromSeconds(seconds[i]))
                {
                    AutoReverse = true,
                    RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                    EasingFunction = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
                };
                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(drift, 30);
                if (moving) drifts.Add(Tuple.Create(spot, drift));
            }
            var brush = new DrawingBrush(group) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 1, 1) };
            if (!moving) { brush.Freeze(); return brush; }
            if (running) Start();
            return brush;
        }

        // If WPF has frozen the brush somewhere along the way, the titles just stay still: never worth a crash.
        static void Start()
        {
            foreach (var d in drifts)
            {
                if (d.Item1.IsFrozen) continue;
                try
                {
                    var clock = d.Item2.CreateClock();
                    d.Item1.ApplyAnimationClock(EllipseGeometry.CenterProperty, clock);
                    clocks.Add(clock);
                }
                catch (InvalidOperationException) { }
            }
        }

        internal static int Moving { get { return clocks.Count; } } // for --dev-home-live

        // Nook's window opened, closed or was minimized: nothing moves when no one can see it.
        public static void Run(bool on)
        {
            if (running == on) return;
            running = on;
            if (on && clocks.Count == 0) Start();
            else foreach (var c in clocks) { if (on) c.Controller.Resume(); else c.Controller.Pause(); }
        }
    }
}
