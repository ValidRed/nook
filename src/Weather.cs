using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinGroups
{
    // The weather right now (and today's high and low) for the place you picked.
    class WeatherReport
    {
        public double Temp, High, Low;
        public int Code;   // WMO weather code
        public bool Day;
        public DateTime At;
    }

    // A place found by the search in settings.
    class WeatherPlace
    {
        public string Name, Region, Country;
        public double Lat, Lon;
        // How it's shown once picked: "Seattle, Washington" (or the country when there's no region).
        public string Label { get { return Name + (string.IsNullOrEmpty(Region) ? (string.IsNullOrEmpty(Country) ? "" : ", " + Country) : ", " + Region); } }
        public string Detail { get { return string.Join(", ", new[] { Region, Country }.Where(x => !string.IsNullOrEmpty(x))); } }
    }

    // Live weather from Open-Meteo (open-meteo.com): free, no account, updated every 15 minutes. Only the place you
    // picked is sent, as coordinates, and only while the weather is showing somewhere.
    class WeatherService
    {
        readonly App app;
        readonly DispatcherTimer timer;
        string key;
        bool busy;

        public WeatherReport Now { get; private set; }
        public string Problem { get; private set; } // why there's no reading, if there's a reason
        public event Action Changed;

        public WeatherService(App app)
        {
            this.app = app;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            timer.Tick += (s, e) => Refresh();
        }

        // A reading from the last three hours; older than that, it's left off rather than shown wrong.
        public bool Fresh { get { return Now != null && (DateTime.Now - Now.At).TotalHours < 3; } }

        bool Wanted
        {
            get
            {
                var s = app.Settings;
                return !string.IsNullOrEmpty(s.WeatherPlace) && ((s.IslandOn && s.IslandWeather) || (s.ClockOn && s.ClockWeather));
            }
        }

        // Starts, stops or re-aims the updates after a setting changes.
        public void Sync()
        {
            var s = app.Settings;
            string k = s.WeatherPlace + "|" + s.WeatherLat.ToString(CultureInfo.InvariantCulture) + "|" + s.WeatherLon.ToString(CultureInfo.InvariantCulture) + "|" + s.WeatherUnit;
            if (!Wanted) { timer.Stop(); return; }
            if (k != key)
            {
                key = k;
                Now = null;
                Problem = null;
                Raise();
                Refresh();
            }
            else if (!Fresh) Refresh();
            timer.Start();
        }

        void Raise() { var h = Changed; if (h != null) h(); }

        // --dev-render: a reading without asking anyone.
        internal void Pretend(WeatherReport r) { Now = r; Problem = null; Raise(); }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 };
            wc.Headers[HttpRequestHeader.UserAgent] = "Nook/" + Program.Version;
            return wc;
        }

        static string Num(double v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }
        static double D(object o) { return o == null ? 0 : Convert.ToDouble(o, CultureInfo.InvariantCulture); }

        public async void Refresh()
        {
            if (busy || string.IsNullOrEmpty(app.Settings.WeatherPlace)) return;
            busy = true;
            var s = app.Settings;
            string asked = key;
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + Num(s.WeatherLat) + "&longitude=" + Num(s.WeatherLon) +
                         "&current=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min" +
                         "&temperature_unit=" + (s.WeatherUnit == "f" ? "fahrenheit" : "celsius") + "&timezone=auto&forecast_days=1";
            try
            {
                string json;
                using (var wc = Client()) json = await wc.DownloadStringTaskAsync(url);
                if (asked != key) return; // the place changed while this was on its way
                var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                var cur = (Dictionary<string, object>)root["current"];
                var daily = root.ContainsKey("daily") ? (Dictionary<string, object>)root["daily"] : null;
                Func<string, double?> first = name =>
                {
                    if (daily == null || !daily.ContainsKey(name)) return null;
                    var list = daily[name] as System.Collections.ArrayList;
                    return list != null && list.Count > 0 && list[0] != null ? D(list[0]) : (double?)null;
                };
                double t = D(cur["temperature_2m"]);
                Now = new WeatherReport
                {
                    Temp = t,
                    Code = (int)D(cur["weather_code"]),
                    Day = D(cur["is_day"]) >= 1,
                    High = first("temperature_2m_max") ?? t,
                    Low = first("temperature_2m_min") ?? t,
                    At = DateTime.Now
                };
                Problem = null;
                timer.Interval = TimeSpan.FromMinutes(15);
            }
            catch
            {
                // No connection (or Open-Meteo is busy): try again soon, and keep the last reading while it's recent.
                Problem = "Couldn't reach Open-Meteo just now";
                if (!Fresh) Now = null;
                timer.Interval = TimeSpan.FromMinutes(2);
            }
            finally
            {
                busy = false;
                Raise();
            }
        }

        // Places matching what you typed, for the search in settings.
        public static async Task<List<WeatherPlace>> Search(string text)
        {
            var found = new List<WeatherPlace>();
            text = (text ?? "").Trim();
            if (text.Length < 2) return found;
            string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=6&format=json&language=" + Uri.EscapeDataString(lang) + "&name=" + Uri.EscapeDataString(text);
            string json;
            using (var wc = Client()) json = await wc.DownloadStringTaskAsync(url);
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            object results;
            if (root == null || !root.TryGetValue("results", out results) || !(results is System.Collections.ArrayList)) return found;
            foreach (Dictionary<string, object> r in (System.Collections.ArrayList)results)
            {
                Func<string, string> str = k => r.ContainsKey(k) && r[k] != null ? r[k].ToString() : "";
                if (!r.ContainsKey("latitude") || !r.ContainsKey("longitude")) continue;
                found.Add(new WeatherPlace { Name = str("name"), Region = str("admin1"), Country = str("country"), Lat = D(r["latitude"]), Lon = D(r["longitude"]) });
            }
            return found;
        }

        // ---- words and icons for the WMO weather codes Open-Meteo uses ----

        public static string KindOf(int code, bool day)
        {
            if (code <= 1) return day ? "sun" : "moon";
            if (code == 2) return day ? "partly" : "partlynight";
            if (code == 3) return "cloud";
            if (code == 45 || code == 48) return "fog";
            if (code >= 51 && code <= 57) return "drizzle";
            if ((code >= 61 && code <= 67) || (code >= 80 && code <= 82)) return "rain";
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "snow";
            if (code >= 95) return "storm";
            return "cloud";
        }

        public static string Words(int code, bool day)
        {
            switch (code)
            {
                case 0: return day ? "Sunny" : "Clear";
                case 1: return day ? "Mostly sunny" : "Mostly clear";
                case 2: return "Partly cloudy";
                case 3: return "Cloudy";
                case 45: case 48: return "Fog";
                case 51: case 53: case 55: return "Drizzle";
                case 56: case 57: return "Freezing drizzle";
                case 61: return "Light rain";
                case 63: return "Rain";
                case 65: return "Heavy rain";
                case 66: case 67: return "Freezing rain";
                case 71: return "Light snow";
                case 73: return "Snow";
                case 75: return "Heavy snow";
                case 77: return "Snow grains";
                case 80: case 81: return "Showers";
                case 82: return "Heavy showers";
                case 85: case 86: return "Snow showers";
                case 95: return "Thunderstorms";
                case 96: case 99: return "Thunderstorms and hail";
            }
            return "Cloudy";
        }

        public static string Degrees(double t) { return Math.Round(t).ToString("0", CultureInfo.CurrentCulture) + "°"; }
    }

    // A small line-drawn weather icon, the same weight as Windows' own icons, that moves gently: the sun's rays turn,
    // clouds drift, rain and snow fall, lightning flickers, fog slides. Drawn on a 24-unit square and scaled to fit.
    // Kind: sun, moon, partly, partlynight, cloud, fog, drizzle, rain, snow, storm.
    public class WeatherIcon : Decorator
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register("Kind", typeof(string), typeof(WeatherIcon),
            new FrameworkPropertyMetadata("", (d, e) => ((WeatherIcon)d).Build()));
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(WeatherIcon),
            new FrameworkPropertyMetadata(Brushes.White, (d, e) => ((WeatherIcon)d).Build()));

        public string Kind { get { return (string)GetValue(KindProperty); } set { SetValue(KindProperty, value); } }
        public Brush Stroke { get { return (Brush)GetValue(StrokeProperty); } set { SetValue(StrokeProperty, value); } }

        // Set false to draw it still (--dev-render, and when Windows' animations are off).
        public bool Animated { get; set; }

        static readonly Geometry Cloud = Frozen(Geometry.Parse("M17.5,19 L9,19 A7,7 0 1 1 15.71,10 L17.5,10 A4.5,4.5 0 1 1 17.5,19 Z"));
        static readonly Geometry Moon = Frozen(Geometry.Parse("M12,3 A6.4,6.4 0 0 0 21,12 A9,9 0 1 1 12,3 Z"));
        // Clouds higher up, to make room for what falls out of them; and smaller and lower, in front of the sun.
        static readonly Matrix High = new Matrix(0.85, 0, 0, 0.85, 1.8, -1.7);
        static readonly Matrix Front = new Matrix(0.7, 0, 0, 0.7, 5.6, 6.7);

        readonly Canvas canvas = new Canvas { Width = 24, Height = 24 };
        readonly List<Action> starts = new List<Action>();
        readonly List<Action> stops = new List<Action>();
        bool running;

        public WeatherIcon()
        {
            Animated = true;
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = canvas };
            Loaded += (s, e) => Start();
            Unloaded += (s, e) => Stop();
            IsVisibleChanged += (s, e) => { if (IsVisible) Start(); else Stop(); };
        }

        static Geometry Frozen(Geometry g) { g.Freeze(); return g; }
        static Geometry Moved(Geometry g, Matrix m) { var c = g.Clone(); c.Transform = new MatrixTransform(m); c.Freeze(); return c; }

        Path Line(Geometry g)
        {
            return new Path
            {
                Data = g, Stroke = Stroke, StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
            };
        }

        Path Add(Panel to, Geometry g) { var p = Line(g); to.Children.Add(p); return p; }

        Canvas Layer() { var c = new Canvas { Width = 24, Height = 24 }; canvas.Children.Add(c); return c; }

        // Everything outside a cloud, so what's behind it (the sun, the moon) only shows around it.
        static Geometry Behind(Geometry cloud)
        {
            var widened = cloud.GetWidenedPathGeometry(new Pen(Brushes.Black, 3.6));
            var cover = new CombinedGeometry(GeometryCombineMode.Union, cloud, widened);
            var g = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(-4, -4, 32, 32)), cover);
            g.Freeze();
            return g;
        }

        // ---- motion ----

        static Timeline Slow(Timeline t) { Timeline.SetDesiredFrameRate(t, 30); return t; }

        void Animate(IAnimatable target, DependencyProperty prop, AnimationTimeline a)
        {
            Slow(a);
            starts.Add(() => target.BeginAnimation(prop, a));
            stops.Add(() => target.BeginAnimation(prop, null));
        }

        static DoubleAnimation Sway(double from, double to, double seconds, double delay = 0)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
        }

        void Start()
        {
            if (running || !Animated || !IsLoaded || !IsVisible || !Anim.Enabled || !SystemParameters.ClientAreaAnimation) return;
            running = true;
            foreach (var a in starts) a();
        }

        void Stop()
        {
            if (!running) return;
            running = false;
            foreach (var a in stops) a();
        }

        // ---- drawing ----

        void Build()
        {
            Stop();
            starts.Clear();
            stops.Clear();
            canvas.Children.Clear();
            switch (Kind ?? "")
            {
                case "sun": Sun(Layer(), 12, 12, 4, 7, 9.5); break;
                case "moon": MoonShape(Layer(), Matrix.Identity, true); break;
                case "partly":
                    {
                        var behind = Layer();
                        behind.Clip = Behind(Moved(Cloud, Front));
                        Sun(behind, 9, 9, 3.3, 5.5, 7.4);
                        DriftingCloud(Front, 0.6);
                        break;
                    }
                case "partlynight":
                    {
                        var behind = Layer();
                        behind.Clip = Behind(Moved(Cloud, Front));
                        MoonShape(behind, new Matrix(0.6, 0, 0, 0.6, 0.4, 0.4), false);
                        DriftingCloud(Front, 0.6);
                        break;
                    }
                case "cloud": DriftingCloud(Matrix.Identity, 1.2); break;
                case "fog": Fog(); break;
                case "drizzle": DriftingCloud(High, 0.5); Falling(1.6, 1.5); break;
                case "rain": DriftingCloud(High, 0.5); Falling(1.05, 2.6); break;
                case "snow": DriftingCloud(High, 0.5); Snow(); break;
                case "storm": Storm(); break;
            }
            Start();
        }

        // A round sun whose rays turn slowly.
        void Sun(Canvas into, double cx, double cy, double r, double r1, double r2)
        {
            Add(into, new EllipseGeometry(new Point(cx, cy), r, r));
            var rays = new GeometryGroup();
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                rays.Children.Add(new LineGeometry(new Point(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1), new Point(cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2)));
            }
            var p = Add(into, rays);
            var spin = new RotateTransform(0, cx, cy);
            p.RenderTransform = spin;
            Animate(spin, RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(40)) { RepeatBehavior = RepeatBehavior.Forever });
        }

        // A crescent moon that rocks a little, with a star twinkling beside it.
        void MoonShape(Canvas into, Matrix at, bool star)
        {
            var p = Add(into, Moved(Moon, at));
            var rock = new RotateTransform(0, at.Transform(new Point(12, 12)).X, at.Transform(new Point(12, 12)).Y);
            p.RenderTransform = rock;
            Animate(rock, RotateTransform.AngleProperty, Sway(-7, 7, 4.5));
            if (!star) return;
            var s = Add(into, Geometry.Parse("M19,2.6 L19,6.4 M17.1,4.5 L20.9,4.5"));
            s.StrokeThickness = 1.4;
            var twinkle = new ScaleTransform(1, 1, 19, 4.5);
            s.RenderTransform = twinkle;
            Animate(s, OpacityProperty, Sway(1, 0.15, 1.6));
            Animate(twinkle, ScaleTransform.ScaleXProperty, Sway(1, 0.55, 1.6));
            Animate(twinkle, ScaleTransform.ScaleYProperty, Sway(1, 0.55, 1.6));
        }

        void DriftingCloud(Matrix at, double drift)
        {
            var p = Add(canvas, Moved(Cloud, at));
            var move = new TranslateTransform();
            p.RenderTransform = move;
            Animate(move, TranslateTransform.XProperty, Sway(-drift, drift, 3.6));
        }

        // Rain (long, quick, slanted) or drizzle (short and slow), falling from under the cloud and fading.
        void Falling(double seconds, double length)
        {
            double[] xs = { 8.5, 12.5, 16.5 };
            for (int i = 0; i < xs.Length; i++)
            {
                var drop = Add(canvas, new LineGeometry(new Point(xs[i] + length * 0.25, 16.6), new Point(xs[i] - length * 0.25, 16.6 + length)));
                var fall = new TranslateTransform();
                drop.RenderTransform = fall;
                double delay = seconds / xs.Length * i;
                Animate(fall, TranslateTransform.YProperty, new DoubleAnimation(-0.5, 3.2, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
                Animate(fall, TranslateTransform.XProperty, new DoubleAnimation(0.3, -0.6, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
                var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.6)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                Animate(drop, OpacityProperty, fade);
                // Still (no animation), the drops just sit under the cloud.
                stops.Add(() => drop.Opacity = 1);
            }
        }

        // Soft flakes that drift down, swaying.
        void Snow()
        {
            double[] xs = { 8.5, 12.5, 16.5 };
            for (int i = 0; i < xs.Length; i++)
            {
                var flake = new Path { Data = new EllipseGeometry(new Point(xs[i], 17.4), 1.1, 1.1), Fill = Stroke };
                canvas.Children.Add(flake);
                var move = new TranslateTransform();
                flake.RenderTransform = move;
                double delay = 0.8 * i, fallTime = 2.4;
                Animate(move, TranslateTransform.YProperty, new DoubleAnimation(-0.6, 4.2, TimeSpan.FromSeconds(fallTime)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
                Animate(move, TranslateTransform.XProperty, Sway(-0.9, 0.9, 0.9, delay));
                var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(fallTime), RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) };
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.2)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.65)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                Animate(flake, OpacityProperty, fade);
                stops.Add(() => flake.Opacity = 1);
            }
        }

        // A cloud with a bolt under it that flickers now and then.
        void Storm()
        {
            DriftingCloud(High, 0.4);
            var bolt = Add(canvas, Geometry.Parse("M13,15.2 L10.4,19 L13.6,19 L11,22.6"));
            var flash = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(3.2), RepeatBehavior = RepeatBehavior.Forever };
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0)));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0.7)));
            flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.72)));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.5, KeyTime.FromPercent(0.78)));
            flash.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(0.8)));
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(0.45, KeyTime.FromPercent(0.95)));
            Animate(bolt, OpacityProperty, flash);
            stops.Add(() => bolt.Opacity = 1);
        }

        // Three bands of mist sliding past each other.
        void Fog()
        {
            var bands = new[] { new[] { 6.0, 8, 17 }, new[] { 3.5, 12.5, 20.5 }, new[] { 7.0, 17, 18 } };
            double[] times = { 3.4, 4.2, 3.8 };
            for (int i = 0; i < bands.Length; i++)
            {
                var b = bands[i];
                var p = Add(canvas, new LineGeometry(new Point(b[0], b[1]), new Point(b[2], b[1])));
                var move = new TranslateTransform();
                p.RenderTransform = move;
                double d = i % 2 == 0 ? 1.6 : -1.6;
                Animate(move, TranslateTransform.XProperty, Sway(-d, d, times[i]));
            }
        }
    }
}
