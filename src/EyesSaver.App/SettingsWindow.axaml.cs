using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using EyesSaver.App.Platform;
using EyesSaver.Core;
using Path = Avalonia.Controls.Shapes.Path;

namespace EyesSaver.App;

internal sealed partial class SettingsWindow : Window
{
    private readonly AppController _app;
    private readonly List<(Button Button, string? Key, Ellipse? Dot)> _chips = [];
    private readonly List<(Button Button, TintPreset Preset)> _presetSwatches = [];
    private readonly Button _customSwatch;
    private readonly Ellipse _customFill;
    private readonly TextBlock _customPlus;
    private readonly ColorView _colorView;
    private string? _selectedKey;
    private bool _refreshing;
    private bool _pickerOpen;

    public SettingsWindow(AppController app)
    {
        _app = app;
        InitializeComponent();

        // Color swatches: the presets, then one that opens a color picker.
        foreach (var preset in TintPresets.All)
        {
            var swatch = MakeSwatch(new SolidColorBrush(ToAvalonia(preset.Color)), out _);
            swatch.Click += (_, _) => EditTargets(tint => tint.Color = preset.Color, wakeUp: true);
            ToolTip.SetTip(swatch, $"{preset.Name}: {preset.Description}");
            _presetSwatches.Add((swatch, preset));
            Swatches.Children.Add(swatch);
        }

        _customSwatch = MakeSwatch(Brushes.Transparent, out _customFill);
        _customFill.Stroke = this.FindResource("Es.TextMuted") as IBrush;
        _customFill.StrokeThickness = 1.5;
        _customFill.StrokeDashArray = [3, 2];
        _customPlus = new TextBlock
        {
            Text = "+",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -2, 0, 0),
            IsHitTestVisible = false
        };
        ((Grid)_customSwatch.Content!).Children.Add(_customPlus);
        ToolTip.SetTip(_customSwatch, "Pick any color");

        _colorView = new ColorView
        {
            IsAlphaEnabled = false,
            IsAlphaVisible = false,
            IsAccentColorsVisible = false,
            Width = 340,
            Height = 360
        };
        _colorView.ColorChanged += (_, e) =>
        {
            // ColorView also raises this while it initializes; only react to the user's picks.
            if (!_refreshing && _pickerOpen)
            {
                var chosen = new ColorDto(255, e.NewColor.R, e.NewColor.G, e.NewColor.B);
                EditTargets(tint => tint.Color = chosen, wakeUp: true);
            }
        };
        var flyout = new Flyout { Content = _colorView, Placement = PlacementMode.Bottom };
        flyout.Opening += (_, _) =>
        {
            _refreshing = true;
            var first = Targets().FirstOrDefault()?.Color ?? ColorDto.WarmDefault;
            _colorView.Color = ToAvalonia(first);
            _refreshing = false;
        };
        flyout.Opened += (_, _) => _pickerOpen = true;
        flyout.Closed += (_, _) => _pickerOpen = false;
        _customSwatch.Flyout = flyout;
        Swatches.Children.Add(_customSwatch);

        // Header and options
        MasterSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _app.SetTintEnabled(MasterSwitch.IsChecked == true, showToast: false);
                RefreshAll();
            }
        };
        ToolTip.SetTip(MasterSwitch, $"Turn the tint on or off on every display ({ToggleLabel()})");

        EnabledSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                var on = EnabledSwitch.IsChecked == true;
                EditTargets(tint => tint.Enabled = on, wakeUp: on);
            }
        };

        IntensitySlider.ValueChanged += (_, e) =>
        {
            if (!_refreshing)
            {
                var value = Math.Round(e.NewValue) / 100.0;
                EditTargets(tint => tint.Opacity = value, wakeUp: true);
            }
        };

        IdentifyButton.Click += (_, _) => _app.IdentifyMonitors();
        RescanButton.Click += (_, _) => _app.RescanMonitors();
        QuitButton.Click += (_, _) => _app.ExitApplication();
        Map.MonitorClicked += (_, key) => SelectTarget(key);

        var platform = _app.Platform;
        StartupSwitch.OnContent = platform.StartupLabel;
        StartupSwitch.OffContent = platform.StartupLabel;
        ToolTip.SetTip(StartupSwitch, $"Start in the {AppController.TrayName} when you sign in");
        StartupSwitch.IsCheckedChanged += (_, _) =>
        {
            if (_refreshing)
            {
                return;
            }

            try
            {
                platform.SetStartupEnabled(StartupSwitch.IsChecked == true);
                FooterText.Text = FooterMessage();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                FooterText.Text = $"Could not change the startup setting: {ex.Message}";
            }

            RefreshOptions();
        };

        CaptureSwitch.IsVisible = platform.SupportsCaptureExclusion;
        CaptureSwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _app.SetExcludeFromCapture(CaptureSwitch.IsChecked == true);
            }
        };

        HotkeySwitch.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
            {
                _app.SetHotkeysEnabled(HotkeySwitch.IsChecked == true);
                RefreshOptions();
            }
        };

        FooterText.Text = FooterMessage();
        ToolTip.SetTip(FooterText, $"Settings file: {_app.SettingsPath}\nTint method: {platform.TintMethod}\nVersion {AppInfo.Version}");

        _app.StateChanged += App_StateChanged;
        _app.MonitorsChanged += App_MonitorsChanged;
        Closed += (_, _) =>
        {
            _app.StateChanged -= App_StateChanged;
            _app.MonitorsChanged -= App_MonitorsChanged;
        };

        RebuildChips();
        RefreshAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !e.Handled)
        {
            Close();
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------------ building

    private static Button MakeSwatch(IBrush fill, out Ellipse ellipse)
    {
        ellipse = new Ellipse { Fill = fill, Stroke = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), StrokeThickness = 1 };
        var check = new Path
        {
            Data = Geometry.Parse("M 0,5 L 4,9 L 12,1"),
            Stroke = Brushes.White,
            StrokeThickness = 2.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = false,
            IsHitTestVisible = false,
            Name = "Check"
        };
        var button = new Button { Content = new Grid { Children = { ellipse, check } } };
        button.Classes.Add("swatch");
        return button;
    }

    private void RebuildChips()
    {
        if (_selectedKey is not null && _app.Monitors.All(monitor => !string.Equals(monitor.Key, _selectedKey, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedKey = null;
        }

        Chips.Children.Clear();
        _chips.Clear();

        var all = new Button { Content = "All displays" };
        all.Classes.Add("chip");
        all.Click += (_, _) => SelectTarget(null);
        Chips.Children.Add(all);
        _chips.Add((all, null, null));

        foreach (var monitor in _app.Monitors)
        {
            var dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = monitor.Number.ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center });

            var chip = new Button { Content = content };
            chip.Classes.Add("chip");
            var key = monitor.Key;
            chip.Click += (_, _) => SelectTarget(key);
            ToolTip.SetTip(chip, monitor.IsPrimary ? $"{monitor.DisplayName} (primary)" : monitor.DisplayName);
            Chips.Children.Add(chip);
            _chips.Add((chip, key, dot));
        }
    }

    // ------------------------------------------------------------------ state

    private void SelectTarget(string? key)
    {
        _selectedKey = key;
        RefreshAll();
    }

    private List<MonitorTintSettings> Targets()
    {
        var settings = _app.Settings;
        if (_selectedKey is null)
        {
            return settings.Monitors;
        }

        return settings.Monitors
            .Where(monitor => string.Equals(monitor.MonitorKey, _selectedKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Applies a change to the monitors being edited. Changing the look of a tint that is
    /// switched off turns it on, since the change could not be seen otherwise.
    /// </summary>
    private void EditTargets(Action<MonitorTintSettings> change, bool wakeUp)
    {
        var targets = Targets();
        if (targets.Count == 0)
        {
            return;
        }

        foreach (var tint in targets)
        {
            change(tint);
            if (wakeUp)
            {
                tint.Enabled = true;
            }
        }

        if (wakeUp)
        {
            _app.Settings.TintEnabled = true;
        }

        _app.NotifySettingsEdited(this);
        RefreshAll();
    }

    private void App_StateChanged(object? sender, EventArgs e) => RefreshAll();

    private void App_MonitorsChanged(object? sender, EventArgs e)
    {
        RebuildChips();
        RefreshAll();
    }

    private void RefreshAll()
    {
        _refreshing = true;
        try
        {
            RefreshHeader();
            RefreshDisplays();
            RefreshEditor();
            RefreshOptions();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshHeader()
    {
        var settings = _app.Settings;
        var monitors = _app.Monitors;
        var enabledCount = settings.Monitors.Count(monitor => monitor.Enabled);

        MasterSwitch.IsChecked = settings.TintEnabled;

        var problem = _app.Platform.TintProblem;
        StatusText.Classes.Set("warning", problem is not null);
        StatusText.Text = problem ?? (monitors.Count == 0
            ? "No displays detected"
            : !settings.TintEnabled
                ? $"Paused on every display. Turn the switch on or press {ToggleLabel()} to resume."
                : enabledCount == 0
                    ? "Every display is switched off below."
                    : $"Tinting {enabledCount} of {monitors.Count} display{(monitors.Count == 1 ? string.Empty : "s")}");
    }

    private void RefreshDisplays()
    {
        var monitors = _app.Monitors;
        DisplaysSubtitle.Text = monitors.Count switch
        {
            0 => "No displays found. Try Rescan.",
            1 => "1 display found. Connecting another one is picked up automatically.",
            _ => $"{monitors.Count} displays found. Plugging displays in or out is picked up automatically."
        };
        Map.SetData(monitors, _app.Settings, _selectedKey);
    }

    private void RefreshEditor()
    {
        var targets = Targets();
        var hasTargets = targets.Count > 0;
        EnabledSwitch.IsEnabled = hasTargets;
        IntensitySlider.IsEnabled = hasTargets;
        Swatches.IsEnabled = hasTargets;

        var trackOff = this.FindResource("Es.TrackOff") as IBrush ?? Brushes.Gray;
        foreach (var (button, key, dot) in _chips)
        {
            button.Classes.Set("selected", string.Equals(key, _selectedKey, StringComparison.OrdinalIgnoreCase));
            if (key is not null && dot is not null)
            {
                var tint = _app.Settings.Monitors.FirstOrDefault(monitor => string.Equals(monitor.MonitorKey, key, StringComparison.OrdinalIgnoreCase));
                dot.Fill = tint is { Enabled: true } ? new SolidColorBrush(ToAvalonia(tint.Color)) : trackOff;
            }
        }

        if (!hasTargets)
        {
            TargetName.Text = "No displays";
            TargetDetail.Text = string.Empty;
            IntensityValue.Text = string.Empty;
            ColorDescription.Text = string.Empty;
            return;
        }

        var monitor = _selectedKey is null
            ? null
            : _app.Monitors.FirstOrDefault(candidate => string.Equals(candidate.Key, _selectedKey, StringComparison.OrdinalIgnoreCase));

        var first = targets[0];
        var allEnabled = targets.All(tint => tint.Enabled);
        var noneEnabled = targets.All(tint => !tint.Enabled);
        var sameOpacity = targets.All(tint => Math.Abs(tint.Opacity - first.Opacity) < 0.0001);
        var sameColor = targets.All(tint => tint.Color == first.Color);

        string switchText;
        if (monitor is not null)
        {
            TargetName.Text = $"{monitor.Number}  ·  {monitor.DisplayName}";
            var parts = new List<string> { $"{monitor.Bounds.Width} × {monitor.Bounds.Height}" };
            if (OperatingSystem.IsWindows() || monitor.Dpi != 96)
            {
                parts.Add($"{monitor.ScalePercent}% scale");
            }

            if (!OperatingSystem.IsWindows())
            {
                parts.Add(monitor.DeviceName);
            }

            if (monitor.IsPrimary)
            {
                parts.Add("primary");
            }

            TargetDetail.Text = string.Join("  ·  ", parts);
            switchText = "Tint this display";
        }
        else
        {
            TargetName.Text = "All displays";
            TargetDetail.Text = sameOpacity && sameColor && (allEnabled || noneEnabled)
                ? "Every display uses the same settings"
                : "Displays differ. A change here applies to all of them.";
            switchText = !allEnabled && !noneEnabled ? "Tint every display (some are off)" : "Tint every display";
        }

        EnabledSwitch.OnContent = switchText;
        EnabledSwitch.OffContent = switchText;
        EnabledSwitch.IsChecked = allEnabled;

        var opacity = sameOpacity ? first.Opacity : targets.Average(tint => tint.Opacity);
        var percent = Math.Round(Math.Min(opacity, TintPresets.MaxOpacity) * 100);
        IntensitySlider.Value = percent;
        IntensityValue.Text = sameOpacity ? $"{percent:0}%" : $"mixed, avg {percent:0}%";

        var color = sameColor ? first.Color : (ColorDto?)null;
        var preset = color is { } c ? TintPresets.Find(c) : null;
        foreach (var (button, swatchPreset) in _presetSwatches)
        {
            SetSwatchSelected(button, preset is not null && swatchPreset == preset);
        }

        var isCustom = color is not null && preset is null;
        SetSwatchSelected(_customSwatch, isCustom);
        _customPlus.IsVisible = !isCustom;
        _customFill.Fill = isCustom ? new SolidColorBrush(ToAvalonia(color!.Value)) : Brushes.Transparent;
        _customFill.StrokeDashArray = isCustom ? null : [3, 2];

        ColorDescription.Text = color is null
            ? "Displays use different colors. Pick one to use it on all of them."
            : preset is not null
                ? $"{preset.Name}: {preset.Description}"
                : $"Custom color #{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}";
    }

    private void RefreshOptions()
    {
        var settings = _app.Settings;
        var platform = _app.Platform;
        StartupSwitch.IsChecked = SafeStartupState();
        CaptureSwitch.IsChecked = settings.ExcludeFromCapture;
        HotkeySwitch.IsChecked = settings.HotkeysEnabled;

        var bindings = platform.HotkeyBindings;
        var toggle = bindings.First(binding => binding.Action == HotkeyAction.ToggleTint);
        var stronger = bindings.First(binding => binding.Action == HotkeyAction.Stronger).Label;
        var weaker = bindings.First(binding => binding.Action == HotkeyAction.Weaker).Label.Replace("Ctrl+Alt+", string.Empty, StringComparison.Ordinal);
        var summary = $"{toggle.Label}  {toggle.Description.ToLowerInvariant()}   ·   {stronger} / {weaker}  stronger or weaker";

        if (!settings.HotkeysEnabled)
        {
            HotkeyHint.Text = "Shortcuts are off.";
        }
        else
        {
            var taken = bindings.Where(binding => platform.UnavailableHotkeys.Contains(binding.Action)).Select(binding => binding.Label).ToList();
            var text = taken.Count > 0 ? $"{summary}      ({string.Join(", ", taken)} already used by another app)" : summary;
            HotkeyHint.Text = platform.HotkeyNote is { } note ? $"{text}\n{note}" : text;
        }
    }

    private bool SafeStartupState()
    {
        try
        {
            return _app.Platform.IsStartupEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ helpers

    private string ToggleLabel() => _app.Platform.HotkeyBindings.First(binding => binding.Action == HotkeyAction.ToggleTint).Label;

    private static string FooterMessage() =>
        $"Closing this window keeps the tint on. Eyes Saver stays in the {AppController.TrayName}.";

    private static void SetSwatchSelected(Button button, bool selected)
    {
        button.Classes.Set("selected", selected);
        if (button.Content is Grid grid && grid.Children.OfType<Path>().FirstOrDefault() is { } check)
        {
            check.IsVisible = selected;
            var fill = grid.Children.OfType<Ellipse>().First().Fill as ISolidColorBrush;
            check.Stroke = fill is not null && Luminance(fill.Color) > 150 ? new SolidColorBrush(Color.FromRgb(28, 25, 23)) : Brushes.White;
        }
    }

    private static double Luminance(Color color) => (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B);

    private static Color ToAvalonia(ColorDto color) => Color.FromRgb(color.R, color.G, color.B);
}
