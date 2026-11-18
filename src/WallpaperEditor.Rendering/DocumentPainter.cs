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

        var orientation = background.Orientation;
        var placement = FitCalculator.Compute(orientation.Apply(size), canvas, background.Fit);
        var (width, height) = OrientedDrawing.DecodeSize(placement, orientation);
        if (images.Get(background.Source, width, height) is { } bitmap)
        {
            context.PushClip(new RectangleGeometry(area));
            OrientedDrawing.Draw(context, bitmap, placement, orientation);
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
    /// <param name="interactive">Draw what only the editor shows, such as a counter's buttons. Never in the final image.</param>
    public static void DrawItem(DrawingContext context, DocumentItem item, double width, double height, DocumentImages images, bool showMissing, bool preferCached = false, bool interactive = false)
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
            case StyledItem styled:
                StyledItemPainter.Draw(context, styled, width, height, interactive);
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
            if (item.Mirrored)
            {
                context.PushTransform(new ScaleTransform(-1, 1, width / 2, 0));
            }

            context.PushOpacity(item.Opacity);
            context.DrawImage(bitmap, area);
            context.Pop();

            if (item.Mirrored)
            {
                context.Pop();
            }
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

/// <summary>What a click on a hotspot does.</summary>
public enum HotspotAction
{
    ToggleEntry,
    Decrement,
    Increment,
    SetProgress,
    Toggle,
}

/// <summary>A place in an item, in the item's own coordinates, that does something when clicked in the editor.</summary>
public sealed record Hotspot(HotspotAction Action, Rect Area, string? EntryId = null)
{
    /// <summary>For a progress bar: the percentage at a horizontal position, in the item's own coordinates.</summary>
    public int PercentAt(double x) =>
        Area.Width <= 0 ? 0 : (int)Math.Round(Math.Clamp((x - Area.Left) / Area.Width, 0, 1) * 100);
}

/// <summary>Draws text on a panel, knows how tall it needs to be, and where it can be clicked.</summary>
public static class StyledItemPainter
{
    /// <summary>The height needed to show all of the content at the item's current width.</summary>
    public static double RequiredHeight(StyledItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var frame = new Frame(item.Style, item.Rect.Width);
        return frame.BodyHeight(Measure(item, frame)) + frame.Tail;
    }

    public static void Draw(DrawingContext context, StyledItem item, double width, double height, bool interactive = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);

        var frame = new Frame(item.Style, width);
        var body = Math.Max(0, height - frame.Tail);
        DrawPanel(context, frame, body);

        context.PushTransform(new TranslateTransform(frame.InsetX, frame.InsetY(Measure(item, frame), body)));
        Layout(new Pass(item, frame.ContentWidth, context, interactive, Hotspots: null));
        context.Pop();
    }

    /// <summary>Everything that can be clicked, from the same layout that draws it, so a click lands where it looks.</summary>
    /// <param name="item">The item, whose height places the content in a cloud.</param>
    /// <param name="width">The width it is drawn at.</param>
    public static IReadOnlyList<Hotspot> Hotspots(StyledItem item, double width)
    {
        ArgumentNullException.ThrowIfNull(item);

        var frame = new Frame(item.Style, width);
        var (x, y) = (frame.InsetX, frame.InsetY(Measure(item, frame), Math.Max(0, item.Rect.Height - frame.Tail)));
        var hotspots = new List<Hotspot>();
        Layout(new Pass(item, frame.ContentWidth, Context: null, Interactive: true, hotspots));
        return [.. hotspots.Select(h => h with { Area = new Rect(h.Area.X + x, h.Area.Y + y, h.Area.Width, h.Area.Height) })];
    }

    private static double Measure(StyledItem item, Frame frame) =>
        Layout(new Pass(item, frame.ContentWidth, Context: null, Interactive: false, Hotspots: null));

    /// <summary>
    /// The panel, drawn in its full color inside one opacity layer: where the parts of a balloon overlap, they
    /// must not show darker than the rest.
    /// </summary>
    private static void DrawPanel(DrawingContext context, Frame frame, double bodyHeight)
    {
        var (style, width) = (frame.Style, frame.Width);
        var panel = ParseBrush(style.Panel, "#BE101218");
        var solid = new SolidColorBrush(panel.Color with { A = 255 });
        solid.Freeze();

        var size = style.Size;
        var body = new Rect(0, 0, width, bodyHeight);
        context.PushOpacity(panel.Color.A / 255.0);

        switch (style.Shape)
        {
            case PanelShape.Speech:
            {
                var radius = Math.Min(size * 0.9, bodyHeight / 2);
                context.DrawRoundedRectangle(solid, null, body, radius, radius);

                // Starts a little inside the body, so the two parts meet without a seam.
                var start = Math.Max(0, Math.Min(width * 0.2, width - (size * 1.9)));
                var tip = Math.Max(0, start - (size * 0.6));
                var tail = new StreamGeometry();
                using (var path = tail.Open())
                {
                    path.BeginFigure(Mirror(new Point(start, bodyHeight - radius), style.Tail, width), isFilled: true, isClosed: true);
                    path.LineTo(Mirror(new Point(start + (size * 1.8), bodyHeight - radius), style.Tail, width), isStroked: false, isSmoothJoin: false);
                    path.LineTo(Mirror(new Point(tip, bodyHeight + frame.Tail), style.Tail, width), isStroked: false, isSmoothJoin: false);
                }

                tail.Freeze();
                context.DrawGeometry(solid, null, tail);
                break;
            }

            case PanelShape.Thought:
            {
                // A cloud: an ellipse, with puffs of the same size spread evenly around its outline.
                var puff = Math.Min(frame.Puff, bodyHeight / 3);
                var center = new Point(width / 2, bodyHeight / 2);
                var (a, b) = (Math.Max(1, (width / 2) - puff), Math.Max(1, (bodyHeight / 2) - puff));
                context.DrawEllipse(solid, null, center, a, b);
                foreach (var point in AroundEllipse(center, a, b, puff * 1.35))
                {
                    context.DrawEllipse(solid, null, point, puff, puff);
                }

                // Smaller and smaller circles, trailing off toward the thinker.
                foreach (var (x, y, r) in new[] { (0.22, 0.55, 0.5), (0.15, 1.45, 0.33), (0.1, 2.15, 0.22) })
                {
                    context.DrawEllipse(solid, null, Mirror(new Point(width * x, bodyHeight + (size * y)), style.Tail, width), size * r, size * r);
                }

                break;
            }

            default:
                context.DrawRoundedRectangle(solid, null, body, size * 0.5, size * 0.5);
                break;
        }

        context.Pop();
    }

    /// <summary>
    /// Points evenly spaced by distance along an ellipse, not by angle: by angle, a long flat cloud would have its
    /// puffs crowded at the ends and sparse along the top.
    /// </summary>
    private static List<Point> AroundEllipse(Point center, double a, double b, double spacing)
    {
        const int Samples = 720;

        var outline = new Point[Samples + 1];
        var distance = new double[Samples + 1];
        for (var i = 0; i <= Samples; i++)
        {
            var angle = Math.Tau * i / Samples;
            outline[i] = new Point(center.X + (a * Math.Cos(angle)), center.Y + (b * Math.Sin(angle)));
            distance[i] = i == 0 ? 0 : distance[i - 1] + (outline[i] - outline[i - 1]).Length;
        }

        var count = Math.Max(8, (int)Math.Round(distance[Samples] / spacing));
        var points = new List<Point>(count);
        var sample = 0;
        for (var k = 0; k < count; k++)
        {
            var target = distance[Samples] * k / count;
            while (sample < Samples && distance[sample + 1] < target)
            {
                sample++;
            }

            points.Add(outline[sample]);
        }

        return points;
    }

    private static Point Mirror(Point point, TailSide side, double width) =>
        side == TailSide.Right ? new Point(width - point.X, point.Y) : point;

    /// <summary>The hotspot under a point in the item's own coordinates, if any.</summary>
    public static Hotspot? HotspotAt(StyledItem item, double width, Point local) =>
        Hotspots(item, width).LastOrDefault(h => h.Area.Contains(local));

    /// <summary>One routine both measures and draws, so the two can never disagree.</summary>
    private static double Layout(Pass pass) => pass.Item switch
    {
        ChecklistItem checklist => LayoutChecklist(checklist, pass),
        TextItem text => LayoutText(text, pass.Width, pass.Context),
        CounterItem counter => LayoutCounter(counter, pass),
        ProgressItem progress => LayoutProgress(progress, pass),
        ToggleItem toggle => LayoutToggle(toggle, pass),
        _ => 0,
    };

    private static double LayoutText(TextItem item, double width, DrawingContext? context)
    {
        var padding = item.Style.Size * 0.9;
        var text = Text(
            item.Text,
            item.Style.Font,
            item.Style.Size,
            item.Bold ? FontWeights.Bold : FontWeights.Normal,
            ParseBrush(item.Style.Color, "#FFFFFF"),
            width - (padding * 2),
            item.Italic ? FontStyles.Italic : FontStyles.Normal);

        // Automatic is centered in a cloud, as in a comic, and on the left anywhere else. Aligned lines still sit
        // inside the same box, so alignment never changes how much room the text needs.
        text.TextAlignment = item.Align switch
        {
            TextAlign.Center => TextAlignment.Center,
            TextAlign.Right => TextAlignment.Right,
            TextAlign.Justify => TextAlignment.Justify,
            TextAlign.Left => TextAlignment.Left,
            _ => item.Style.Shape == PanelShape.Thought ? TextAlignment.Center : TextAlignment.Left,
        };

        context?.DrawText(text, new Point(padding, padding));
        return padding + text.Height + padding;
    }

    private static double LayoutChecklist(ChecklistItem item, Pass pass)
    {
        var (width, context) = (pass.Width, pass.Context);
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
                DrawCheckbox(context, new Rect(padding, y + ((firstLineHeight - box) / 2), box, box), entry.Done, textBrush, dimBrush, item.Style.TextOpacity);
                context.DrawText(text, new Point(textLeft, y));
            }

            // The whole row, not only the box: a task is easier to hit by its text.
            pass.Hotspots?.Add(new Hotspot(HotspotAction.ToggleEntry, new Rect(0, y - (rowGap / 2), width, text.Height + rowGap), entry.Id));

            y += text.Height + (i < item.Entries.Count - 1 ? rowGap : 0);
        }

        return y + padding;
    }

    /// <summary>The label, the value, and in the editor a − and a + to the right of it.</summary>
    private static double LayoutCounter(CounterItem item, Pass pass)
    {
        var (width, context, size) = (pass.Width, pass.Context, item.Style.Size);
        var padding = size * 0.9;
        var brush = ParseBrush(item.Style.Color, "#FFFFFF");
        var y = LayoutLabel(item, pass, padding, padding, brush);

        var button = size * 1.7;
        var gap = size * 0.5;
        var value = Text(item.Value.ToString(CultureInfo.CurrentCulture), item.Style.Font, size * 2.6, FontWeights.SemiBold, brush, width - (padding * 2) - (button * 2) - (gap * 2));
        context?.DrawText(value, new Point(padding, y));

        var middle = y + (value.Height / 2);
        var minus = new Rect(width - padding - (button * 2) - gap, middle - (button / 2), button, button);
        var plus = new Rect(width - padding - button, middle - (button / 2), button, button);
        if (pass.Interactive && context is not null)
        {
            DrawRoundButton(context, minus, brush, plus: false);
            DrawRoundButton(context, plus, brush, plus: true);
        }

        pass.Hotspots?.Add(new Hotspot(HotspotAction.Decrement, Inflate(minus, gap / 2)));
        pass.Hotspots?.Add(new Hotspot(HotspotAction.Increment, Inflate(plus, gap / 2)));

        return y + value.Height + padding;
    }

    /// <summary>The label and the percentage above a bar. Dragging along the bar sets the value.</summary>
    private static double LayoutProgress(ProgressItem item, Pass pass)
    {
        var (width, context, size) = (pass.Width, pass.Context, item.Style.Size);
        var padding = size * 0.9;
        var brush = ParseBrush(item.Style.Color, "#FFFFFF");
        var y = padding;

        var percent = Text($"{item.Percent}%", item.Style.Font, size, FontWeights.SemiBold, brush, width);
        var label = Text(item.Label, item.Style.Font, size, FontWeights.Normal, brush, width - (padding * 2) - percent.Width - (size * 0.5));
        if (context is not null)
        {
            context.DrawText(label, new Point(padding, y));
            context.DrawText(percent, new Point(width - padding - percent.Width, y));
        }

        y += Math.Max(label.Height, percent.Height) + (size * 0.45);

        var bar = new Rect(padding, y, Math.Max(1, width - (padding * 2)), size * 0.7);
        if (context is not null)
        {
            var shape = new RectangleGeometry(bar, bar.Height / 2, bar.Height / 2);
            context.PushClip(shape);
            context.DrawRectangle(WithOpacity(brush, 0.25), null, bar);
            context.DrawRectangle(brush, null, new Rect(bar.X, bar.Y, bar.Width * item.Percent / 100.0, bar.Height));
            context.Pop();
        }

        // Taller than the bar, so it is easy to grab, but exactly as wide, so the percentage matches the pointer.
        pass.Hotspots?.Add(new Hotspot(HotspotAction.SetProgress, new Rect(bar.X, bar.Y - (size * 0.5), bar.Width, bar.Height + size)));

        return y + bar.Height + padding;
    }

    /// <summary>The label, then a switch and the text of the current state. Clicking the row flips it.</summary>
    private static double LayoutToggle(ToggleItem item, Pass pass)
    {
        var (width, context, size) = (pass.Width, pass.Context, item.Style.Size);
        var padding = size * 0.9;
        var brush = ParseBrush(item.Style.Color, "#FFFFFF");
        var y = LayoutLabel(item, pass, padding, padding, brush);

        var switchHeight = size * 1.25;
        var switchWidth = switchHeight * 1.9;
        var textLeft = padding + switchWidth + (size * 0.6);
        var state = Text(item.StateText, item.Style.Font, size, FontWeights.SemiBold, brush, width - textLeft - padding);
        var row = Math.Max(switchHeight, state.Height);

        if (context is not null)
        {
            DrawSwitch(context, new Rect(padding, y + ((row - switchHeight) / 2), switchWidth, switchHeight), item.On, brush, ParseBrush(item.Style.Panel, "#BE101218"));
            context.DrawText(state, new Point(textLeft, y + ((row - state.Height) / 2)));
        }

        pass.Hotspots?.Add(new Hotspot(HotspotAction.Toggle, new Rect(0, y - (size * 0.3), width, row + (size * 0.6))));

        return y + row + padding;
    }

    /// <summary>A control's label, slightly dimmed, or nothing when it has none.</summary>
    private static double LayoutLabel(ControlItem item, Pass pass, double padding, double y, SolidColorBrush brush)
    {
        if (item.Label.Length == 0)
        {
            return y;
        }

        var size = item.Style.Size;
        var label = Text(item.Label, item.Style.Font, size, FontWeights.Normal, WithOpacity(brush, 0.8), pass.Width - (padding * 2));
        pass.Context?.DrawText(label, new Point(padding, y));
        return y + label.Height + (size * 0.35);
    }

    private static void DrawRoundButton(DrawingContext context, Rect area, SolidColorBrush color, bool plus)
    {
        var thickness = Math.Max(1.5, area.Width * 0.07);
        var pen = new Pen(color, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var center = new Point(area.X + (area.Width / 2), area.Y + (area.Height / 2));
        var arm = area.Width * 0.24;

        context.DrawEllipse(WithOpacity(color, 0.12), pen, center, area.Width / 2, area.Height / 2);
        context.DrawLine(pen, new Point(center.X - arm, center.Y), new Point(center.X + arm, center.Y));
        if (plus)
        {
            context.DrawLine(pen, new Point(center.X, center.Y - arm), new Point(center.X, center.Y + arm));
        }
    }

    private static void DrawSwitch(DrawingContext context, Rect area, bool on, SolidColorBrush color, SolidColorBrush panel)
    {
        var radius = area.Height / 2;
        var knob = radius * 0.72;
        var knobCenter = new Point(on ? area.Right - radius : area.Left + radius, area.Y + radius);

        if (on)
        {
            // The knob takes the panel's color at full strength, so it reads as a hole in the filled track.
            var hole = new SolidColorBrush(panel.Color with { A = 255 });
            hole.Freeze();
            context.DrawRoundedRectangle(color, null, area, radius, radius);
            context.DrawEllipse(hole, null, knobCenter, knob, knob);
        }
        else
        {
            var thickness = Math.Max(1.5, area.Height * 0.08);
            context.DrawRoundedRectangle(null, new Pen(color, thickness), Inflate(area, -thickness / 2), radius, radius);
            context.DrawEllipse(WithOpacity(color, 0.8), null, knobCenter, knob * 0.85, knob * 0.85);
        }
    }

    private static Rect Inflate(Rect rect, double amount)
    {
        var inflated = rect;
        inflated.Inflate(amount, amount);
        return inflated;
    }

    private static SolidColorBrush WithOpacity(SolidColorBrush brush, double opacity)
    {
        var copy = brush.Clone();
        copy.Opacity = opacity;
        copy.Freeze();
        return copy;
    }

    /// <summary>One run of the layout: measuring, drawing, or collecting where it can be clicked.</summary>
    private sealed record Pass(StyledItem Item, double Width, DrawingContext? Context, bool Interactive, List<Hotspot>? Hotspots);

    /// <summary>
    /// How a panel's shape changes the room for content. A balloon's tail needs room below the body. A cloud is an
    /// ellipse, and the content goes in the largest rectangle inside it, whose corners sit at 45 degrees on the
    /// ellipse: 0.707 of each half-axis. So the cloud is sized from its content, never the other way around.
    /// </summary>
    private sealed record Frame(TextStyle Style, double Width)
    {
        private const double Inner = 0.7071;

        private bool IsCloud => Style.Shape == PanelShape.Thought;

        /// <summary>The radius of a cloud's puffs, from the width and font size, never from the height.</summary>
        public double Puff => Math.Clamp(Width * 0.06, Style.Size * 0.6, Style.Size * 1.6);

        public double Tail => Style.Shape switch
        {
            PanelShape.Speech => Style.Size * 2,
            PanelShape.Thought => Style.Size * 2.6,
            _ => 0,
        };

        public double InsetX => IsCloud ? (Width / 2) - (((Width / 2) - Puff) * Inner) : 0;

        public double ContentWidth => Math.Max(1, Width - (InsetX * 2));

        /// <summary>
        /// Where the content starts. In a cloud, in the middle of the body as drawn, which may be taller than the
        /// content needs; the content is never pushed above where it fits.
        /// </summary>
        public double InsetY(double content, double body) =>
            IsCloud ? (Math.Max(body, BodyHeight(content)) - content) / 2 : 0;

        public double BodyHeight(double content) => IsCloud ? (Puff * 2) + (content / Inner) : content;
    }

    /// <summary>
    /// Geometry rather than the ☐ and ☑ characters: those come from a fallback font and were measured coming out
    /// at different sizes from each other.
    /// </summary>
    private static void DrawCheckbox(DrawingContext context, Rect box, bool done, Brush color, Brush dim, double opacity)
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

        // Follows the text's opacity, or a check mark would linger on after the text has faded out.
        var markBrush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), 0, 0, 0));
        markBrush.Freeze();
        context.DrawGeometry(null, new Pen(markBrush, thickness * 1.3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, mark);
    }

    private static FormattedText Text(string text, string font, double size, FontWeight weight, Brush brush, double maxWidth, FontStyle? style = null) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(font), style ?? FontStyles.Normal, weight, FontStretches.Normal),
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
