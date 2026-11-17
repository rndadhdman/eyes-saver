using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DrawingColor = System.Drawing.Color;

namespace EyesSaver.App;

internal static class TrayIconRenderer
{
    /// <summary>A dot in the current tint color. Paused shows a grey dot with a slash.</summary>
    public static WindowIcon Create(DrawingColor color, bool paused)
    {
        const int size = 64;
        using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            var unit = size / 32.0;
            var center = new Point(size / 2.0, size / 2.0);
            var radius = 13 * unit;
            var fill = paused ? Color.FromRgb(150, 146, 140) : Color.FromRgb(color.R, color.G, color.B);

            context.DrawEllipse(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 1.2 * unit), center, radius, radius);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)), 2.5 * unit), center, radius - (1.25 * unit), radius - (1.25 * unit));

            if (paused)
            {
                var slash = new Pen(new SolidColorBrush(Color.FromRgb(40, 36, 32)), 3.5 * unit, lineCap: PenLineCap.Round);
                context.DrawLine(slash, new Point(9 * unit, 23 * unit), new Point(23 * unit, 9 * unit));
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
