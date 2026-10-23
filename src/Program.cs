using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Markup;

[assembly: AssemblyTitle("WinGroups")]
[assembly: AssemblyDescription("Groups of apps on your Windows desktop")]
[assembly: AssemblyProduct("WinGroups")]
[assembly: AssemblyCompany("WinGroups")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]
[assembly: ComVisible(false)]

namespace WinGroups
{
    static class Program
    {
        public const string Version = "0.3.0";
        public const string ReleasesUrl = "https://github.com/ValidRed/desk-groups/releases";
        const string MutexName = "Local\\WinGroups.Instance";
        const char Sep = '\u001F';
        static Mutex mutex;

        static string PipeName
        {
            get { return "WinGroups-" + Environment.UserName + "-" + Process.GetCurrentProcess().SessionId; }
        }

        [STAThread]
        static int Main(string[] argv)
        {
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            var args = new List<string>(argv);

            if (args.Count > 0 && args[0].StartsWith("--dev-", StringComparison.OrdinalIgnoreCase)) return DevTools.Run(args);

            // One copy at a time: a second launch (for example double-clicking another group) hands its
            // command to the copy that's already open, then exits.
            bool created;
            mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                SendToRunning(args.Count == 0 ? new List<string> { "show" } : args);
                return 0;
            }

            try
            {
                var app = new App();
                app.Init();
                StartPipeServer(app);
                app.Dispatcher.BeginInvoke(new Action(() => app.HandleCommand(args)));
                app.Run();
            }
            finally
            {
                mutex.ReleaseMutex();
            }
            return 0;
        }

        // Hands the command to the copy that's already open. Returns false if none is.
        static bool SendToRunning(List<string> args)
        {
            Native.AllowSetForegroundWindow(-1); // the open copy may bring its window to the front
            byte[] payload = Encoding.UTF8.GetBytes(string.Join(Sep.ToString(), args));
            for (int tries = 0; tries < 4; tries++)
            {
                try
                {
                    using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                    {
                        client.Connect(tries == 0 ? 300 : 1200);
                        client.Write(payload, 0, payload.Length);
                        client.Flush();
                        return true;
                    }
                }
                catch (TimeoutException) { if (!IsRunning()) return false; }
                catch (IOException) { Thread.Sleep(150); }
            }
            return false;
        }

        static bool IsRunning()
        {
            Mutex m;
            if (!Mutex.TryOpenExisting(MutexName, out m)) return false;
            m.Dispose();
            return true;
        }

        static void StartPipeServer(App app)
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte))
                        {
                            server.WaitForConnection();
                            using (var ms = new MemoryStream())
                            {
                                server.CopyTo(ms);
                                var parts = Encoding.UTF8.GetString(ms.ToArray()).Split(Sep).Where(s => s.Length > 0).ToList();
                                app.Dispatcher.BeginInvoke(new Action(() => app.HandleCommand(parts)));
                            }
                        }
                    }
                    catch { Thread.Sleep(200); }
                }
            });
            t.IsBackground = true;
            t.Name = "WinGroups pipe";
            t.Start();
        }
    }

    static class Xaml
    {
        public static object Load(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinGroups." + name))
                return XamlReader.Load(s);
        }
    }
}
