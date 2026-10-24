using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;

namespace WinGroups
{
    // A monitor, for the features that let you pick one (the island, the clock, workspaces).
    class ScreenInfo
    {
        public string Device;            // \\.\DISPLAY2
        public Native.RECT Bounds, Work; // screen pixels
        public bool Primary;
        public double Scale;
        public int Number;

        public int PixelWidth { get { return Bounds.Right - Bounds.Left; } }
        public int PixelHeight { get { return Bounds.Bottom - Bounds.Top; } }
        public string Label { get { return "Screen " + Number + " · " + PixelWidth + " × " + PixelHeight + (Primary ? " (main)" : ""); } }
    }

    static class Screens
    {
        public static List<ScreenInfo> All()
        {
            var list = new List<ScreenInfo>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr mon, IntPtr hdc, ref Native.RECT r, IntPtr data) =>
            {
                var mi = new Native.MONITORINFOEX { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX)) };
                if (!Native.GetMonitorInfoEx(mon, ref mi)) return true;
                uint dx, dy;
                double scale = Native.GetDpiForMonitor(mon, 0, out dx, out dy) == 0 && dx > 0 ? dx / 96.0 : 1.0;
                string device = mi.szDevice ?? "";
                int n;
                string digits = new string(device.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
                list.Add(new ScreenInfo
                {
                    Device = device, Bounds = mi.rcMonitor, Work = mi.rcWork, Primary = (mi.dwFlags & 1) != 0, Scale = scale,
                    Number = int.TryParse(digits, out n) ? n : list.Count + 1
                });
                return true;
            }, IntPtr.Zero);
            return list.OrderBy(s => s.Number).ToList();
        }

        public static ScreenInfo Primary(List<ScreenInfo> all) { return all.FirstOrDefault(s => s.Primary) ?? all.FirstOrDefault(); }

        // The screen with this device name, or the main screen when it's "" or no longer connected.
        public static ScreenInfo Find(string device)
        {
            var all = All();
            return all.FirstOrDefault(s => !string.IsNullOrEmpty(device) && string.Equals(s.Device, device, StringComparison.OrdinalIgnoreCase)) ?? Primary(all);
        }
    }

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

        // ---- keeping widgets right on top of the desktop ----
        // Widgets sit directly above the desktop window. Normally that's the bottom of the stack; when you press
        // Win+D, Windows raises the desktop above your apps, and widgets follow so they stay visible.

        static IntPtr hostCache;
        static DateTime hostTime;
        static readonly uint myPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        public static IntPtr DesktopHost()
        {
            if (hostCache != IntPtr.Zero && (DateTime.Now - hostTime).TotalSeconds < 2) return hostCache;
            IntPtr lv = DesktopListView();
            hostCache = lv == IntPtr.Zero ? IntPtr.Zero : Native.GetAncestor(lv, 2 /*GA_ROOT*/);
            hostTime = DateTime.Now;
            return hostCache;
        }

        static bool Mine(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return pid == myPid;
        }

        static bool Topmost(IntPtr hwnd)
        {
            return (Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64() & Native.WS_EX_TOPMOST) != 0;
        }

        // What to pass as "insert after" so a widget lands just above the desktop window.
        public static IntPtr InsertAfterForWidgets()
        {
            IntPtr host = DesktopHost();
            if (host == IntPtr.Zero) return Native.HWND_BOTTOM;
            IntPtr prev = Native.GetWindow(host, Native.GW_HWNDPREV);
            while (prev != IntPtr.Zero && Mine(prev)) prev = Native.GetWindow(prev, Native.GW_HWNDPREV);
            if (prev == IntPtr.Zero || Topmost(prev)) return IntPtr.Zero; // HWND_TOP of the normal windows
            return prev;
        }

        // True when only WinGroups' own windows (or hidden ones) are between the widget and the desktop.
        public static bool IsRightAboveDesktop(IntPtr widget)
        {
            IntPtr host = DesktopHost();
            if (host == IntPtr.Zero) return true;
            for (IntPtr w = Native.GetWindow(widget, Native.GW_HWNDNEXT); w != IntPtr.Zero; w = Native.GetWindow(w, Native.GW_HWNDNEXT))
            {
                if (w == host) return true;
                if (!Mine(w) && Native.IsWindowVisible(w)) return false;
            }
            return false;
        }

        // ---- grid snapping ----

        // Top-left (screen pixels) of the grid cell nearest to a widget rectangle, kept inside that screen.
        public static Native.POINT SnapTarget(Rect r)
        {
            var c = new Native.POINT { X = (int)(r.X + r.Width / 2), Y = (int)(r.Y + r.Height / 2) };
            Native.RECT work;
            double scale;
            Monitor(c, true, out work, out scale);
            double unit = Unit * scale;
            int maxCol = Math.Max(0, (int)Math.Floor((work.Right - work.Left - r.Width) / unit + 0.01));
            int maxRow = Math.Max(0, (int)Math.Floor((work.Bottom - work.Top - r.Height) / unit + 0.01));
            int col = Math.Max(0, Math.Min(maxCol, (int)Math.Round((r.X - work.Left) / unit)));
            int row = Math.Max(0, Math.Min(maxRow, (int)Math.Round((r.Y - work.Top) / unit)));
            return new Native.POINT { X = work.Left + (int)Math.Round(col * unit), Y = work.Top + (int)Math.Round(row * unit) };
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
