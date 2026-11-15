using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WallpaperEditor.App.ViewModels;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.Views;

/// <summary>
/// The canvas the user arranges items on. Each item is its own element, drawn by the same code as the published
/// image, so a drag only changes an element's transform and nothing has to be recomposed while the mouse moves.
/// The document only changes when a drag ends.
/// </summary>
/// <remarks>
/// Controls drawn in items, such as a task's checkbox, work here as if the wallpaper were clickable. A click that
/// does not move acts on the control; one that moves drags the item, as anywhere else. A progress bar is the one
/// exception: dragging along it sets the value. Nothing reaches the monitor until "Atualizar wallpaper".
/// </remarks>
public sealed class EditorSurface : Grid
{
    private const double HandleRadius = 6;
    private const double EdgeHandleSize = 10;
    private const double HandleGrabRadius = 11;
    private const double RotationHandleDistance = 28;
    private const double MinimumItemSize = 16;
    private const double Margin_ = 12;

    /// <summary>In screen pixels: less than this is a click on a control, more is a drag of the item.</summary>
    private const double ClickSlop = 4;

    private readonly Canvas _content = new();
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly Polygon _outline = new() { Stroke = Accent, StrokeThickness = 2, Visibility = Visibility.Collapsed };
    private readonly Line _rotationStem = new() { Stroke = Accent, StrokeThickness = 2, Visibility = Visibility.Collapsed };
    private readonly Ellipse _rotationHandle = MakeHandle();
    private readonly Ellipse[] _cornerHandles = [MakeHandle(), MakeHandle(), MakeHandle(), MakeHandle()];

    /// <summary>Square, unlike the round corners: a side changes one dimension, a corner both.</summary>
    private readonly Rectangle[] _edgeHandles = [MakeEdgeHandle(), MakeEdgeHandle(), MakeEdgeHandle(), MakeEdgeHandle()];

    private EditorViewModel? _editor;
    private ItemView? _selectedView;
    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;

    private DragMode _drag;
    private ItemCorner _dragCorner;
    private ItemEdge _dragEdge;
    private CanvasPoint _dragStart;
    private ItemRect _dragStartRect;
    private Point _pressedAt;
    private Hotspot? _pendingClick;
    private bool _nudging;

    public EditorSurface()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;

        Children.Add(_content);
        Children.Add(_overlay);

        _overlay.Children.Add(_outline);
        _overlay.Children.Add(_rotationStem);
        _overlay.Children.Add(_rotationHandle);
        foreach (var handle in _cornerHandles)
        {
            _overlay.Children.Add(handle);
        }

        foreach (var handle in _edgeHandles)
        {
            _overlay.Children.Add(handle);
        }

        SizeChanged += (_, _) => UpdateScale();
    }

    private enum DragMode
    {
        None,
        Move,
        Resize,
        ResizeEdge,
        Rotate,
        Progress,
    }

    private static SolidColorBrush Accent { get; } = MakeAccent();

    public void Attach(EditorViewModel? editor)
    {
        if (_editor is not null)
        {
            _editor.DocumentChanged -= OnDocumentChanged;
            _editor.SelectionChanged -= OnSelectionChanged;
        }

        _editor = editor;

        if (_editor is not null)
        {
            _editor.DocumentChanged += OnDocumentChanged;
            _editor.SelectionChanged += OnSelectionChanged;
            UpdateScale();
            Rebuild();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        if (_editor is not { IsEditable: true })
        {
            return;
        }

        var display = e.GetPosition(this);
        var canvas = ToCanvas(display);

        // Handles first: they can sit outside the item, and over other items.
        if (_selectedView is { } selected)
        {
            if (Near(display, ToDisplay(ItemGeometry.RotationHandle(selected.LiveRect, selected.LiveRotation, RotationHandleDistance / _scale))))
            {
                StartDrag(DragMode.Rotate, selected, canvas);
                return;
            }

            foreach (var corner in Enum.GetValues<ItemCorner>())
            {
                if (Near(display, ToDisplay(ItemGeometry.CornerPoint(selected.LiveRect, selected.LiveRotation, corner))))
                {
                    _dragCorner = corner;
                    StartDrag(DragMode.Resize, selected, canvas);
                    return;
                }
            }

            // After the corners, which win on a small item where both are close together.
            foreach (var edge in Enum.GetValues<ItemEdge>())
            {
                if (Near(display, ToDisplay(ItemGeometry.EdgePoint(selected.LiveRect, selected.LiveRotation, edge))))
                {
                    _dragEdge = edge;
                    StartDrag(DragMode.ResizeEdge, selected, canvas);
                    return;
                }
            }
        }

        var hit = _editor.Document.Items.LastOrDefault(i => ItemGeometry.Contains(i.Rect, i.Rotation, canvas));
        _editor.Select(hit?.Id);

        if (_selectedView is not { } nowSelected || hit is null)
        {
            return;
        }

        _pressedAt = display;
        _pendingClick = HotspotAt(hit, canvas);
        if (_pendingClick is { Action: HotspotAction.SetProgress })
        {
            StartDrag(DragMode.Progress, nowSelected, canvas);
            PreviewProgress(nowSelected, canvas);
            return;
        }

        StartDrag(DragMode.Move, nowSelected, canvas);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var display = e.GetPosition(this);
        if (_drag == DragMode.None)
        {
            Cursor = CursorAt(display);
            return;
        }

        if (_selectedView is not { } view)
        {
            return;
        }

        if (_drag == DragMode.Move && _pendingClick is not null)
        {
            if ((display - _pressedAt).Length < ClickSlop)
            {
                return;
            }

            // It moved: a drag of the item after all, not a click on its control.
            _pendingClick = null;
        }

        var canvas = ToCanvas(display);
        switch (_drag)
        {
            case DragMode.Progress:
                PreviewProgress(view, canvas);
                return;

            case DragMode.Move:
                view.LiveRect = _dragStartRect with
                {
                    X = _dragStartRect.X + canvas.X - _dragStart.X,
                    Y = _dragStartRect.Y + canvas.Y - _dragStart.Y,
                };
                break;

            case DragMode.Resize:
                // Images keep their shape unless Shift is held; a checklist has no shape to keep.
                var keepAspect = view.Item is ImageItem && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                view.LiveRect = ItemGeometry.ResizeFromCorner(_dragStartRect, view.LiveRotation, _dragCorner, canvas, keepAspect, MinimumItemSize);
                break;

            case DragMode.ResizeEdge:
                // A side always changes one dimension only, an image included: that is what it is for.
                view.LiveRect = ItemGeometry.ResizeFromEdge(_dragStartRect, view.LiveRotation, _dragEdge, canvas, MinimumItemSize);
                break;

            case DragMode.Rotate:
                view.LiveRotation = ItemGeometry.RotationToward(_dragStartRect, canvas);
                break;
        }

        UpdateOverlay();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        FinishDrag(commit: true);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        FinishDrag(commit: false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_editor is not { IsEditable: true } || _selectedView is not { } view)
        {
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        (double X, double Y)? nudge = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => null,
        };

        if (nudge is { } delta)
        {
            view.LiveRect = view.LiveRect with { X = view.LiveRect.X + delta.X, Y = view.LiveRect.Y + delta.Y };
            view.IsInteracting = true;
            _nudging = true;
            UpdateOverlay();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            _editor.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _drag != DragMode.None)
        {
            FinishDrag(commit: false);
            e.Handled = true;
        }
    }

    /// <summary>Holding an arrow key is one revision, not one per repeat.</summary>
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (_nudging && e.Key is Key.Left or Key.Right or Key.Up or Key.Down && _selectedView is { } view && _editor is not null)
        {
            _nudging = false;
            view.IsInteracting = false;
            _editor.CommitPlacement(view.Item.Id, view.LiveRect, view.LiveRotation);
        }
    }

    private static SolidColorBrush MakeAccent()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xA4, 0x77, 0x9A));
        brush.Freeze();
        return brush;
    }

    private static Rectangle MakeEdgeHandle() => new()
    {
        Width = EdgeHandleSize,
        Height = EdgeHandleSize,
        RadiusX = 2,
        RadiusY = 2,
        Fill = Brushes.White,
        Stroke = Accent,
        StrokeThickness = 2,
        Visibility = Visibility.Collapsed,
    };

    private static Ellipse MakeHandle() => new()
    {
        Width = HandleRadius * 2,
        Height = HandleRadius * 2,
        Fill = Brushes.White,
        Stroke = Accent,
        StrokeThickness = 2,
        Visibility = Visibility.Collapsed,
    };

    private void StartDrag(DragMode mode, ItemView view, CanvasPoint canvas)
    {
        _drag = mode;
        _dragStart = canvas;
        _dragStartRect = view.LiveRect;
        view.IsInteracting = true;
        CaptureMouse();
    }

    private void FinishDrag(bool commit)
    {
        if (_drag == DragMode.None)
        {
            return;
        }

        var view = _selectedView;
        var mode = _drag;
        var click = _pendingClick;
        _drag = DragMode.None;
        _pendingClick = null;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (view is null)
        {
            return;
        }

        view.IsInteracting = false;

        if (!commit)
        {
            view.ResetToItem();
            UpdateOverlay();
            return;
        }

        switch (mode)
        {
            case DragMode.Progress when view.Shown is ProgressItem progress:
                _editor?.SetProgress(view.Item.Id, progress.Percent);
                break;

            case DragMode.Move when click is not null:
                _editor?.Interact(view.Item.Id, click);
                break;

            default:
                _editor?.CommitPlacement(view.Item.Id, view.LiveRect, view.LiveRotation);
                break;
        }
    }

    private static Hotspot? HotspotAt(DocumentItem item, CanvasPoint canvas) => DocumentHits.HotspotAt(item, canvas);

    /// <summary>Shows the new percentage while dragging. The document only changes when the mouse is released.</summary>
    private void PreviewProgress(ItemView view, CanvasPoint canvas)
    {
        if (view.Item is ProgressItem progress && _pendingClick is { } bar)
        {
            var local = ItemGeometry.ToLocal(progress.Rect, progress.Rotation, canvas);
            view.Shown = progress with { Percent = bar.PercentAt(local.X) };
        }
    }

    private void OnDocumentChanged(object? sender, EventArgs e) => Rebuild();

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _selectedView = _content.Children.OfType<ItemView>().FirstOrDefault(v => v.Item.Id == _editor?.SelectedItem?.Id);
        UpdateOverlay();
    }

    /// <summary>Cheap: decoded images are kept by <see cref="DocumentImages"/>, so only the elements are new.</summary>
    private void Rebuild()
    {
        if (_editor is null)
        {
            return;
        }

        _content.Children.Clear();
        _content.Children.Add(new BackgroundView(_editor.Document.Background, _editor.Canvas, _editor.Images));

        foreach (var item in _editor.Document.Items)
        {
            _content.Children.Add(new ItemView(item, _editor.Images));
        }

        OnSelectionChanged(null, EventArgs.Empty);
    }

    private void UpdateScale()
    {
        if (_editor is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var canvas = _editor.Canvas;
        var availableWidth = Math.Max(1, ActualWidth - (Margin_ * 2));
        var availableHeight = Math.Max(1, ActualHeight - (Margin_ * 2) - RotationHandleDistance);
        _scale = Math.Min(availableWidth / canvas.Width, availableHeight / canvas.Height);
        _offsetX = (ActualWidth - (canvas.Width * _scale)) / 2;
        _offsetY = (ActualHeight - (canvas.Height * _scale)) / 2;

        _content.RenderTransform = new MatrixTransform(_scale, 0, 0, _scale, _offsetX, _offsetY);
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        if (_selectedView is not { } view)
        {
            _outline.Visibility = Visibility.Collapsed;
            _rotationStem.Visibility = Visibility.Collapsed;
            _rotationHandle.Visibility = Visibility.Collapsed;
            foreach (var handle in _cornerHandles)
            {
                handle.Visibility = Visibility.Collapsed;
            }

            foreach (var handle in _edgeHandles)
            {
                handle.Visibility = Visibility.Collapsed;
            }

            return;
        }

        var edges = Enum.GetValues<ItemEdge>();
        for (var i = 0; i < edges.Length; i++)
        {
            var point = ToDisplay(ItemGeometry.EdgePoint(view.LiveRect, view.LiveRotation, edges[i]));
            Canvas.SetLeft(_edgeHandles[i], point.X - (EdgeHandleSize / 2));
            Canvas.SetTop(_edgeHandles[i], point.Y - (EdgeHandleSize / 2));
            _edgeHandles[i].RenderTransform = new RotateTransform(view.LiveRotation, EdgeHandleSize / 2, EdgeHandleSize / 2);
            _edgeHandles[i].Visibility = Visibility.Visible;
        }

        var points = new PointCollection();
        var corners = Enum.GetValues<ItemCorner>();
        for (var i = 0; i < corners.Length; i++)
        {
            var point = ToDisplay(ItemGeometry.CornerPoint(view.LiveRect, view.LiveRotation, corners[i]));
            points.Add(point);
            PlaceHandle(_cornerHandles[i], point);
        }

        _outline.Points = points;
        _outline.Visibility = Visibility.Visible;

        var topMiddle = new Point((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
        var rotation = ToDisplay(ItemGeometry.RotationHandle(view.LiveRect, view.LiveRotation, RotationHandleDistance / _scale));
        _rotationStem.X1 = topMiddle.X;
        _rotationStem.Y1 = topMiddle.Y;
        _rotationStem.X2 = rotation.X;
        _rotationStem.Y2 = rotation.Y;
        _rotationStem.Visibility = Visibility.Visible;
        PlaceHandle(_rotationHandle, rotation);
    }

    private static void PlaceHandle(Ellipse handle, Point center)
    {
        Canvas.SetLeft(handle, center.X - HandleRadius);
        Canvas.SetTop(handle, center.Y - HandleRadius);
        handle.Visibility = Visibility.Visible;
    }

    private Cursor CursorAt(Point display)
    {
        if (_editor is not { IsEditable: true })
        {
            return Cursors.Arrow;
        }

        if (_selectedView is { } view)
        {
            if (Near(display, ToDisplay(ItemGeometry.RotationHandle(view.LiveRect, view.LiveRotation, RotationHandleDistance / _scale))))
            {
                return Cursors.Hand;
            }

            foreach (var corner in Enum.GetValues<ItemCorner>())
            {
                if (Near(display, ToDisplay(ItemGeometry.CornerPoint(view.LiveRect, view.LiveRotation, corner))))
                {
                    return Cursors.SizeAll;
                }
            }

            foreach (var edge in Enum.GetValues<ItemEdge>())
            {
                if (Near(display, ToDisplay(ItemGeometry.EdgePoint(view.LiveRect, view.LiveRotation, edge))))
                {
                    return EdgeCursor(edge, view.LiveRotation);
                }
            }
        }

        var canvas = ToCanvas(display);
        var hit = _editor.Document.Items.LastOrDefault(i => ItemGeometry.Contains(i.Rect, i.Rotation, canvas));
        return hit is null ? Cursors.Arrow
            : HotspotAt(hit, canvas) is not null ? Cursors.Hand
            : Cursors.SizeAll;
    }

    private static bool Near(Point a, Point b) => (a - b).Length <= HandleGrabRadius;

    /// <summary>The double arrow closest to the side's direction on screen, once the item is turned.</summary>
    private static Cursor EdgeCursor(ItemEdge edge, double rotation)
    {
        var horizontal = edge is ItemEdge.Left or ItemEdge.Right;
        var turned = ((rotation % 180) + 180) % 180;
        if (turned is > 45 and < 135)
        {
            horizontal = !horizontal;
        }

        return horizontal ? Cursors.SizeWE : Cursors.SizeNS;
    }

    private CanvasPoint ToCanvas(Point display) =>
        new((display.X - _offsetX) / _scale, (display.Y - _offsetY) / _scale);

    private Point ToDisplay(CanvasPoint canvas) =>
        new((canvas.X * _scale) + _offsetX, (canvas.Y * _scale) + _offsetY);
}
