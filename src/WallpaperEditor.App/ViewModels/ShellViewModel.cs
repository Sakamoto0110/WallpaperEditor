using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Core.Wallpapers;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.ViewModels;

public interface IProjectNavigator
{
    void NewProject(MonitorInfo? forMonitor);

    void OpenProject();

    void OpenProject(string path);
}

/// <summary>Which screen is showing, and the open project's lifetime.</summary>
public sealed partial class ShellViewModel : ObservableObject, IProjectNavigator, IDisposable
{
    private readonly IMonitorService _monitors;
    private readonly IWallpaperService _wallpapers;
    private readonly WallpaperPublisher _publisher;
    private readonly WpfWallpaperRenderer _renderer;
    private readonly IDialogs _dialogs;
    private readonly string _projectsRoot;
    private readonly MainViewModel _monitorsScreen;
    private EditorViewModel? _editor;

    public ShellViewModel(
        IMonitorService monitors,
        IWallpaperService wallpapers,
        WallpaperPublisher publisher,
        WpfWallpaperRenderer renderer,
        IDialogs dialogs,
        string projectsRoot)
    {
        _monitors = monitors;
        _wallpapers = wallpapers;
        _publisher = publisher;
        _renderer = renderer;
        _dialogs = dialogs;
        _projectsRoot = projectsRoot;
        _monitorsScreen = new MainViewModel(monitors, wallpapers, publisher, renderer, dialogs, this);
        Current = _monitorsScreen;
    }

    [ObservableProperty]
    public partial object Current { get; set; }

    /// <summary>Opens the last project, since the usual reason to open the app is a small change to it.</summary>
    public void Start(string? project)
    {
        var path = project ?? _publisher.LastProjectPath;
        if (path is not null && Directory.Exists(path))
        {
            OpenProject(path);
        }
    }

    public void OnDisplayChanged()
    {
        _monitorsScreen.Refresh();
        _editor?.RefreshMonitors();
    }

    public void NewProject(MonitorInfo? forMonitor)
    {
        var name = _dialogs.AskText("Novo wallpaper dinâmico", "Nome do projeto:", "Minha mesa");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var image = _dialogs.ChooseImage("Imagem de fundo");
        if (image is null)
        {
            return;
        }

        if (new DocumentImages(path => path).SizeOf(new AssetRef(image, Path.GetFileName(image))) is null)
        {
            _dialogs.Inform("Novo wallpaper dinâmico", "Não foi possível ler essa imagem.");
            return;
        }

        var canvas = (forMonitor ?? _monitors.GetMonitors().FirstOrDefault(m => m.IsPrimary))?.Bounds.Size ?? new PixelSize(1920, 1080);

        try
        {
            var project = ProjectFolder.Create(UniqueFolder(name), name.Trim(), canvas, image, FitMode.Fill);
            ShowEditor(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectUnavailableException)
        {
            _dialogs.Inform("Novo wallpaper dinâmico", $"Não foi possível criar o projeto: {ex.Message}");
        }
    }

    public void OpenProject()
    {
        if (_dialogs.ChooseFolder("Pasta do projeto") is { } folder)
        {
            OpenProject(folder);
        }
    }

    public void OpenProject(string path)
    {
        try
        {
            ShowEditor(ProjectFolder.Open(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectUnavailableException)
        {
            _publisher.RememberProject(null);
            _dialogs.Inform("Abrir projeto", $"Não foi possível abrir o projeto: {ex.Message}");
            ShowMonitors();
        }
    }

    public void ShowMonitors()
    {
        CloseEditor();
        _publisher.RememberProject(null);
        _monitorsScreen.Refresh();
        Current = _monitorsScreen;
    }

    public void Dispose() => CloseEditor();

    private void ShowEditor(ProjectFolder project)
    {
        CloseEditor();
        _editor = new EditorViewModel(project, _monitors, _wallpapers, _publisher, _renderer, _dialogs, ShowMonitors);
        _publisher.RememberProject(project.FolderPath);
        Current = _editor;
    }

    private void CloseEditor()
    {
        _editor?.Dispose();
        _editor = null;
    }

    /// <summary>Never reuses a folder: a new project must not land inside something that already exists.</summary>
    private string UniqueFolder(string name)
    {
        var safe = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)).TrimEnd('.', ' ');
        if (safe.Length == 0)
        {
            safe = "Projeto";
        }

        var candidate = Path.Combine(_projectsRoot, safe);
        for (var i = 2; Directory.Exists(candidate); i++)
        {
            candidate = Path.Combine(_projectsRoot, $"{safe} {i}");
        }

        return candidate;
    }
}
