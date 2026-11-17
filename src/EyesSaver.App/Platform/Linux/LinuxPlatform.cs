using System.Drawing;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using EyesSaver.Core;
using EyesSaver.Core.Platform;

namespace EyesSaver.App.Platform.Linux;

/// <summary>
/// Linux (X11, and Wayland through XWayland). With a compositor the tint is a set of
/// override-redirect ARGB windows that ignore input. Without one, ARGB windows would paint
/// opaque, so the tint is applied through each monitor's gamma ramp instead, which gives the
/// same blend: out = in × (1 − opacity) + color × opacity.
/// </summary>
internal sealed unsafe class LinuxPlatform : IPlatformServices
{
    private const int XK_Home = 0xFF50;
    private const int XK_Prior = 0xFF55;
    private const int XK_Next = 0xFF56;

    private readonly nint _display = X11Session.Shared;
    private readonly X11EventThread _events = new();
    private readonly bool _wayland;
    private readonly Dictionary<string, X11Overlay> _overlays = new(StringComparer.Ordinal);
    private readonly Dictionary<nint, ushort[][]> _savedGamma = [];
    private readonly DispatcherTimer _restackTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private X11.XVisualInfo _visual;
    private nint _colormap;
    private bool _usingGamma;
    private bool _disposed;

    public LinuxPlatform()
    {
        _wayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase) ||
                   !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        _events.HotkeyPressed += action => Dispatcher.UIThread.Post(() => HotkeyPressed?.Invoke(this, action));
        _events.DisplaysChanged += () => Dispatcher.UIThread.Post(() => DisplaysChanged?.Invoke(this, EventArgs.Empty));
        _events.StackingChanged += () => Dispatcher.UIThread.Post(() =>
        {
            if (!_restackTimer.IsEnabled)
            {
                _restackTimer.Start();
            }
        });
        _restackTimer.Tick += (_, _) =>
        {
            _restackTimer.Stop();
            ReassertTopMost();
        };

        // The gamma ramp outlives the process, so put it back however the app ends.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreGamma();
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public event EventHandler? DisplaysChanged;

    public event EventHandler? SessionResumed
    {
        add { }
        remove { }
    }

    public event EventHandler? ExitRequested
    {
        add { }
        remove { }
    }

    public string StartupLabel => "Start when I log in";

    public bool SupportsCaptureExclusion => false;

    // Ctrl+Alt+F-keys switch virtual terminals on Linux, so the toggle uses Home instead of F9.
    public IReadOnlyList<HotkeyBinding> HotkeyBindings { get; } =
    [
        new(HotkeyAction.ToggleTint, XK_Home, "Ctrl+Alt+Home", "Pause or resume"),
        new(HotkeyAction.Stronger, XK_Prior, "Ctrl+Alt+PgUp", "Stronger"),
        new(HotkeyAction.Weaker, XK_Next, "Ctrl+Alt+PgDn", "Weaker")
    ];

    public IReadOnlyCollection<HotkeyAction> UnavailableHotkeys { get; private set; } = [];

    public string? HotkeyNote => _wayland
        ? $"On Wayland these work only while an X11 app has focus. For shortcuts everywhere, add custom shortcuts in your desktop settings that run: {CommandName} --toggle, --stronger, --weaker"
        : null;

    public string? TintProblem { get; private set; }

    public string TintMethod => _usingGamma
        ? "Display gamma (no compositor is running)"
        : _wayland ? "Overlay windows through XWayland" : "Overlay windows";

    /// <summary>How to run this app from a shell or a desktop shortcut.</summary>
    public static string CommandName
    {
        get
        {
            var flatpak = Environment.GetEnvironmentVariable("FLATPAK_ID");
            if (!string.IsNullOrEmpty(flatpak))
            {
                return $"flatpak run {flatpak}";
            }

            return File.Exists("/usr/bin/eyessaver") ? "eyessaver" : QuoteExec(Environment.ProcessPath ?? "EyesSaver");
        }
    }

    public void ApplyTint(IReadOnlyList<MonitorDescriptor> monitors, AppSettings settings)
    {
        if (_display == 0)
        {
            TintProblem = "No X11 display. Eyes Saver needs X11 or XWayland.";
            return;
        }

        var tints = PlatformServices.ActiveTints(monitors, settings).ToList();
        var composited = _wayland || X11.IsComposited(_display);
        TintProblem = null;

        if (composited)
        {
            if (_usingGamma)
            {
                RestoreGamma();
                _usingGamma = false;
            }

            ApplyOverlays(tints);
        }
        else
        {
            DestroyOverlays();
            _usingGamma = true;
            if (!ApplyGamma(tints))
            {
                TintProblem = "No compositor is running and this display does not allow gamma changes. Start a compositor (for example picom) to see the tint.";
            }
        }

        X11Session.SyncAndTakeError(_display);
    }

    public void ReassertTopMost()
    {
        if (_display == 0 || _overlays.Count == 0)
        {
            return;
        }

        foreach (var overlay in _overlays.Values)
        {
            X11.XRaiseWindow(_display, overlay.Window);
        }

        X11.XFlush(_display);
    }

    public void SetHotkeysEnabled(bool enabled)
    {
        UnavailableHotkeys = _events.SetHotkeys(enabled ? HotkeyBindings : null);
    }

    public bool IsStartupEnabled()
    {
        var path = AutostartPath();
        if (!File.Exists(path))
        {
            return false;
        }

        var text = File.ReadAllText(path);
        return !text.Contains("Hidden=true", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("X-GNOME-Autostart-enabled=false", StringComparison.OrdinalIgnoreCase);
    }

    public void SetStartupEnabled(bool enabled)
    {
        var path = AutostartPath();
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, AutostartEntry());
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void RefreshStartupEntry()
    {
        try
        {
            var path = AutostartPath();
            if (File.Exists(path) && IsStartupEnabled() && File.ReadAllText(path) != AutostartEntry())
            {
                File.WriteAllText(path, AutostartEntry());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void PrepareBadgeWindow(Window window)
    {
        window.Opened += (_, _) =>
        {
            if (_display != 0 && window.TryGetPlatformHandle()?.Handle is { } handle and not 0)
            {
                X11.MakeClickThrough(_display, handle);
                X11.XFlush(_display);
            }
        };
    }

    public Point? GetCursorPosition()
    {
        if (_display == 0)
        {
            return null;
        }

        var root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));
        return X11.XQueryPointer(_display, root, out _, out _, out var x, out var y, out _, out _, out _) != 0
            ? new Point(x, y)
            : null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _restackTimer.Stop();
        _events.Dispose();
        DestroyOverlays();
        RestoreGamma();
        if (_colormap != 0)
        {
            X11.XFreeColormap(_display, _colormap);
            _colormap = 0;
        }

        if (_display != 0)
        {
            X11.XSync(_display, 0);
        }
    }

    // ------------------------------------------------------------------ overlays

    private void ApplyOverlays(List<(MonitorDescriptor Monitor, Color Color, double Opacity)> tints)
    {
        if (_colormap == 0)
        {
            var screen = X11.XDefaultScreen(_display);
            if (X11.XMatchVisualInfo(_display, screen, 32, X11.TrueColor, out _visual) == 0)
            {
                TintProblem = "This X server has no 32-bit visual, so the tint cannot be transparent.";
                return;
            }

            _colormap = X11.XCreateColormap(_display, X11.XRootWindow(_display, screen), _visual.Visual, X11.AllocNone);
        }

        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (monitor, color, opacity) in tints)
        {
            active.Add(monitor.Key);
            var pixel = PremultipliedPixel(color, opacity);
            if (!_overlays.TryGetValue(monitor.Key, out var overlay))
            {
                overlay = CreateOverlay(monitor.Bounds, pixel);
                if (overlay is null)
                {
                    continue;
                }

                _overlays[monitor.Key] = overlay;
            }
            else
            {
                if (overlay.Pixel != pixel)
                {
                    overlay.Pixel = pixel;
                    X11.XSetWindowBackground(_display, overlay.Window, pixel);
                    X11.XClearWindow(_display, overlay.Window);
                }

                if (overlay.Bounds != monitor.Bounds)
                {
                    overlay.Bounds = monitor.Bounds;
                    X11.XMoveResizeWindow(_display, overlay.Window, monitor.Bounds.X, monitor.Bounds.Y, (uint)monitor.Bounds.Width, (uint)monitor.Bounds.Height);
                }
            }
        }

        foreach (var stale in _overlays.Keys.Where(key => !active.Contains(key)).ToList())
        {
            X11.XDestroyWindow(_display, _overlays[stale].Window);
            _overlays.Remove(stale);
        }

        _events.SetOwnWindows(_overlays.Values.Select(overlay => overlay.Window));
        X11.XFlush(_display);
    }

    private X11Overlay? CreateOverlay(Rectangle bounds, nint pixel)
    {
        var root = X11.XRootWindow(_display, X11.XDefaultScreen(_display));
        var attributes = new X11.XSetWindowAttributes
        {
            BackgroundPixel = pixel,
            BorderPixel = 0,
            OverrideRedirect = 1,
            Colormap = _colormap
        };

        var window = X11.XCreateWindow(
            _display, root, bounds.X, bounds.Y, (uint)Math.Max(1, bounds.Width), (uint)Math.Max(1, bounds.Height), 0,
            _visual.Depth, X11.InputOutput, _visual.Visual,
            X11.CWBackPixel | X11.CWBorderPixel | X11.CWOverrideRedirect | X11.CWColormap,
            ref attributes);
        if (window == 0)
        {
            return null;
        }

        X11.XStoreName(_display, window, "Eyes Saver overlay");
        SetWindowClass(window, "eyessaver-overlay", "EyesSaver");
        X11.SetAtomProperty(_display, window, "_NET_WM_WINDOW_TYPE", "_NET_WM_WINDOW_TYPE_NOTIFICATION");
        X11.SetCardinalProperty(_display, window, "_COMPTON_SHADOW", 0);
        X11.MakeClickThrough(_display, window);
        X11.XMapRaised(_display, window);
        return new X11Overlay(window, bounds, pixel);
    }

    private void DestroyOverlays()
    {
        if (_display == 0)
        {
            return;
        }

        foreach (var overlay in _overlays.Values)
        {
            X11.XDestroyWindow(_display, overlay.Window);
        }

        _overlays.Clear();
        _events.SetOwnWindows([]);
        X11.XFlush(_display);
    }

    private void SetWindowClass(nint window, string instance, string className)
    {
        var bytes = Encoding.ASCII.GetBytes($"{instance}\0{className}\0");
        fixed (byte* data = bytes)
        {
            X11.XChangeProperty(_display, window, X11.XInternAtom(_display, "WM_CLASS", 0), X11.XA_STRING, 8, X11.PropModeReplace, data, bytes.Length);
        }
    }

    private static nint PremultipliedPixel(Color color, double opacity)
    {
        var a = Math.Clamp(opacity, 0, 1);
        var alpha = (long)Math.Round(a * 255);
        var r = (long)Math.Round(color.R * a);
        var g = (long)Math.Round(color.G * a);
        var b = (long)Math.Round(color.B * a);
        return (nint)((alpha << 24) | (r << 16) | (g << 8) | b);
    }

    // ------------------------------------------------------------------ gamma

    private bool ApplyGamma(List<(MonitorDescriptor Monitor, Color Color, double Opacity)> tints)
    {
        var wanted = new Dictionary<nint, (Color Color, double Opacity)>();
        foreach (var (monitor, color, opacity) in tints)
        {
            var crtc = X11Monitors.FindCrtc(_display, monitor.DeviceName);
            if (crtc != 0)
            {
                wanted[crtc] = (color, opacity);
            }
        }

        var ok = true;
        foreach (var (crtc, tint) in wanted)
        {
            var size = X11.XRRGetCrtcGammaSize(_display, crtc);
            if (size <= 1)
            {
                ok = false;
                continue;
            }

            if (!_savedGamma.TryGetValue(crtc, out var original))
            {
                original = ReadGamma(crtc, size);
                _savedGamma[crtc] = original;
            }

            var ramp = X11.XRRAllocGamma(size);
            if (ramp == 0)
            {
                ok = false;
                continue;
            }

            var a = tint.Opacity;
            var targets = new[] { tint.Color.R / 255.0, tint.Color.G / 255.0, tint.Color.B / 255.0 };
            for (var channel = 0; channel < 3; channel++)
            {
                var values = *(ushort**)(ramp + 8 + (channel * 8));
                for (var i = 0; i < size; i++)
                {
                    var value = (original[channel][i] * (1 - a)) + (targets[channel] * 65535 * a);
                    values[i] = (ushort)Math.Clamp(Math.Round(value), 0, 65535);
                }
            }

            X11.XRRSetCrtcGamma(_display, crtc, ramp);
            X11.XRRFreeGamma(ramp);
        }

        foreach (var crtc in _savedGamma.Keys.Where(crtc => !wanted.ContainsKey(crtc)).ToList())
        {
            WriteGamma(crtc, _savedGamma[crtc]);
            _savedGamma.Remove(crtc);
        }

        X11.XFlush(_display);
        return ok || wanted.Count == 0;
    }

    private ushort[][] ReadGamma(nint crtc, int size)
    {
        var result = new[] { new ushort[size], new ushort[size], new ushort[size] };
        var gamma = X11.XRRGetCrtcGamma(_display, crtc);
        if (gamma == 0 || *(int*)gamma != size)
        {
            // Unknown current ramp: assume linear.
            for (var i = 0; i < size; i++)
            {
                result[0][i] = result[1][i] = result[2][i] = (ushort)(i * 65535 / (size - 1));
            }
        }
        else
        {
            for (var channel = 0; channel < 3; channel++)
            {
                var values = *(ushort**)(gamma + 8 + (channel * 8));
                for (var i = 0; i < size; i++)
                {
                    result[channel][i] = values[i];
                }
            }
        }

        if (gamma != 0)
        {
            X11.XRRFreeGamma(gamma);
        }

        return result;
    }

    private void WriteGamma(nint crtc, ushort[][] channels)
    {
        var size = channels[0].Length;
        var ramp = X11.XRRAllocGamma(size);
        if (ramp == 0)
        {
            return;
        }

        for (var channel = 0; channel < 3; channel++)
        {
            var values = *(ushort**)(ramp + 8 + (channel * 8));
            for (var i = 0; i < size; i++)
            {
                values[i] = channels[channel][i];
            }
        }

        X11.XRRSetCrtcGamma(_display, crtc, ramp);
        X11.XRRFreeGamma(ramp);
    }

    private void RestoreGamma()
    {
        if (_display == 0 || _savedGamma.Count == 0)
        {
            return;
        }

        foreach (var (crtc, channels) in _savedGamma)
        {
            WriteGamma(crtc, channels);
        }

        _savedGamma.Clear();
        X11.XSync(_display, 0);
    }

    // ------------------------------------------------------------------ autostart

    private static string AutostartPath()
    {
        // In a Flatpak XDG_CONFIG_HOME is the sandboxed one; autostart entries live in the real ~/.config.
        string config;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID")))
        {
            config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        else
        {
            config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg && Path.IsPathRooted(xdg)
                ? xdg
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(config, "autostart", "eyessaver.desktop");
    }

    private static string AutostartEntry()
    {
        return string.Join(
            "\n",
            "[Desktop Entry]",
            "Type=Application",
            "Name=Eyes Saver",
            "Comment=Screen tint for every display",
            $"Exec={CommandName} {CommandLine.TrayArgument}",
            $"Icon={AppInfo.LinuxAppId}",
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            string.Empty);
    }

    private static string QuoteExec(string path)
    {
        if (!path.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '\'' or '\\' or '$' or '`'))
        {
            return path;
        }

        var escaped = new StringBuilder();
        foreach (var ch in path)
        {
            if (ch is '"' or '`' or '$' or '\\')
            {
                escaped.Append('\\');
            }

            escaped.Append(ch);
        }

        return $"\"{escaped}\"";
    }

    private sealed class X11Overlay(nint window, Rectangle bounds, nint pixel)
    {
        public nint Window { get; } = window;

        public Rectangle Bounds { get; set; } = bounds;

        public nint Pixel { get; set; } = pixel;
    }
}
