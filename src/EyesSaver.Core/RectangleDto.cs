using System.Drawing;

namespace EyesSaver.Core;

public readonly record struct RectangleDto(int X, int Y, int Width, int Height)
{
    public static RectangleDto FromRectangle(Rectangle rectangle)
    {
        return new RectangleDto(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
    }

    public Rectangle ToRectangle() => new(X, Y, Width, Height);
}
