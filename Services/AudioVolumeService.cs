using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WPlayer.Services;

internal readonly record struct VolumeAdjustmentResult(bool Success, int Percent);

internal interface IAudioVolumeSession
{
    float Level { get; }
    bool Muted { get; }
    bool SetLevel(float level);
    bool SetMuted(bool muted);
}

internal static class AudioSessionVolumeBatch
{
    public static VolumeAdjustmentResult Adjust(IReadOnlyList<IAudioVolumeSession> sessions, int percentageDelta)
    {
        if (sessions.Count == 0)
        {
            return default;
        }

        var allMuted = sessions.All(session => session.Muted);
        if (allMuted && percentageDelta < 0)
        {
            return new(true, 0);
        }

        var target = VolumeMath.ApplyDelta(sessions.Average(session => session.Level), percentageDelta);
        var changedLevels = new List<(IAudioVolumeSession Session, float PreviousLevel)>();
        foreach (var session in sessions)
        {
            var previousLevel = session.Level;
            changedLevels.Add((session, previousLevel));
            if (!session.SetLevel(target))
            {
                Restore(changedLevels, []);
                return default;
            }
        }

        var unmutedSessions = new List<IAudioVolumeSession>();
        if (percentageDelta > 0)
        {
            foreach (var session in sessions.Where(session => session.Muted))
            {
                unmutedSessions.Add(session);
                if (!session.SetMuted(false))
                {
                    Restore(changedLevels, unmutedSessions);
                    return default;
                }
            }
        }

        return new(true, VolumeMath.ToPercent(target, allMuted && unmutedSessions.Count == 0));
    }

    private static void Restore(
        IReadOnlyList<(IAudioVolumeSession Session, float PreviousLevel)> changedLevels,
        IReadOnlyList<IAudioVolumeSession> unmutedSessions)
    {
        for (var index = unmutedSessions.Count - 1; index >= 0; index--)
        {
            _ = unmutedSessions[index].SetMuted(true);
        }

        for (var index = changedLevels.Count - 1; index >= 0; index--)
        {
            var changed = changedLevels[index];
            _ = changed.Session.SetLevel(changed.PreviousLevel);
        }
    }
}

internal enum AppVolumeLookupStatus
{
    Found,
    Missing,
    Failed
}

internal static class VolumeTargetPolicy
{
    public static bool ShouldUseMaster(
        VolumeScrollTarget preferredTarget,
        bool hasCurrentApp,
        AppVolumeLookupStatus appLookupStatus) =>
        preferredTarget == VolumeScrollTarget.WindowsMaster
        || !hasCurrentApp
        || appLookupStatus == AppVolumeLookupStatus.Missing;
}

internal static class AudioVolumeService
{
    private const uint DeviceStateActive = 0x00000001;
    private const uint ClsCtxAll = 0x17;

    public static VolumeAdjustmentResult GetVolume(string? sourceAppUserModelId, VolumeScrollTarget preferredTarget) =>
        AccessVolume(sourceAppUserModelId, preferredTarget, 0);

    public static VolumeAdjustmentResult AdjustVolume(
        string? sourceAppUserModelId,
        VolumeScrollTarget preferredTarget,
        int percentageDelta) =>
        AccessVolume(sourceAppUserModelId, preferredTarget, percentageDelta);

    private static VolumeAdjustmentResult AccessVolume(
        string? sourceAppUserModelId,
        VolumeScrollTarget preferredTarget,
        int percentageDelta)
    {
        try
        {
            var hasCurrentApp = !string.IsNullOrWhiteSpace(sourceAppUserModelId);
            var lookupStatus = AppVolumeLookupStatus.Missing;
            if (preferredTarget == VolumeScrollTarget.CurrentApp
                && hasCurrentApp)
            {
                using var sessions = AppAudioSessions.Find(sourceAppUserModelId!);
                lookupStatus = sessions.DiscoverySucceeded
                    ? sessions.Count > 0 ? AppVolumeLookupStatus.Found : AppVolumeLookupStatus.Missing
                    : AppVolumeLookupStatus.Failed;
                if (lookupStatus == AppVolumeLookupStatus.Found)
                {
                    return percentageDelta == 0
                        ? new(true, VolumeMath.ToPercent(sessions.AverageLevel, sessions.AllMuted))
                        : sessions.Adjust(percentageDelta);
                }
            }

            return VolumeTargetPolicy.ShouldUseMaster(
                preferredTarget,
                hasCurrentApp,
                lookupStatus)
                ? AccessMasterVolume(percentageDelta)
                : default;
        }
        catch (Exception ex)
        {
            AppLog.Error(percentageDelta == 0 ? "Could not read volume" : "Could not change volume", ex);
            return default;
        }
    }

    private static VolumeAdjustmentResult AccessMasterVolume(int percentageDelta)
    {
        object? enumeratorObject = null;
        IMMDevice? device = null;
        object? endpointObject = null;
        try
        {
            enumeratorObject = new MMDeviceEnumeratorComObject();
            var enumerator = (IMMDeviceEnumerator)enumeratorObject;
            if (!NativeCall.Succeeded(
                    enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out device),
                    "Could not get the default audio endpoint")
                || !TryActivate(
                    device,
                    typeof(IAudioEndpointVolume).GUID,
                    "Could not activate the default endpoint volume",
                    out endpointObject))
            {
                return default;
            }

            if (endpointObject is not IAudioEndpointVolume endpoint)
            {
                AppLog.Error(
                    "The default audio endpoint returned invalid volume control",
                    new InvalidCastException("The activated endpoint does not implement IAudioEndpointVolume."));
                return default;
            }

            if (!NativeCall.Succeeded(
                    endpoint.GetMasterVolumeLevelScalar(out var level),
                    "Could not read the default endpoint volume")
                || !NativeCall.Succeeded(
                    endpoint.GetMute(out var muted),
                    "Could not read the default endpoint mute state"))
            {
                return default;
            }

            if (percentageDelta == 0)
            {
                return new(true, VolumeMath.ToPercent(level, muted));
            }

            if (muted && percentageDelta < 0)
            {
                return new(true, 0);
            }

            var target = VolumeMath.ApplyDelta(level, percentageDelta);
            if (!NativeCall.Succeeded(
                    endpoint.SetMasterVolumeLevelScalar(target, IntPtr.Zero),
                    "Could not set the default endpoint volume"))
            {
                _ = NativeCall.Succeeded(
                    endpoint.SetMasterVolumeLevelScalar(level, IntPtr.Zero),
                    "Could not restore the default endpoint volume");
                return default;
            }

            if (muted
                && percentageDelta > 0
                && !NativeCall.Succeeded(
                    endpoint.SetMute(false, IntPtr.Zero),
                    "Could not unmute the default endpoint"))
            {
                _ = NativeCall.Succeeded(
                    endpoint.SetMute(true, IntPtr.Zero),
                    "Could not restore the default endpoint mute state");
                _ = NativeCall.Succeeded(
                    endpoint.SetMasterVolumeLevelScalar(level, IntPtr.Zero),
                    "Could not restore the default endpoint volume");
                return default;
            }

            return new(true, VolumeMath.ToPercent(target, false));
        }
        finally
        {
            ReleaseComObject(endpointObject);
            ReleaseComObject(device);
            ReleaseComObject(enumeratorObject);
        }
    }

    private static bool TryActivate(
        IMMDevice device,
        Guid interfaceId,
        string failureContext,
        out object? activated)
    {
        activated = null;
        return NativeCall.Succeeded(
            device.Activate(ref interfaceId, ClsCtxAll, IntPtr.Zero, out activated),
            failureContext);
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.ReleaseComObject(value);
        }
    }

    private sealed class AppAudioSessions : IDisposable
    {
        private readonly List<Session> _sessions = [];

        public int Count => _sessions.Count;
        public float AverageLevel => _sessions.Average(session => session.Level);
        public bool AllMuted => _sessions.All(session => session.Muted);
        public bool DiscoverySucceeded { get; private set; } = true;

        public static AppAudioSessions Find(string sourceAppUserModelId)
        {
            var result = new AppAudioSessions();
            object? enumeratorObject = null;
            IMMDeviceCollection? devices = null;
            try
            {
                enumeratorObject = new MMDeviceEnumeratorComObject();
                var enumerator = (IMMDeviceEnumerator)enumeratorObject;
                if (!result.Succeeded(
                        enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateActive, out devices),
                        "Could not enumerate active audio endpoints")
                    || !result.Succeeded(
                        devices.GetCount(out var deviceCount),
                        "Could not count active audio endpoints"))
                {
                    return result;
                }

                for (uint deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
                {
                    if (!result.Succeeded(
                            devices.Item(deviceIndex, out var device),
                            "Could not read an active audio endpoint"))
                    {
                        continue;
                    }

                    try
                    {
                        result.AddFromDevice(device, sourceAppUserModelId);
                    }
                    finally
                    {
                        ReleaseComObject(device);
                    }
                }

                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
            finally
            {
                ReleaseComObject(devices);
                ReleaseComObject(enumeratorObject);
            }
        }

        public VolumeAdjustmentResult Adjust(int percentageDelta) =>
            AudioSessionVolumeBatch.Adjust(_sessions, percentageDelta);

        private void AddFromDevice(IMMDevice device, string sourceAppUserModelId)
        {
            object? managerObject = null;
            IAudioSessionEnumerator? sessionEnumerator = null;
            try
            {
                if (!TryActivate(
                        device,
                        typeof(IAudioSessionManager2).GUID,
                        "Could not activate an audio session manager",
                        out managerObject))
                {
                    DiscoverySucceeded = false;
                    return;
                }

                if (managerObject is not IAudioSessionManager2 manager)
                {
                    Fail("The audio endpoint returned an invalid session manager");
                    return;
                }

                if (!Succeeded(
                        manager.GetSessionEnumerator(out sessionEnumerator),
                        "Could not enumerate audio sessions")
                    || !Succeeded(
                        sessionEnumerator.GetCount(out var sessionCount),
                        "Could not count audio sessions"))
                {
                    return;
                }

                for (var sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
                {
                    if (!Succeeded(
                            sessionEnumerator.GetSession(sessionIndex, out var control),
                            "Could not read an audio session"))
                    {
                        continue;
                    }

                    var keepControl = false;
                    try
                    {
                        if (!Succeeded(control.GetState(out var state), "Could not read an audio session state"))
                        {
                            continue;
                        }

                        if (state == AudioSessionState.Expired)
                        {
                            continue;
                        }

                        if (control is not IAudioSessionControl2 control2)
                        {
                            Fail("An audio session did not expose its process identity");
                            continue;
                        }

                        if (!Succeeded(control2.GetProcessId(out var processId), "Could not read an audio session process"))
                        {
                            continue;
                        }

                        if (processId == 0)
                        {
                            continue;
                        }

                        var identityMatch = AudioProcessIdentity.Match(sourceAppUserModelId, processId);
                        if (identityMatch == AudioProcessMatchResult.Failed)
                        {
                            DiscoverySucceeded = false;
                            continue;
                        }

                        if (identityMatch == AudioProcessMatchResult.NoMatch)
                        {
                            continue;
                        }

                        if (control is not ISimpleAudioVolume volume)
                        {
                            Fail("An audio session did not expose volume control");
                            continue;
                        }

                        if (!Succeeded(volume.GetMasterVolume(out var level), "Could not read an app session volume")
                            || !Succeeded(volume.GetMute(out var muted), "Could not read an app session mute state"))
                        {
                            continue;
                        }

                        _sessions.Add(new(control, volume, level, muted));
                        keepControl = true;
                    }
                    finally
                    {
                        if (!keepControl)
                        {
                            ReleaseComObject(control);
                        }
                    }
                }
            }
            finally
            {
                ReleaseComObject(sessionEnumerator);
                ReleaseComObject(managerObject);
            }
        }

        public void Dispose()
        {
            foreach (var session in _sessions)
            {
                ReleaseComObject(session.Control);
            }

            _sessions.Clear();
        }

        private bool Succeeded(int hresult, string failureContext)
        {
            if (NativeCall.Succeeded(hresult, failureContext))
            {
                return true;
            }

            DiscoverySucceeded = false;
            return false;
        }

        private void Fail(string failureContext)
        {
            DiscoverySucceeded = false;
            AppLog.Error(failureContext, new InvalidCastException(failureContext));
        }

        private sealed record Session(
            IAudioSessionControl Control,
            ISimpleAudioVolume Volume,
            float Level,
            bool Muted) : IAudioVolumeSession
        {
            public bool SetLevel(float level) => NativeCall.Succeeded(
                Volume.SetMasterVolume(level, IntPtr.Zero),
                "Could not set an app session volume");

            public bool SetMuted(bool muted) => NativeCall.Succeeded(
                Volume.SetMute(muted, IntPtr.Zero),
                muted ? "Could not restore an app session mute state" : "Could not unmute an app session");
        }
    }
}

internal static class NativeCall
{
    public static bool Succeeded(int hresult, string failureContext)
    {
        if (hresult >= 0)
        {
            return true;
        }

        AppLog.Error(
            failureContext,
            new COMException($"{failureContext} (HRESULT 0x{hresult:X8})", hresult));
        return false;
    }
}

internal sealed class VolumeWheelAccumulator
{
    private const int DeltaPerDetent = 120;
    private int _remainder;

    public int Add(int delta)
    {
        _remainder += delta;
        var detents = _remainder / DeltaPerDetent;
        _remainder -= detents * DeltaPerDetent;
        return detents;
    }

    public void Reset() => _remainder = 0;
}

internal static class VolumeMath
{
    public static float ApplyDelta(float level, int percentageDelta) =>
        Math.Clamp(level + percentageDelta / 100f, 0f, 1f);

    public static int ToPercent(float level, bool muted) =>
        muted ? 0 : (int)Math.Round(Math.Clamp(level, 0f, 1f) * 100, MidpointRounding.AwayFromZero);
}

internal enum AudioProcessMatchResult
{
    Match,
    NoMatch,
    Failed
}

internal static class AudioProcessIdentity
{
    private const int ErrorSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoApplication = 15703;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public static AudioProcessMatchResult Match(string sourceAppUserModelId, uint processId)
    {
        using var processHandle = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (processHandle.IsInvalid)
        {
            AppLog.Error(
                "Could not open an audio session process",
                new Win32Exception(Marshal.GetLastWin32Error()));
            return AudioProcessMatchResult.Failed;
        }

        try
        {
            var result = ReadApplicationUserModelId(processHandle, out var processAppUserModelId);
            if (result == ErrorSuccess)
            {
                return string.Equals(
                    sourceAppUserModelId,
                    processAppUserModelId,
                    StringComparison.OrdinalIgnoreCase)
                    ? AudioProcessMatchResult.Match
                    : AudioProcessMatchResult.NoMatch;
            }

            if (result != AppModelErrorNoApplication)
            {
                AppLog.Error(
                    "Could not read an audio session application identity",
                    new Win32Exception(result));
                return AudioProcessMatchResult.Failed;
            }

            using var process = Process.GetProcessById((int)processId);
            return MatchesProcessName(sourceAppUserModelId, process.ProcessName)
                ? AudioProcessMatchResult.Match
                : AudioProcessMatchResult.NoMatch;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            AppLog.Error("Could not read an audio session process", ex);
            return AudioProcessMatchResult.Failed;
        }
    }

    internal static bool MatchesProcessName(string sourceAppUserModelId, string? processName) =>
        !string.IsNullOrWhiteSpace(processName)
            && string.Equals(
                MediaProcess.GetProcessName(sourceAppUserModelId),
                processName,
                StringComparison.OrdinalIgnoreCase);

    private static int ReadApplicationUserModelId(SafeProcessHandle processHandle, out string? value)
    {
        value = null;
        uint length = 0;
        var result = NativeMethods.GetApplicationUserModelId(processHandle, ref length, null);
        if (result == AppModelErrorNoApplication)
        {
            return result;
        }

        if (result != ErrorInsufficientBuffer || length == 0)
        {
            return result;
        }

        var buffer = new char[length];
        result = NativeMethods.GetApplicationUserModelId(processHandle, ref length, buffer);
        if (result == ErrorSuccess)
        {
            value = new string(buffer).TrimEnd('\0');
        }

        return result;
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern SafeProcessHandle OpenProcess(
        uint processAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetApplicationUserModelId(
        SafeProcessHandle processHandle,
        ref uint applicationUserModelIdLength,
        [Out] char[]? applicationUserModelId);
}

internal enum EDataFlow
{
    Render
}

internal enum ERole
{
    Console
}

internal enum AudioSessionState
{
    Expired = 2
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class MMDeviceEnumeratorComObject;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig]
    int GetCount(out uint deviceCount);

    [PreserveSig]
    int Item(uint deviceIndex, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(
        ref Guid interfaceId,
        uint classContext,
        IntPtr activationParameters,
        [MarshalAs(UnmanagedType.IUnknown)] out object? activatedInterface);
}

[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    [PreserveSig]
    int GetAudioSessionControl(IntPtr audioSessionGuid, uint streamFlags, out IAudioSessionControl sessionControl);

    [PreserveSig]
    int GetSimpleAudioVolume(IntPtr audioSessionGuid, uint streamFlags, out ISimpleAudioVolume audioVolume);

    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnumerator);
}

[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int sessionCount);

    [PreserveSig]
    int GetSession(int sessionIndex, out IAudioSessionControl sessionControl);
}

[ComImport]
[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState state);
}

[ComImport]
[Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    [PreserveSig]
    int GetState(out AudioSessionState state);

    [PreserveSig]
    int GetDisplayName(out IntPtr displayName);

    [PreserveSig]
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, IntPtr eventContext);

    [PreserveSig]
    int GetIconPath(out IntPtr iconPath);

    [PreserveSig]
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, IntPtr eventContext);

    [PreserveSig]
    int GetGroupingParam(out Guid groupingId);

    [PreserveSig]
    int SetGroupingParam(ref Guid groupingId, IntPtr eventContext);

    [PreserveSig]
    int RegisterAudioSessionNotification(IntPtr client);

    [PreserveSig]
    int UnregisterAudioSessionNotification(IntPtr client);

    [PreserveSig]
    int GetSessionIdentifier(out IntPtr sessionIdentifier);

    [PreserveSig]
    int GetSessionInstanceIdentifier(out IntPtr sessionInstanceIdentifier);

    [PreserveSig]
    int GetProcessId(out uint processId);
}

[ComImport]
[Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float level, IntPtr eventContext);

    [PreserveSig]
    int GetMasterVolume(out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    [PreserveSig]
    int RegisterControlChangeNotify(IntPtr notify);

    [PreserveSig]
    int UnregisterControlChangeNotify(IntPtr notify);

    [PreserveSig]
    int GetChannelCount(out uint channelCount);

    [PreserveSig]
    int SetMasterVolumeLevel(float levelDb, IntPtr eventContext);

    [PreserveSig]
    int SetMasterVolumeLevelScalar(float level, IntPtr eventContext);

    [PreserveSig]
    int GetMasterVolumeLevel(out float levelDb);

    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float level);

    [PreserveSig]
    int SetChannelVolumeLevel(uint channel, float levelDb, IntPtr eventContext);

    [PreserveSig]
    int SetChannelVolumeLevelScalar(uint channel, float level, IntPtr eventContext);

    [PreserveSig]
    int GetChannelVolumeLevel(uint channel, out float levelDb);

    [PreserveSig]
    int GetChannelVolumeLevelScalar(uint channel, out float level);

    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr eventContext);

    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
}
