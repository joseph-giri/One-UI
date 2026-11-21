using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace OneUI.App.Native;

public sealed record BatteryStatus(bool HasBattery, int Percent, bool IsCharging);
public sealed record NetworkStatus(bool IsConnected, string Label);
public sealed record SystemTelemetry(int CpuPercent, int RamPercent, double UsedRamGb, double TotalRamGb);
public sealed record NetworkThroughput(double DownloadBytesPerSec, double UploadBytesPerSec);

/// <summary>
/// Provides live Windows 11 system telemetry (CoreAudio volume, battery, network, CPU/RAM)
/// and safe system power/media/utility actions.
/// </summary>
public sealed class SystemStatusService
{
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _hasCpuSample;
    private long _prevNetRecv;
    private long _prevNetSent;
    private DateTime _prevNetTime = DateTime.MinValue;

    public NetworkThroughput GetNetworkThroughput()
    {
        try
        {
            long totalRecv = 0;
            long totalSent = 0;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var stats = nic.GetIPv4Statistics();
                totalRecv += stats.BytesReceived;
                totalSent += stats.BytesSent;
            }

            var now = DateTime.UtcNow;
            if (_prevNetTime == DateTime.MinValue)
            {
                _prevNetRecv = totalRecv;
                _prevNetSent = totalSent;
                _prevNetTime = now;
                return new NetworkThroughput(0, 0);
            }

            double dt = Math.Max(0.2, (now - _prevNetTime).TotalSeconds);
            double down = Math.Max(0, (totalRecv - _prevNetRecv) / dt);
            double up = Math.Max(0, (totalSent - _prevNetSent) / dt);
            _prevNetRecv = totalRecv;
            _prevNetSent = totalSent;
            _prevNetTime = now;
            return new NetworkThroughput(down, up);
        }
        catch
        {
            return new NetworkThroughput(0, 0);
        }
    }

    public static string FormatSpeedCompact(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024)
            return $"{bytesPerSec / (1024 * 1024):0.0}M";
        if (bytesPerSec >= 1024)
            return $"{bytesPerSec / 1024:0}K";
        return $"{bytesPerSec:0}B";
    }
    public BatteryStatus GetBatteryStatus()
    {
        if (!Win32Api.GetSystemPowerStatus(out var status))
            return new BatteryStatus(false, 100, false);

        bool noBattery = (status.BatteryFlag & 128) != 0 || status.BatteryLifePercent == 255;
        bool charging = status.ACLineStatus == 1;
        return new BatteryStatus(!noBattery, noBattery ? 100 : status.BatteryLifePercent, charging);
    }

    public NetworkStatus GetNetworkStatus()
    {
        try
        {
            if (!NetworkInterface.GetIsNetworkAvailable())
                return new NetworkStatus(false, "Offline");

            var active = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet);

            if (active is null)
                return new NetworkStatus(true, "Connected");

            return active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                ? new NetworkStatus(true, "Wi-Fi")
                : new NetworkStatus(true, "Ethernet");
        }
        catch
        {
            return new NetworkStatus(true, "Network");
        }
    }

    public float GetMasterVolume()
    {
        try
        {
            var ep = GetAudioEndpointVolume();
            if (ep is null) return 0.5f;
            try
            {
                ep.GetMasterVolumeLevelScalar(out float level);
                return Math.Clamp(level, 0f, 1f);
            }
            finally
            {
                Marshal.ReleaseComObject(ep);
            }
        }
        catch
        {
            return 0.5f;
        }
    }

    public void SetMasterVolume(float scalar)
    {
        try
        {
            var ep = GetAudioEndpointVolume();
            if (ep is null) return;
            try
            {
                var guid = Guid.Empty;
                ep.SetMasterVolumeLevelScalar(Math.Clamp(scalar, 0f, 1f), ref guid);
            }
            finally
            {
                Marshal.ReleaseComObject(ep);
            }
        }
        catch { }
    }

    public bool GetIsMuted()
    {
        try
        {
            var ep = GetAudioEndpointVolume();
            if (ep is null) return false;
            try
            {
                ep.GetMute(out bool muted);
                return muted;
            }
            finally
            {
                Marshal.ReleaseComObject(ep);
            }
        }
        catch
        {
            return false;
        }
    }

    public void SetMute(bool mute)
    {
        try
        {
            var ep = GetAudioEndpointVolume();
            if (ep is null) return;
            try
            {
                var guid = Guid.Empty;
                ep.SetMute(mute, ref guid);
            }
            finally
            {
                Marshal.ReleaseComObject(ep);
            }
        }
        catch { }
    }

    public static void LockScreen() => Win32Api.LockWorkStation();

    public static void OpenStartMenu()
    {
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, 0, 0);
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, Win32Api.KEYEVENTF_KEYUP, 0);
    }

    public static void OpenTaskView()
    {
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, 0, 0);
        Win32Api.keybd_event(Win32Api.VK_TAB, 0, 0, 0);
        Win32Api.keybd_event(Win32Api.VK_TAB, 0, Win32Api.KEYEVENTF_KEYUP, 0);
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, Win32Api.KEYEVENTF_KEYUP, 0);
    }

    public static void OpenNotificationCenter()
    {
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, 0, 0);
        Win32Api.keybd_event(Win32Api.VK_N, 0, 0, 0);
        Win32Api.keybd_event(Win32Api.VK_N, 0, Win32Api.KEYEVENTF_KEYUP, 0);
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, Win32Api.KEYEVENTF_KEYUP, 0);
    }

    public static void ShowDesktop()
    {
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, 0, 0);
        Win32Api.keybd_event(Win32Api.VK_D, 0, 0, 0);
        Win32Api.keybd_event(Win32Api.VK_D, 0, Win32Api.KEYEVENTF_KEYUP, 0);
        Win32Api.keybd_event(Win32Api.VK_LWIN, 0, Win32Api.KEYEVENTF_KEYUP, 0);
    }

    public static void MediaPlayPause() => TapKey(Win32Api.VK_MEDIA_PLAY_PAUSE);
    public static void MediaNext() => TapKey(Win32Api.VK_MEDIA_NEXT_TRACK);
    public static void MediaPrevious() => TapKey(Win32Api.VK_MEDIA_PREV_TRACK);

    private static void TapKey(byte vk)
    {
        Win32Api.keybd_event(vk, 0, 0, 0);
        Win32Api.keybd_event(vk, 0, Win32Api.KEYEVENTF_KEYUP, 0);
    }

    public SystemTelemetry GetTelemetry()
    {
        int cpu = 0;
        if (Win32Api.GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
        {
            ulong idle = idleFt.ToUInt64();
            ulong kernel = kernelFt.ToUInt64();
            ulong user = userFt.ToUInt64();

            if (_hasCpuSample)
            {
                ulong dIdle = idle - _prevIdle;
                ulong dKernel = kernel - _prevKernel;
                ulong dUser = user - _prevUser;
                ulong total = dKernel + dUser;
                if (total > 0)
                {
                    cpu = (int)Math.Clamp(Math.Round((1.0 - (double)dIdle / total) * 100.0), 0, 100);
                }
            }

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasCpuSample = true;
        }

        int ramPct = 0;
        double usedGb = 0;
        double totalGb = 0;
        var mem = new Win32Api.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32Api.MEMORYSTATUSEX>() };
        if (Win32Api.GlobalMemoryStatusEx(ref mem))
        {
            ramPct = (int)mem.dwMemoryLoad;
            totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0);
        }

        return new SystemTelemetry(cpu, ramPct, Math.Round(usedGb, 1), Math.Round(totalGb, 1));
    }

    public static void SetLaunchAtStartup(bool enable)
    {
        try
        {
            const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
            if (key is null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue("OneUI", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("OneUI", throwOnMissingValue: false);
            }
        }
        catch { }
    }

    public static void SleepComputer() => Win32Api.SetSuspendState(false, false, false);

    public static void OpenRecycleBin() => AppDiscoveryService.Launch("explorer.exe", "shell:RecycleBinFolder");

    public static void OpenTaskManager() => AppDiscoveryService.Launch("taskmgr.exe");

    public static void OpenWindowsSettings(string page = "") => AppDiscoveryService.Launch($"ms-settings:{page}");

    public static void OpenSnippingTool() => AppDiscoveryService.Launch("ms-screenclip:");

    public static void RestartComputer() => RunShutdown("/r /t 0");

    public static void ShutdownComputer() => RunShutdown("/s /t 0");

    private static void RunShutdown(string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", args) { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    private static IAudioEndpointVolume? GetAudioEndpointVolume()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device);
            if (device is null) return null;
            try
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                device.Activate(ref iid, 23 /* CLSCTX_ALL */, nint.Zero, out var obj);
                return obj as IAudioEndpointVolume;
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator { }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out nint ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, nint pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(nint pNotify);
        int UnregisterControlChangeNotify(nint pNotify);
        int GetChannelCount(out uint pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
}
