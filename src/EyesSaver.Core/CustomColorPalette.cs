using System.Drawing;

namespace EyesSaver.Core;

public static class CustomColorPalette
{
    public static int[] ToColorDialogValues(IEnumerable<ColorDto> colors)
    {
        return colors
            .Select(color => ColorTranslator.ToWin32(color.ToColor()))
            .ToArray();
    }

    public static List<ColorDto> FromColorDialogValues(IEnumerable<int> values)
    {
        return values
            .Select(ColorTranslator.FromWin32)
            .Select(ColorDto.FromColor)
            .ToList();
    }
}
