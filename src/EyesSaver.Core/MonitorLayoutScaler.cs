using System.Drawing;

namespace EyesSaver.Core;

public static class MonitorLayoutScaler
{
    public static List<MonitorPreviewItem> Scale(IReadOnlyList<MonitorDescriptor> monitors, Size targetSize, int padding)
    {
        if (monitors.Count == 0 || targetSize.Width <= 0 || targetSize.Height <= 0)
        {
            return [];
        }

        var virtualBounds = Rectangle.Union(monitors[0].Bounds, monitors[0].Bounds);
        foreach (var monitor in monitors.Skip(1))
        {
            virtualBounds = Rectangle.Union(virtualBounds, monitor.Bounds);
        }

        var drawableWidth = Math.Max(1, targetSize.Width - (padding * 2));
        var drawableHeight = Math.Max(1, targetSize.Height - (padding * 2));
        var scale = Math.Min(
            drawableWidth / (double)Math.Max(1, virtualBounds.Width),
            drawableHeight / (double)Math.Max(1, virtualBounds.Height));

        var scaledVirtualWidth = virtualBounds.Width * scale;
        var scaledVirtualHeight = virtualBounds.Height * scale;
        var offsetX = (targetSize.Width - scaledVirtualWidth) / 2.0;
        var offsetY = (targetSize.Height - scaledVirtualHeight) / 2.0;

        return monitors
            .Select(monitor =>
            {
                var left = (int)Math.Round(offsetX + ((monitor.Bounds.Left - virtualBounds.Left) * scale));
                var top = (int)Math.Round(offsetY + ((monitor.Bounds.Top - virtualBounds.Top) * scale));
                var width = Math.Max(1, (int)Math.Round(monitor.Bounds.Width * scale));
                var height = Math.Max(1, (int)Math.Round(monitor.Bounds.Height * scale));
                return new MonitorPreviewItem(monitor, new Rectangle(left, top, width, height));
            })
            .ToList();
    }
}

public sealed record MonitorPreviewItem(MonitorDescriptor Monitor, Rectangle PreviewBounds);
