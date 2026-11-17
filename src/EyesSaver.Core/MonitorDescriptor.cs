using System.Drawing;

namespace EyesSaver.Core;

/// <summary>
/// One connected display. <see cref="Bounds"/> is in physical pixels on the virtual desktop.
/// </summary>
/// <param name="DeviceName">GDI name such as \\.\DISPLAY1. Can move between physical monitors.</param>
/// <param name="DisplayName">Label shown to the user.</param>
/// <param name="StableId">Monitor device path when Windows reports one; survives reconnects and reboots.</param>
/// <param name="FriendlyName">Model name from the monitor's EDID, for example "DELL U2720Q".</param>
/// <param name="Number">The number Windows uses for this display (DISPLAYn).</param>
public sealed record MonitorDescriptor(
    string DeviceName,
    string DisplayName,
    Rectangle Bounds,
    bool IsPrimary,
    string StableId = "",
    string FriendlyName = "",
    int Number = 0,
    int Dpi = 96,
    Rectangle WorkArea = default)
{
    /// <summary>The key settings are stored under.</summary>
    public string Key => string.IsNullOrEmpty(StableId) ? DeviceName : StableId;

    public int ScalePercent => (int)Math.Round(Dpi * 100.0 / 96.0);
}
