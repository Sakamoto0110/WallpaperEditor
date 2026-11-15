using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WallpaperEditor.App.ViewModels;

namespace WallpaperEditor.App.Views;

/// <summary>
/// A window over the desktop showing one dynamic wallpaper, large enough to click comfortably but not covering
/// everything. Closing it lets the project go, so the editor can open it.
/// </summary>
public sealed class InteractionWindow : Window
{
    /// <summary>Share of the screen's work area the wallpaper may take, leaving the desktop visible around it.</summary>
    private const double ScreenShare = 0.72;

    private readonly InteractionViewModel _model;

    public InteractionWindow(InteractionViewModel model)
    {
        _model = model;
        DataContext = model;
        Title = model.Title;
        Icon = new BitmapImage(new Uri("pack://application:,,,/assets/neko-purple-light.ico"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;

        var surface = new InteractionSurface();
        surface.Attach(model);

        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        status.SetBinding(TextBlock.TextProperty, nameof(InteractionViewModel.Status));

        var editor = new Button { Content = "Abrir no editor", Margin = new Thickness(12, 0, 0, 0) };
        editor.Click += (_, _) => CloseForEditor();

        var bar = new DockPanel { Margin = new Thickness(12, 8, 12, 8) };
        DockPanel.SetDock(editor, Dock.Right);
        bar.Children.Add(editor);
        bar.Children.Add(status);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(surface);
        Content = root;

        SizeToFit(surface);

        // A screen reader names the window after the wallpaper, not after the view model's type.
        AutomationProperties.SetName(this, model.Title);
    }

    /// <summary>
    /// Closed to go to the editor. It is started only once the project is let go, or it would open read-only.
    /// </summary>
    public bool EditorRequested { get; private set; }

    public void CloseForEditor()
    {
        EditorRequested = true;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>The wallpaper's own shape, as large as the share allows; the window can still be resized freely.</summary>
    private void SizeToFit(FrameworkElement surface)
    {
        var area = SystemParameters.WorkArea;
        var canvas = _model.Canvas;
        var scale = Math.Min(area.Width * ScreenShare / canvas.Width, area.Height * ScreenShare / canvas.Height);

        surface.Width = Math.Round(canvas.Width * scale);
        surface.Height = Math.Round(canvas.Height * scale);
        SizeToContent = SizeToContent.WidthAndHeight;

        // Measured once at that size, then free: resizing scales the wallpaper, with bars where the shape differs.
        ContentRendered += (_, _) =>
        {
            SizeToContent = SizeToContent.Manual;
            surface.Width = double.NaN;
            surface.Height = double.NaN;
        };
    }
}
