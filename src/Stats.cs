using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WinGroups
{
    // CPU, memory, GPU and network use, the same numbers Task Manager shows. Read only while the island's
    // System page is open, a few times a second at most.
    class SystemStats
    {
        const int HistoryLength = 40;

        long lastIdle, lastKernel, lastUser, lastRx, lastTx;
        DateTime lastNet;
        PerformanceCounterCategory gpuCategory;
        bool gpuMissing;
        Dictionary<string, CounterSample> lastGpu = new Dictionary<string, CounterSample>();

        public double Cpu { get; private set; }          // 0 to 1
        public double Memory { get; private set; }       // 0 to 1
        public double MemoryUsedGb { get; private set; }
        public double MemoryTotalGb { get; private set; }
        public double Gpu { get; private set; }          // 0 to 1, or -1 when Windows doesn't report it
        public double Down { get; private set; }         // bytes per second
        public double Up { get; private set; }

        public readonly List<double> CpuHistory = new List<double>(), MemoryHistory = new List<double>(),
                                     GpuHistory = new List<double>(), NetHistory = new List<double>();

        public SystemStats() { Gpu = -1; }

        // Takes a moment (the GPU numbers especially), so call it off the UI thread.
        public void Sample()
        {
            long idle, kernel, user;
            if (Native.GetSystemTimes(out idle, out kernel, out user))
            {
                if (lastKernel != 0)
                {
                    long total = (kernel - lastKernel) + (user - lastUser); // kernel time includes idle time
                    Cpu = total > 0 ? Clamp(1 - (double)(idle - lastIdle) / total) : 0;
                }
                lastIdle = idle; lastKernel = kernel; lastUser = user;
            }

            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            if (Native.GlobalMemoryStatusEx(ref m) && m.ullTotalPhys > 0)
            {
                MemoryTotalGb = m.ullTotalPhys / 1073741824.0;
                MemoryUsedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
                Memory = Clamp(MemoryUsedGb / MemoryTotalGb);
            }

            long rx = 0, tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    try
                    {
                        var s = ni.GetIPStatistics();
                        rx += s.BytesReceived;
                        tx += s.BytesSent;
                    }
                    catch { }
                }
            }
            catch { }
            var now = DateTime.UtcNow;
            if (lastNet != default(DateTime))
            {
                double secs = (now - lastNet).TotalSeconds;
                if (secs > 0.05)
                {
                    Down = Math.Max(0, (rx - lastRx) / secs);
                    Up = Math.Max(0, (tx - lastTx) / secs);
                }
            }
            lastNet = now; lastRx = rx; lastTx = tx;

            SampleGpu();

            Push(CpuHistory, Cpu);
            Push(MemoryHistory, Memory);
            Push(GpuHistory, Math.Max(0, Gpu));
            Push(NetHistory, Down + Up);
        }

        // Adds up the 3D engine use of every process, like the GPU column in Task Manager.
        void SampleGpu()
        {
            if (gpuMissing) return;
            try
            {
                if (gpuCategory == null)
                {
                    if (!PerformanceCounterCategory.Exists("GPU Engine")) { gpuMissing = true; Gpu = -1; return; }
                    gpuCategory = new PerformanceCounterCategory("GPU Engine");
                }
                var data = gpuCategory.ReadCategory();
                var util = data["utilization percentage"];
                if (util == null) { Gpu = -1; return; }
                double total = 0;
                var next = new Dictionary<string, CounterSample>();
                foreach (InstanceData inst in util.Values)
                {
                    if (inst.InstanceName.IndexOf("engtype_3D", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    next[inst.InstanceName] = inst.Sample;
                    CounterSample prev;
                    if (lastGpu.TryGetValue(inst.InstanceName, out prev)) total += CounterSample.Calculate(prev, inst.Sample);
                }
                bool first = lastGpu.Count == 0;
                lastGpu = next;
                if (!first) Gpu = Clamp(total / 100);
            }
            catch { gpuMissing = true; Gpu = -1; }
        }

        static void Push(List<double> list, double v)
        {
            list.Add(v);
            if (list.Count > HistoryLength) list.RemoveAt(0);
        }

        static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }

        public static string Speed(double bytesPerSecond)
        {
            if (bytesPerSecond >= 1024 * 1024) return (bytesPerSecond / 1048576).ToString(bytesPerSecond >= 10 * 1048576 ? "0" : "0.0") + " MB/s";
            if (bytesPerSecond >= 1024) return Math.Round(bytesPerSecond / 1024) + " KB/s";
            return Math.Round(bytesPerSecond) + " B/s";
        }
    }
}
