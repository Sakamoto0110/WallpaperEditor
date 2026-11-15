using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.Views;

/// <summary>
/// The document as on-screen elements, one per item, drawn by the same code as the published image. Shared by the
/// editor and the interaction window, so both show exactly what the wallpaper will.
/// </summary>
internal static class DocumentHits
{
    /// <summary>The item on top at a canvas point: the last one drawn wins.</summary>
    public static DocumentItem? TopItemAt(WallpaperDocument document, CanvasPoint canvas) =>
        document.Items.LastOrDefault(i => ItemGeometry.Contains(i.Rect, i.Rotation, canvas));

    /// <summary>The control under a canvas point, worked out by the same layout that draws it.</summary>
    public static Hotspot? HotspotAt(DocumentItem item, CanvasPoint canvas)
    {
        if (item is not StyledItem styled)
        {
            return null;
        }

        var local = ItemGeometry.ToLocal(item.Rect, item.Rotation, canvas);
        return StyledItemPainter.HotspotAt(styled, item.Rect.Width, new Point(local.X, local.Y));
    }
}

/// <summary>Elements draw from their own origin. Quality settings match the published image.</summary>
internal abstract class PaintedView : FrameworkElement
{
    protected PaintedView()
    {
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
    }
}

internal sealed class BackgroundView : PaintedView
{
    private readonly BackgroundSpec _background;
    private readonly PixelSize _canvas;
    private readonly DocumentImages _images;

    public BackgroundView(BackgroundSpec background, PixelSize canvas, DocumentImages images)
    {
        _background = background;
        _canvas = canvas;
        _images = images;
        Width = canvas.Width;
        Height = canvas.Height;
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        DocumentPainter.DrawBackground(drawingContext, _background, _canvas, _images);
}

internal sealed class ItemView : PaintedView
{
    private readonly DocumentImages _images;
    private readonly RotateTransform _rotation = new();
    private ItemRect _rect;
    private bool _isInteracting;
    private DocumentItem _shown;

    public ItemView(DocumentItem item, DocumentImages images)
    {
        Item = item;
        _shown = item;
        _images = images;
        RenderTransform = _rotation;
        ResetToItem();
    }

    public DocumentItem Item { get; }

    /// <summary>What is drawn: the item, or a version of it not yet committed, such as a bar being dragged.</summary>
    public DocumentItem Shown
    {
        get => _shown;
        set
        {
            _shown = value;
            InvalidateVisual();
        }
    }

    public ItemRect LiveRect
    {
        get => _rect;
        set
        {
            _rect = value;
            Canvas.SetLeft(this, value.X);
            Canvas.SetTop(this, value.Y);
            Width = value.Width;
            Height = value.Height;
            _rotation.CenterX = value.Width / 2;
            _rotation.CenterY = value.Height / 2;
        }
    }

    public double LiveRotation
    {
        get => _rotation.Angle;
        set => _rotation.Angle = value;
    }

    /// <summary>While dragging, use what is already decoded; the drop draws it again at full quality.</summary>
    public bool IsInteracting
    {
        get => _isInteracting;
        set
        {
            if (_isInteracting && !value)
            {
                InvalidateVisual();
            }

            _isInteracting = value;
        }
    }

    public void ResetToItem()
    {
        LiveRect = Item.Rect;
        LiveRotation = Item.Rotation;
        if (!ReferenceEquals(_shown, Item))
        {
            Shown = Item;
        }
    }

    protected override void OnRender(DrawingContext drawingContext) =>
        DocumentPainter.DrawItem(drawingContext, _shown, Width, Height, _images, showMissing: true, preferCached: _isInteracting, interactive: true);
}
