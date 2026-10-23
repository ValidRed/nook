using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WinGroups
{
    // Awaits Windows Runtime calls. (The usual System.Runtime.WindowsRuntime helpers don't bind against the
    // per-area .winmd files that ship with Windows, which is all this build uses.)
    static class WinRT
    {
        public static Task<T> Await<T>(IAsyncOperation<T> op)
        {
            var tcs = new TaskCompletionSource<T>();
            op.Completed = (o, status) =>
            {
                try
                {
                    if (status == AsyncStatus.Completed) tcs.TrySetResult(o.GetResults());
                    else if (status == AsyncStatus.Error) tcs.TrySetException(o.ErrorCode ?? new Exception("Windows Runtime call failed"));
                    else tcs.TrySetCanceled();
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
            return tcs.Task;
        }

        public static async void Fire<T>(IAsyncOperation<T> op)
        {
            try { await Await(op); } catch { }
        }
    }

    // What's playing, read from the media controls Windows shows in its volume flyout and on the lock screen
    // (Spotify, Apple Music, browsers and so on). Only reads, except when you press play, pause or skip.
    class NowPlaying
    {
        public static readonly Color DefaultTint = Color.FromRgb(0xE8, 0xE8, 0xEE);

        readonly Dispatcher ui;
        GlobalSystemMediaTransportControlsSessionManager manager;
        GlobalSystemMediaTransportControlsSession session;
        TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> onSessionChanged;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> onProps;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> onPlayback;
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> onTimeline;
        DispatcherTimer poll;
        bool reading, readAgain, started, stopped;
        string trackKey = "";
        TimeSpan position, duration;
        DateTime positionAt;

        public NowPlaying(Dispatcher ui)
        {
            this.ui = ui;
            Tint = DefaultTint;
            Title = Artist = AppId = "";
        }

        public bool Available { get; private set; }      // a player is sharing what it plays
        public string Title { get; private set; }
        public string Artist { get; private set; }
        public BitmapSource Art { get; private set; }
        public Color Tint { get; private set; }          // a bright color from the album art, for the visualizer
        public bool Playing { get; private set; }
        public DateTime LastPlayed { get; private set; }
        public string AppId { get; private set; }
        public TimeSpan Duration { get { return duration; } }

        public TimeSpan Position
        {
            get
            {
                var p = position + (Playing ? DateTime.UtcNow - positionAt : TimeSpan.Zero);
                if (p < TimeSpan.Zero) p = TimeSpan.Zero;
                return duration > TimeSpan.Zero && p > duration ? duration : p;
            }
        }

        public event Action Changed;
        public event Action TrackChanged;   // a new song started playing

        public async void Start()
        {
            try
            {
                manager = await WinRT.Await(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                if (stopped) { manager = null; return; }
                onSessionChanged = (s, e) => ui.BeginInvoke(new Action(Attach));
                onProps = (s, e) => ui.BeginInvoke(new Action(Refresh));
                onPlayback = (s, e) => ui.BeginInvoke(new Action(Refresh));
                onTimeline = (s, e) => ui.BeginInvoke(new Action(Refresh));
                manager.CurrentSessionChanged += onSessionChanged;
                Attach();
                // Some players skip change notifications, so look again every few seconds.
                poll = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(3) };
                poll.Tick += (s, e) => Attach();
                poll.Start();
            }
            catch { manager = null; } // older Windows: the island just shows the time
        }

        public void Stop()
        {
            stopped = true;
            if (poll != null) poll.Stop();
            Detach();
            try { if (manager != null && onSessionChanged != null) manager.CurrentSessionChanged -= onSessionChanged; } catch { }
            manager = null;
        }

        void Attach()
        {
            if (manager == null) return;
            GlobalSystemMediaTransportControlsSession s = null;
            try { s = manager.GetCurrentSession(); } catch { }
            if (!ReferenceEquals(s, session))
            {
                Detach();
                session = s;
                if (session != null)
                {
                    try
                    {
                        session.MediaPropertiesChanged += onProps;
                        session.PlaybackInfoChanged += onPlayback;
                        session.TimelinePropertiesChanged += onTimeline;
                    }
                    catch { }
                }
            }
            Refresh();
        }

        void Detach()
        {
            if (session == null) return;
            try
            {
                session.MediaPropertiesChanged -= onProps;
                session.PlaybackInfoChanged -= onPlayback;
                session.TimelinePropertiesChanged -= onTimeline;
            }
            catch { }
            session = null;
        }

        async void Refresh()
        {
            if (reading) { readAgain = true; return; }
            reading = true;
            try
            {
                do
                {
                    readAgain = false;
                    await Read();
                } while (readAgain && !stopped);
            }
            catch { }
            finally { reading = false; }
        }

        async Task Read()
        {
            var s = session;
            if (s == null || stopped)
            {
                bool had = Available;
                Available = Playing = false;
                trackKey = "";
                if (had) Raise(Changed);
                return;
            }

            var info = s.GetPlaybackInfo();
            bool playing = info != null && info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var props = await WinRT.Await(s.TryGetMediaPropertiesAsync());
            if (stopped || !ReferenceEquals(s, session)) return;

            string title = props.Title ?? "", artist = props.Artist ?? "";
            if (artist.Length == 0) artist = props.AlbumArtist ?? "";
            string app = "";
            try { app = s.SourceAppUserModelId ?? ""; } catch { }
            string key = app + "\u001F" + title + "\u001F" + artist;
            bool newTrack = key != trackKey;
            if (newTrack)
            {
                trackKey = key;
                Art = null;
                Tint = DefaultTint;
            }
            // Players often send the cover a moment after the title, so keep asking until it arrives.
            if (Art == null && props.Thumbnail != null)
            {
                var art = await LoadArt(props.Thumbnail);
                if (stopped || trackKey != key) return;
                Art = art;
                Tint = TintOf(art);
            }

            var tl = s.GetTimelineProperties();
            duration = tl.EndTime - tl.StartTime;
            position = tl.Position - tl.StartTime;
            positionAt = tl.LastUpdatedTime.UtcDateTime;
            if (positionAt > DateTime.UtcNow || (DateTime.UtcNow - positionAt).TotalDays > 1) positionAt = DateTime.UtcNow;

            Title = title;
            Artist = artist;
            AppId = app;
            Available = title.Length > 0;
            Playing = playing && Available;
            if (Playing) LastPlayed = DateTime.UtcNow;
            Raise(Changed);
            if (newTrack && Playing && started) Raise(TrackChanged);
            started = true;
        }

        void Raise(Action a) { if (a != null) a(); }

        public void PlayPause()
        {
            var s = session;
            if (s == null) return;
            Playing = !Playing; // show it right away; the player confirms a moment later
            if (Playing) LastPlayed = DateTime.UtcNow;
            Raise(Changed);
            WinRT.Fire(s.TryTogglePlayPauseAsync());
        }

        public void Next() { var s = session; if (s != null) WinRT.Fire(s.TrySkipNextAsync()); }
        public void Previous() { var s = session; if (s != null) WinRT.Fire(s.TrySkipPreviousAsync()); }

        static async Task<BitmapSource> LoadArt(IRandomAccessStreamReference reference)
        {
            try
            {
                var stream = await WinRT.Await(reference.OpenReadAsync());
                using (stream)
                {
                    var reader = new DataReader(stream.GetInputStreamAt(0));
                    uint n = await WinRT.Await(reader.LoadAsync((uint)Math.Min(stream.Size, 8 * 1024 * 1024)));
                    var bytes = new byte[n];
                    reader.ReadBytes(bytes);
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bmp.DecodePixelWidth = 160;
                    bmp.StreamSource = new MemoryStream(bytes);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
            }
            catch { return null; }
        }

        // The album's most vivid color, lightened enough to read on black.
        public static Color TintOf(BitmapSource art)
        {
            if (art == null) return DefaultTint;
            try
            {
                var small = new TransformedBitmap(art, new ScaleTransform(16.0 / art.PixelWidth, 16.0 / art.PixelHeight));
                var px = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
                int w = px.PixelWidth, h = px.PixelHeight;
                var data = new byte[w * h * 4];
                px.CopyPixels(data, w * 4, 0);
                double r = 0, g = 0, b = 0, total = 0;
                for (int i = 0; i < data.Length; i += 4)
                {
                    var c = Color.FromRgb(data[i + 2], data[i + 1], data[i]);
                    double ch, cs, cl;
                    Theme.ToHsl(c, out ch, out cs, out cl);
                    double weight = cs * cs * (1 - Math.Abs(cl - 0.5) * 1.6) + 0.002;
                    if (weight <= 0) continue;
                    r += c.R * weight; g += c.G * weight; b += c.B * weight; total += weight;
                }
                if (total <= 0) return DefaultTint;
                var avg = Color.FromRgb((byte)(r / total), (byte)(g / total), (byte)(b / total));
                double hh, ss, ll;
                Theme.ToHsl(avg, out hh, out ss, out ll);
                if (ss < 0.18) return DefaultTint; // a black-and-white cover: keep the bars white
                return Theme.FromHsl(hh, Math.Min(1, Math.Max(ss, 0.55)), Math.Max(0.62, Math.Min(ll, 0.75)));
            }
            catch { return DefaultTint; }
        }
    }

    // How loud the speakers are right now, from the level meter Windows keeps for them (the bouncing bar in
    // Sound settings). It never records or hears the sound itself. Also reads and sets the volume.
    class AudioLevel
    {
        Native.IAudioMeterInformation meter;
        Native.IAudioEndpointVolume volume;
        DateTime acquired;
        readonly float[] peaks = new float[2];
        Guid context = Guid.NewGuid();

        void Ensure()
        {
            // Look again every few seconds so switching to headphones is picked up.
            if (meter != null && (DateTime.UtcNow - acquired).TotalSeconds < 3) return;
            Release();
            acquired = DateTime.UtcNow;
            Native.IMMDeviceEnumerator en = null;
            Native.IMMDevice dev = null;
            try
            {
                en = (Native.IMMDeviceEnumerator)new Native.MMDeviceEnumeratorClass();
                if (en.GetDefaultAudioEndpoint(Native.eRender, Native.eMultimedia, out dev) != 0 || dev == null) return;
                object o;
                var iid = Native.IID_IAudioMeterInformation;
                if (dev.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out o) == 0) meter = o as Native.IAudioMeterInformation;
                iid = Native.IID_IAudioEndpointVolume;
                if (dev.Activate(ref iid, Native.CLSCTX_ALL, IntPtr.Zero, out o) == 0) volume = o as Native.IAudioEndpointVolume;
            }
            catch { Release(); }
            finally
            {
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        public bool Read(out float left, out float right)
        {
            left = right = 0;
            Ensure();
            if (meter == null) return false;
            try
            {
                int n;
                if (meter.GetMeteringChannelCount(out n) == 0 && n >= 2 && meter.GetChannelsPeakValues(2, peaks) == 0)
                {
                    left = peaks[0];
                    right = peaks[1];
                    return true;
                }
                float p;
                if (meter.GetPeakValue(out p) != 0) return false;
                left = right = p;
                return true;
            }
            catch { Release(); return false; }
        }

        // 0 to 1, or -1 when there are no speakers.
        public double Volume
        {
            get
            {
                Ensure();
                float v;
                try { return volume != null && volume.GetMasterVolumeLevelScalar(out v) == 0 ? v : -1; }
                catch { return -1; }
            }
            set
            {
                Ensure();
                if (volume == null) return;
                try
                {
                    volume.SetMasterVolumeLevelScalar((float)Math.Max(0, Math.Min(1, value)), ref context);
                    if (value > 0) volume.SetMute(false, ref context);
                }
                catch { }
            }
        }

        public bool Muted
        {
            get
            {
                bool m;
                try { return volume != null && volume.GetMute(out m) == 0 && m; }
                catch { return false; }
            }
        }

        public void Release()
        {
            try { if (meter != null) Marshal.ReleaseComObject(meter); } catch { }
            try { if (volume != null) Marshal.ReleaseComObject(volume); } catch { }
            meter = null;
            volume = null;
        }
    }
}
