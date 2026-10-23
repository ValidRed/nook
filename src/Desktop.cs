using System;
using System.Runtime.InteropServices;

namespace WinGroups
{
    // The desktop icon grid, so widgets line up with desktop icons. Sizes are in device-independent pixels
    // (the same at every display scale); 75 x 98 is Windows' default for medium icons.
    static class DesktopGrid
    {
        public static double CellW = 75, CellH = 98;
        const uint LVM_GETITEMSPACING = 0x1033;

        // Reads the spacing the desktop is using right now (a read-only query).
        public static void Refresh()
        {
            try
            {
                IntPtr lv = DesktopListView();
                if (lv == IntPtr.Zero) return;
                long r = Native.SendMessage(lv, LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero).ToInt64();
                int cx = (int)(r & 0xFFFF), cy = (int)((r >> 16) & 0xFFFF);
                uint dpi = Native.GetDpiForWindow(lv);
                double scale = dpi > 0 ? dpi / 96.0 : 1.0;
                if (cx >= 40 && cy >= 40) { CellW = cx / scale; CellH = cy / scale; }
            }
            catch { }
        }

        static IntPtr DesktopListView()
        {
            IntPtr view = Native.FindWindowEx(Native.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr w = IntPtr.Zero;
            while (view == IntPtr.Zero && (w = Native.FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero)
                view = Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            return view == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        }

        // Widgets use square spaces the width of a desktop icon column, so 1x1, 2x2 and 3x3 come out square.
        public static double Unit { get { return CellW; } }
        public static double WidthFor(int cols) { return cols * Unit; }
        public static double HeightFor(int rows) { return rows * Unit; }

        // Work area (screen pixels) and scale of the monitor at a point. nearest=false returns false when the point is off-screen.
        public static bool Monitor(Native.POINT p, bool nearest, out Native.RECT work, out double scale)
        {
            work = new Native.RECT();
            scale = 1.0;
            IntPtr mon = Native.MonitorFromPoint(p, nearest ? 2 : 0);
            if (mon == IntPtr.Zero) return false;
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
            if (!Native.GetMonitorInfo(mon, ref mi)) return false;
            work = mi.rcWork;
            uint dx, dy;
            if (Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0) scale = dx / 96.0;
            return true;
        }
    }
}
