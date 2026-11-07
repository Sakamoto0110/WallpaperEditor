using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.App.ViewModels;

/// <summary>One monitor, including what the user has chosen for it but not applied yet.</summary>
public sealed partial class MonitorViewModel(MonitorInfo info) : ObservableObject
{
    [ObservableProperty]
    public partial MonitorInfo Info { get; set; } = info;

    /// <summary>Position and size on the on-screen map, in device-independent pixels.</summary>
    [ObservableProperty]
    public partial double MapLeft { get; set; }

    [ObservableProperty]
    public partial double MapTop { get; set; }

    [ObservableProperty]
    public partial double MapWidth { get; set; }

    [ObservableProperty]
    public partial double MapHeight { get; set; }

    /// <summary>A small picture of the wallpaper the monitor currently has.</summary>
    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }

    [ObservableProperty]
    public partial string? CurrentWallpaper { get; set; }

    [ObservableProperty]
    public partial string? SourcePath { get; set; }

    [ObservableProperty]
    public partial FitMode Fit { get; set; }

    [ObservableProperty]
    public partial bool CanRestore { get; set; }

    /// <summary>The dynamic wallpaper this monitor is showing, if any.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(ProjectButtonLabel))]
    public partial string? ProjectPath { get; set; }

    public bool HasProject => ProjectPath is not null;

    public string ProjectButtonLabel => $"Editar \"{System.IO.Path.GetFileName(ProjectPath)}\"";

    [ObservableProperty]
    public partial bool NeedsRepublish { get; set; }

    /// <summary>Short enough for the narrow rectangle of a vertical monitor on the map.</summary>
    public string Resolution => $"{Info.Bounds.Width} × {Info.Bounds.Height}";

    public string Details
    {
        get
        {
            var kind = Info.IsPrimary ? " · principal" : Info.IsPortrait ? " · vertical" : string.Empty;
            return $"{Resolution}{kind}";
        }
    }

    partial void OnInfoChanged(MonitorInfo value)
    {
        OnPropertyChanged(nameof(Resolution));
        OnPropertyChanged(nameof(Details));
    }
}
