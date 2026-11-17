namespace EyesSaver.Core;

public static class GlobalMonitorSettings
{
    public static GlobalMonitorSettingsSummary Summarize(AppSettings settings)
    {
        if (settings.Monitors.Count == 0)
        {
            return new GlobalMonitorSettingsSummary(
                GlobalEnabledState.None,
                ColorDto.WarmDefault,
                0.35,
                HasMixedColor: false,
                HasMixedOpacity: false);
        }

        var enabledState = settings.Monitors.All(monitor => monitor.Enabled)
            ? GlobalEnabledState.All
            : settings.Monitors.All(monitor => !monitor.Enabled)
                ? GlobalEnabledState.None
                : GlobalEnabledState.Mixed;

        var firstColor = settings.Monitors[0].Color;
        var hasMixedColor = settings.Monitors.Any(monitor => monitor.Color != firstColor);
        var averageOpacity = settings.Monitors.Average(monitor => monitor.Opacity);
        var hasMixedOpacity = settings.Monitors.Any(monitor => Math.Abs(monitor.Opacity - settings.Monitors[0].Opacity) > 0.0001);

        return new GlobalMonitorSettingsSummary(
            enabledState,
            firstColor,
            averageOpacity,
            hasMixedColor,
            hasMixedOpacity);
    }

    public static void ApplyToAll(
        AppSettings settings,
        bool? enabled = null,
        ColorDto? color = null,
        double? opacity = null)
    {
        foreach (var monitor in settings.Monitors)
        {
            if (enabled.HasValue)
            {
                monitor.Enabled = enabled.Value;
            }

            if (color.HasValue)
            {
                monitor.Color = color.Value;
            }

            if (opacity.HasValue)
            {
                monitor.Opacity = opacity.Value;
            }
        }
    }
}

public enum GlobalEnabledState
{
    None,
    Mixed,
    All
}

public sealed record GlobalMonitorSettingsSummary(
    GlobalEnabledState EnabledState,
    ColorDto Color,
    double Opacity,
    bool HasMixedColor,
    bool HasMixedOpacity);
