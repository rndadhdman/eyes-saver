using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using EyesSaver.Core;
using DrawingColor = System.Drawing.Color;

namespace EyesSaver.App;

/// <summary>
/// Draws the monitors as they are arranged on the desktop, each tinted the way the overlay
/// will tint it. Clicking a monitor selects it; clicking empty space selects all monitors.
/// </summary>
internal sealed class MonitorMap : Control, ICustomHitTest
{
    public static readonly StyledProperty<double> FontSizeProperty =
        Avalonia.Controls.Documents.TextElement.FontSizeProperty.AddOwner<MonitorMap>();

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private IReadOnlyList<MonitorDescriptor> _monitors = [];
    private AppSettings _settings = new();
    private List<MonitorPreviewItem> _layout = [];
    private string? _hoverKey;

    public MonitorMap()
    {
        MinHeight = 140;
        MinWidth = 240;
        ClipToBounds = false;
    }

    /// <summary>Raised with the clicked monitor's key, or null for the empty area.</summary>
    public event EventHandler<string?>? MonitorClicked;

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>The selected monitor key, or null when every monitor is selected.</summary>
    public string? SelectedKey { get; private set; }

    public void SetData(IReadOnlyList<MonitorDescriptor> monitors, AppSettings settings, string? selectedKey)
    {
        _monitors = monitors;
        _settings = settings;
        SelectedKey = selectedKey;
        RecalculateLayout();
        InvalidateVisual();
    }

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    public override void Render(DrawingContext context)
    {
        var area = new Rect(Bounds.Size);
        context.DrawRectangle(Brush("Es.SurfaceSunken"), null, area, 8, 8);

        var typeface = new Typeface(FontFamily.Default);
        var bold = new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

        if (_monitors.Count == 0)
        {
            var empty = Text("No displays found", typeface, FontSize, Brush("Es.TextMuted"), area.Width);
            context.DrawText(empty, new Point(0, (area.Height - empty.Height) / 2));
            return;
        }

        var accent = Brush("Es.Accent");
        var accentText = Brush("Es.AccentText");
        var muted = Brush("Es.TextMuted");
        var offScreen = ThemeColor("Es.ScreenOff", Avalonia.Media.Color.FromRgb(90, 88, 85));
        var bezel = Brush("Es.Bezel");
        const double radius = 6;

        foreach (var item in _layout)
        {
            var monitor = item.Monitor;
            var tint = _settings.FindMonitor(monitor);
            var bounds = new Rect(item.PreviewBounds.X, item.PreviewBounds.Y, item.PreviewBounds.Width, item.PreviewBounds.Height);
            var selected = SelectedKey is not null && string.Equals(SelectedKey, monitor.Key, StringComparison.OrdinalIgnoreCase);
            var hovered = string.Equals(_hoverKey, monitor.Key, StringComparison.OrdinalIgnoreCase);
            var active = _settings.TintEnabled && tint is { Enabled: true };

            context.DrawRectangle(bezel, null, bounds, radius, radius);

            // Screen: a light "page" with the tint blended on top, like the real overlay.
            var screen = bounds.Deflate(3);
            var screenColor = active
                ? Blend(DrawingColor.FromArgb(246, 248, 250), tint!.Color.ToColor(), Math.Min(tint.Opacity, TintPresets.MaxOpacity))
                : offScreen;
            context.DrawRectangle(new SolidColorBrush(screenColor), null, screen, radius - 2, radius - 2);

            if (monitor.IsPrimary)
            {
                // A taskbar strip marks the primary display.
                var bar = new Rect(screen.Left, screen.Bottom - 5, screen.Width, 5);
                context.DrawRectangle(new SolidColorBrush(Avalonia.Media.Color.FromArgb(70, 0, 0, 0)), null, bar);
            }

            var textBrush = new SolidColorBrush(ContrastText(screenColor));
            const double badge = 22;
            if (screen.Width > badge * 2.2 && screen.Height > badge * 1.6)
            {
                var center = new Point(screen.Left + 7 + (badge / 2), screen.Top + 7 + (badge / 2));
                context.DrawEllipse(selected ? accent : new SolidColorBrush(Avalonia.Media.Color.FromArgb(150, 0, 0, 0)), null, center, badge / 2, badge / 2);
                var number = Text(monitor.Number.ToString(CultureInfo.InvariantCulture), bold, FontSize, selected ? accentText : Brushes.White, badge);
                context.DrawText(number, new Point(center.X - (badge / 2), center.Y - (number.Height / 2)));
            }

            var label = screen.Deflate(new Thickness(8, 6));
            var status = !_settings.TintEnabled
                ? "Paused"
                : tint is { Enabled: true } ? $"{Math.Round(Math.Min(tint.Opacity, TintPresets.MaxOpacity) * 100):0}%" : "Off";

            var name = Text(monitor.DisplayName, bold, FontSize, textBrush, Math.Max(1, label.Width));
            var statusText = Text(status, typeface, FontSize, textBrush, Math.Max(1, label.Width));
            if (label.Height >= (name.Height * 2.4) && label.Width > 30)
            {
                var middle = label.Top + (label.Height / 2);
                context.DrawText(name, new Point(label.Left, middle - name.Height));
                context.DrawText(statusText, new Point(label.Left, middle + 1));
            }
            else if (label.Width > 20)
            {
                context.DrawText(statusText, new Point(label.Left, label.Top + ((label.Height - statusText.Height) / 2)));
            }

            if (selected)
            {
                context.DrawRectangle(null, new Pen(accent, 3), bounds.Inflate(2.5), radius + 2, radius + 2);
            }
            else if (hovered)
            {
                context.DrawRectangle(null, new Pen(muted, 1.5), bounds.Inflate(2), radius + 2, radius + 2);
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            RecalculateLayout();
        }
        else if (change.Property == ThemeVariantScope.ActualThemeVariantProperty || change.Property == FontSizeProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitMonitor(e.GetPosition(this));
        if (!string.Equals(hit, _hoverKey, StringComparison.OrdinalIgnoreCase))
        {
            _hoverKey = hit;
            Cursor = hit is null ? Cursor.Default : HandCursor;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverKey = null;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            MonitorClicked?.Invoke(this, HitMonitor(e.GetPosition(this)));
            e.Handled = true;
        }
    }

    private string? HitMonitor(Point point)
    {
        return _layout.FirstOrDefault(item => item.PreviewBounds.Contains((int)point.X, (int)point.Y))?.Monitor.Key;
    }

    private void RecalculateLayout()
    {
        var size = Bounds.Size;
        if (_monitors.Count == 0 || size.Width <= 0 || size.Height <= 0)
        {
            _layout = [];
            return;
        }

        // Real monitors usually touch; a small gap per monitor makes each one readable.
        const int gap = 6;
        _layout = MonitorLayoutScaler.Scale(_monitors, new System.Drawing.Size((int)size.Width, (int)size.Height), 18)
            .Select(item => item with { PreviewBounds = System.Drawing.Rectangle.Inflate(item.PreviewBounds, -gap / 2, -gap / 2) })
            .ToList();
    }

    private IBrush Brush(string key)
    {
        return this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
    }

    private Color ThemeColor(string key, Color fallback)
    {
        return this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush brush ? brush.Color : fallback;
    }

    private static FormattedText Text(string text, Typeface typeface, double size, IBrush brush, double maxWidth)
    {
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };
    }

    private static Color Blend(DrawingColor background, DrawingColor overlay, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Avalonia.Media.Color.FromRgb(
            (byte)Math.Round(background.R + ((overlay.R - background.R) * amount)),
            (byte)Math.Round(background.G + ((overlay.G - background.G) * amount)),
            (byte)Math.Round(background.B + ((overlay.B - background.B) * amount)));
    }

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    private static Color ContrastText(Color background)
    {
        var luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);
        return luminance > 150 ? Avalonia.Media.Color.FromRgb(28, 25, 23) : Colors.White;
    }
}
