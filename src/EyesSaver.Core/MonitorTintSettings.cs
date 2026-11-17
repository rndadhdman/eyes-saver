namespace EyesSaver.Core;

public sealed class MonitorTintSettings
{
    public const double DefaultOpacity = 0.35;

    private double _opacity = DefaultOpacity;

    /// <summary>Stable monitor id (device path) or, for older settings files, the GDI device name.</summary>
    public string MonitorKey { get; set; } = string.Empty;

    /// <summary>GDI device name (\\.\DISPLAYn) last seen for this monitor.</summary>
    public string DeviceName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public RectangleDto Bounds { get; set; }

    public bool Enabled { get; set; } = true;

    public ColorDto Color { get; set; } = ColorDto.WarmDefault;

    public double Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, 0.0, 1.0);
    }

    public bool HasSameTint(MonitorTintSettings other)
    {
        return Enabled == other.Enabled &&
               Color == other.Color &&
               Math.Abs(Opacity - other.Opacity) < 0.0001;
    }

    public MonitorTintSettings CloneForMonitor(MonitorDescriptor monitor)
    {
        return new MonitorTintSettings
        {
            MonitorKey = monitor.Key,
            DeviceName = monitor.DeviceName,
            DisplayName = monitor.DisplayName,
            Bounds = RectangleDto.FromRectangle(monitor.Bounds),
            Enabled = Enabled,
            Color = Color,
            Opacity = Opacity
        };
    }

    public MonitorTintSettings Clone()
    {
        return new MonitorTintSettings
        {
            MonitorKey = MonitorKey,
            DeviceName = DeviceName,
            DisplayName = DisplayName,
            Bounds = Bounds,
            Enabled = Enabled,
            Color = Color,
            Opacity = Opacity
        };
    }

    public static MonitorTintSettings CreateDefault(MonitorDescriptor monitor)
    {
        return new MonitorTintSettings
        {
            MonitorKey = monitor.Key,
            DeviceName = monitor.DeviceName,
            DisplayName = monitor.DisplayName,
            Bounds = RectangleDto.FromRectangle(monitor.Bounds),
            Enabled = true,
            Color = ColorDto.WarmDefault,
            Opacity = DefaultOpacity
        };
    }
}
