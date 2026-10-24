using System;
using System.IO;
using System.Media;
using System.Windows.Threading;

namespace WinGroups
{
    enum TimerState { Idle, Running, Paused, Done }

    // The island's timer. There's one for the whole app, so every island (one per screen) shows the same countdown.
    // While it runs, the countdown is the island's live activity; when it ends, the island says so and Windows' own
    // alarm sound plays for a little while. Nothing about it is saved.
    class IslandTimer
    {
        readonly App app;
        readonly DispatcherTimer tick, quiet, forget;
        SoundPlayer chime;
        DateTime endsAt;   // UTC, while running
        TimeSpan left;     // while paused
        string shown = "";

        public TimerState State { get; private set; }
        public TimeSpan Length { get; private set; }   // what it was started with, plus any minutes added
        public event Action Changed;                   // a new state, or a new second to show
        public event Action Finished;

        public IslandTimer(App app)
        {
            this.app = app;
            tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            tick.Tick += (s, e) => Step();
            quiet = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            quiet.Tick += (s, e) => StopChime();
            // An ended timer that nobody stops goes away on its own after a couple of minutes.
            forget = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            forget.Tick += (s, e) => Cancel();
        }

        public bool Active { get { return State != TimerState.Idle; } }

        public TimeSpan Remaining
        {
            get
            {
                if (State == TimerState.Running) { var r = endsAt - DateTime.UtcNow; return r < TimeSpan.Zero ? TimeSpan.Zero : r; }
                return State == TimerState.Paused ? left : TimeSpan.Zero;
            }
        }

        // 0 when it starts, 1 when it ends.
        public double Progress
        {
            get
            {
                if (State == TimerState.Done) return 1;
                return Length.TotalSeconds <= 0 ? 0 : Math.Max(0, Math.Min(1, 1 - Remaining.TotalSeconds / Length.TotalSeconds));
            }
        }

        public void Start(TimeSpan length)
        {
            demoRunning = false;
            StopChime();
            forget.Stop();
            Length = length;
            endsAt = DateTime.UtcNow + length;
            State = TimerState.Running;
            tick.Start();
            Raise();
        }

        public void Pause()
        {
            demoRunning = false;
            if (State != TimerState.Running) return;
            left = Remaining;
            State = TimerState.Paused;
            tick.Stop();
            Raise();
        }

        public void Resume()
        {
            demoRunning = false;
            if (State != TimerState.Paused) return;
            endsAt = DateTime.UtcNow + left;
            State = TimerState.Running;
            tick.Start();
            Raise();
        }

        public void AddMinute()
        {
            if (State == TimerState.Running) endsAt += TimeSpan.FromMinutes(1);
            else if (State == TimerState.Paused) left += TimeSpan.FromMinutes(1);
            else return;
            Length += TimeSpan.FromMinutes(1);
            Raise();
        }

        public void Restart() { Start(Length); }

        public void Cancel()
        {
            demoRunning = false;
            tick.Stop();
            forget.Stop();
            StopChime();
            if (State == TimerState.Idle) return;
            State = TimerState.Idle;
            Raise();
        }

        void Step()
        {
            if (State != TimerState.Running) return;
            if (DateTime.UtcNow >= endsAt) { Finish(); return; }
            string s = Text(Remaining);
            if (s == shown) return;
            shown = s;
            if (Changed != null) Changed();
        }

        void Finish()
        {
            tick.Stop();
            State = TimerState.Done;
            Raise();
            if (app.Settings.IslandTimerSound) PlayChime();
            forget.Start();
            if (Finished != null) Finished();
        }

        void Raise()
        {
            shown = Text(Remaining);
            if (Changed != null) Changed();
        }

        // m:ss, or h:mm:ss from an hour up. Rounds up, so it shows 0:01 until the very end.
        public static string Text(TimeSpan t)
        {
            long secs = Math.Max(0, (long)Math.Ceiling(t.TotalSeconds - 0.001));
            long h = secs / 3600, m = secs % 3600 / 60, s = secs % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, s) : string.Format("{0}:{1:00}", m, s);
        }

        // "5 min", "1 hr 30 min"
        public static string Words(TimeSpan t)
        {
            int mins = (int)Math.Round(t.TotalMinutes);
            if (mins < 60) return mins + " min";
            return mins / 60 + " hr" + (mins % 60 > 0 ? " " + mins % 60 + " min" : "");
        }

        // Windows' alarm sound, over and over for a little while, or until you stop it.
        void PlayChime()
        {
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
                if (File.Exists(file))
                {
                    chime = new SoundPlayer(file);
                    chime.PlayLooping();
                    quiet.Start();
                }
                else SystemSounds.Exclamation.Play();
            }
            catch { }
        }

        void StopChime()
        {
            quiet.Stop();
            if (chime == null) return;
            try { chime.Stop(); chime.Dispose(); } catch { }
            chime = null;
        }

        // --dev-island: a timer frozen in a state, for screenshots.
        internal void Demo(TimerState state, TimeSpan length, TimeSpan remaining)
        {
            tick.Stop();
            State = state;
            Length = length;
            left = remaining;
            endsAt = DateTime.UtcNow + remaining;
            if (state == TimerState.Running) State = TimerState.Paused; // frozen, but drawn as running
            demoRunning = state == TimerState.Running;
            Raise();
        }

        bool demoRunning;
        public bool Running { get { return State == TimerState.Running || demoRunning && State == TimerState.Paused; } }
        public bool Paused { get { return State == TimerState.Paused && !demoRunning; } }
    }
}
