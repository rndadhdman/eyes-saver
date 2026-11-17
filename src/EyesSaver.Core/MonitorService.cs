using System.Drawing;
using System.Globalization;
using EyesSaver.Core.Platform;

namespace EyesSaver.Core;

public sealed class MonitorService
{
    /// <summary>
    /// Reads the live monitor layout from the OS (Win32 on Windows, XRandR on Linux). Nothing is
    /// cached, so it is safe to call from a display-change notification.
    /// Monitors are ordered left to right, then top to bottom.
    /// </summary>
    public IReadOnlyList<MonitorDescriptor> GetMonitors()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return GetWindowsMonitors();
            }

            if (OperatingSystem.IsLinux())
            {
                return GetX11Monitors();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return [];
    }

    /// <summary>
    /// A cheap fingerprint of the live layout, polled to notice changes the OS did not announce.
    /// Compare with <see cref="QuickSignatureOf"/>.
    /// </summary>
    public string GetQuickSignature()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return Signature(Win32Display.EnumerateMonitors()
                    .Select(monitor => (monitor.DeviceName, monitor.Bounds, monitor.IsPrimary, monitor.Dpi)));
            }

            if (OperatingSystem.IsLinux())
            {
                return Signature(X11Monitors.Enumerate(X11Session.Shared, readEdid: false)
                    .Select(monitor => (monitor.DeviceName, monitor.Bounds, monitor.IsPrimary, monitor.Dpi)));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return string.Empty;
    }

    public static string QuickSignatureOf(IReadOnlyList<MonitorDescriptor> monitors)
    {
        return Signature(monitors.Select(monitor => (monitor.DeviceName, monitor.Bounds, monitor.IsPrimary, monitor.Dpi)));
    }

    internal static int? ParseDisplayNumber(string deviceName)
    {
        var digits = new string(deviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number > 0
            ? number
            : null;
    }

    /// <summary>The settings key for an X11 monitor: EDID serial if it has one, else model plus connector.</summary>
    internal static string X11StableId(string connector, EdidInfo? edid)
    {
        if (edid?.SerialKey is { } serial)
        {
            return $"x11:{serial}";
        }

        return edid is not null ? $"x11:{edid.ModelKey}@{connector}" : $"x11:{connector}";
    }

    private static string Signature(IEnumerable<(string DeviceName, Rectangle Bounds, bool IsPrimary, int Dpi)> monitors)
    {
        return string.Join(
            "|",
            monitors
                .OrderBy(monitor => monitor.Bounds.X)
                .ThenBy(monitor => monitor.Bounds.Y)
                .Select(monitor => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{monitor.DeviceName};{monitor.Bounds.X},{monitor.Bounds.Y},{monitor.Bounds.Width},{monitor.Bounds.Height};{monitor.IsPrimary};{monitor.Dpi}")));
    }

    private static List<MonitorDescriptor> GetWindowsMonitors()
    {
        var targets = Win32Display.QueryTargets();
        var ordered = Win32Display.EnumerateMonitors()
            .OrderBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .ToList();

        var result = new List<MonitorDescriptor>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var monitor = ordered[index];
            targets.TryGetValue(monitor.DeviceName, out var target);

            var number = ParseDisplayNumber(monitor.DeviceName) ?? index + 1;
            var friendly = target.FriendlyName?.Trim() ?? string.Empty;
            if (friendly.Length == 0 && target.IsInternal)
            {
                friendly = "Built-in display";
            }

            result.Add(new MonitorDescriptor(
                monitor.DeviceName,
                friendly.Length > 0 ? friendly : $"Display {number}",
                monitor.Bounds,
                monitor.IsPrimary,
                target.DevicePath ?? string.Empty,
                friendly,
                number,
                monitor.Dpi,
                monitor.WorkArea));
        }

        return result;
    }

    private static List<MonitorDescriptor> GetX11Monitors()
    {
        var display = X11Session.Shared;
        if (display == 0)
        {
            return [];
        }

        var ordered = X11Monitors.Enumerate(display, readEdid: true)
            .OrderBy(monitor => monitor.Bounds.X)
            .ThenBy(monitor => monitor.Bounds.Y)
            .ToList();

        var result = new List<MonitorDescriptor>(ordered.Count);
        for (var index = 0; index < ordered.Count; index++)
        {
            var monitor = ordered[index];
            var number = index + 1;
            var friendly = monitor.Edid?.MonitorName?.Trim() ?? string.Empty;
            if (friendly.Length == 0 && X11Monitors.IsInternalConnector(monitor.DeviceName))
            {
                friendly = "Built-in display";
            }

            result.Add(new MonitorDescriptor(
                monitor.DeviceName,
                friendly.Length > 0 ? friendly : $"Display {number}",
                monitor.Bounds,
                monitor.IsPrimary,
                X11StableId(monitor.DeviceName, monitor.Edid),
                friendly,
                number,
                monitor.Dpi,
                monitor.Bounds));
        }

        return result;
    }
}
