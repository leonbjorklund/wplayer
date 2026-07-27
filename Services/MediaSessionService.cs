using Windows.Media.Control;

namespace WPlayer.Services;

public sealed class MediaSessionService : IDisposable
{
    private readonly List<GlobalSystemMediaTransportControlsSession> _trackedSessions = [];
    private Task<GlobalSystemMediaTransportControlsSessionManager>? _managerTask;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _cycledSession;
    private bool _disposed;

    public event EventHandler? Changed;

    public async Task<MediaSnapshot> GetSnapshotAsync(AppConfig config)
    {
        var (session, configChanged, enabledSessionCount) = await SelectSessionAsync(config);
        if (session is null)
        {
            return MediaSnapshot.None with { ConfigChanged = configChanged, EnabledSessionCount = enabledSessionCount };
        }

        var properties = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();

        return new MediaSnapshot(
            true,
            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            FormatDisplayText(properties.Artist, properties.Title),
            session.SourceAppUserModelId,
            configChanged,
            enabledSessionCount);
    }

    internal static string FormatDisplayText(string? artist, string? title)
    {
        artist = artist?.Trim();
        title = title?.Trim();
        return (string.IsNullOrEmpty(artist), string.IsNullOrEmpty(title)) switch
        {
            (false, false) => $"{artist} – {title}",
            (true, false) => title!,
            (false, true) => artist!,
            _ => "Unknown media"
        };
    }

    public async Task CycleSessionAsync(AppConfig config)
    {
        var context = await GetSelectionContextAsync(config);
        _cycledSession = MediaSessionSelection.Next(context.EnabledSessions, SelectSession(context), SameSession);
    }

    public async Task TogglePlayPauseAsync(AppConfig config)
    {
        var session = (await SelectSessionAsync(config)).Session;
        if (session is not null)
        {
            await session.TryTogglePlayPauseAsync();
        }
    }

    public async Task PreviousAsync(AppConfig config)
    {
        var session = (await SelectSessionAsync(config)).Session;
        if (session is not null)
        {
            await session.TrySkipPreviousAsync();
        }
    }

    public async Task NextAsync(AppConfig config)
    {
        var session = (await SelectSessionAsync(config)).Session;
        if (session is not null)
        {
            await session.TrySkipNextAsync();
        }
    }

    private async Task<(GlobalSystemMediaTransportControlsSession? Session, bool ConfigChanged, int EnabledSessionCount)> SelectSessionAsync(AppConfig config)
    {
        var context = await GetSelectionContextAsync(config);
        return (SelectSession(context), context.ConfigChanged, context.EnabledSessions.Count);
    }

    private async Task<(
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> EnabledSessions,
        GlobalSystemMediaTransportControlsSession? Current,
        bool ConfigChanged)> GetSelectionContextAsync(AppConfig config)
    {
        var manager = await GetManagerAsync();
        var sessions = manager.GetSessions();
        var configChanged = DiscoverMediaApps(sessions, config);
        var enabledSessions = sessions
            .Where(session => config.IsMediaAppEnabled(session.SourceAppUserModelId))
            .ToList();
        UpdateTrackedSessions(enabledSessions);
        return (enabledSessions, manager.GetCurrentSession(), configChanged);
    }

    private GlobalSystemMediaTransportControlsSession? SelectSession((
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> EnabledSessions,
        GlobalSystemMediaTransportControlsSession? Current,
        bool ConfigChanged) context) =>
        MediaSessionSelection.Select(
            context.EnabledSessions,
            _cycledSession,
            context.Current,
            SameSession,
            SameApp);

    private async Task<GlobalSystemMediaTransportControlsSessionManager> GetManagerAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_manager is not null)
        {
            return _manager;
        }

        var initialization = _managerTask ??=
            GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
        try
        {
            var manager = await initialization;
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_manager is null)
            {
                manager.CurrentSessionChanged += OnManagerChanged;
                manager.SessionsChanged += OnManagerChanged;
                _manager = manager;
            }

            return _manager;
        }
        catch
        {
            if (ReferenceEquals(_managerTask, initialization))
            {
                _managerTask = null;
            }

            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_manager is not null)
        {
            _manager.CurrentSessionChanged -= OnManagerChanged;
            _manager.SessionsChanged -= OnManagerChanged;
            _manager = null;
        }

        UpdateTrackedSessions([]);
    }

    private static bool DiscoverMediaApps(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions, AppConfig config)
    {
        var changed = false;
        foreach (var session in sessions)
        {
            changed |= config.EnsureMediaApp(session.SourceAppUserModelId, MediaProcess.GetProcessName(session.SourceAppUserModelId));
        }

        return changed;
    }

    private void UpdateTrackedSessions(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        foreach (var tracked in _trackedSessions.Where(tracked => !sessions.Any(session => SameSession(session, tracked))).ToList())
        {
            tracked.MediaPropertiesChanged -= OnSessionChanged;
            tracked.PlaybackInfoChanged -= OnSessionChanged;
            _trackedSessions.Remove(tracked);
        }

        foreach (var session in sessions)
        {
            if (_trackedSessions.Any(tracked => SameSession(tracked, session)))
            {
                continue;
            }

            session.MediaPropertiesChanged += OnSessionChanged;
            session.PlaybackInfoChanged += OnSessionChanged;
            _trackedSessions.Add(session);
        }
    }

    private static bool SameSession(GlobalSystemMediaTransportControlsSession left, GlobalSystemMediaTransportControlsSession right) =>
        ReferenceEquals(left, right);

    private static bool SameApp(GlobalSystemMediaTransportControlsSession left, GlobalSystemMediaTransportControlsSession right) =>
        string.Equals(left.SourceAppUserModelId, right.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase);

    private void OnManagerChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args) =>
        Changed?.Invoke(this, EventArgs.Empty);

    private void OnSessionChanged(GlobalSystemMediaTransportControlsSession sender, object args) =>
        Changed?.Invoke(this, EventArgs.Empty);

}

public sealed record MediaSnapshot(bool HasSession, bool IsPlaying, string DisplayText, string? SourceAppUserModelId = null, bool ConfigChanged = false, int EnabledSessionCount = 0)
{
    public static MediaSnapshot None { get; } = new(false, false, "Nothing playing");
}

internal static class MediaSessionSelection
{
    public static T? Select<T>(
        IReadOnlyList<T> enabledSessions,
        T? cycledSession,
        T? currentSession,
        Func<T, T, bool> sameSession,
        Func<T, T, bool> sameApp)
        where T : class
    {
        if (cycledSession is not null && enabledSessions.Any(session => sameSession(session, cycledSession)))
        {
            return cycledSession;
        }

        if (currentSession is not null && enabledSessions.Any(session => sameApp(session, currentSession)))
        {
            return currentSession;
        }

        return enabledSessions.FirstOrDefault();
    }

    public static T? Next<T>(IReadOnlyList<T> sessions, T? current, Func<T, T, bool> sameSession)
        where T : class
    {
        if (sessions.Count == 0)
        {
            return null;
        }

        var index = current is null ? -1 : sessions.ToList().FindIndex(session => sameSession(session, current));
        return sessions[(index + 1) % sessions.Count];
    }
}
