using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WinGroups
{
    // Gets the same icons Windows shows (Start, Explorer) for any file, shortcut or installed app.
    static class ShellIcons
    {
        const int SIIGBF_ICONONLY = 0x4;

        public static BitmapSource Get(string parsingName, int size)
        {
            if (string.IsNullOrEmpty(parsingName)) return null;
            Native.IShellItem item = null;
            try
            {
                Native.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(Native.IShellItem).GUID, out item);
                var factory = (Native.IShellItemImageFactory)item;
                IntPtr hbm;
                var sz = new Native.SIZE();
                sz.cx = size;
                sz.cy = size;
                if (factory.GetImage(sz, SIIGBF_ICONONLY, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                try { return FromHBitmap(hbm); }
                finally { Native.DeleteObject(hbm); }
            }
            catch
            {
                return null;
            }
            finally
            {
                if (item != null) Marshal.ReleaseComObject(item);
            }
        }

        static BitmapSource FromHBitmap(IntPtr hbm)
        {
            Native.BITMAP bm;
            Native.GetObject(hbm, Marshal.SizeOf(typeof(Native.BITMAP)), out bm);
            int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0) return null;

            var bi = new Native.BITMAPINFO();
            bi.bmiHeader.biSize = 40;
            bi.bmiHeader.biWidth = w;
            bi.bmiHeader.biHeight = -h; // top-down
            bi.bmiHeader.biPlanes = 1;
            bi.bmiHeader.biBitCount = 32;
            var px = new byte[w * h * 4];
            IntPtr dc = Native.GetDC(IntPtr.Zero);
            try
            {
                if (Native.GetDIBits(dc, hbm, 0, (uint)h, px, ref bi, 0) == 0) return null;
            }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, dc);
            }

            bool anyAlpha = false, straight = false;
            for (int i = 0; i < px.Length; i += 4)
            {
                byte a = px[i + 3];
                if (a != 0) anyAlpha = true;
                if (px[i] > a || px[i + 1] > a || px[i + 2] > a) straight = true;
            }
            if (!anyAlpha)
            {
                for (int i = 3; i < px.Length; i += 4) px[i] = 255;
                straight = false;
            }
            var src = BitmapSource.Create(w, h, 96, 96, straight ? PixelFormats.Bgra32 : PixelFormats.Pbgra32, null, px, w * 4);
            src.Freeze();
            return src;
        }
    }

    // Loads icons on a background STA thread and caches them, so lists of apps fill in without freezing the UI.
    static class IconLoader
    {
        static readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        static Thread worker;

        static void EnsureWorker()
        {
            if (worker != null) return;
            worker = new Thread(() =>
            {
                foreach (var job in queue.GetConsumingEnumerable())
                {
                    try { job(); } catch { }
                }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Name = "WinGroups icons";
            worker.Start();
        }

        public static BitmapSource Cached(string key)
        {
            lock (cache)
            {
                BitmapSource b;
                return cache.TryGetValue(key, out b) ? b : null;
            }
        }

        public static void Request(string key, Func<string> resolve, int size, Action<BitmapSource> done)
        {
            string k = key + "|" + size;
            var hit = Cached(k);
            if (hit != null) { done(hit); return; }
            EnsureWorker();
            var dispatcher = Dispatcher.CurrentDispatcher;
            queue.Add(() =>
            {
                var bmp = ShellIcons.Get(resolve(), size);
                if (bmp != null) lock (cache) cache[k] = bmp;
                dispatcher.BeginInvoke(new Action(() => done(bmp)));
            });
        }

        // Synchronous version for the UI thread (used when drawing a group's desktop icon).
        public static BitmapSource GetNow(string key, Func<string> resolve, int size)
        {
            string k = key + "|" + size;
            var hit = Cached(k);
            if (hit != null) return hit;
            var bmp = ShellIcons.Get(resolve(), size);
            if (bmp != null) lock (cache) cache[k] = bmp;
            return bmp;
        }
    }
}
