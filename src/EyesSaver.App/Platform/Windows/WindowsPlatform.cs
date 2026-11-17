using System.Drawing;
using Avalonia.Controls;
using EyesSaver.Core;
using Microsoft.Win32;
using static EyesSaver.App.Platform.Windows.Win32;

namespace EyesSaver.App.Platform.Windows;

/// <summary>
/// Tint overlays are plain Win32 layered windows: click-through, topmost, never activated and,
/// when asked, excluded from screen capture. A hidden window receives hotkeys and the display,
/// power and session notifications. Avalonia's message loop dispatches to both.
/// </summary>
internal sealed class WindowsPlatform : IPlatformServices
{
    private const string OverlayClass = "EyesSaver.Overlay";
    private const string HostClass = "EyesSaver.Host";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "EyesSaver";

    private static readonly WndProc Procedure = WindowProcedure;
    private static WindowsPlatform? _instance;

    private readonly Dictionary<string, Overlay> _overlays = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<nint, Overlay> _overlaysByHandle = [];
    private readonly HashSet<HotkeyAction> _registered = [];
    private readonly nint _instanceHandle = GetModuleHandle(null);
    private readonly nint _host;

    public WindowsPlatform()
    {
        _instance = this;
        RegisterClass(OverlayClass);
        RegisterClass(HostClass);

        // A hidden top-level window (not message-only) so it also gets broadcasts such as
        // WM_DISPLAYCHANGE and the WM_CLOSE that installers send before replacing files.
        _host = CreateWindowEx(WS_EX_TOOLWINDOW, HostClass, "Eyes Saver", WS_POPUP, 0, 0, 0, 0, 0, 0, _instanceHandle, 0);
        if (_host != 0)
        {
            WTSRegisterSessionNotification(_host, NOTIFY_FOR_THIS_SESSION);
        }
    }

    public event EventHandler<HotkeyAction>? HotkeyPressed;

    public event EventHandler? DisplaysChanged;

    public event EventHandler? SessionResumed;

    public event EventHandler? ExitRequested;

    public string StartupLabel => "Start with Windows";

    public bool SupportsCaptureExclusion => true;

    public IReadOnlyList<HotkeyBinding> HotkeyBindings { get; } =
    [
        new(HotkeyAction.ToggleTint, VK_F9, "Ctrl+Alt+F9", "Pause or resume"),
        new(HotkeyAction.Stronger, VK_PRIOR, "Ctrl+Alt+PgUp", "Stronger"),
        new(HotkeyAction.Weaker, VK_NEXT, "Ctrl+Alt+PgDn", "Weaker")
    ];

    public IReadOnlyCollection<HotkeyAction> UnavailableHotkeys { get; private set; } = [];

    public string? HotkeyNote => null;

    public string? TintProblem => null;

    public string TintMethod => "Click-through overlay windows";

    public void ApplyTint(IReadOnlyList<MonitorDescriptor> monitors, AppSettings settings)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (monitor, color, opacity) in PlatformServices.ActiveTints(monitors, settings))
        {
            active.Add(monitor.Key);
            if (!_overlays.TryGetValue(monitor.Key, out var overlay))
            {
                overlay = Overlay.Create(_instanceHandle);
                if (overlay is null)
                {
                    continue;
                }

                _overlays[monitor.Key] = overlay;
                _overlaysByHandle[overlay.Handle] = overlay;
            }

            overlay.Update(monitor.Bounds, color, opacity, settings.ExcludeFromCapture);
        }

        foreach (var stale in _overlays.Keys.Where(key => !active.Contains(key)).ToList())
        {
            var overlay = _overlays[stale];
            _overlays.Remove(stale);
            _overlaysByHandle.Remove(overlay.Handle);
            overlay.Destroy();
        }
    }

    public void ReassertTopMost()
    {
        foreach (var overlay in _overlays.Values)
        {
            SetTopMost(overlay.Handle);
        }
    }

    public void SetHotkeysEnabled(bool enabled)
    {
        foreach (var action in _registered)
        {
            UnregisterHotKey(_host, (int)action);
        }

        _registered.Clear();
        if (!enabled || _host == 0)
        {
            UnavailableHotkeys = [];
            return;
        }

        var unavailable = new List<HotkeyAction>();
        foreach (var binding in HotkeyBindings)
        {
            if (RegisterHotKey(_host, (int)binding.Action, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)binding.Key))
            {
                _registered.Add(binding.Action);
            }
            else
            {
                unavailable.Add(binding.Action);
            }
        }

        UnavailableHotkeys = unavailable;
    }

    public bool IsStartupEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string value && value.Length > 0;
    }

    public void SetStartupEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(RunValueName, StartupCommand());
        }
        else if (key.GetValue(RunValueName) is not null)
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    public void RefreshStartupEntry()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(RunValueName) is string value && !string.Equals(value, StartupCommand(), StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(RunValueName, StartupCommand());
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
        }
    }

    public void PrepareBadgeWindow(Window window)
    {
        window.Opened += (_, _) =>
        {
            if (window.TryGetPlatformHandle()?.Handle is { } handle and not 0)
            {
                var style = GetWindowLongPtr(handle, GWL_EXSTYLE);
                SetWindowLongPtr(handle, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
                SetTopMost(handle);
            }
        };
    }

    public Point? GetCursorPosition()
    {
        return GetCursorPos(out var point) ? new Point(point.X, point.Y) : null;
    }

    public void Dispose()
    {
        SetHotkeysEnabled(false);
        foreach (var overlay in _overlays.Values)
        {
            overlay.Destroy();
        }

        _overlays.Clear();
        _overlaysByHandle.Clear();
        if (_host != 0)
        {
            WTSUnRegisterSessionNotification(_host);
            DestroyWindow(_host);
        }

        _instance = null;
    }

    private static string StartupCommand()
    {
        return $"\"{Environment.ProcessPath}\" {CommandLine.TrayArgument}";
    }

    private void RegisterClass(string name)
    {
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(Procedure),
            hInstance = _instanceHandle,
            lpszClassName = name
        };
        RegisterClassEx(ref windowClass);
    }

    private static nint WindowProcedure(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        var platform = _instance;
        if (platform is null)
        {
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        if (platform._overlaysByHandle.TryGetValue(hwnd, out var overlay))
        {
            switch (msg)
            {
                case WM_ERASEBKGND:
                    return 1;
                case WM_PAINT:
                    overlay.Paint();
                    return 0;
                case WM_NCHITTEST:
                    return HTTRANSPARENT;
                case WM_DPICHANGED:
                    // The overlay must match its monitor exactly, so ignore the suggested rescale.
                    overlay.ApplyBounds();
                    return 0;
                case WM_CLOSE:
                    platform.ExitRequested?.Invoke(platform, EventArgs.Empty);
                    return 0;
            }
        }
        else if (hwnd == platform._host)
        {
            switch (msg)
            {
                case WM_HOTKEY:
                    var action = (HotkeyAction)(int)wParam;
                    if (platform._registered.Contains(action))
                    {
                        platform.HotkeyPressed?.Invoke(platform, action);
                    }

                    return 0;
                case WM_DISPLAYCHANGE:
                    platform.DisplaysChanged?.Invoke(platform, EventArgs.Empty);
                    break;
                case WM_POWERBROADCAST when (int)wParam is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND:
                    platform.SessionResumed?.Invoke(platform, EventArgs.Empty);
                    return 1;
                case WM_WTSSESSION_CHANGE when (int)wParam is WTS_SESSION_UNLOCK or WTS_CONSOLE_CONNECT or WTS_REMOTE_CONNECT:
                    platform.SessionResumed?.Invoke(platform, EventArgs.Empty);
                    return 0;
                case WM_QUERYENDSESSION:
                    return 1;
                case WM_ENDSESSION when wParam != 0:
                case WM_CLOSE:
                    platform.ExitRequested?.Invoke(platform, EventArgs.Empty);
                    return 0;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private sealed class Overlay
    {
        private Rectangle _bounds;
        private nint _brush;
        private Color _color;
        private byte _alpha = 255;
        private bool? _excluded;

        private Overlay(nint handle)
        {
            Handle = handle;
        }

        public nint Handle { get; }

        public static Overlay? Create(nint instance)
        {
            var handle = CreateWindowEx(
                WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
                OverlayClass, "Eyes Saver overlay", WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
            return handle == 0 ? null : new Overlay(handle);
        }

        public void Update(Rectangle bounds, Color color, double opacity, bool excludeFromCapture)
        {
            var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
            var firstShow = _brush == 0;

            if (firstShow || color.ToArgb() != _color.ToArgb())
            {
                _color = color;
                var previous = _brush;
                _brush = CreateSolidBrush((uint)(color.R | (color.G << 8) | (color.B << 16)));
                if (previous != 0)
                {
                    DeleteObject(previous);
                }

                InvalidateRect(Handle, 0, true);
            }

            if (firstShow || alpha != _alpha)
            {
                _alpha = alpha;
                SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
            }

            if (_excluded != excludeFromCapture)
            {
                _excluded = excludeFromCapture;
                SetWindowDisplayAffinity(Handle, excludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            }

            if (firstShow || bounds != _bounds)
            {
                _bounds = bounds;
                ApplyBounds();
            }

            if (firstShow)
            {
                ShowWindow(Handle, SW_SHOWNOACTIVATE);
                ApplyBounds();
            }
        }

        public void ApplyBounds()
        {
            if (_bounds.Width > 0 && _bounds.Height > 0)
            {
                SetWindowPos(Handle, HWND_TOPMOST, _bounds.X, _bounds.Y, _bounds.Width, _bounds.Height, SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            }
        }

        public void Paint()
        {
            var hdc = BeginPaint(Handle, out var paint);
            if (hdc != 0 && _brush != 0)
            {
                FillRect(hdc, ref paint.rcPaint, _brush);
            }

            EndPaint(Handle, ref paint);
        }

        public void Destroy()
        {
            DestroyWindow(Handle);
            if (_brush != 0)
            {
                DeleteObject(_brush);
                _brush = 0;
            }
        }
    }
}
