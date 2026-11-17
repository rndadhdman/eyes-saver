using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;

namespace EyesSaver.Core.Platform;

/// <summary>Monitor enumeration through XRandR 1.5 monitors, with names and ids from EDID.</summary>
public static class X11Monitors
{
    /// <summary>The CRTC that shows the named RandR monitor, or 0 if there is none.</summary>
    public static nint FindCrtc(nint display, string monitorName)
    {
        var monitor = Enumerate(display, readEdid: false)
            .FirstOrDefault(candidate => string.Equals(candidate.DeviceName, monitorName, StringComparison.Ordinal));
        return monitor.Output != 0 ? X11.CrtcForOutput(display, monitor.Output) : 0;
    }

    public readonly record struct RawMonitor(
        string DeviceName,
        Rectangle Bounds,
        bool IsPrimary,
        int Dpi,
        nint Output,
        EdidInfo? Edid);

    public static List<RawMonitor> Enumerate(nint display, bool readEdid)
    {
        var result = new List<RawMonitor>();
        if (display == 0)
        {
            return result;
        }

        var screen = X11.XDefaultScreen(display);
        var root = X11.XRootWindow(display, screen);
        var dpi = ReadXftDpi(display);

        if (X11.XRRQueryExtension(display, out _, out _) != 0 &&
            X11.XRRQueryVersion(display, out var major, out var minor) != 0 &&
            (major > 1 || (major == 1 && minor >= 5)))
        {
            var monitors = X11.XRRGetMonitors(display, root, 1, out var count);
            if (monitors != 0)
            {
                try
                {
                    var size = Marshal.SizeOf<X11.XRRMonitorInfo>();
                    for (var i = 0; i < count; i++)
                    {
                        var info = Marshal.PtrToStructure<X11.XRRMonitorInfo>(monitors + (i * size));
                        if (info.Width <= 0 || info.Height <= 0)
                        {
                            continue;
                        }

                        var output = info.NOutput > 0 && info.Outputs != 0 ? Marshal.ReadIntPtr(info.Outputs) : 0;
                        var name = X11.AtomName(display, info.Name) ?? $"Monitor{i + 1}";
                        result.Add(new RawMonitor(
                            name,
                            new Rectangle(info.X, info.Y, info.Width, info.Height),
                            info.Primary != 0,
                            dpi,
                            output,
                            readEdid && output != 0 ? ReadEdid(display, output) : null));
                    }
                }
                finally
                {
                    X11.XRRFreeMonitors(monitors);
                }
            }
        }

        if (result.Count == 0)
        {
            // No RandR 1.5: treat the whole X screen as one monitor.
            result.Add(new RawMonitor(
                "Screen",
                new Rectangle(0, 0, X11.XDisplayWidth(display, screen), X11.XDisplayHeight(display, screen)),
                true,
                dpi,
                0,
                null));
        }

        if (!result.Any(monitor => monitor.IsPrimary))
        {
            var first = result.OrderBy(monitor => monitor.Bounds.X).ThenBy(monitor => monitor.Bounds.Y).First();
            result[result.IndexOf(first)] = first with { IsPrimary = true };
        }

        return result;
    }

    public static bool IsInternalConnector(string name)
    {
        return name.StartsWith("eDP", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("LVDS", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("DSI", StringComparison.OrdinalIgnoreCase);
    }

    private static EdidInfo? ReadEdid(nint display, nint output)
    {
        var atom = X11.XInternAtom(display, "EDID", 1);
        if (atom == 0)
        {
            return null;
        }

        // Length is in 32-bit units: 128 * 4 = 512 bytes, enough for the base block and extensions.
        var status = X11.XRRGetOutputProperty(
            display, output, atom, 0, 128, 0, 0, 0,
            out _, out var format, out var itemCount, out _, out var data);
        if (status != X11.Success || data == 0)
        {
            return null;
        }

        try
        {
            if (format != 8 || itemCount < 128)
            {
                return null;
            }

            var bytes = new byte[(int)itemCount];
            Marshal.Copy(data, bytes, 0, bytes.Length);
            return Edid.Parse(bytes);
        }
        finally
        {
            X11.XFree(data);
        }
    }

    /// <summary>X has one scale for every monitor; desktops publish it as Xft.dpi.</summary>
    private static int ReadXftDpi(nint display)
    {
        var value = X11.XGetDefault(display, "Xft", "dpi");
        if (value != 0 &&
            double.TryParse(Marshal.PtrToStringUTF8(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) &&
            dpi >= 48 && dpi <= 480)
        {
            return (int)Math.Round(dpi);
        }

        return 96;
    }
}
