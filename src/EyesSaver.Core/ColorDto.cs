using System.Drawing;

namespace EyesSaver.Core;

public readonly record struct ColorDto(byte A, byte R, byte G, byte B)
{
    public static ColorDto WarmDefault { get; } = FromColor(Color.FromArgb(255, 255, 214, 132));

    public static ColorDto FromColor(Color color) => new(color.A, color.R, color.G, color.B);

    public Color ToColor() => Color.FromArgb(A, R, G, B);
}
