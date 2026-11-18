using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WallpaperEditor.App.Views;

/// <summary>
/// A cursor for the rotation handle. Windows has no stock one (the IDC_ cursors stop at sizes, hand and the like),
/// so it is drawn here from the Rotate glyph of the system icon font, white with a dark outline like the system
/// cursors, and wrapped in a .cur in memory. No file ships with the app.
/// </summary>
internal static class RotationCursor
{
    private const int Size = 32;
    private const string RotateGlyph = "";

    private static Cursor? _cursor;

    /// <summary>Falls back to the hand if the icon font is missing, which it never is on Windows 10 and 11.</summary>
    public static Cursor Value => _cursor ??= TryCreate() ?? Cursors.Hand;

    private static Cursor? TryCreate()
    {
        try
        {
            return new Cursor(new MemoryStream(ToCur(Draw(), hotspotX: Size / 2, hotspotY: Size / 2)));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    private static BitmapSource Draw()
    {
        var typeface = new Typeface(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var text = new FormattedText(RotateGlyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 22, Brushes.White, 1);
        var glyph = text.BuildGeometry(new Point((Size - text.Width) / 2, (Size - text.Height) / 2));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawGeometry(null, new Pen(Brushes.Black, 3) { LineJoin = PenLineJoin.Round }, glyph);
            context.DrawGeometry(Brushes.White, null, glyph);
        }

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
    }

    /// <summary>A one-image .cur: the icon directory, then a 32-bit DIB with straight alpha and an empty AND mask.</summary>
    private static byte[] ToCur(BitmapSource image, int hotspotX, int hotspotY)
    {
        var stride = Size * 4;
        var pixels = new byte[stride * Size];
        image.CopyPixels(pixels, stride, 0);

        var maskStride = Size / 8;
        var imageSize = 40 + pixels.Length + (maskStride * Size);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // ICONDIR: reserved, type 2 for a cursor, one image.
        writer.Write((ushort)0);
        writer.Write((ushort)2);
        writer.Write((ushort)1);

        // ICONDIRENTRY: for a cursor, the planes and bit count fields hold the hotspot.
        writer.Write((byte)Size);
        writer.Write((byte)Size);
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)hotspotX);
        writer.Write((ushort)hotspotY);
        writer.Write(imageSize);
        writer.Write(6 + 16);

        // BITMAPINFOHEADER: the height counts the color image and the mask together.
        writer.Write(40);
        writer.Write(Size);
        writer.Write(Size * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixels.Length + (maskStride * Size));
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        // Rows bottom-up, as a DIB stores them.
        for (var row = Size - 1; row >= 0; row--)
        {
            writer.Write(pixels, row * stride, stride);
        }

        writer.Write(new byte[maskStride * Size]);
        writer.Flush();
        return stream.ToArray();
    }
}
