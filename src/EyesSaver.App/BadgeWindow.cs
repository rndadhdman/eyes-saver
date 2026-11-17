using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using EyesSaver.App.Platform;
using EyesSaver.Core;
using DrawingRectangle = System.Drawing.Rectangle;

namespace EyesSaver.App;

/// <summary>
/// A short-lived label floating above everything: a big number per monitor (Identify) or a small
/// confirmation for shortcuts and display changes. It never takes focus.
/// </summary>
internal sealed class BadgeWindow : Window
{
    private readonly DrawingRectangle _area;
    private readonly bool _centered;

    private BadgeWindow(string headline, string detail, bool large, DrawingRectangle area, bool centered, int milliseconds)
    {
        _area = area;
        _centered = centered;

        Title = large ? "Eyes Saver identify" : "Eyes Saver toast";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Width = large ? 300 : 340;
        Height = large ? 220 : 84;
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;

        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = large ? 2 : 1
        };
        panel.Children.Add(new TextBlock
        {
            Text = headline,
            FontSize = large ? 84 : 19,
            FontWeight = FontWeight.Bold,
            Foreground = large ? new SolidColorBrush(Color.FromRgb(245, 165, 36)) : Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        if (!string.IsNullOrEmpty(detail))
        {
            panel.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = large ? 14 : 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(214, 208, 200)),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        Content = new Border
        {
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(242, 28, 26, 24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 8),
            Child = panel
        };

        // Start on the target monitor so the window picks up that monitor's scale.
        Position = new PixelPoint(area.X + (area.Width / 2), area.Y + (area.Height / 2));
        Opened += (_, _) => PlaceOnArea();
        DispatcherTimer.RunOnce(Close, TimeSpan.FromMilliseconds(milliseconds));
    }

    /// <summary>Shows a big number in the middle of the monitor.</summary>
    public static BadgeWindow ShowIdentify(IPlatformServices platform, MonitorDescriptor monitor)
    {
        var detail = monitor.IsPrimary ? $"{monitor.DisplayName} · primary" : monitor.DisplayName;
        var window = new BadgeWindow(monitor.Number.ToString(), detail, large: true, monitor.Bounds, centered: true, milliseconds: 2600);
        platform.PrepareBadgeWindow(window);
        window.Show();
        return window;
    }

    /// <summary>Shows a small confirmation near the bottom of the monitor under the mouse.</summary>
    public static BadgeWindow ShowToast(IPlatformServices platform, IReadOnlyList<MonitorDescriptor> monitors, string headline, string detail, int milliseconds)
    {
        var cursor = platform.GetCursorPosition();
        var monitor = monitors.FirstOrDefault(candidate => cursor is { } point && candidate.Bounds.Contains(point))
            ?? monitors.FirstOrDefault(candidate => candidate.IsPrimary)
            ?? monitors.FirstOrDefault();
        var area = monitor is null
            ? new DrawingRectangle(0, 0, 1280, 720)
            : monitor.WorkArea is { Width: > 0, Height: > 0 } work ? work : monitor.Bounds;

        var window = new BadgeWindow(headline, detail, large: false, area, centered: false, milliseconds);
        platform.PrepareBadgeWindow(window);
        window.Show();
        return window;
    }

    private void PlaceOnArea()
    {
        var scale = RenderScaling;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        var x = _area.X + ((_area.Width - width) / 2);
        var y = _centered
            ? _area.Y + ((_area.Height - height) / 2)
            : _area.Bottom - height - (int)Math.Round(56 * scale);
        Position = new PixelPoint(x, y);
    }
}
