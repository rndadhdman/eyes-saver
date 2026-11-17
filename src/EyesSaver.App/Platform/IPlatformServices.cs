using System.Drawing;
using Avalonia.Controls;
using EyesSaver.App.Platform.Linux;
using EyesSaver.App.Platform.Windows;
using EyesSaver.Core;

namespace EyesSaver.App.Platform;

internal enum HotkeyAction
{
    ToggleTint = 1,
    Stronger = 2,
    Weaker = 3
}

/// <param name="Key">Win32 virtual-key code on Windows, X keysym on Linux.</param>
internal sealed record HotkeyBinding(HotkeyAction Action, int Key, string Label, string Description);

/// <summary>The OS-specific half of the app: tint surfaces, global shortcuts, login start and system events.</summary>
internal interface IPlatformServices : IDisposable
{
    /// <summary>Label of the "start at sign-in" switch.</summary>
    string StartupLabel { get; }

    /// <summary>Whether the tint can be hidden from screenshots and screen sharing.</summary>
    bool SupportsCaptureExclusion { get; }

    IReadOnlyList<HotkeyBinding> HotkeyBindings { get; }

    /// <summary>Shortcuts that another app already owns, or that this session does not allow.</summary>
    IReadOnlyCollection<HotkeyAction> UnavailableHotkeys { get; }

    /// <summary>Extra advice for shortcuts, for example on Wayland. Null when there is nothing to say.</summary>
    string? HotkeyNote { get; }

    /// <summary>Why the tint cannot be shown right now, or null when it works.</summary>
    string? TintProblem { get; }

    /// <summary>Short description of how the tint is drawn, shown in the footer tooltip.</summary>
    string TintMethod { get; }

    event EventHandler<HotkeyAction>? HotkeyPressed;

    /// <summary>The display layout may have changed: rescan now and again once it settles.</summary>
    event EventHandler? DisplaysChanged;

    /// <summary>The session came back from sleep or lock: rescan once things settle.</summary>
    event EventHandler? SessionResumed;

    /// <summary>The OS or an installer asked the app to close.</summary>
    event EventHandler? ExitRequested;

    void ApplyTint(IReadOnlyList<MonitorDescriptor> monitors, AppSettings settings);

    /// <summary>Puts the tint back above windows that were raised over it.</summary>
    void ReassertTopMost();

    void SetHotkeysEnabled(bool enabled);

    bool IsStartupEnabled();

    void SetStartupEnabled(bool enabled);

    /// <summary>Points an existing start-at-sign-in entry at the current executable.</summary>
    void RefreshStartupEntry();

    /// <summary>Makes a short-lived label window float without taking focus (and click-through where possible).</summary>
    void PrepareBadgeWindow(Window window);

    Point? GetCursorPosition();
}

internal static class PlatformServices
{
    public static IPlatformServices Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPlatform();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxPlatform();
        }

        throw new PlatformNotSupportedException("Eyes Saver runs on Windows and Linux.");
    }

    /// <summary>Opacity and color each monitor should get right now, or nothing when it is untinted.</summary>
    public static IEnumerable<(MonitorDescriptor Monitor, Color Color, double Opacity)> ActiveTints(
        IReadOnlyList<MonitorDescriptor> monitors,
        AppSettings settings)
    {
        if (!settings.TintEnabled)
        {
            yield break;
        }

        foreach (var monitor in monitors)
        {
            var tint = settings.FindMonitor(monitor);
            if (tint is null || !tint.Enabled || tint.Opacity <= 0.001)
            {
                continue;
            }

            yield return (monitor, tint.Color.ToColor(), Math.Min(tint.Opacity, TintPresets.MaxOpacity));
        }
    }
}
