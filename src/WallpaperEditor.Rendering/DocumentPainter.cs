using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Rendering;

/// <summary>
/// The one place that knows what a document looks like. The editor and the published image both draw through
/// here, so what the user sees while editing is what ends up on the monitor.
/// </summary>
public static class DocumentPainter
{
    /// <summary>Draws the whole document in canvas coordinates.</summary>
    public static void Paint(DrawingContext context, WallpaperDocument document, PixelSize canvas, DocumentImages images)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(images);

        DrawBackground(context, document.Background, canvas, images);

        foreach (var item in document.Items)
        {
            context.PushTransform(new TranslateTransform(item.Rect.X, item.Rect.Y));
            context.PushTransform(new RotateTransform(item.Rotation, item.Rect.Width / 2, item.Rect.Height / 2));
            DrawItem(context, item, item.Rect.Width, item.Rect.Height, images, showMissing: false);
            context.Pop();
            context.Pop();
        }
    }

    public static void DrawBackground(DrawingContext context, BackgroundSpec background, PixelSize canvas, DocumentImages images)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(images);

        var area = new Rect(0, 0, canvas.Width, canvas.Height);
        context.DrawRectangle(Brushes.Black, null, area);

        if (images.SizeOf(background.Source) is not { } size)
        {
            return;
        }

        var placement = FitCalculator.Compute(size, canvas, background.Fit);
        if (images.Get(background.Source, placement.Width, placement.Height) is { } bitmap)
        {
            context.PushClip(new RectangleGeometry(area));
            context.DrawImage(bitmap, new Rect(placement.X, placement.Y, placement.Width, placement.Height));
            context.Pop();
        }
    }

    /// <summary>Draws an item in its own coordinates, from (0,0), clipped to the given size.</summary>
    /// <param name="context">Where to draw.</param>
    /// <param name="item">What to draw.</param>
    /// <param name="width">The width, which during a drag differs from the item's own.</param>
    /// <param name="height">The height, which during a drag differs from the item's own.</param>
    /// <param name="images">Decoded assets.</param>
    /// <param name="showMissing">Mark an unreadable asset visibly, for the editor. The final image leaves it out.</param>
    /// <param name="preferCached">Use what is already decoded, to keep a drag smooth.</param>
    public static void DrawItem(DrawingContext context, DocumentItem item, double width, double height, DocumentImages images, bool showMissing, bool preferCached = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(images);

        context.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        switch (item)
        {
            case ImageItem image:
                DrawImageItem(context, image, width, height, images, showMissing, preferCached);
                break;
            case ChecklistItem checklist:
                ChecklistPainter.Draw(context, checklist, width, height);
                break;
        }

        context.Pop();
    }

    private static void DrawImageItem(DrawingContext context, ImageItem item, double width, double height, DocumentImages images, bool showMissing, bool preferCached)
    {
        var bitmap = (preferCached ? images.GetCached(item.Source) : null) ?? images.Get(item.Source, width, height);
        var area = new Rect(0, 0, width, height);

        if (bitmap is not null)
        {
            context.PushOpacity(item.Opacity);
            context.DrawImage(bitmap, area);
            context.Pop();
        }
        else if (showMissing)
        {
            DrawMissingMarker(context, item, area);
        }
    }

    private static void DrawMissingMarker(DrawingContext context, ImageItem item, Rect area)
    {
        var pen = new Pen(Brushes.IndianRed, 3);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 30, 10, 10)), pen, area);
        context.DrawLine(pen, area.TopLeft, area.BottomRight);
        context.DrawLine(pen, area.TopRight, area.BottomLeft);
        context.DrawText(
            new FormattedText(
                $"Não foi possível ler {item.Source.OriginalName}",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                20,
                Brushes.White,
                null,
                TextFormattingMode.Display,
                1.0) { MaxTextWidth = Math.Max(1, area.Width - 24) },
            new Point(12, 12));
    }
}

/// <summary>Draws checklists, and knows how tall they need to be.</summary>
public static class ChecklistPainter
{
    /// <summary>The height needed to show the whole list at the item's current width.</summary>
    public static double RequiredHeight(ChecklistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Layout(item, item.Rect.Width, context: null);
    }

    public static void Draw(DrawingContext context, ChecklistItem item, double width, double height)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);

        var size = item.Style.Size;
        context.DrawRoundedRectangle(ParseBrush(item.Style.Panel, "#BE101218"), null, new Rect(0, 0, width, height), size * 0.5, size * 0.5);
        Layout(item, width, context);
    }

    /// <summary>One routine both measures and draws, so the two can never disagree.</summary>
    private static double Layout(ChecklistItem item, double width, DrawingContext? context)
    {
        var size = item.Style.Size;
        var padding = size * 0.9;
        var textBrush = ParseBrush(item.Style.Color, "#FFFFFF");
        var y = padding;

        if (item.Title.Length > 0)
        {
            var title = Text(item.Title, item.Style.Font, size * 1.25, FontWeights.SemiBold, textBrush, width - (padding * 2));
            context?.DrawText(title, new Point(padding, y));
            y += title.Height + (size * 0.7);
        }

        var box = size * 0.95;
        var textLeft = padding + box + (size * 0.6);
        var rowGap = size * 0.55;

        var dimBrush = textBrush.Clone();
        dimBrush.Opacity = 0.55;
        dimBrush.Freeze();

        for (var i = 0; i < item.Entries.Count; i++)
        {
            var entry = item.Entries[i];
            var text = Text(entry.Text, item.Style.Font, size, FontWeights.Normal, entry.Done ? dimBrush : textBrush, width - textLeft - padding);
            if (entry.Done)
            {
                text.SetTextDecorations(TextDecorations.Strikethrough);
            }

            if (context is not null)
            {
                // Aligned with the first line of the text, which may wrap onto more.
                var firstLineHeight = size * 1.33;
                DrawCheckbox(context, new Rect(padding, y + ((firstLineHeight - box) / 2), box, box), entry.Done, textBrush, dimBrush);
                context.DrawText(text, new Point(textLeft, y));
            }

            y += text.Height + (i < item.Entries.Count - 1 ? rowGap : 0);
        }

        return y + padding;
    }

    /// <summary>
    /// Geometry rather than the ☐ and ☑ characters: those come from a fallback font and were measured coming out
    /// at different sizes from each other.
    /// </summary>
    private static void DrawCheckbox(DrawingContext context, Rect box, bool done, Brush color, Brush dim)
    {
        var thickness = Math.Max(1.5, box.Width * 0.09);
        var radius = box.Width * 0.18;

        if (!done)
        {
            context.DrawRoundedRectangle(null, new Pen(color, thickness), box, radius, radius);
            return;
        }

        context.DrawRoundedRectangle(dim, null, box, radius, radius);

        var mark = new StreamGeometry();
        using (var path = mark.Open())
        {
            path.BeginFigure(new Point(box.Left + (box.Width * 0.22), box.Top + (box.Height * 0.52)), isFilled: false, isClosed: false);
            path.LineTo(new Point(box.Left + (box.Width * 0.42), box.Top + (box.Height * 0.72)), isStroked: true, isSmoothJoin: false);
            path.LineTo(new Point(box.Left + (box.Width * 0.78), box.Top + (box.Height * 0.3)), isStroked: true, isSmoothJoin: false);
        }

        mark.Freeze();
        context.DrawGeometry(null, new Pen(Brushes.Black, thickness * 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, mark);
    }

    private static FormattedText Text(string text, string font, double size, FontWeight weight, Brush brush, double maxWidth) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font), FontStyles.Normal, weight, FontStretches.Normal),
            size,
            brush,
            null,
            TextFormattingMode.Display,
            1.0) { MaxTextWidth = Math.Max(1, maxWidth) };

    private static SolidColorBrush ParseBrush(string color, string fallback)
    {
        Color parsed;
        try
        {
            parsed = (Color)ColorConverter.ConvertFromString(color);
        }
        catch (FormatException)
        {
            parsed = (Color)ColorConverter.ConvertFromString(fallback);
        }

        var brush = new SolidColorBrush(parsed);
        brush.Freeze();
        return brush;
    }
}
