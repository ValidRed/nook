using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinGroups
{
    // Draws a group's desktop icon (a Start-folder-style tile showing up to four apps) and packs it into an .ico.
    static class IconRenderer
    {
        static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 72, 96, 128, 256 };

        public static byte[] BuildIco(IList<ImageSource> icons, bool dark, bool folder)
        {
            var frames = new List<KeyValuePair<int, byte[]>>();
            foreach (int s in Sizes)
            {
                var bmp = Render(icons, null, s, dark, folder);
                frames.Add(new KeyValuePair<int, byte[]>(s, s >= 64 ? Png(bmp) : Dib(bmp)));
            }
            return Pack(frames);
        }

        // The app's own icon: the same tile with four colored squares.
        public static byte[] BuildAppIco()
        {
            var colors = new[] { Color.FromRgb(0x4C, 0xA0, 0xE0), Color.FromRgb(0x5B, 0xC0, 0x7A), Color.FromRgb(0xF2, 0xB9, 0x4A), Color.FromRgb(0xE0, 0x60, 0x7E) };
            var frames = new List<KeyValuePair<int, byte[]>>();
            foreach (int s in Sizes)
            {
                var bmp = Render(null, colors, s, true, false);
                frames.Add(new KeyValuePair<int, byte[]>(s, s >= 64 ? Png(bmp) : Dib(bmp)));
            }
            return Pack(frames);
        }

        public static BitmapSource RenderAppIcon(int size)
        {
            var colors = new[] { Color.FromRgb(0x4C, 0xA0, 0xE0), Color.FromRgb(0x5B, 0xC0, 0x7A), Color.FromRgb(0xF2, 0xB9, 0x4A), Color.FromRgb(0xE0, 0x60, 0x7E) };
            return Render(null, colors, size, true, false);
        }

        public static BitmapSource Render(IList<ImageSource> icons, IList<Color> swatches, int s, bool dark, bool folder)
        {
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
            {
                if (folder) DrawFolder(dc, icons, s);
                else DrawTile(dc, icons, swatches, s, dark);
            }
            var rtb = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        static Brush B(byte a, byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
            br.Freeze();
            return br;
        }

        static void DrawTile(DrawingContext dc, IList<ImageSource> icons, IList<Color> swatches, double s, bool dark)
        {
            double m = Math.Max(0.5, s * 0.03);
            double stroke = Math.Max(1, s / 64.0);
            var tile = new Rect(m + stroke / 2, m + stroke / 2, s - 2 * m - stroke, s - 2 * m - stroke);
            double r = s * 0.22;
            var fill = dark ? B(222, 42, 44, 54) : B(230, 250, 250, 252);
            var line = new Pen(dark ? B(80, 255, 255, 255) : B(50, 0, 0, 0), stroke);
            line.Freeze();
            dc.DrawRoundedRectangle(fill, line, tile, r, r);

            double pad = s * 0.15, gap = s * 0.08;
            double cell = (s - 2 * pad - gap) / 2;
            var empty = dark ? B(34, 255, 255, 255) : B(22, 0, 0, 0);
            for (int i = 0; i < 4; i++)
            {
                var rc = new Rect(pad + (i % 2) * (cell + gap), pad + (i / 2) * (cell + gap), cell, cell);
                if (s <= 24) rc = new Rect(Math.Round(rc.X), Math.Round(rc.Y), Math.Round(rc.Width), Math.Round(rc.Height));
                if (swatches != null && i < swatches.Count)
                {
                    var sw = new SolidColorBrush(swatches[i]);
                    sw.Freeze();
                    dc.DrawRoundedRectangle(sw, null, rc, cell * 0.24, cell * 0.24);
                }
                else if (icons != null && i < icons.Count && icons[i] != null)
                    dc.DrawImage(icons[i], rc);
                else
                    dc.DrawRoundedRectangle(empty, null, rc, cell * 0.24, cell * 0.24);
            }
        }

        static void DrawFolder(DrawingContext dc, IList<ImageSource> icons, double s)
        {
            double m = Math.Max(0.5, s * 0.03);
            var back = B(255, 0xE9, 0xA7, 0x33);
            var front = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD8, 0x77), Color.FromRgb(0xF4, 0xB7, 0x40), 90);
            front.Freeze();
            double w = s - 2 * m;
            dc.DrawRoundedRectangle(back, null, new Rect(m + s * 0.04, s * 0.1, s * 0.4, s * 0.2), s * 0.05, s * 0.05);
            dc.DrawRoundedRectangle(back, null, new Rect(m, s * 0.16, w, s * 0.72), s * 0.08, s * 0.08);
            var face = new Rect(m, s * 0.27, w, s * 0.66);
            dc.DrawRoundedRectangle(front, null, face, s * 0.08, s * 0.08);

            double gap = s * 0.05;
            double cell = Math.Min((face.Width - s * 0.2 - gap) / 2, (face.Height - s * 0.12 - gap) / 2);
            double gx = face.X + (face.Width - (2 * cell + gap)) / 2;
            double gy = face.Y + (face.Height - (2 * cell + gap)) / 2;
            var empty = B(40, 255, 255, 255);
            for (int i = 0; i < 4; i++)
            {
                var rc = new Rect(gx + (i % 2) * (cell + gap), gy + (i / 2) * (cell + gap), cell, cell);
                if (icons != null && i < icons.Count && icons[i] != null) dc.DrawImage(icons[i], rc);
                else dc.DrawRoundedRectangle(empty, null, rc, cell * 0.2, cell * 0.2);
            }
        }

        static byte[] Png(BitmapSource bmp)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0)));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        // 32-bit BMP icon frame: header, bottom-up straight-alpha BGRA pixels, empty AND mask.
        static byte[] Dib(BitmapSource bmp)
        {
            int w = bmp.PixelWidth, h = bmp.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0).CopyPixels(px, stride, 0);
            int maskRow = ((w + 31) / 32) * 4;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40); bw.Write(w); bw.Write(h * 2);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(stride * h + maskRow * h);
                bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                for (int y = h - 1; y >= 0; y--) bw.Write(px, y * stride, stride);
                bw.Write(new byte[maskRow * h]);
                return ms.ToArray();
            }
        }

        static byte[] Pack(List<KeyValuePair<int, byte[]>> frames)
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)frames.Count);
                int offset = 6 + 16 * frames.Count;
                foreach (var f in frames)
                {
                    byte dim = (byte)(f.Key >= 256 ? 0 : f.Key);
                    bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(f.Value.Length); bw.Write(offset);
                    offset += f.Value.Length;
                }
                foreach (var f in frames) bw.Write(f.Value);
                return ms.ToArray();
            }
        }
    }
}
