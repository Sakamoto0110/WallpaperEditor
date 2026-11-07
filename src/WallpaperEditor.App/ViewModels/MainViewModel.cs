using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.ViewModels;

public sealed record FitOption(FitMode Mode, string Label);

public sealed partial class MainViewModel : ObservableObject
{
    private const double MapAreaWidth = 700;
    private const double MapAreaHeight = 170;
    private const double MapGap = 3;
    private const double PreviewMaxSide = 300;

    private readonly IMonitorService _monitors;
    private readonly IWallpaperService _wallpapers;
    private readonly WallpaperPublisher _publisher;
    private readonly WpfWallpaperRenderer _renderer;
    private readonly IDialogs _dialogs;
    private readonly IProjectNavigator _projects;
    private CancellationTokenSource? _previewCancellation;

    public MainViewModel(
        IMonitorService monitors,
        IWallpaperService wallpapers,
        WallpaperPublisher publisher,
        WpfWallpaperRenderer renderer,
        IDialogs dialogs,
        IProjectNavigator projects)
    {
        _monitors = monitors;
        _wallpapers = wallpapers;
        _publisher = publisher;
        _renderer = renderer;
        _dialogs = dialogs;
        _projects = projects;
    }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    public IReadOnlyList<FitOption> FitOptions { get; } =
    [
        new(FitMode.Fill, "Preencher (corta o que sobrar)"),
        new(FitMode.Fit, "Ajustar (mostra tudo, com barras)"),
        new(FitMode.Center, "Centralizar (tamanho original)"),
        new(FitMode.Stretch, "Esticar (pode distorcer)"),
    ];

    [ObservableProperty]
    public partial double MapWidth { get; set; }

    [ObservableProperty]
    public partial double MapHeight { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseImageCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial MonitorViewModel? SelectedMonitor { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    public partial double PreviewWidth { get; set; } = PreviewMaxSide;

    [ObservableProperty]
    public partial double PreviewHeight { get; set; } = PreviewMaxSide;

    [ObservableProperty]
    public partial string? PreviewError { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial string? Warning { get; set; }

    /// <summary>Reads the monitors again, keeping the selection and what was chosen but not applied.</summary>
    [RelayCommand]
    public void Refresh()
    {
        IReadOnlyList<MonitorInfo> current;
        try
        {
            current = _monitors.GetMonitors();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Status = $"Não foi possível ler os monitores: {ex.Message}";
            return;
        }

        var selectedId = SelectedMonitor?.Info.Id;
        var known = Monitors.ToDictionary(m => m.Info.Id);

        Monitors.Clear();
        foreach (var info in current)
        {
            if (known.TryGetValue(info.Id, out var existing))
            {
                existing.Info = info;
                Monitors.Add(existing);
            }
            else
            {
                Monitors.Add(CreateMonitor(info));
            }
        }

        LayOutMap();
        foreach (var monitor in Monitors)
        {
            UpdateFromWindows(monitor);
            _ = LoadThumbnailAsync(monitor);
        }

        SelectedMonitor = Monitors.FirstOrDefault(m => m.Info.Id == selectedId)
            ?? Monitors.FirstOrDefault(m => m.Info.IsPrimary)
            ?? Monitors.FirstOrDefault();

        UpdateWarning();
    }

    private MonitorViewModel CreateMonitor(MonitorInfo info)
    {
        var monitor = new MonitorViewModel(info);
        if (_publisher.GetAssignment(info.Id) is { SourcePath: { } source } assignment)
        {
            monitor.SourcePath = source;
            monitor.Fit = assignment.Fit;
        }

        monitor.PropertyChanged += OnMonitorChanged;
        return monitor;
    }

    private void OnMonitorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, SelectedMonitor))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(MonitorViewModel.SourcePath):
            case nameof(MonitorViewModel.Fit):
                ApplyCommand.NotifyCanExecuteChanged();
                _ = UpdatePreviewAsync();
                break;

            // Thumbnails load after the selection is made, and stand in until an image is chosen.
            case nameof(MonitorViewModel.Thumbnail) when SelectedMonitor?.SourcePath is null:
                Preview = SelectedMonitor?.Thumbnail;
                break;
        }
    }

    partial void OnSelectedMonitorChanged(MonitorViewModel? value) => _ = UpdatePreviewAsync();

    private bool HasSelection => SelectedMonitor is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ChooseImage()
    {
        if (SelectedMonitor is { } monitor && _dialogs.ChooseImage($"Imagem para {monitor.Info.Name}") is { } file)
        {
            monitor.SourcePath = file;
            Status = null;
        }
    }

    [RelayCommand]
    private void NewProject() => _projects.NewProject(SelectedMonitor?.Info);

    [RelayCommand]
    private void OpenProject() => _projects.OpenProject();

    [RelayCommand]
    private void EditMonitorProject()
    {
        if (SelectedMonitor?.ProjectPath is { } path)
        {
            _projects.OpenProject(path);
        }
    }

    private bool CanApply => !IsBusy && SelectedMonitor?.SourcePath is not null;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (SelectedMonitor is not { SourcePath: { } source } monitor)
        {
            return;
        }

        IsBusy = true;
        Status = "Aplicando...";
        try
        {
            await PublishAsync(monitor, source);
        }
        catch (SpanModeActiveException)
        {
            if (_dialogs.Confirm(
                "Modo span ativo",
                "O Windows está estendendo uma única imagem por todos os monitores, e assim não há wallpaper por monitor.\n\n"
                + "Mudar o Windows para o modo Preencher? Isso afeta os outros monitores."))
            {
                await TryAsync(async () =>
                {
                    _wallpapers.SetPosition(WallpaperPosition.Fill);
                    await PublishAsync(monitor, source);
                });
            }
            else
            {
                Status = "Nada foi alterado.";
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Status = Describe(ex);
        }
        finally
        {
            IsBusy = false;
            UpdateWarning();
        }
    }

    private async Task PublishAsync(MonitorViewModel monitor, string source)
    {
        await _publisher.PublishAsync(monitor.Info.Id, source, monitor.Fit);
        UpdateFromWindows(monitor);
        _ = LoadThumbnailAsync(monitor);
        Status = $"Aplicado em {monitor.Info.Name}.";
    }

    private async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Status = Describe(ex);
        }
    }

    private bool CanRestoreSelected => !IsBusy && SelectedMonitor?.CanRestore == true;

    [RelayCommand(CanExecute = nameof(CanRestoreSelected))]
    private void Restore()
    {
        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        try
        {
            _publisher.RestoreOriginal(monitor.Info.Id);
            monitor.SourcePath = null;
            monitor.Fit = FitMode.Fill;
            UpdateFromWindows(monitor);
            _ = LoadThumbnailAsync(monitor);
            Status = $"Wallpaper original de volta em {monitor.Info.Name}.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Status = Describe(ex);
        }

        RestoreCommand.NotifyCanExecuteChanged();
        UpdateWarning();
    }

    private void UpdateFromWindows(MonitorViewModel monitor)
    {
        monitor.CurrentWallpaper = _wallpapers.GetWallpaper(monitor.Info.Id);
        var assignment = _publisher.GetAssignment(monitor.Info.Id);
        monitor.CanRestore = assignment?.OriginalWallpaper is not null;
        monitor.ProjectPath = assignment?.ProjectPath;
        monitor.NeedsRepublish = _publisher.NeedsRepublish(monitor.Info);
        if (ReferenceEquals(monitor, SelectedMonitor))
        {
            RestoreCommand.NotifyCanExecuteChanged();
        }
    }

    private void UpdateWarning()
    {
        var warnings = new List<string>();
        if (_wallpapers.GetPosition() == WallpaperPosition.Span)
        {
            warnings.Add("O Windows está em modo span: uma imagem atravessa todos os monitores.");
        }

        if (_wallpapers.GetSlideshowState().HasFlag(SlideshowState.Slideshow))
        {
            warnings.Add("Há um slideshow do Windows ativo, que vai sobrescrever o wallpaper.");
        }

        foreach (var monitor in Monitors.Where(m => m.NeedsRepublish))
        {
            warnings.Add($"A resolução de {monitor.Info.Name} mudou. Aplique de novo para ajustar a imagem.");
        }

        Warning = warnings.Count == 0 ? null : string.Join("\n", warnings);
    }

    private void LayOutMap()
    {
        if (Monitors.Count == 0)
        {
            MapWidth = MapHeight = 0;
            return;
        }

        var left = Monitors.Min(m => m.Info.Bounds.Left);
        var top = Monitors.Min(m => m.Info.Bounds.Top);
        var right = Monitors.Max(m => m.Info.Bounds.Right);
        var bottom = Monitors.Max(m => m.Info.Bounds.Bottom);
        var scale = Math.Min(MapAreaWidth / (right - left), MapAreaHeight / (bottom - top));

        foreach (var monitor in Monitors)
        {
            var bounds = monitor.Info.Bounds;
            monitor.MapLeft = (bounds.Left - left) * scale;
            monitor.MapTop = (bounds.Top - top) * scale;
            monitor.MapWidth = Math.Max(1, (bounds.Width * scale) - MapGap);
            monitor.MapHeight = Math.Max(1, (bounds.Height * scale) - MapGap);
        }

        MapWidth = (right - left) * scale;
        MapHeight = (bottom - top) * scale;
    }

    private async Task LoadThumbnailAsync(MonitorViewModel monitor)
    {
        if (monitor.CurrentWallpaper is not { } path)
        {
            monitor.Thumbnail = null;
            return;
        }

        try
        {
            monitor.Thumbnail = await _renderer.RenderPreviewAsync(
                new StaticImageRequest(path, FitMode.Fill, ThumbnailSize(monitor.Info.Bounds.Size)));
        }
        catch (ImageLoadException)
        {
            // The wallpaper file may be gone: Windows keeps its own copy, so this is not an error.
            monitor.Thumbnail = null;
        }
    }

    /// <summary>Twice the map size, so the picture stays sharp on a high-DPI display.</summary>
    private static PixelSize ThumbnailSize(PixelSize monitor)
    {
        var scale = 2 * Math.Min(MapAreaWidth / monitor.Width, MapAreaHeight / monitor.Height);
        return new PixelSize(Math.Max(1, (int)(monitor.Width * scale)), Math.Max(1, (int)(monitor.Height * scale)));
    }

    /// <summary>Redraws the preview, abandoning any previous drawing still in progress.</summary>
    private async Task UpdatePreviewAsync()
    {
        _previewCancellation?.Cancel();
        _previewCancellation = new CancellationTokenSource();
        var cancellation = _previewCancellation.Token;

        Preview = null;
        PreviewError = null;

        if (SelectedMonitor is not { } monitor)
        {
            return;
        }

        var size = monitor.Info.Bounds.Size;
        var scale = PreviewMaxSide / Math.Max(size.Width, size.Height);
        PreviewWidth = size.Width * scale;
        PreviewHeight = size.Height * scale;

        if (monitor.SourcePath is not { } source)
        {
            Preview = monitor.Thumbnail;
            return;
        }

        try
        {
            // Twice the displayed size keeps the preview sharp on a high-DPI display.
            var target = new PixelSize((int)(PreviewWidth * 2), (int)(PreviewHeight * 2));
            var bitmap = await _renderer.RenderPreviewAsync(new StaticImageRequest(source, monitor.Fit, target), cancellation);
            if (!cancellation.IsCancellationRequested)
            {
                Preview = bitmap;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer preview replaced this one.
        }
        catch (ImageLoadException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                PreviewError = "Não foi possível ler esta imagem.";
            }
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is ImageLoadException or MonitorUnavailableException or OriginalWallpaperUnavailableException
            or COMException or IOException or UnauthorizedAccessException;

    private static string Describe(Exception ex) => ex switch
    {
        ImageLoadException => "Não foi possível ler esta imagem. O arquivo pode ter sido movido ou não ser uma imagem. O wallpaper não mudou.",
        MonitorUnavailableException => "Este monitor não está mais conectado. O wallpaper não mudou.",
        OriginalWallpaperUnavailableException { OriginalPath: { } path } => $"O wallpaper original não existe mais em {path}.",
        OriginalWallpaperUnavailableException => "Este monitor não tinha uma imagem de wallpaper para restaurar.",
        _ => $"Não foi possível concluir: {ex.Message}",
    };
}
