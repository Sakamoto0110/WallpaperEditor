using System.Security.Cryptography;
using System.Text;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Publishing;

/// <summary>The current Windows setting would make a per-monitor wallpaper meaningless.</summary>
public sealed class SpanModeActiveException()
    : Exception("Windows is spanning one image across all monitors.");

/// <summary>There is nothing to go back to, or the file no longer exists.</summary>
public sealed class OriginalWallpaperUnavailableException(string? path)
    : Exception($"The original wallpaper is not available: {path ?? "(none)"}")
{
    public string? OriginalPath { get; } = path;
}

/// <summary>
/// Turns a chosen image into what one monitor displays, and remembers it. Any failure leaves the
/// monitor and the saved state as they were.
/// </summary>
public sealed class WallpaperPublisher(
    IMonitorService monitors,
    IWallpaperService wallpapers,
    IWallpaperRenderer renderer,
    AppStateStore store,
    string publishedRoot)
{
    /// <summary>
    /// Published files kept per monitor: the current one and the one before it. SetWallpaper returns
    /// before the shell has read the file, so during quick successive changes the previous file may
    /// still be in use. Keeping it costs one PNG on disk.
    /// </summary>
    private const int FilesKeptPerMonitor = 2;

    public Task<MonitorAssignment> PublishAsync(
        MonitorId monitorId,
        string sourcePath,
        FitMode fit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        return PublishCoreAsync(
            monitorId,
            (size, output, cancel) => renderer.RenderToFileAsync(new StaticImageRequest(sourcePath, fit, size), output, cancel),
            assignment => assignment with
            {
                SourcePath = Path.GetFullPath(sourcePath),
                Fit = fit,
                ProjectPath = null,
                ProjectRevision = null,
            },
            cancellationToken);
    }

    /// <summary>Publishes one revision of a project. If the monitor has another shape, the whole canvas adapts.</summary>
    /// <param name="monitorId">The monitor to change.</param>
    /// <param name="projectPath">The project, remembered so the monitor knows what it is showing.</param>
    /// <param name="revision">The revision being published.</param>
    /// <param name="document">The document of that revision.</param>
    /// <param name="canvas">The project's canvas.</param>
    /// <param name="resolveAsset">Finds the project's asset files.</param>
    /// <param name="skipUnreadableAssets">Publish without an asset that cannot be read, at the user's request.</param>
    /// <param name="cancellationToken">Cancels before the wallpaper changes.</param>
    public Task<MonitorAssignment> PublishProjectAsync(
        MonitorId monitorId,
        string projectPath,
        int revision,
        WallpaperDocument document,
        PixelSize canvas,
        Func<string, string> resolveAsset,
        bool skipUnreadableAssets = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(monitorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(resolveAsset);

        return PublishCoreAsync(
            monitorId,
            (size, output, cancel) => renderer.RenderDocumentToFileAsync(
                new DocumentRenderRequest(document, canvas, resolveAsset, size, FitMode.Fill, skipUnreadableAssets), output, cancel),
            assignment => assignment with
            {
                SourcePath = null,
                Fit = FitMode.Fill,
                ProjectPath = Path.GetFullPath(projectPath),
                ProjectRevision = revision,
            },
            cancellationToken);
    }

    /// <summary>The monitors currently showing a project, as far as this machine knows.</summary>
    public IReadOnlyList<MonitorAssignment> AssignmentsOf(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        return store.Load().Monitors.Values
            .Where(a => IsSamePath(a.ProjectPath, full))
            .ToList();
    }

    /// <summary>
    /// The attached monitors that show a project right now: the app published it, and nothing replaced it since,
    /// inside the app or out of it. These are the wallpapers the user can interact with.
    /// </summary>
    public IReadOnlyList<MonitorAssignment> ShownProjects()
    {
        var attached = monitors.GetMonitors().ToDictionary(m => m.Id.Value, StringComparer.OrdinalIgnoreCase);
        return store.Load().Monitors.Values
            .Where(a => a is { ProjectPath: not null, PublishedFile: not null }
                && attached.TryGetValue(a.MonitorId, out var monitor)
                && IsSamePath(wallpapers.GetWallpaper(monitor.Id), a.PublishedFile))
            .ToList();
    }

    public string? LastProjectPath => store.Load().LastProjectPath;

    public void RememberProject(string? projectPath)
    {
        var state = store.Load();
        state.LastProjectPath = projectPath;
        store.Save(state);
    }

    private async Task<MonitorAssignment> PublishCoreAsync(
        MonitorId monitorId,
        Func<PixelSize, string, CancellationToken, Task> render,
        Func<MonitorAssignment, MonitorAssignment> describe,
        CancellationToken cancellationToken)
    {
        var monitor = FindActive(monitorId);
        if (wallpapers.GetPosition() == WallpaperPosition.Span)
        {
            throw new SpanModeActiveException();
        }

        var folder = Directory.CreateDirectory(FolderFor(monitorId)).FullName;
        var temporary = Path.Combine(folder, $".tmp-{Guid.NewGuid():N}.png");
        var file = Path.Combine(folder, $"{Guid.NewGuid():N}.png");

        try
        {
            await render(monitor.Bounds.Size, temporary, cancellationToken);
            File.Move(temporary, file);

            var state = store.Load();
            var assignment = WithOriginalCaptured(state, monitor);

            // Saved before the change: if the app dies right after it, the original is not lost.
            state.Monitors[monitorId.Value] = assignment;
            store.Save(state);

            wallpapers.SetWallpaper(monitorId, file);

            assignment = describe(assignment) with
            {
                PublishedFile = file,
                PublishedSize = monitor.Bounds.Size,
                PublishedAt = DateTimeOffset.UtcNow,
            };
            state.Monitors[monitorId.Value] = assignment;
            store.Save(state);

            RemoveOldFiles(folder, file);
            return assignment;
        }
        catch
        {
            TryDelete(temporary);
            TryDelete(file);
            throw;
        }
    }

    /// <summary>
    /// Puts back what the monitor had before the app first touched it: the original file where it was, or the
    /// app's copy of it if that file is gone.
    /// </summary>
    /// <returns>True if the copy was used, because the original file no longer exists.</returns>
    public bool RestoreOriginal(MonitorId monitorId)
    {
        ArgumentNullException.ThrowIfNull(monitorId);

        FindActive(monitorId);

        var state = store.Load();
        var assignment = state.Monitors.GetValueOrDefault(monitorId.Value);
        var original = assignment?.OriginalWallpaper;
        var copy = assignment?.OriginalCopy;

        // The original where it was comes first: that is the file the user chose, and Windows keeps pointing at it.
        var source = original is not null && File.Exists(original) ? original
            : copy is not null && File.Exists(copy) ? copy
            : throw new OriginalWallpaperUnavailableException(original);

        wallpapers.SetWallpaper(monitorId, source);

        state.Monitors.Remove(monitorId.Value);
        store.Save(state);

        // A copy now on screen stays; one that was not needed goes.
        var usedCopy = IsSamePath(source, copy);
        if (!usedCopy)
        {
            RemoveFiles(OriginalFolderFor(monitorId), keep: null);
        }

        // The shell may still be reading the last published file for a moment; a leftover is harmless
        // and is removed on the next publish.
        RemoveOldFiles(FolderFor(monitorId), keep: null);
        return usedCopy;
    }

    public MonitorAssignment? GetAssignment(MonitorId monitorId) =>
        store.Load().Monitors.GetValueOrDefault(monitorId.Value);

    /// <summary>
    /// True if the monitor's resolution or orientation changed since the app rendered for it, and the
    /// image the app published is still the one on screen.
    /// </summary>
    public bool NeedsRepublish(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return GetAssignment(monitor.Id) is { PublishedFile: { } file, PublishedSize: { } size }
            && size != monitor.Bounds.Size
            && IsSamePath(wallpapers.GetWallpaper(monitor.Id), file);
    }

    private MonitorInfo FindActive(MonitorId monitorId) =>
        monitors.GetMonitors().FirstOrDefault(m => m.Id == monitorId)
            ?? throw new MonitorUnavailableException(monitorId);

    /// <summary>
    /// The current wallpaper counts as the original the first time, and also whenever someone changed
    /// it outside the app: that newer choice is what "restore" should bring back.
    /// </summary>
    private MonitorAssignment WithOriginalCaptured(AppState state, MonitorInfo monitor)
    {
        var current = wallpapers.GetWallpaper(monitor.Id);
        var existing = state.Monitors.GetValueOrDefault(monitor.Id.Value);

        if (existing is null)
        {
            return new MonitorAssignment
            {
                MonitorId = monitor.Id.Value,
                LastKnownName = monitor.Name,
                OriginalWallpaper = current,
                OriginalCopy = KeepCopy(monitor.Id, current),
            };
        }

        if (!IsSamePath(current, existing.PublishedFile))
        {
            return existing with { LastKnownName = monitor.Name, OriginalWallpaper = current, OriginalCopy = KeepCopy(monitor.Id, current) };
        }

        // Same original as before. A state from before copies were kept gets one now, while the file is still there.
        var copy = existing.OriginalCopy is { } kept && File.Exists(kept) ? kept : KeepCopy(monitor.Id, existing.OriginalWallpaper);
        return existing with { LastKnownName = monitor.Name, OriginalCopy = copy };
    }

    /// <summary>
    /// Copies the original into the app's folder, replacing any older copy. Never fails a publish: without a copy,
    /// restoring still works as long as the original file stays where it is.
    /// </summary>
    private string? KeepCopy(MonitorId monitorId, string? original)
    {
        if (original is null || !File.Exists(original))
        {
            return null;
        }

        var folder = OriginalFolderFor(monitorId);
        var target = Path.Combine(folder, Path.GetFileName(original));

        // Already the copy: it went back on screen through a restore, and must not be deleted to copy it onto itself.
        if (IsSamePath(Path.GetFullPath(original), Path.GetFullPath(target)))
        {
            return target;
        }

        var temporary = Path.Combine(folder, $".tmp-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(folder);
            File.Copy(original, temporary);
            RemoveFiles(folder, keep: temporary);
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return null;
        }
    }

    private string FolderFor(MonitorId monitorId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(monitorId.Value.ToUpperInvariant()));
        return Path.Combine(publishedRoot, Convert.ToHexStringLower(hash)[..16]);
    }

    /// <summary>Inside the monitor's folder, where the clean-up of published files never looks.</summary>
    private string OriginalFolderFor(MonitorId monitorId) => Path.Combine(FolderFor(monitorId), "original");

    private static void RemoveFiles(string folder, string? keep)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(folder).Where(f => !IsSamePath(f, keep)))
        {
            TryDelete(file);
        }
    }

    private static void RemoveOldFiles(string folder, string? keep)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var files = new DirectoryInfo(folder).GetFiles("*.png")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var obsolete = keep is null
            ? files
            : files.Where(f => !IsSamePath(f.FullName, keep)).Skip(FilesKeptPerMonitor - 1);

        foreach (var file in obsolete)
        {
            TryDelete(file.FullName);
        }
    }

    private static bool IsSamePath(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    /// <summary>A file that cannot be deleted now only costs disk, and goes away on a later publish.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
