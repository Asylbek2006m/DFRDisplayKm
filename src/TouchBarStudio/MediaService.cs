using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace TouchBarStudio
{
    /// <summary>
    /// Now-playing information and seeking through the Windows media session API
    /// (the same source as the system media overlay: browsers, Spotify, system players).
    /// All failures are swallowed: when the API is missing the service simply reports no session.
    /// </summary>
    public sealed class MediaService : IDisposable
    {
        GlobalSystemMediaTransportControlsSessionManager _manager;
        GlobalSystemMediaTransportControlsSession _session;

        volatile string _title = "";
        volatile string _artist = "";
        volatile bool _playing;
        volatile bool _hasSession;
        volatile bool _canSeek;
        double _durationSeconds;
        double _positionSeconds;
        double _rate = 1.0;
        DateTimeOffset _stamp = DateTimeOffset.UtcNow;

        /// <summary>Raised on a worker thread whenever title, state or timeline changed.</summary>
        public event Action Changed;

        public string Title => _title;
        public string Artist => _artist;
        public bool Playing => _playing;
        public bool HasSession => _hasSession;
        public bool CanSeek => _canSeek && _durationSeconds > 0;
        public double Duration => _durationSeconds;

        /// <summary>Current position, extrapolated while playing.</summary>
        public double Position
        {
            get
            {
                double p = _positionSeconds;
                if (_playing)
                {
                    p += (DateTimeOffset.UtcNow - _stamp).TotalSeconds * _rate;
                }

                if (_durationSeconds > 0) p = Math.Min(p, _durationSeconds);
                return Math.Max(0, p);
            }
        }

        public async Task StartAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.CurrentSessionChanged += (s, e) => Attach();
                Attach();
            }
            catch
            {
                _hasSession = false;
            }
        }

        void Attach()
        {
            try
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnChanged;
                    _session.PlaybackInfoChanged -= OnChanged;
                    _session.TimelinePropertiesChanged -= OnChanged;
                }

                _session = _manager?.GetCurrentSession();
                if (_session != null)
                {
                    _session.MediaPropertiesChanged += OnChanged;
                    _session.PlaybackInfoChanged += OnChanged;
                    _session.TimelinePropertiesChanged += OnChanged;
                }
            }
            catch
            {
                _session = null;
            }

            _ = RefreshAsync();
        }

        void OnChanged(GlobalSystemMediaTransportControlsSession sender, object args)
        {
            _ = RefreshAsync();
        }

        async Task RefreshAsync()
        {
            var session = _session;
            try
            {
                if (session == null)
                {
                    _hasSession = false;
                    _playing = false;
                    _title = "";
                    _artist = "";
                    _durationSeconds = 0;
                }
                else
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    var info = session.GetPlaybackInfo();
                    var timeline = session.GetTimelineProperties();

                    _title = props?.Title ?? "";
                    _artist = props?.Artist ?? "";
                    _playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    _rate = info.PlaybackRate ?? 1.0;
                    _canSeek = info.Controls.IsPlaybackPositionEnabled;

                    double duration = (timeline.EndTime - timeline.StartTime).TotalSeconds;
                    _durationSeconds = duration > 0 ? duration : 0;
                    _positionSeconds = Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);
                    _stamp = timeline.LastUpdatedTime.ToUniversalTime();
                    if (_stamp.Year < 2000) _stamp = DateTimeOffset.UtcNow;
                    _hasSession = true;
                }
            }
            catch
            {
                _hasSession = false;
            }

            Changed?.Invoke();
        }

        /// <summary>Jumps to a position in seconds. Returns false when the player does not allow seeking.</summary>
        public async Task<bool> SeekAsync(double seconds)
        {
            var session = _session;
            if (session == null || !CanSeek) return false;
            try
            {
                long ticks = (long)(Math.Max(0, Math.Min(_durationSeconds, seconds)) * TimeSpan.TicksPerSecond);
                bool ok = await session.TryChangePlaybackPositionAsync(ticks);
                if (ok)
                {
                    _positionSeconds = seconds;
                    _stamp = DateTimeOffset.UtcNow;
                    Changed?.Invoke();
                }

                return ok;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            try
            {
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnChanged;
                    _session.PlaybackInfoChanged -= OnChanged;
                    _session.TimelinePropertiesChanged -= OnChanged;
                }
            }
            catch
            {
            }
        }
    }
}
