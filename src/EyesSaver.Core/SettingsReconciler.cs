namespace EyesSaver.Core;

/// <summary>
/// Lines saved settings up with the monitors that are connected now.
/// </summary>
/// <remarks>
/// Matching runs in three passes so a weak match for one monitor can never take settings
/// that belong to another monitor by a stronger rule:
/// 1. stable id (monitor device path),
/// 2. GDI device name, for settings written by older versions or monitors without a stable id,
/// 3. identical position and size.
/// Settings for monitors that are no longer connected are kept in
/// <see cref="AppSettings.RememberedMonitors"/> and restored when the monitor comes back.
/// A brand new monitor copies the shared tint when every other monitor uses the same one.
/// </remarks>
public static class SettingsReconciler
{
    public static AppSettings Reconcile(AppSettings? existing, IReadOnlyList<MonitorDescriptor> monitors)
    {
        var settings = new AppSettings
        {
            Version = AppSettings.CurrentVersion,
            TintEnabled = existing?.TintEnabled ?? true,
            ExcludeFromCapture = existing?.ExcludeFromCapture ?? true,
            HotkeysEnabled = existing?.HotkeysEnabled ?? true,
            TrayHintShown = existing?.TrayHintShown ?? false,
            CustomColors = existing?.CustomColors.ToList() ?? []
        };

        var current = existing?.Monitors ?? [];
        var candidates = current
            .Concat(existing?.RememberedMonitors ?? [])
            .ToList();
        var used = new HashSet<MonitorTintSettings>(ReferenceEqualityComparer.Instance);
        var matches = new MonitorTintSettings?[monitors.Count];

        MatchPass(monitors, candidates, used, matches, (candidate, monitor) =>
            Same(candidate.MonitorKey, monitor.Key));

        MatchPass(monitors, candidates, used, matches, (candidate, monitor) =>
            CanUseFallback(candidate, monitor) &&
            (Same(candidate.MonitorKey, monitor.DeviceName) || Same(candidate.DeviceName, monitor.DeviceName)));

        MatchPass(monitors, candidates, used, matches, (candidate, monitor) =>
            CanUseFallback(candidate, monitor) &&
            candidate.Bounds == RectangleDto.FromRectangle(monitor.Bounds));

        var template = SharedTint(current);
        for (var i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            var match = matches[i];
            settings.Monitors.Add(match is not null
                ? match.CloneForMonitor(monitor)
                : template?.CloneForMonitor(monitor) ?? MonitorTintSettings.CreateDefault(monitor));
        }

        var activeKeys = new HashSet<string>(settings.Monitors.Select(monitor => monitor.MonitorKey), StringComparer.OrdinalIgnoreCase);
        foreach (var leftover in candidates.Where(candidate => !used.Contains(candidate)))
        {
            if (settings.RememberedMonitors.Count >= AppSettings.MaxRememberedMonitors)
            {
                break;
            }

            if (string.IsNullOrEmpty(leftover.MonitorKey) || !activeKeys.Add(leftover.MonitorKey))
            {
                continue;
            }

            settings.RememberedMonitors.Add(leftover.Clone());
        }

        return settings;
    }

    private static void MatchPass(
        IReadOnlyList<MonitorDescriptor> monitors,
        List<MonitorTintSettings> candidates,
        HashSet<MonitorTintSettings> used,
        MonitorTintSettings?[] matches,
        Func<MonitorTintSettings, MonitorDescriptor, bool> rule)
    {
        for (var i = 0; i < monitors.Count; i++)
        {
            if (matches[i] is not null)
            {
                continue;
            }

            var match = candidates.FirstOrDefault(candidate => !used.Contains(candidate) && rule(candidate, monitors[i]));
            if (match is not null)
            {
                used.Add(match);
                matches[i] = match;
            }
        }
    }

    /// <summary>
    /// Weak matches are only trusted when one side has no stable id. Two monitors that both
    /// have device paths are either the same monitor (pass 1) or different monitors.
    /// </summary>
    private static bool CanUseFallback(MonitorTintSettings candidate, MonitorDescriptor monitor)
    {
        return string.IsNullOrEmpty(monitor.StableId) || IsLegacyKey(candidate.MonitorKey);
    }

    private static bool IsLegacyKey(string key)
    {
        return string.IsNullOrEmpty(key) || key.StartsWith(@"\\.\", StringComparison.Ordinal);
    }

    private static MonitorTintSettings? SharedTint(List<MonitorTintSettings> monitors)
    {
        if (monitors.Count == 0)
        {
            return null;
        }

        var first = monitors[0];
        return monitors.All(monitor => monitor.HasSameTint(first)) ? first : null;
    }

    private static bool Same(string? left, string? right)
    {
        return !string.IsNullOrEmpty(left) && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
