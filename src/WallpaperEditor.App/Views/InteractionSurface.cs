using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WallpaperEditor.App.ViewModels;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.Views;

/// <summary>
/// The wallpaper as it looks, where only the components respond: tasks, − and +, the bar and the toggle. Items
/// cannot be selected, moved or resized here.
/// </summary>
public sealed class InteractionSurface : Grid
{
    /// <summary>In screen pixels: a press that moves further than this before release is not a click.</summary>
    private const double ClickSlop = 6;

    private readonly Canvas _content = new();
    private InteractionViewModel? _model;
    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;

    private ItemView? _pressedView;
    private Hotspot? _pressed;
    private Point _pressedAt;
    private bool _draggingBar;

    public InteractionSurface()
    {
        Background = Brushes.Black;
        ClipToBounds = true;
        Children.Add(_content);
        SizeChanged += (_, _) => UpdateScale();
    }

    public void Attach(InteractionViewModel model)
    {
        _model = model;
        model.DocumentChanged += (_, _) => Rebuild();
        UpdateScale();
        Rebuild();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (_model is not { IsInteractive: true } || Find(e.GetPosition(this)) is not ({ } view, { } hotspot))
        {
            return;
        }

        _pressedView = view;
        _pressed = hotspot;
        _pressedAt = e.GetPosition(this);
        _draggingBar = hotspot.Action == HotspotAction.SetProgress;
        CaptureMouse();

        if (_draggingBar)
        {
            PreviewProgress(e.GetPosition(this));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var display = e.GetPosition(this);
        if (_pressed is null)
        {
            Cursor = _model is { IsInteractive: true } && Find(display).Hotspot is not null ? Cursors.Hand : Cursors.Arrow;
            return;
        }

        if (_draggingBar)
        {
            PreviewProgress(display);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        Finish(e.GetPosition(this), commit: true);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        Finish(default, commit: false);
    }

    private void Finish(Point display, bool commit)
    {
        if (_pressed is not { } hotspot || _pressedView is not { } view)
        {
            return;
        }

        var bar = _draggingBar;
        _pressed = null;
        _pressedView = null;
        _draggingBar = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (!commit)
        {
            view.ResetToItem();
            return;
        }

        if (bar)
        {
            if (view.Shown is ProgressItem progress)
            {
                _model?.SetProgress(view.Item.Id, progress.Percent);
            }
        }
        else if ((display - _pressedAt).Length <= ClickSlop)
        {
            // Released where it was pressed: a click. Sliding away is the usual way to change one's mind.
            _model?.Interact(view.Item.Id, hotspot);
        }
    }

    private (ItemView? View, Hotspot? Hotspot) Find(Point display)
    {
        if (_model is null)
        {
            return (null, null);
        }

        var canvas = ToCanvas(display);
        if (DocumentHits.TopItemAt(_model.Document, canvas) is not { } item)
        {
            return (null, null);
        }

        var view = _content.Children.OfType<ItemView>().FirstOrDefault(v => v.Item.Id == item.Id);
        return (view, DocumentHits.HotspotAt(item, canvas));
    }

    /// <summary>Shows the new percentage while dragging. The wallpaper changes once, when the mouse is released.</summary>
    private void PreviewProgress(Point display)
    {
        if (_pressedView is { Item: ProgressItem progress } view && _pressed is { } bar)
        {
            var local = ItemGeometry.ToLocal(progress.Rect, progress.Rotation, ToCanvas(display));
            view.Shown = progress with { Percent = bar.PercentAt(local.X) };
        }
    }

    private void Rebuild()
    {
        if (_model is null)
        {
            return;
        }

        _content.Children.Clear();
        _content.Children.Add(new BackgroundView(_model.Document.Background, _model.Canvas, _model.Images));
        foreach (var item in _model.Document.Items)
        {
            _content.Children.Add(new ItemView(item, _model.Images));
        }
    }

    private void UpdateScale()
    {
        if (_model is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var canvas = _model.Canvas;
        _scale = Math.Min(ActualWidth / canvas.Width, ActualHeight / canvas.Height);
        _offsetX = (ActualWidth - (canvas.Width * _scale)) / 2;
        _offsetY = (ActualHeight - (canvas.Height * _scale)) / 2;
        _content.RenderTransform = new MatrixTransform(_scale, 0, 0, _scale, _offsetX, _offsetY);
    }

    private CanvasPoint ToCanvas(Point display) =>
        new((display.X - _offsetX) / _scale, (display.Y - _offsetY) / _scale);
}
