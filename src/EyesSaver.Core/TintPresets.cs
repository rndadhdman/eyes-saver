using System.Drawing;

namespace EyesSaver.Core;

public sealed record TintPreset(string Name, string Description, ColorDto Color);

public static class TintPresets
{
    /// <summary>Overlays above this opacity make the screen hard to read, so the UI stops here.</summary>
    public const double MaxOpacity = 0.9;

    public static IReadOnlyList<TintPreset> All { get; } =
    [
        new("Warm", "Soft yellow, good for daytime", ColorDto.WarmDefault),
        new("Amber", "Cuts more blue light", ColorDto.FromColor(Color.FromArgb(255, 255, 170, 60))),
        new("Sunset", "Strong orange for evenings", ColorDto.FromColor(Color.FromArgb(255, 255, 115, 30))),
        new("Night", "Deep red for late at night", ColorDto.FromColor(Color.FromArgb(255, 200, 35, 0))),
        new("Sepia", "Brown, also dims the screen", ColorDto.FromColor(Color.FromArgb(255, 112, 66, 20))),
        new("Dim", "No color change, only darker", ColorDto.FromColor(Color.FromArgb(255, 0, 0, 0)))
    ];

    public static TintPreset? Find(ColorDto color)
    {
        return All.FirstOrDefault(preset => preset.Color == color);
    }
}
