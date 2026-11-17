using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace EyesSaver.Core.Platform;

/// <summary>The identifying fields of a monitor's EDID block.</summary>
public sealed record EdidInfo(string Manufacturer, ushort ProductCode, uint SerialNumber, string? MonitorName, string? SerialText)
{
    /// <summary>
    /// An id that follows the physical monitor, or null when the monitor reports no serial
    /// (common on cheaper panels, where identical models would otherwise collide).
    /// </summary>
    public string? SerialKey
    {
        get
        {
            var model = string.Create(CultureInfo.InvariantCulture, $"{Manufacturer}-{ProductCode:X4}");
            if (!string.IsNullOrWhiteSpace(SerialText))
            {
                return $"{model}-{SerialText}";
            }

            return SerialNumber != 0
                ? string.Create(CultureInfo.InvariantCulture, $"{model}-{SerialNumber:X8}")
                : null;
        }
    }

    public string ModelKey => string.Create(CultureInfo.InvariantCulture, $"{Manufacturer}-{ProductCode:X4}");
}

public static class Edid
{
    private static readonly byte[] Header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    public static EdidInfo? Parse(ReadOnlySpan<byte> edid)
    {
        if (edid.Length < 128 || !edid[..8].SequenceEqual(Header))
        {
            return null;
        }

        var packed = (edid[8] << 8) | edid[9];
        var manufacturer = new string(
        [
            (char)('@' + ((packed >> 10) & 0x1F)),
            (char)('@' + ((packed >> 5) & 0x1F)),
            (char)('@' + (packed & 0x1F))
        ]);

        var product = BinaryPrimitives.ReadUInt16LittleEndian(edid[10..12]);
        var serial = BinaryPrimitives.ReadUInt32LittleEndian(edid[12..16]);

        string? name = null;
        string? serialText = null;
        foreach (var offset in new[] { 54, 72, 90, 108 })
        {
            var descriptor = edid.Slice(offset, 18);
            if (descriptor[0] != 0 || descriptor[1] != 0 || descriptor[2] != 0)
            {
                continue;
            }

            var text = ReadDescriptorText(descriptor[5..18]);
            switch (descriptor[3])
            {
                case 0xFC:
                    name ??= text;
                    break;
                case 0xFF:
                    serialText ??= text;
                    break;
            }
        }

        return new EdidInfo(manufacturer, product, serial, name, serialText);
    }

    private static string? ReadDescriptorText(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0x0A);
        if (end >= 0)
        {
            bytes = bytes[..end];
        }

        var text = Encoding.ASCII.GetString(bytes).Trim('\0', ' ');
        return text.Length == 0 ? null : text;
    }
}
