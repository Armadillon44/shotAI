using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShotAI.App.Tests.Support;

/// <summary>
/// Image files for the report's image tests, made with WPF's encoders on the calling thread: a
/// PNG or JPEG of known pixels, a JPEG with an EXIF orientation, and GIF or BMP bytes that must
/// never be decoded as a report image.
/// </summary>
internal static class TestImages
{
    public static readonly Color Red = Color.FromRgb(255, 0, 0);
    public static readonly Color Green = Color.FromRgb(0, 255, 0);
    public static readonly Color Blue = Color.FromRgb(0, 0, 255);
    public static readonly Color Yellow = Color.FromRgb(255, 255, 0);

    /// <summary>Red top left, green top right, blue bottom left, yellow bottom right.</summary>
    public static Color Quadrant(int x, int y, int width, int height) =>
        (x < width / 2, y < height / 2) switch
        {
            (true, true) => Red,
            (false, true) => Green,
            (true, false) => Blue,
            _ => Yellow,
        };

    /// <summary>A <paramref name="width"/> by <paramref name="height"/> opaque bitmap of <paramref name="colour"/>.</summary>
    public static BitmapSource Bitmap(int width, int height, Func<int, int, Color> colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = colour(x, y);
                var i = (y * width + x) * 4;
                pixels[i] = c.B;
                pixels[i + 1] = c.G;
                pixels[i + 2] = c.R;
                pixels[i + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] Png(int width, int height, Func<int, int, Color>? colour = null) =>
        Encode(new PngBitmapEncoder(), Bitmap(width, height, colour ?? ((x, y) => Quadrant(x, y, width, height))));

    public static byte[] Jpeg(int width, int height, Func<int, int, Color>? colour = null) =>
        Encode(new JpegBitmapEncoder { QualityLevel = 100 }, Bitmap(width, height, colour ?? ((x, y) => Quadrant(x, y, width, height))));

    public static byte[] Gif(int width, int height) => Encode(new GifBitmapEncoder(), Bitmap(width, height, (x, y) => Quadrant(x, y, width, height)));

    public static byte[] Bmp(int width, int height) => Encode(new BmpBitmapEncoder(), Bitmap(width, height, (x, y) => Quadrant(x, y, width, height)));

    /// <summary>
    /// <paramref name="jpeg"/> with an EXIF APP1 segment right after its SOI marker whose first
    /// IFD holds one tag, the orientation (274, a SHORT), little-endian.
    /// </summary>
    public static byte[] WithOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22,                         // APP1, 34 bytes with this length field
            0x45, 0x78, 0x69, 0x66, 0x00, 0x00,             // "Exif\0\0"
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // "II", 42, the first IFD at 8
            0x01, 0x00,                                     // one entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // tag 274, SHORT, count 1
            (byte)(orientation & 0xFF), (byte)(orientation >> 8), 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,                         // no next IFD
        ];
        return [.. jpeg.AsSpan(0, 2), .. app1, .. jpeg.AsSpan(2)];
    }

    /// <summary>The stored pixel (x, y) of an image of <paramref name="width"/> by <paramref name="height"/> that shows at (dx, dy) upright under EXIF <paramref name="orientation"/>.</summary>
    public static (int X, int Y) StoredPoint(ushort orientation, int dx, int dy, int width, int height) => orientation switch
    {
        2 => (width - 1 - dx, dy),
        3 => (width - 1 - dx, height - 1 - dy),
        4 => (dx, height - 1 - dy),
        5 => (dy, dx),
        6 => (dy, height - 1 - dx),
        7 => (width - 1 - dy, height - 1 - dx),
        8 => (width - 1 - dy, dx),
        _ => (dx, dy),
    };

    /// <summary>The colour at (x, y) of premultiplied BGRA pixels <paramref name="width"/> wide.</summary>
    public static Color PixelAt(byte[] pbgra, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return Color.FromArgb(pbgra[i + 3], pbgra[i + 2], pbgra[i + 1], pbgra[i]);
    }

    /// <summary>Whether <paramref name="actual"/> is within <paramref name="tolerance"/> of <paramref name="expected"/> on each channel.</summary>
    public static bool Near(Color expected, Color actual, int tolerance = 40) =>
        Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance;

    private static byte[] Encode(BitmapEncoder encoder, BitmapSource bitmap)
    {
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
