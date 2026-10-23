using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinGroups
{
    // Draws WinGroups' own icon: a Start-folder-style tile with four colored squares.
    static class IconRenderer
    {
        static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 72, 96, 128, 256 };
        static readonly Color[] Swatches =
        {
            Color.FromRgb(0x4C, 0xA0, 0xE0), Color.FromRgb(0x5B, 0xC0, 0x7A),
            Color.FromRgb(0xF2, 0xB9, 0x4A), Color.FromRgb(0xE0, 0x60, 0x7E)
        };

        public static BitmapSource RenderAppIcon(int s)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                double m = Math.Max(0.5, s * 0.03);
                double stroke = Math.Max(1, s / 64.0);
                var fill = new SolidColorBrush(Color.FromArgb(222, 42, 44, 54));
                var line = new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), stroke);
                dc.DrawRoundedRectangle(fill, line, new Rect(m + stroke / 2, m + stroke / 2, s - 2 * m - stroke, s - 2 * m - stroke), s * 0.22, s * 0.22);
                double pad = s * 0.15, gap = s * 0.08, cell = (s - 2 * pad - gap) / 2;
                for (int i = 0; i < 4; i++)
                {
                    var rc = new Rect(pad + (i % 2) * (cell + gap), pad + (i / 2) * (cell + gap), cell, cell);
                    if (s <= 24) rc = new Rect(Math.Round(rc.X), Math.Round(rc.Y), Math.Round(rc.Width), Math.Round(rc.Height));
                    dc.DrawRoundedRectangle(new SolidColorBrush(Swatches[i]), null, rc, cell * 0.24, cell * 0.24);
                }
            }
            var rtb = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        public static byte[] BuildAppIco()
        {
            var frames = new List<byte[]>();
            foreach (int s in Sizes) frames.Add(s >= 64 ? Png(RenderAppIcon(s)) : Dib(RenderAppIcon(s)));
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write((short)0); bw.Write((short)1); bw.Write((short)frames.Count);
                int offset = 6 + 16 * frames.Count;
                for (int i = 0; i < frames.Count; i++)
                {
                    byte dim = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
                    bw.Write(dim); bw.Write(dim); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((short)1); bw.Write((short)32);
                    bw.Write(frames[i].Length); bw.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) bw.Write(f);
                return ms.ToArray();
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
    }
}
