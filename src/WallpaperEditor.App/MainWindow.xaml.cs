using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Wallpapers;
using WallpaperEditor.Platform.Windows;

namespace WallpaperEditor.App;

/// <summary>
/// Phase 2 proof that one monitor can be changed on its own. Code-behind is deliberate here: the MVVM
/// structure arrives with the real screens in phase 3, and this window is replaced then. The image is
/// also applied as chosen for now; rendering to the monitor's exact size is phase 3 work.
/// </summary>
public partial class MainWindow : Window
{
    private readonly DesktopWallpaperService _service = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadMonitors();
    }

    private void LoadMonitors(MonitorId? keepSelected = null)
    {
        try
        {
            var rows = _service.GetMonitors().Select(ToRow).ToList();
            MonitorList.ItemsSource = rows;
            MonitorList.SelectedItem = rows.FirstOrDefault(r => r.Monitor.Id == keepSelected);
            StatusText.Text = DescribeGlobalState();
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidCastException)
        {
            StatusText.Text = $"Não foi possível ler os monitores: {ex.Message}";
        }
    }

    private MonitorRow ToRow(MonitorInfo monitor)
    {
        var flags = string.Join(", ", new[]
        {
            monitor.IsPrimary ? "primário" : null,
            monitor.IsPortrait ? "vertical" : null,
        }.Where(f => f is not null));

        return new MonitorRow(
            monitor,
            flags.Length == 0 ? monitor.Name : $"{monitor.Name} ({flags})",
            $"{monitor.Bounds.Width} x {monitor.Bounds.Height}, em ({monitor.Bounds.Left}, {monitor.Bounds.Top})",
            _service.GetWallpaper(monitor.Id) ?? "Sem wallpaper");
    }

    private string DescribeGlobalState()
    {
        var warnings = new List<string>();
        if (_service.GetPosition() == WallpaperPosition.Span)
        {
            warnings.Add("O Windows está em modo span: um wallpaper por monitor não terá efeito.");
        }

        if (_service.GetSlideshowState().HasFlag(SlideshowState.Slideshow))
        {
            warnings.Add("Há um slideshow do Windows ativo, que vai sobrescrever o wallpaper.");
        }

        return string.Join(" ", warnings);
    }

    private void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorList.SelectedItem is not MonitorRow row)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"Imagem para {row.Monitor.Name}",
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp|Todos os arquivos|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _service.SetWallpaper(row.Monitor.Id, dialog.FileName);
            LoadMonitors(row.Monitor.Id);
            StatusText.Text = $"Aplicado em {row.Monitor.Name}.";
        }
        catch (Exception ex) when (ex is MonitorUnavailableException or FileNotFoundException or System.Runtime.InteropServices.COMException)
        {
            StatusText.Text = $"Não foi possível aplicar: {ex.Message}";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        LoadMonitors((MonitorList.SelectedItem as MonitorRow)?.Monitor.Id);

    private void MonitorList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ChooseImageButton.IsEnabled = MonitorList.SelectedItem is not null;

    private sealed record MonitorRow(MonitorInfo Monitor, string Title, string Details, string Wallpaper);
}
