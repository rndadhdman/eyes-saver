using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using EyesSaver.App.Platform;
using EyesSaver.Core;

namespace EyesSaver.App;

/// <summary>
/// Owns the app state: detected monitors, settings, tint, tray icon and shortcuts. The settings
/// window reads <see cref="Settings"/>, edits it in place and then calls <see cref="NotifySettingsEdited"/>.
/// </summary>
internal sealed class AppController
{
    private const double HotkeyStep = 0.05;

    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly MonitorService _monitorService = new();
    private readonly SettingsStore _settingsStore;
    private readonly IPlatformServices _platform;
    private readonly TrayIcon _tray;
    private readonly NativeMenuItem _pauseItem;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _watchdogTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private SettingsWindow? _window;
    private BadgeWindow? _toast;
    private List<BadgeWindow> _identifyWindows = [];
    private AppSettings _settings;
    private IReadOnlyList<MonitorDescriptor> _monitors;
    private bool _savePending;
    private bool _exiting;

    public AppController(IClassicDesktopStyleApplicationLifetime desktop, bool showSettings)
    {
        _desktop = desktop;
        _platform = PlatformServices.Create();

        _settingsStore = SettingsStore.CreateForApplication(AppContext.BaseDirectory);
        var firstRun = !_settingsStore.SettingsFileExists;
        _monitors = _monitorService.GetMonitors();
        _settings = _settingsStore.LoadOrCreateDefaults(_monitors);
        _platform.ApplyTint(_monitors, _settings);

        _platform.HotkeyPressed += (_, action) => OnHotkey(action);
        _platform.SetHotkeysEnabled(_settings.HotkeysEnabled);
        _platform.DisplaysChanged += (_, _) =>
        {
            // Rescan now so the tint follows right away, then once more after the layout settles;
            // docking and hot-plugging often arrive as several changes in a row.
            RescanMonitors();
            RestartSettleTimer();
        };
        _platform.SessionResumed += (_, _) => RestartSettleTimer();
        _platform.ExitRequested += (_, _) => ExitApplication();

        _pauseItem = new NativeMenuItem("Pause tint");
        _pauseItem.Click += (_, _) => SetTintEnabled(!_settings.TintEnabled, showToast: false);
        _tray = new TrayIcon
        {
            ToolTipText = "Eyes Saver",
            Menu = BuildMenu(),
            IsVisible = true
        };
        _tray.Clicked += (_, _) => ShowSettings();
        TrayIcon.SetIcons(Application.Current!, [_tray]);
        UpdateTray();

        _saveTimer.Tick += (_, _) => FlushSave();
        _settleTimer.Tick += (_, _) =>
        {
            _settleTimer.Stop();
            RescanMonitors();
        };
        _watchdogTimer.Tick += (_, _) => Watchdog();
        _watchdogTimer.Start();

        _platform.RefreshStartupEntry();

        if (showSettings || firstRun)
        {
            ShowSettings();
        }
    }

    /// <summary>Raised after tint values change from outside the settings window (shortcuts, tray, commands).</summary>
    public event EventHandler? StateChanged;

    /// <summary>Raised after the set or layout of monitors changes.</summary>
    public event EventHandler? MonitorsChanged;

    public IReadOnlyList<MonitorDescriptor> Monitors => _monitors;

    public AppSettings Settings => _settings;

    public string SettingsPath => _settingsStore.SettingsPath;

    public IPlatformServices Platform => _platform;

    /// <summary>What the tray is called on this OS, for messages.</summary>
    public static string TrayName => OperatingSystem.IsWindows() ? "notification area" : "system tray";

    public string HandleCommand(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.Show:
                ShowSettings();
                break;
            case AppCommand.Toggle:
                SetTintEnabled(!_settings.TintEnabled, showToast: true);
                break;
            case AppCommand.Pause:
                SetTintEnabled(false, showToast: true);
                break;
            case AppCommand.Resume:
                SetTintEnabled(true, showToast: true);
                break;
            case AppCommand.Stronger:
                AdjustIntensity(HotkeyStep);
                break;
            case AppCommand.Weaker:
                AdjustIntensity(-HotkeyStep);
                break;
            case AppCommand.Quit:
                Dispatcher.UIThread.Post(ExitApplication);
                break;
        }

        return "ok";
    }

    public void NotifySettingsEdited(object? source)
    {
        _platform.ApplyTint(_monitors, _settings);
        ScheduleSave();
        UpdateTray();
        if (source != _window)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetTintEnabled(bool enabled, bool showToast)
    {
        _settings.TintEnabled = enabled;
        _platform.ApplyTint(_monitors, _settings);
        ScheduleSave();
        UpdateTray();
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (showToast)
        {
            var toggle = _platform.HotkeyBindings.First(binding => binding.Action == HotkeyAction.ToggleTint).Label;
            ShowToast(enabled ? "Tint on" : "Tint paused", enabled ? DescribeIntensity() : $"{toggle} to resume");
        }
    }

    public void SetHotkeysEnabled(bool enabled)
    {
        _settings.HotkeysEnabled = enabled;
        _platform.SetHotkeysEnabled(enabled);
        ScheduleSave();
    }

    public void SetExcludeFromCapture(bool exclude)
    {
        _settings.ExcludeFromCapture = exclude;
        _platform.ApplyTint(_monitors, _settings);
        ScheduleSave();
    }

    public void IdentifyMonitors()
    {
        foreach (var window in _identifyWindows)
        {
            window.Close();
        }

        _identifyWindows = _monitors.Select(monitor => BadgeWindow.ShowIdentify(_platform, monitor)).ToList();
    }

    /// <summary>Re-reads the monitor layout and lines settings up with it.</summary>
    public void RescanMonitors()
    {
        if (_exiting)
        {
            return;
        }

        var previous = _monitors;
        _monitors = _monitorService.GetMonitors();
        _settings = SettingsReconciler.Reconcile(_settings, _monitors);
        _platform.ApplyTint(_monitors, _settings);
        ScheduleSave();
        UpdateTray();
        AnnounceChanges(previous, _monitors);
        MonitorsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _watchdogTimer.Stop();
        _settleTimer.Stop();
        FlushSave();

        _window?.Close();
        _toast?.Close();
        foreach (var window in _identifyWindows)
        {
            window.Close();
        }

        _tray.IsVisible = false;
        _tray.Dispose();
        _platform.Dispose();
        _desktop.Shutdown();
    }

    // ------------------------------------------------------------------ window and tray

    private NativeMenu BuildMenu()
    {
        var open = new NativeMenuItem("Open Eyes Saver");
        open.Click += (_, _) => ShowSettings();
        var identify = new NativeMenuItem("Identify displays");
        identify.Click += (_, _) => IdentifyMonitors();
        var rescan = new NativeMenuItem("Rescan displays");
        rescan.Click += (_, _) => RescanMonitors();
        var exit = new NativeMenuItem("Quit Eyes Saver");
        exit.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        foreach (var item in new NativeMenuItemBase[] { open, _pauseItem, new NativeMenuItemSeparator(), identify, rescan, new NativeMenuItemSeparator(), exit })
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private void ShowSettings()
    {
        if (_exiting)
        {
            return;
        }

        if (_window is null)
        {
            _window = new SettingsWindow(this);
            _window.Closed += SettingsWindow_Closed;
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        _window = null;
        FlushSave();

        if (!_exiting && !_settings.TrayHintShown)
        {
            _settings.TrayHintShown = true;
            ScheduleSave();
            ShowToast(
                "Eyes Saver is still running",
                $"The tint stays on. Open Eyes Saver from the {TrayName} or launch it again to change it.",
                milliseconds: 4500);
        }
    }

    private void UpdateTray()
    {
        var color = _settings.Monitors.FirstOrDefault(monitor => monitor.Enabled)?.Color.ToColor()
            ?? _settings.Monitors.FirstOrDefault()?.Color.ToColor()
            ?? ColorDto.WarmDefault.ToColor();
        var paused = !_settings.TintEnabled || _settings.Monitors.All(monitor => !monitor.Enabled);
        _tray.Icon = TrayIconRenderer.Create(color, paused);

        var enabledCount = _settings.Monitors.Count(monitor => monitor.Enabled);
        _tray.ToolTipText = !_settings.TintEnabled
            ? "Eyes Saver: paused"
            : $"Eyes Saver: {enabledCount} of {_monitors.Count} display{(_monitors.Count == 1 ? string.Empty : "s")} tinted";

        var toggle = _platform.HotkeyBindings.First(binding => binding.Action == HotkeyAction.ToggleTint).Label;
        _pauseItem.Header = _settings.TintEnabled ? $"Pause tint ({toggle})" : $"Resume tint ({toggle})";
    }

    // ------------------------------------------------------------------ shortcuts and toasts

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleTint:
                SetTintEnabled(!_settings.TintEnabled, showToast: true);
                break;
            case HotkeyAction.Stronger:
                AdjustIntensity(HotkeyStep);
                break;
            case HotkeyAction.Weaker:
                AdjustIntensity(-HotkeyStep);
                break;
        }
    }

    private void AdjustIntensity(double delta)
    {
        if (_settings.Monitors.Count == 0)
        {
            return;
        }

        var targets = _settings.Monitors.Where(monitor => monitor.Enabled).ToList();
        if (targets.Count == 0)
        {
            targets = _settings.Monitors;
            targets.ForEach(monitor => monitor.Enabled = true);
        }

        foreach (var monitor in targets)
        {
            monitor.Opacity = Math.Clamp(Math.Round((monitor.Opacity + delta) * 100) / 100, 0, TintPresets.MaxOpacity);
        }

        _settings.TintEnabled = true;
        _platform.ApplyTint(_monitors, _settings);
        ScheduleSave();
        UpdateTray();
        StateChanged?.Invoke(this, EventArgs.Empty);

        var labels = _platform.HotkeyBindings
            .Where(binding => binding.Action is HotkeyAction.Stronger or HotkeyAction.Weaker)
            .Select(binding => binding.Label.Replace("Ctrl+Alt+", string.Empty, StringComparison.Ordinal));
        ShowToast(DescribeIntensity(), $"Ctrl+Alt+{string.Join(" / ", labels)}");
    }

    private string DescribeIntensity()
    {
        var enabled = _settings.Monitors.Where(monitor => monitor.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return "All displays off";
        }

        var min = enabled.Min(monitor => monitor.Opacity);
        var max = enabled.Max(monitor => monitor.Opacity);
        return Math.Abs(max - min) < 0.005
            ? $"Tint {Math.Round(min * 100):0}%"
            : $"Tint {Math.Round(min * 100):0}–{Math.Round(max * 100):0}%";
    }

    private void ShowToast(string headline, string detail, int milliseconds = 1400)
    {
        _toast?.Close();
        _toast = BadgeWindow.ShowToast(_platform, _monitors, headline, detail, milliseconds);
    }

    private void AnnounceChanges(IReadOnlyList<MonitorDescriptor> previous, IReadOnlyList<MonitorDescriptor> current)
    {
        var previousKeys = previous.Select(monitor => monitor.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentKeys = current.Select(monitor => monitor.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = current.Where(monitor => !previousKeys.Contains(monitor.Key)).ToList();
        var removed = previous.Where(monitor => !currentKeys.Contains(monitor.Key)).ToList();

        if (added.Count == 0 && removed.Count == 0)
        {
            return;
        }

        string headline;
        string detail;
        if (added.Count > 0 && removed.Count == 0)
        {
            headline = added.Count == 1 ? $"{added[0].DisplayName} connected" : $"{added.Count} displays connected";
            detail = added.Count == 1 ? "Its tint is applied." : "Their tint is applied.";
        }
        else if (removed.Count > 0 && added.Count == 0)
        {
            headline = removed.Count == 1 ? $"{removed[0].DisplayName} disconnected" : $"{removed.Count} displays disconnected";
            detail = "Settings are kept for when it comes back.";
        }
        else
        {
            headline = "Display layout changed";
            detail = $"{current.Count} display{(current.Count == 1 ? string.Empty : "s")} now.";
        }

        ShowToast(headline, detail, milliseconds: 3000);
    }

    // ------------------------------------------------------------------ timers

    private void Watchdog()
    {
        // The OS does not always announce layout changes (for example a monitor waking from
        // standby on another input), so compare the live layout with what we know.
        var live = _monitorService.GetQuickSignature();
        if (live.Length > 0 && !string.Equals(live, MonitorService.QuickSignatureOf(_monitors), StringComparison.Ordinal))
        {
            RescanMonitors();
            return;
        }

        _platform.ReassertTopMost();
    }

    private void RestartSettleTimer()
    {
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private void ScheduleSave()
    {
        _savePending = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSave()
    {
        _saveTimer.Stop();
        if (!_savePending)
        {
            return;
        }

        try
        {
            _settingsStore.Save(_settings);
            _savePending = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep running with the in-memory settings; the next change retries.
        }
    }
}
