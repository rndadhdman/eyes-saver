namespace EyesSaver.Core;

public sealed class AppSettings
{
    public const int CurrentVersion = 2;

    /// <summary>How many disconnected monitors to remember settings for.</summary>
    public const int MaxRememberedMonitors = 16;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Master switch. When false no overlay is shown, but per-monitor settings are kept.</summary>
    public bool TintEnabled { get; set; } = true;

    /// <summary>Keep the tint out of screenshots, recordings and screen sharing.</summary>
    public bool ExcludeFromCapture { get; set; } = true;

    public bool HotkeysEnabled { get; set; } = true;

    /// <summary>Set once the user has been told that closing the window keeps the app in the tray.</summary>
    public bool TrayHintShown { get; set; }

    public List<ColorDto> CustomColors { get; set; } = [];

    /// <summary>Settings for the monitors that are connected right now, in display order.</summary>
    public List<MonitorTintSettings> Monitors { get; set; } = [];

    /// <summary>Settings for monitors that were connected before, restored when they come back.</summary>
    public List<MonitorTintSettings> RememberedMonitors { get; set; } = [];

    public MonitorTintSettings? FindMonitor(MonitorDescriptor monitor)
    {
        return Monitors.FirstOrDefault(candidate =>
            string.Equals(candidate.MonitorKey, monitor.Key, StringComparison.OrdinalIgnoreCase));
    }
}
