using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinGroups
{
    // The depth effect's automatic cutout: the wallpaper's subject (a mountain, a skyline, a jet), found the way a
    // phone's lock screen does it, only simpler. The open background is grown in from the top and sides of the
    // picture, through colors that look like those edges and through smooth stretches like a sky's gradient; what
    // it can't reach, and is big enough or stands up from the bottom, is the subject. It's only used when the
    // result is clear: open background around the time, and a crisp edge where the subject meets it. Reads the
    // wallpaper; never changes it.
    public class DepthSubject
    {
        public BitmapSource Cutout;   // covers Area exactly; null when there's no subject to use
        public string Why = "";       // why not, when Cutout is null
        public Rect Area;             // screen pixels
        // for tests and the report
        public double Background, Reach, Contrast, Calm;

        byte[] alpha;
        int aw, ah;

        internal void Keep(byte[] a, int w, int h) { alpha = a; aw = w; ah = h; }

        // How much of a screen rectangle (0-1) the subject covers.
        public double Coverage(Rect screenRect)
        {
            if (alpha == null || Area.Width <= 0) return 0;
            var r = Rect.Intersect(screenRect, Area);
            if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) return 0;
            int x0 = Clamp((int)((r.X - Area.X) / Area.Width * aw), 0, aw - 1), x1 = Clamp((int)Math.Ceiling((r.Right - Area.X) / Area.Width * aw), x0 + 1, aw);
            int y0 = Clamp((int)((r.Y - Area.Y) / Area.Height * ah), 0, ah - 1), y1 = Clamp((int)Math.Ceiling((r.Bottom - Area.Y) / Area.Height * ah), y0 + 1, ah);
            int step = Math.Max(1, (x1 - x0) / 400), solid = 0, n = 0;
            for (int y = y0; y < y1; y += step)
                for (int x = x0; x < x1; x += step, n++)
                    if (alpha[y * aw + x] >= 128) solid++;
            double covered = n == 0 ? 0 : (double)solid / n;
            return covered * (r.Width * r.Height) / (screenRect.Width * screenRect.Height);
        }

        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }

    // The wallpaper looked at a few screen pixels per sample: which parts are subject, and the subject's and the
    // background's colors near the edge between them (for cutting that edge cleanly at full size).
    class SubjectMap
    {
        public int W, H;
        public float[] C;          // smoothed colors, r g b per sample
        public byte[] Solid;       // 1 = subject
        public byte[] Band;        // 1 = close to the subject's edge
        public float[] Soft;       // Solid, blurred a little (0-1)
        public float[] BandF;      // Band, as 0-1
        public float[] Fg, Bg;     // nearby subject and background colors, r g b per sample
        public float[] Grad;       // edge strength
        public byte[] HasFg, HasBg;
    }

    static class SubjectFinder
    {
        // ---- finding the subject on the whole picture ----

        // Background color model: what the top and upper sides of the picture look like.
        const int Clusters = 6;
        const double LikeBg = 1.6;     // how close (in cluster spreads) a color must be to count as background
        const float StepColor = 9f;    // a smooth stretch may change this much per sample...
        const float StepEdge = 7f;     // ...where there's no edge stronger than this
        const float EdgeStop = 15f;    // and nothing crosses an edge stronger than this, whatever its color
        const double SoftEdge = 12;    // a part whose outline is softer than this on average is a glow or haze
        internal static System.Text.StringBuilder Log; // for --dev-subjects

        public static SubjectMap Analyze(BitmapSource image, int width)
        {
            var src = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int iw = src.PixelWidth, ih = src.PixelHeight;
            var px = new byte[iw * ih * 4];
            src.CopyPixels(px, iw * 4, 0);
            int W = Math.Max(16, Math.Min(width, iw)), H = Math.Max(16, (int)Math.Round((double)W * ih / iw));
            int N = W * H;

            // shrink by averaging, then smooth a little more so fine texture and noise don't read as edges
            var c = new float[N * 3];
            var cnt = new int[N];
            for (int y = 0; y < ih; y++)
            {
                int ty = Math.Min(H - 1, y * H / ih);
                for (int x = 0; x < iw; x++)
                {
                    int t = ty * W + Math.Min(W - 1, x * W / iw), i = (y * iw + x) * 4;
                    c[t * 3] += px[i + 2]; c[t * 3 + 1] += px[i + 1]; c[t * 3 + 2] += px[i];
                    cnt[t]++;
                }
            }
            for (int t = 0; t < N; t++) { float k = 1f / Math.Max(1, cnt[t]); c[t * 3] *= k; c[t * 3 + 1] *= k; c[t * 3 + 2] *= k; }
            c = BoxBlur(BoxBlur(c, W, H, 3, 1), W, H, 3, 1);

            // edge strength (Sobel, all three colors)
            var grad = new float[N];
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    double sum = 0;
                    int up = ((y - 1) * W + x) * 3, mid = (y * W + x) * 3, down = ((y + 1) * W + x) * 3;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        double gx = c[up + 3 + ch] + 2 * c[mid + 3 + ch] + c[down + 3 + ch] - c[up - 3 + ch] - 2 * c[mid - 3 + ch] - c[down - 3 + ch];
                        double gy = c[down - 3 + ch] + 2 * c[down + ch] + c[down + 3 + ch] - c[up - 3 + ch] - 2 * c[up + ch] - c[up + 3 + ch];
                        sum += gx * gx + gy * gy;
                    }
                    grad[y * W + x] = (float)(Math.Sqrt(sum) / 4);
                }

            // what the edges of the picture look like: the top, and the sides down to just past the middle
            var edge = new List<int>();
            for (int x = 0; x < W; x++) { edge.Add(x); edge.Add(W + x); }
            for (int y = 0; y < H * 0.55; y++) { edge.Add(y * W); edge.Add(y * W + 1); edge.Add(y * W + W - 1); edge.Add(y * W + W - 2); }
            var model = Model(c, edge);
            var like = new float[N];
            for (int t = 0; t < N; t++) like[t] = Likeness(c, t, model);

            // grow the background in from those edges
            var bg = new byte[N];
            var refc = new float[N * 3];
            var queue = new int[N];
            int head = 0, tail = 0;
            foreach (int t in edge)
            {
                if (bg[t] != 0 || like[t] > LikeBg) continue;
                bg[t] = 1;
                refc[t * 3] = c[t * 3]; refc[t * 3 + 1] = c[t * 3 + 1]; refc[t * 3 + 2] = c[t * 3 + 2];
                queue[tail++] = t;
            }
            while (head < tail)
            {
                int p = queue[head++], x = p % W, y = p / W;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int q = ny * W + nx;
                    if (bg[q] != 0) continue;
                    float dr = c[q * 3] - refc[p * 3], dg = c[q * 3 + 1] - refc[p * 3 + 1], db = c[q * 3 + 2] - refc[p * 3 + 2];
                    bool smooth = grad[q] < StepEdge && dr * dr + dg * dg + db * db < StepColor * StepColor;
                    if (!smooth && (like[q] > LikeBg || grad[q] > EdgeStop)) continue;
                    bg[q] = 1;
                    for (int ch = 0; ch < 3; ch++) refc[q * 3 + ch] = refc[p * 3 + ch] + 0.25f * (c[q * 3 + ch] - refc[p * 3 + ch]);
                    queue[tail++] = q;
                }
            }

            // what's left, tidied: specks and thin threads go, small gaps close
            var solid = new byte[N];
            for (int t = 0; t < N; t++) solid[t] = (byte)(bg[t] == 0 ? 1 : 0);
            solid = Dilate(Erode(solid, W, H, 1), W, H, 1);
            solid = Erode(Dilate(solid, W, H, 1), W, H, 1);

            // keep what stands up from the bottom of the picture, or is big enough to be the subject on its own;
            // smaller floating bits (birds, wisps of cloud) stay behind the time
            var label = new int[N];
            int next = 0;
            for (int t = 0; t < N; t++)
            {
                if (solid[t] == 0 || label[t] != 0) continue;
                next++;
                head = tail = 0;
                queue[tail++] = t;
                label[t] = next;
                bool grounded = false;
                while (head < tail)
                {
                    int p = queue[head++], x = p % W, y = p / W;
                    if (y == H - 1) grounded = true;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int q = ny * W + nx;
                        if (solid[q] == 0 || label[q] != 0) continue;
                        label[q] = next;
                        queue[tail++] = q;
                    }
                }
                // a subject has a crisp outline; a glow (the sun, a lamp) or a haze fades out and stays behind
                double edgeSum = 0;
                int edgeN = 0;
                for (int i = 0; i < tail; i++)
                {
                    int p = queue[i], x = p % W, y = p / W;
                    if ((x > 0 && solid[p - 1] == 0) || (x < W - 1 && solid[p + 1] == 0) || (y > 0 && solid[p - W] == 0) || (y < H - 1 && solid[p + W] == 0))
                    {
                        // the strongest edge just outside or on the outline
                        float g = grad[p];
                        if (x > 1) g = Math.Max(g, grad[p - 1]);
                        if (x < W - 2) g = Math.Max(g, grad[p + 1]);
                        if (y > 1) g = Math.Max(g, grad[p - W]);
                        if (y < H - 2) g = Math.Max(g, grad[p + W]);
                        edgeSum += g;
                        edgeN++;
                    }
                }
                double crisp = edgeN == 0 ? 0 : edgeSum / edgeN;
                // a bright white blob floating in the sky is the sun or a light, not a subject
                double lr = 0, lg = 0, lb = 0;
                for (int i = 0; i < tail; i++) { int p = queue[i]; lr += c[p * 3]; lg += c[p * 3 + 1]; lb += c[p * 3 + 2]; }
                lr /= tail; lg /= tail; lb /= tail;
                bool light = !grounded && Math.Min(lr, Math.Min(lg, lb)) > 225;
                // floating parts: big ones, or smaller solid ones with a sharp outline (a jet's fins, say; not
                // thin streaks)
                double thick = edgeN == 0 ? 0 : (double)tail / edgeN;
                bool keep = crisp >= SoftEdge && !light &&
                    (grounded ? tail >= N * 0.001 : tail >= N * 0.008 || (tail >= N * 0.0015 && crisp >= 35 && thick >= 4));
                if (Log != null && tail >= N * 0.001)
                    Log.AppendLine("    part: " + (100.0 * tail / N).ToString("0.00") + "% " + (grounded ? "grounded" : "floating") + (light ? " light" : "") + ", outline " + crisp.ToString("0.0") + ", thickness " + thick.ToString("0.0") + (keep ? "" : " (dropped)"));
                if (!keep) for (int i = 0; i < tail; i++) solid[queue[i]] = 0;
            }

            // the subject's and the background's colors near the edge, and the edge band itself
            // the band reaches a little further in: the background usually stops a sample or two short of the true edge
            var sureFg = Erode(solid, W, H, 3);
            var sureBg = Dilate(solid, W, H, 3);
            var band = new byte[N];
            for (int t = 0; t < N; t++) { band[t] = (byte)(sureBg[t] != 0 && sureFg[t] == 0 ? 1 : 0); sureBg[t] = (byte)(sureBg[t] == 0 ? 1 : 0); }
            var soft = new float[N];
            for (int t = 0; t < N; t++) soft[t] = solid[t];
            soft = BoxBlur(BoxBlur(soft, W, H, 1, 1), W, H, 1, 1);
            var bandF = new float[N];
            for (int t = 0; t < N; t++) bandF[t] = band[t];
            var m = new SubjectMap { W = W, H = H, C = c, Solid = solid, Band = band, Grad = grad, Soft = soft, BandF = bandF };
            LocalMean(c, sureFg, W, H, 5, out m.Fg, out m.HasFg);
            LocalMean(c, sureBg, W, H, 5, out m.Bg, out m.HasBg);
            return m;
        }

        class Cluster { public float R, G, B, Spread; }

        static List<Cluster> Model(float[] c, List<int> samples)
        {
            // k-means, started from samples spread across brightness
            var sorted = new List<int>(samples);
            sorted.Sort((a, b) => (c[a * 3] + c[a * 3 + 1] + c[a * 3 + 2]).CompareTo(c[b * 3] + c[b * 3 + 1] + c[b * 3 + 2]));
            var ks = new List<Cluster>();
            for (int k = 0; k < Clusters; k++)
            {
                int t = sorted[(int)((k + 0.5) / Clusters * (sorted.Count - 1))];
                ks.Add(new Cluster { R = c[t * 3], G = c[t * 3 + 1], B = c[t * 3 + 2] });
            }
            var owner = new int[samples.Count];
            for (int it = 0; it < 10; it++)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    int t = samples[i], best = 0;
                    double bd = double.MaxValue;
                    for (int k = 0; k < ks.Count; k++)
                    {
                        double d = Sq(c[t * 3] - ks[k].R) + Sq(c[t * 3 + 1] - ks[k].G) + Sq(c[t * 3 + 2] - ks[k].B);
                        if (d < bd) { bd = d; best = k; }
                    }
                    owner[i] = best;
                }
                for (int k = 0; k < ks.Count; k++)
                {
                    double r = 0, g = 0, b = 0;
                    int n = 0;
                    for (int i = 0; i < samples.Count; i++)
                        if (owner[i] == k) { int t = samples[i]; r += c[t * 3]; g += c[t * 3 + 1]; b += c[t * 3 + 2]; n++; }
                    if (n > 0) { ks[k].R = (float)(r / n); ks[k].G = (float)(g / n); ks[k].B = (float)(b / n); }
                }
            }
            // each color's spread; colors that only touch the edge here and there (the subject reaching the top,
            // say) aren't background
            var model = new List<Cluster>();
            for (int k = 0; k < ks.Count; k++)
            {
                double s = 0;
                int n = 0;
                for (int i = 0; i < samples.Count; i++)
                    if (owner[i] == k) { int t = samples[i]; s += Sq(c[t * 3] - ks[k].R) + Sq(c[t * 3 + 1] - ks[k].G) + Sq(c[t * 3 + 2] - ks[k].B); n++; }
                if (n < samples.Count * 0.04) continue;
                ks[k].Spread = (float)Math.Max(6, Math.Sqrt(s / n));
                model.Add(ks[k]);
            }
            return model;
        }

        static float Likeness(float[] c, int t, List<Cluster> model)
        {
            double best = double.MaxValue;
            foreach (var k in model)
            {
                double d = Math.Sqrt(Sq(c[t * 3] - k.R) + Sq(c[t * 3 + 1] - k.G) + Sq(c[t * 3 + 2] - k.B)) / (k.Spread + 8);
                if (d < best) best = d;
            }
            return (float)best;
        }

        // ---- the cutout for one rectangle of the picture ----

        // Gates: the time needs open background around it, something of the subject reaching it, and a clear edge.
        const double MinBackground = 0.45, MinReach = 0.004, MinContrast = 28, MinCalm = 0.8;

        // v: the rectangle as fractions of the picture. pixels(x, y, w, h): the picture at full size, cropped (or null).
        public static DepthSubject Cut(SubjectMap m, Rect v, Rect area, string screenName, int fullW, int fullH, Func<Int32Rect, byte[]> pixels)
        {
            var r = new DepthSubject { Area = area };
            int W = m.W, H = m.H;
            int x0 = Clamp((int)Math.Floor(v.X * W), 0, W - 1), x1 = Clamp((int)Math.Ceiling(v.Right * W), x0 + 1, W);
            int y0 = Clamp((int)Math.Floor(v.Y * H), 0, H - 1), y1 = Clamp((int)Math.Ceiling(v.Bottom * H), y0 + 1, H);
            int n = 0, subj = 0, edgeN = 0, calm = 0;
            double contrast = 0;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++, n++)
                {
                    int t = y * W + x;
                    if (m.Solid[t] == 0) { if (m.Grad[t] < StepEdge * 2) calm++; continue; }
                    subj++;
                    bool edge = (x > 0 && m.Solid[t - 1] == 0) || (x < W - 1 && m.Solid[t + 1] == 0) || (y > 0 && m.Solid[t - W] == 0) || (y < H - 1 && m.Solid[t + W] == 0);
                    if (!edge || m.HasFg[t] == 0 || m.HasBg[t] == 0) continue;
                    contrast += Math.Sqrt(Sq(m.Fg[t * 3] - m.Bg[t * 3]) + Sq(m.Fg[t * 3 + 1] - m.Bg[t * 3 + 1]) + Sq(m.Fg[t * 3 + 2] - m.Bg[t * 3 + 2]));
                    edgeN++;
                }
            r.Background = n == 0 ? 0 : 1 - (double)subj / n;
            r.Reach = n == 0 ? 0 : (double)subj / n;
            r.Contrast = edgeN == 0 ? 0 : contrast / edgeN;
            r.Calm = n - subj == 0 ? 0 : (double)calm / (n - subj);
            if (r.Reach < MinReach) { r.Why = "Off on " + screenName + ": nothing in the wallpaper reaches the time there."; return r; }
            if (r.Background < MinBackground || r.Calm < MinCalm) { r.Why = "Off on " + screenName + ": the wallpaper is too busy around the time to pick out a subject."; return r; }
            if (r.Contrast < MinContrast || edgeN > (x1 - x0) * 5) { r.Why = "Off on " + screenName + ": the subject's edges aren't clear enough to cut out neatly."; return r; }

            // full size: the picture's own pixels where it can, with the edge cut by color
            int ox = (int)Math.Round(v.X * fullW), oy = (int)Math.Round(v.Y * fullH);
            int ow = Math.Max(1, (int)Math.Round(v.Width * fullW)), oh = Math.Max(1, (int)Math.Round(v.Height * fullH));
            var out_ = new byte[ow * oh * 4];
            var a = new byte[ow * oh];
            int cx0 = Math.Max(0, ox), cy0 = Math.Max(0, oy), cx1 = Math.Min(fullW, ox + ow), cy1 = Math.Min(fullH, oy + oh);
            if (cx1 <= cx0 || cy1 <= cy0) { r.Why = "Off on " + screenName + ": the wallpaper doesn't cover the time there."; return r; }
            var src = pixels(new Int32Rect(cx0, cy0, cx1 - cx0, cy1 - cy0));
            if (src == null) { r.Why = "Off on " + screenName + ": couldn't read the wallpaper."; return r; }
            int sw = cx1 - cx0;
            double kx = (double)W / fullW, ky = (double)H / fullH;
            for (int y = cy0; y < cy1; y++)
            {
                double ay = (y + 0.5) * ky - 0.5;
                int ny = Clamp((int)Math.Round(ay), 0, H - 1);
                for (int x = cx0; x < cx1; x++)
                {
                    double ax = (x + 0.5) * kx - 0.5;
                    int nx = Clamp((int)Math.Round(ax), 0, W - 1), t = ny * W + nx;
                    int si = ((y - cy0) * sw + (x - cx0)) * 4, oi = ((y - oy) * ow + (x - ox));
                    double al;
                    if (m.Band[t] == 0 && Sample(m.BandF, W, H, ax, ay) <= 0) al = m.Solid[t];
                    else
                    {
                        // the rough outline, smoothed so it doesn't step along the samples
                        double raw = Sample(m.Soft, W, H, ax, ay);
                        double soft = (raw - 0.3) / 0.4;
                        soft = soft < 0 ? 0 : soft > 1 ? 1 : soft * soft * (3 - 2 * soft);
                        al = soft;
                        double fr, fg, fb, br, bgc, bb;
                        if (Sample3(m.Fg, m.HasFg, W, H, ax, ay, out fr, out fg, out fb) && Sample3(m.Bg, m.HasBg, W, H, ax, ay, out br, out bgc, out bb))
                        {
                            // where between the nearby background and subject colors this pixel falls; trusted when the
                            // two are far apart and the pixel really is a blend of them
                            double dr = fr - br, dg = fg - bgc, db = fb - bb, l2 = dr * dr + dg * dg + db * db;
                            double cr = src[si + 2] - br, cg = src[si + 1] - bgc, cb = src[si] - bb;
                            double p = l2 < 1 ? soft : (cr * dr + cg * dg + cb * db) / l2;
                            double rr = cr - p * dr, rg = cg - p * dg, rb = cb - p * db;
                            double apart = Math.Sqrt(l2), off = Math.Sqrt(rr * rr + rg * rg + rb * rb);
                            double trust = Math.Max(0, Math.Min(1, (apart - 15) / 20)) * Math.Max(0, Math.Min(1, 1 - off / (0.6 * apart + 1)));
                            al = trust * Math.Max(0, Math.Min(1, p)) + (1 - trust) * soft;
                        }
                        // nothing well outside the outline (stray specks of a textured sky)
                        al = Math.Min(al, Math.Max(0, Math.Min(1, (raw - 0.02) / 0.1)));
                        al = Math.Max(0, Math.Min(1, (al - 0.06) / 0.88));
                        // ease into the plain mask at the band's edges
                        double inBand = Math.Min(1, Sample(m.BandF, W, H, ax, ay) * 2);
                        al = inBand * al + (1 - inBand) * soft;
                    }
                    byte A = (byte)Math.Round(al * 255);
                    a[oi] = A;
                    out_[oi * 4] = src[si]; out_[oi * 4 + 1] = src[si + 1]; out_[oi * 4 + 2] = src[si + 2]; out_[oi * 4 + 3] = A;
                }
            }
            var bmp = BitmapSource.Create(ow, oh, 96, 96, PixelFormats.Bgra32, null, out_, ow * 4);
            bmp.Freeze();
            r.Cutout = bmp;
            r.Keep(a, ow, oh);
            return r;
        }

        // The picture's own pixels for a crop, read straight from the file (the file is only read).
        public static byte[] ReadCrop(string path, Int32Rect crop)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                    var conv = new FormatConvertedBitmap(dec.Frames[0], PixelFormats.Bgra32, null, 0);
                    var buf = new byte[crop.Width * crop.Height * 4];
                    conv.CopyPixels(crop, buf, crop.Width * 4, 0);
                    return buf;
                }
            }
            catch { return null; }
        }

        public static bool FullSize(string path, out int w, out int h)
        {
            w = h = 0;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                    w = dec.Frames[0].PixelWidth;
                    h = dec.Frames[0].PixelHeight;
                    return w > 0 && h > 0;
                }
            }
            catch { return false; }
        }

        public static byte[] CropOf(BitmapSource img, Int32Rect crop)
        {
            try
            {
                var conv = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
                var buf = new byte[crop.Width * crop.Height * 4];
                conv.CopyPixels(crop, buf, crop.Width * 4, 0);
                return buf;
            }
            catch { return null; }
        }

        // ---- small helpers ----

        // A color from a map that only has values in some places, blended from the nearest four that do.
        static bool Sample3(float[] a, byte[] has, int W, int H, double x, double y, out double r, out double g, out double b)
        {
            r = g = b = 0;
            x = Math.Max(0, Math.Min(W - 1, x)); y = Math.Max(0, Math.Min(H - 1, y));
            int xa = (int)x, ya = (int)y, xb = Math.Min(W - 1, xa + 1), yb = Math.Min(H - 1, ya + 1);
            double fx = x - xa, fy = y - ya, total = 0;
            int[] at = { ya * W + xa, ya * W + xb, yb * W + xa, yb * W + xb };
            double[] wt = { (1 - fx) * (1 - fy), fx * (1 - fy), (1 - fx) * fy, fx * fy };
            for (int i = 0; i < 4; i++)
            {
                if (has[at[i]] == 0) continue;
                r += a[at[i] * 3] * wt[i]; g += a[at[i] * 3 + 1] * wt[i]; b += a[at[i] * 3 + 2] * wt[i];
                total += wt[i];
            }
            if (total < 1e-6) return false;
            r /= total; g /= total; b /= total;
            return true;
        }

        static double Sample(float[] m, int W, int H, double x, double y)
        {
            x = Math.Max(0, Math.Min(W - 1, x)); y = Math.Max(0, Math.Min(H - 1, y));
            int xa = (int)x, ya = (int)y, xb = Math.Min(W - 1, xa + 1), yb = Math.Min(H - 1, ya + 1);
            double fx = x - xa, fy = y - ya;
            return (m[ya * W + xa] * (1 - fx) + m[ya * W + xb] * fx) * (1 - fy) + (m[yb * W + xa] * (1 - fx) + m[yb * W + xb] * fx) * fy;
        }

        static float[] BoxBlur(float[] c, int W, int H, int chans, int r)
        {
            var tmp = new float[c.Length];
            var outp = new float[c.Length];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    for (int ch = 0; ch < chans; ch++)
                    {
                        float s = 0; int n = 0;
                        for (int d = -r; d <= r; d++) { int xx = x + d; if (xx < 0 || xx >= W) continue; s += c[(y * W + xx) * chans + ch]; n++; }
                        tmp[(y * W + x) * chans + ch] = s / n;
                    }
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    for (int ch = 0; ch < chans; ch++)
                    {
                        float s = 0; int n = 0;
                        for (int d = -r; d <= r; d++) { int yy = y + d; if (yy < 0 || yy >= H) continue; s += tmp[(yy * W + x) * chans + ch]; n++; }
                        outp[(y * W + x) * chans + ch] = s / n;
                    }
            return outp;
        }

        // The average color of the marked samples within r of each sample.
        static void LocalMean(float[] c, byte[] mask, int W, int H, int r, out float[] mean, out byte[] has)
        {
            int N = W * H;
            var sum = new float[N * 4];
            for (int t = 0; t < N; t++)
                if (mask[t] != 0) { sum[t * 4] = c[t * 3]; sum[t * 4 + 1] = c[t * 3 + 1]; sum[t * 4 + 2] = c[t * 3 + 2]; sum[t * 4 + 3] = 1; }
            var tmp = new float[N * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    for (int d = -r; d <= r; d++)
                    {
                        int xx = x + d;
                        if (xx < 0 || xx >= W) continue;
                        for (int ch = 0; ch < 4; ch++) tmp[(y * W + x) * 4 + ch] += sum[(y * W + xx) * 4 + ch];
                    }
            var tot = new float[N * 4];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    for (int d = -r; d <= r; d++)
                    {
                        int yy = y + d;
                        if (yy < 0 || yy >= H) continue;
                        for (int ch = 0; ch < 4; ch++) tot[(y * W + x) * 4 + ch] += tmp[(yy * W + x) * 4 + ch];
                    }
            mean = new float[N * 3];
            has = new byte[N];
            for (int t = 0; t < N; t++)
            {
                float k = tot[t * 4 + 3];
                if (k < 1) continue;
                has[t] = 1;
                mean[t * 3] = tot[t * 4] / k; mean[t * 3 + 1] = tot[t * 4 + 1] / k; mean[t * 3 + 2] = tot[t * 4 + 2] / k;
            }
        }

        static byte[] Erode(byte[] m, int W, int H, int r) { return Morph(m, W, H, r, true); }
        static byte[] Dilate(byte[] m, int W, int H, int r) { return Morph(m, W, H, r, false); }

        // Square erode/dilate, done one direction at a time.
        static byte[] Morph(byte[] m, int W, int H, int r, bool erode)
        {
            byte want = (byte)(erode ? 0 : 1);
            var tmp = new byte[m.Length];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    byte v = (byte)(1 - want);
                    for (int d = -r; d <= r && v != want; d++) { int xx = x + d; if (xx >= 0 && xx < W && m[y * W + xx] == want) v = want; }
                    tmp[y * W + x] = v;
                }
            var outp = new byte[m.Length];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    byte v = (byte)(1 - want);
                    for (int d = -r; d <= r && v != want; d++) { int yy = y + d; if (yy >= 0 && yy < H && tmp[yy * W + x] == want) v = want; }
                    outp[y * W + x] = v;
                }
            return outp;
        }

        static double Sq(double v) { return v * v; }
        static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }
}
