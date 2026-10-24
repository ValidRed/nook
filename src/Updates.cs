using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Threading;

namespace WinGroups
{
    // Looks for a newer Nook on the GitHub releases page and, when you click Update, swaps it in:
    // download next to this exe, check it, rename this exe out of the way, put the new one in its place, restart.
    class Updater
    {
        public const string Repo = "ValidRed/nook";

        readonly App app;
        DispatcherTimer timer;
        string downloadUrl, digest, pageUrl;
        long size;

        public string Status { get; private set; }
        public string NewVersion { get; private set; }
        public bool Available { get; private set; }
        public bool Busy { get; private set; }
        public event Action Changed;

        public Updater(App app)
        {
            this.app = app;
            Status = "Nook looks for new versions on GitHub";
        }

        // First look a little after startup, then twice a day while Nook is running.
        public void StartAutomatic()
        {
            if (timer != null) return;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(40) };
            timer.Tick += (s, e) =>
            {
                timer.Interval = TimeSpan.FromHours(12);
                if (app.Settings.CheckUpdates && !Available) Check(false);
            };
            timer.Start();
        }

        void Raise() { var h = Changed; if (h != null) h(); }

        DateTime lastChecked = DateTime.MinValue;

        // --dev-home-live: act as if a newer version were waiting, to show the update note (nothing is downloaded).
        internal void PretendAvailable(string version)
        {
            Available = true;
            NewVersion = version;
            Status = "Version " + version + " is ready to install";
            Raise();
        }

        // Nook's window just opened: look now, unless it looked in the last few minutes.
        public void CheckSoon()
        {
            if (!app.Settings.CheckUpdates || Busy || Available) return;
            if ((DateTime.UtcNow - lastChecked).TotalMinutes < 10) return;
            Check(false);
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "Nook/" + Program.Version;
            return wc;
        }

        public async void Check(bool byHand)
        {
            if (Busy) return;
            Busy = true;
            lastChecked = DateTime.UtcNow;
            if (byHand) { Status = "Checking for updates…"; Raise(); }
            bool was = Available;
            try
            {
                string json;
                using (var wc = Client())
                {
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    json = await wc.DownloadStringTaskAsync("https://api.github.com/repos/" + Repo + "/releases?per_page=15");
                }
                Pick(json);
                Status = Available ? "Version " + NewVersion + " is ready to install" : "You're up to date (version " + Program.Version + ")";
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                Status = r != null && r.StatusCode == HttpStatusCode.NotFound
                    ? "New versions can't be checked yet (the releases page isn't public)"
                    : "Couldn't check for updates right now";
            }
            catch { Status = "Couldn't check for updates right now"; }
            finally { Busy = false; }
            Raise();
            if (Available && !was) app.UpdateFound();
        }

        // The newest release (previews included) that has Nook.exe (or WinGroups.exe, its old name) and is newer than this copy.
        void Pick(string json)
        {
            var releases = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<Dictionary<string, object>>>(json);
            var current = ParseVersion(Program.Version);
            Version best = current;
            Available = false;
            foreach (var r in releases)
            {
                if (Equals(r.ContainsKey("draft") ? r["draft"] : null, true)) continue;
                var v = ParseVersion(r.ContainsKey("tag_name") ? r["tag_name"] as string : null);
                if (v == null || current == null || v <= best) continue;
                var assets = r.ContainsKey("assets") ? r["assets"] as IEnumerable : null;
                if (assets == null) continue;
                Dictionary<string, object> pick = null;
                foreach (Dictionary<string, object> a in assets)
                {
                    string n = a["name"] as string;
                    if (string.Equals(n, "Nook.exe", StringComparison.OrdinalIgnoreCase)) { pick = a; break; }
                    if (string.Equals(n, "WinGroups.exe", StringComparison.OrdinalIgnoreCase)) pick = a;
                }
                foreach (var a in pick == null ? new Dictionary<string, object>[0] : new[] { pick })
                {
                    best = v;
                    Available = true;
                    NewVersion = v.ToString(3);
                    downloadUrl = a["browser_download_url"] as string;
                    size = Convert.ToInt64(a["size"]);
                    digest = a.ContainsKey("digest") ? a["digest"] as string : null;
                    pageUrl = r.ContainsKey("html_url") ? r["html_url"] as string : "https://github.com/" + Repo + "/releases";
                }
            }
        }

        static Version ParseVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            Version v;
            string t = tag.TrimStart('v', 'V');
            int dash = t.IndexOf('-');
            if (dash > 0) t = t.Substring(0, dash);
            if (!Version.TryParse(t, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public async void Install()
        {
            if (Busy || !Available || string.IsNullOrEmpty(downloadUrl)) return;
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string dir = Path.GetDirectoryName(exe);
            string fresh = Path.Combine(dir, "Nook.update.exe"), old = Path.Combine(dir, "Nook.old.exe");
            if (!CanWrite(dir))
            {
                // Somewhere Nook can't write (like Program Files): open the download page instead.
                try { Process.Start(pageUrl); } catch { }
                return;
            }
            Busy = true;
            Status = "Downloading version " + NewVersion + "…";
            Raise();
            try
            {
                using (var wc = Client())
                {
                    wc.DownloadProgressChanged += (s, e) => { Status = "Downloading version " + NewVersion + "… " + e.ProgressPercentage + "%"; Raise(); };
                    await wc.DownloadFileTaskAsync(downloadUrl, fresh);
                }
                if (!Verify(fresh))
                {
                    TryDelete(fresh);
                    Status = "The download didn't match what GitHub lists, so nothing was changed";
                    Busy = false;
                    Raise();
                    return;
                }
                TryDelete(old);
                File.Move(exe, old);          // a running exe can be renamed, just not deleted
                try { File.Move(fresh, exe); }
                catch { File.Move(old, exe); throw; }
                Process.Start(new ProcessStartInfo(exe, "--updated " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
                app.Quit();
            }
            catch
            {
                TryDelete(fresh);
                Status = "Couldn't install the update. Try again, or download it from GitHub.";
                Busy = false;
                Raise();
            }
        }

        // The size must match, and so must the SHA-256 checksum when GitHub provides one.
        bool Verify(string file)
        {
            var info = new FileInfo(file);
            if (!info.Exists || (size > 0 && info.Length != size)) return false;
            if (string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return true;
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(file))
            {
                string hex = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");
                return string.Equals(hex, digest.Substring(7), StringComparison.OrdinalIgnoreCase);
            }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".nook-write-test");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

        // Run by the new copy right after an update: wait for the old copy to close, then remove it.
        public static void FinishUpdate(string oldPid)
        {
            int pid;
            if (int.TryParse(oldPid, out pid))
            {
                try { Process.GetProcessById(pid).WaitForExit(15000); } catch { }
            }
            string dir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
            foreach (var name in new[] { "Nook.old.exe", "WinGroups.old.exe" })
            {
                string old = Path.Combine(dir, name);
                for (int i = 0; i < 20 && File.Exists(old); i++)
                {
                    TryDelete(old);
                    if (File.Exists(old)) System.Threading.Thread.Sleep(250);
                }
            }
        }
    }
}
