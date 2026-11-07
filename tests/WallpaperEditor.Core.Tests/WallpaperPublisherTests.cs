using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Tests;

public sealed class WallpaperPublisherTests : IDisposable
{
    private static readonly MonitorInfo Vertical = new(new MonitorId("vertical"), "HDMI", new PixelRect(2560, -452, 3640, 1468));
    private static readonly MonitorInfo Ultrawide = new(new MonitorId("ultrawide"), "NEXUS", new PixelRect(0, 0, 2560, 1080));

    private readonly string _root = Directory.CreateTempSubdirectory("we-tests-").FullName;
    private readonly FakeWindows _windows = new(Vertical, Ultrawide);
    private readonly FakeRenderer _renderer = new();
    private readonly AppStateStore _store;
    private readonly WallpaperPublisher _publisher;
    private readonly string _source;
    private readonly string _original;

    public WallpaperPublisherTests()
    {
        _store = new AppStateStore(Path.Combine(_root, "state.json"));
        _publisher = new WallpaperPublisher(_windows, _windows, _renderer, _store, Path.Combine(_root, "published"));
        _source = Touch("source.jpg");
        _original = Touch("original.jpg");
        _windows.Wallpapers[Vertical.Id] = _original;
        _windows.Wallpapers[Ultrawide.Id] = _original;
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Publishing_renders_at_the_exact_size_of_that_monitor()
    {
        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fit);

        var request = Assert.Single(_renderer.Requests);
        Assert.Equal(new PixelSize(1080, 1920), request.Target);
        Assert.Equal(FitMode.Fit, request.Fit);
    }

    [Fact]
    public async Task Publishing_changes_only_the_chosen_monitor()
    {
        var assignment = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);

        Assert.Equal(assignment.PublishedFile, _windows.Wallpapers[Vertical.Id]);
        Assert.Equal(_original, _windows.Wallpapers[Ultrawide.Id]);
        Assert.Single(_windows.SetCalls);
    }

    [Fact]
    public async Task The_position_setting_is_never_touched()
    {
        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Stretch);

        Assert.Equal(0, _windows.SetPositionCalls);
    }

    [Fact]
    public async Task Each_publish_uses_a_new_file_name()
    {
        var first = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        var second = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);

        Assert.NotEqual(first.PublishedFile, second.PublishedFile);
    }

    [Fact]
    public async Task At_most_the_current_and_the_previous_file_are_kept()
    {
        MonitorAssignment last = null!;
        for (var i = 0; i < 5; i++)
        {
            last = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        }

        var files = Directory.GetFiles(Path.GetDirectoryName(last.PublishedFile!)!);
        Assert.Equal(2, files.Length);
        Assert.Contains(last.PublishedFile, files);
    }

    [Fact]
    public async Task The_assignment_survives_reloading_the_state()
    {
        var assignment = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Center);

        var loaded = new AppStateStore(Path.Combine(_root, "state.json")).Load().Monitors[Vertical.Id.Value];

        Assert.Equal(assignment, loaded);
        Assert.Equal(new PixelSize(1080, 1920), loaded.PublishedSize);
        Assert.Equal(_original, loaded.OriginalWallpaper);
    }

    [Fact]
    public async Task The_original_is_saved_before_the_wallpaper_changes()
    {
        string? originalAtTheMomentOfTheChange = null;
        _windows.BeforeSet = () =>
            originalAtTheMomentOfTheChange = _store.Load().Monitors[Vertical.Id.Value].OriginalWallpaper;

        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);

        Assert.Equal(_original, originalAtTheMomentOfTheChange);
    }

    [Fact]
    public async Task Publishing_twice_keeps_the_first_original()
    {
        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        var second = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fit);

        Assert.Equal(_original, second.OriginalWallpaper);
    }

    [Fact]
    public async Task A_wallpaper_changed_outside_the_app_becomes_the_new_original()
    {
        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        var chosenInSettings = Touch("settings.jpg");
        _windows.Wallpapers[Vertical.Id] = chosenInSettings;

        var second = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);

        Assert.Equal(chosenInSettings, second.OriginalWallpaper);
    }

    [Fact]
    public async Task A_rendering_failure_leaves_the_monitor_and_the_state_untouched()
    {
        _renderer.Failure = new ImageLoadException(_source);

        await Assert.ThrowsAsync<ImageLoadException>(() => _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill));

        Assert.Equal(_original, _windows.Wallpapers[Vertical.Id]);
        Assert.Empty(_store.Load().Monitors);
        Assert.Empty(PublishedFiles());
    }

    [Fact]
    public async Task A_failure_from_windows_keeps_the_previous_publish_and_removes_the_new_file()
    {
        var first = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        _windows.BeforeSet = () => throw new IOException("refused");

        await Assert.ThrowsAsync<IOException>(() => _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fit));

        Assert.Equal(first.PublishedFile, _windows.Wallpapers[Vertical.Id]);
        Assert.Equal(first, _store.Load().Monitors[Vertical.Id.Value]);
        Assert.Equal(first.PublishedFile, Assert.Single(PublishedFiles()));
    }

    [Fact]
    public async Task Span_mode_blocks_publishing()
    {
        _windows.Position = WallpaperPosition.Span;

        await Assert.ThrowsAsync<SpanModeActiveException>(() => _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill));

        Assert.Empty(_windows.SetCalls);
    }

    [Fact]
    public async Task A_disconnected_monitor_cannot_be_published_to()
    {
        var gone = new MonitorId("gone");

        await Assert.ThrowsAsync<MonitorUnavailableException>(() => _publisher.PublishAsync(gone, _source, FitMode.Fill));

        Assert.Empty(_windows.SetCalls);
    }

    [Fact]
    public async Task Restoring_puts_back_the_original_and_forgets_the_monitor()
    {
        var assignment = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);

        _publisher.RestoreOriginal(Vertical.Id);

        Assert.Equal(_original, _windows.Wallpapers[Vertical.Id]);
        Assert.Null(_publisher.GetAssignment(Vertical.Id));
        Assert.False(File.Exists(assignment.PublishedFile));
    }

    [Fact]
    public void Restoring_a_monitor_the_app_never_touched_is_refused()
    {
        Assert.Throws<OriginalWallpaperUnavailableException>(() => _publisher.RestoreOriginal(Vertical.Id));

        Assert.Empty(_windows.SetCalls);
    }

    [Fact]
    public async Task Restoring_when_the_original_file_is_gone_keeps_the_current_wallpaper()
    {
        var assignment = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        File.Delete(_original);

        Assert.Throws<OriginalWallpaperUnavailableException>(() => _publisher.RestoreOriginal(Vertical.Id));

        Assert.Equal(assignment.PublishedFile, _windows.Wallpapers[Vertical.Id]);
        Assert.NotNull(_publisher.GetAssignment(Vertical.Id));
    }

    [Fact]
    public async Task A_resolution_change_is_noticed()
    {
        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        var rotated = Vertical with { Bounds = new PixelRect(2560, 0, 4480, 1080) };

        Assert.False(_publisher.NeedsRepublish(Vertical));
        Assert.True(_publisher.NeedsRepublish(rotated));
    }

    [Fact]
    public async Task A_resolution_change_does_not_matter_if_the_wallpaper_is_no_longer_ours()
    {
        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);
        _windows.Wallpapers[Vertical.Id] = _original;
        var rotated = Vertical with { Bounds = new PixelRect(2560, 0, 4480, 1080) };

        Assert.False(_publisher.NeedsRepublish(rotated));
    }

    [Fact]
    public async Task A_project_is_rendered_at_the_size_of_the_monitor_and_remembered()
    {
        var project = Path.Combine(_root, "Minha mesa");

        var assignment = await PublishProject(project, revision: 7);

        var request = Assert.Single(_renderer.DocumentRequests);
        Assert.Equal(new PixelSize(1080, 1920), request.Target);
        Assert.Equal(new PixelSize(2560, 1080), request.Canvas);
        Assert.Equal(project, assignment.ProjectPath);
        Assert.Equal(7, assignment.ProjectRevision);
        Assert.Null(assignment.SourcePath);
        Assert.Equal(assignment.PublishedFile, _windows.Wallpapers[Vertical.Id]);
        Assert.Equal(_original, _windows.Wallpapers[Ultrawide.Id]);
    }

    [Fact]
    public async Task Static_and_project_publishes_replace_each_other_cleanly()
    {
        var project = Path.Combine(_root, "Minha mesa");
        await PublishProject(project, revision: 1);

        var asStatic = await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fit);
        Assert.Null(asStatic.ProjectPath);
        Assert.Null(asStatic.ProjectRevision);
        Assert.Empty(_publisher.AssignmentsOf(project));

        var asProject = await PublishProject(project, revision: 2);
        Assert.Null(asProject.SourcePath);
        Assert.Equal(_original, asProject.OriginalWallpaper);
    }

    [Fact]
    public async Task The_monitors_showing_a_project_can_be_found()
    {
        var project = Path.Combine(_root, "Minha mesa");
        await PublishProject(project, revision: 3);

        var assignment = Assert.Single(_publisher.AssignmentsOf(project));

        Assert.Equal(Vertical.Id.Value, assignment.MonitorId);
        Assert.Empty(_publisher.AssignmentsOf(Path.Combine(_root, "Outro")));
    }

    [Fact]
    public async Task A_rendering_failure_while_publishing_a_project_changes_nothing()
    {
        _renderer.Failure = new ImageLoadException("foto.jpg");

        await Assert.ThrowsAsync<ImageLoadException>(() => PublishProject(Path.Combine(_root, "Minha mesa"), revision: 1));

        Assert.Equal(_original, _windows.Wallpapers[Vertical.Id]);
        Assert.Empty(_store.Load().Monitors);
    }

    [Fact]
    public void The_last_project_is_remembered()
    {
        Assert.Null(_publisher.LastProjectPath);

        _publisher.RememberProject(@"C:\projetos\mesa");

        Assert.Equal(@"C:\projetos\mesa", _publisher.LastProjectPath);
    }

    [Fact]
    public async Task Publishing_keeps_the_last_project()
    {
        _publisher.RememberProject(@"C:\projetos\mesa");

        await _publisher.PublishAsync(Vertical.Id, _source, FitMode.Fill);

        Assert.Equal(@"C:\projetos\mesa", _publisher.LastProjectPath);
    }

    [Fact]
    public void A_damaged_state_file_is_set_aside_instead_of_stopping_the_app()
    {
        var path = Path.Combine(_root, "state.json");
        File.WriteAllText(path, "{ not json");

        var state = _store.Load();

        Assert.Empty(state.Monitors);
        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_root, "state.json.*.unreadable"));
    }

    [Fact]
    public void A_state_from_a_newer_version_is_not_overwritten()
    {
        var path = Path.Combine(_root, "state.json");
        File.WriteAllText(path, """{ "FormatVersion": 99, "Monitors": {} }""");

        var state = _store.Load();

        Assert.Empty(state.Monitors);
        Assert.Single(Directory.GetFiles(_root, "state.json.*.unreadable"));
    }

    private Task<MonitorAssignment> PublishProject(string project, int revision)
    {
        var document = new WallpaperDocument(new BackgroundSpec(new AssetRef("a.png", "a.png"), FitMode.Fill), []);
        return _publisher.PublishProjectAsync(Vertical.Id, project, revision, document, new PixelSize(2560, 1080), name => name);
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, name);
        return path;
    }

    private string[] PublishedFiles()
    {
        var published = Path.Combine(_root, "published");
        return Directory.Exists(published) ? Directory.GetFiles(published, "*", SearchOption.AllDirectories) : [];
    }

    private sealed class FakeRenderer : IWallpaperRenderer
    {
        public List<StaticImageRequest> Requests { get; } = [];

        public Exception? Failure { get; set; }

        public Task RenderToFileAsync(StaticImageRequest request, string outputPath, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Requests.Add(request);
            File.WriteAllText(outputPath, "png");
            return Task.CompletedTask;
        }

        public List<DocumentRenderRequest> DocumentRequests { get; } = [];

        public Task RenderDocumentToFileAsync(DocumentRenderRequest request, string outputPath, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            DocumentRequests.Add(request);
            File.WriteAllText(outputPath, "png");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWindows(params MonitorInfo[] monitors) : IMonitorService, IWallpaperService
    {
        public Dictionary<MonitorId, string?> Wallpapers { get; } = [];

        public List<(MonitorId Monitor, string Path)> SetCalls { get; } = [];

        public WallpaperPosition Position { get; set; } = WallpaperPosition.Fill;

        public int SetPositionCalls { get; private set; }

        public Action? BeforeSet { get; set; }

        public IReadOnlyList<MonitorInfo> GetMonitors() => monitors;

        public string? GetWallpaper(MonitorId monitor) => Wallpapers.GetValueOrDefault(monitor);

        public void SetWallpaper(MonitorId monitor, string imagePath)
        {
            BeforeSet?.Invoke();
            SetCalls.Add((monitor, imagePath));
            Wallpapers[monitor] = imagePath;
        }

        public WallpaperPosition GetPosition() => Position;

        public void SetPosition(WallpaperPosition position)
        {
            SetPositionCalls++;
            Position = position;
        }

        public SlideshowState GetSlideshowState() => SlideshowState.Enabled;
    }
}
