using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Projects;

/// <summary>A project folder cannot be used: not a project, damaged, or from a newer version.</summary>
public sealed class ProjectUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Another instance holds the project, so this one may look but not change anything.</summary>
public sealed class ProjectReadOnlyException() : Exception("This project is open in another window.");

/// <summary>
/// One project on disk. See docs/03-formato-de-projeto.md. Revisions and assets never change once written;
/// only <c>project.json</c> does, and it is replaced as a whole, last, so an interruption at any point leaves
/// a usable project.
/// </summary>
public sealed class ProjectFolder : IDisposable
{
    public const int CurrentFormatVersion = 1;

    private const string ProjectFile = "project.json";
    private const string CompositionFile = "composition.json";
    private const string LockFile = ".lock";
    private const string AssetsFolder = "assets";
    private const string RevisionsFolder = "revisions";

    private readonly FileStream? _lock;
    private ProjectInfo _info;

    private ProjectFolder(string path, ProjectInfo info, FileStream? lockHandle)
    {
        FolderPath = path;
        _info = info;
        _lock = lockHandle;
    }

    public string FolderPath { get; }

    public string Name => _info.Name;

    public PixelSize Canvas => new(_info.Canvas.Width, _info.Canvas.Height);

    public int Head => _info.Head;

    public bool IsReadOnly => _lock is null;

    /// <summary>Set when opening had to work around an interrupted write or a damaged revision.</summary>
    public string? RecoveryNote { get; private set; }

    public static ProjectFolder Create(string path, string name, PixelSize canvas, string backgroundFile, FitMode fit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new ProjectUnavailableException($"The folder is not empty: {path}");
        }

        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.Combine(full, AssetsFolder));
        Directory.CreateDirectory(Path.Combine(full, RevisionsFolder));

        var info = new ProjectInfo(
            CurrentFormatVersion,
            Guid.NewGuid(),
            name,
            new CanvasInfo(canvas.Width, canvas.Height),
            Head: 0,
            DateTimeOffset.UtcNow);

        var project = new ProjectFolder(full, info, AcquireLock(full)
            ?? throw new ProjectUnavailableException($"The folder is in use: {path}"));

        try
        {
            var background = project.ImportAsset(backgroundFile);
            project.Commit(
                new WallpaperDocument(new BackgroundSpec(background, fit), []),
                new RevisionChange(ChangeKinds.Created, "Criou o projeto"));
            return project;
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    /// <summary>Opens a project. If another instance has it, the result is read-only.</summary>
    public static ProjectFolder Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var full = Path.GetFullPath(path);
        var file = Path.Combine(full, ProjectFile);
        if (!File.Exists(file))
        {
            throw new ProjectUnavailableException($"This folder is not a project: {path}");
        }

        ProjectInfo info;
        try
        {
            info = JsonSerializer.Deserialize<ProjectInfo>(File.ReadAllText(file), ProjectJson.Options)
                ?? throw new JsonException("Empty file.");
        }
        catch (JsonException ex)
        {
            throw new ProjectUnavailableException($"The project file is damaged: {file}", ex);
        }

        if (info.FormatVersion != CurrentFormatVersion)
        {
            throw new ProjectUnavailableException($"The project uses format {info.FormatVersion}, and this version of the app reads {CurrentFormatVersion}.");
        }

        var project = new ProjectFolder(full, info, AcquireLock(full));
        try
        {
            project.Recover();
            return project;
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    /// <summary>Copies a file in, named by its content, so importing the same file twice stores it once.</summary>
    public AssetRef ImportAsset(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        EnsureWritable();

        string hash;
        using (var source = File.OpenRead(file))
        {
            hash = Convert.ToHexStringLower(SHA256.HashData(source));
        }

        var name = hash + Path.GetExtension(file).ToLowerInvariant();
        var target = Path.Combine(FolderPath, AssetsFolder, name);
        if (!File.Exists(target))
        {
            var temporary = $"{target}.tmp";
            File.Copy(file, temporary, overwrite: true);
            File.Move(temporary, target, overwrite: true);
        }

        return new AssetRef(name, Path.GetFileName(file));
    }

    /// <summary>The full path of an asset. Names come from files on disk, so they are not trusted.</summary>
    public string ResolveAsset(string asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);

        if (Path.GetFileName(asset) != asset)
        {
            throw new ProjectUnavailableException($"Invalid asset name: {asset}");
        }

        return Path.Combine(FolderPath, AssetsFolder, asset);
    }

    /// <exception cref="ProjectUnavailableException">The revision is missing or damaged.</exception>
    public Revision ReadRevision(int number)
    {
        var path = RevisionPath(number);
        try
        {
            return JsonSerializer.Deserialize<Revision>(File.ReadAllText(path), ProjectJson.Options)
                ?? throw new JsonException("Empty file.");
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            throw new ProjectUnavailableException($"Revision {number} cannot be read.", ex);
        }
    }

    /// <summary>Every revision, newest first. A damaged one stays listed, and the others remain usable.</summary>
    public IReadOnlyList<RevisionSummary> ReadHistory() =>
        RevisionNumbers().OrderDescending().Select(Summarize).ToList();

    public Revision Commit(WallpaperDocument document, RevisionChange change)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(change);
        EnsureWritable();

        var number = RevisionNumbers().DefaultIfEmpty(0).Max() + 1;
        var revision = new Revision(number, _info.Head == 0 ? null : _info.Head, DateTimeOffset.UtcNow, change, document);

        // The rename makes the revision appear complete or not at all.
        var temporary = Path.Combine(FolderPath, RevisionsFolder, $".tmp-{Guid.NewGuid():N}.json");
        WriteDurably(temporary, JsonSerializer.Serialize(revision, ProjectJson.Options));
        File.Move(temporary, RevisionPath(number));

        SaveInfo(_info with { Head = number });
        WriteCompositionView(revision);
        return revision;
    }

    public void Dispose() => _lock?.Dispose();

    /// <summary>
    /// Held open with no sharing rather than judged by existing, so the OS releases it even if the app
    /// crashes, and a stale lock can never block a project.
    /// </summary>
    private static FileStream? AcquireLock(string folder)
    {
        try
        {
            return new FileStream(Path.Combine(folder, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the current layers in order, for a person looking at the folder: which file is which, and how they
    /// stack. Purely informative: nothing ever reads it back, so a failure here must not fail the change itself.
    /// </summary>
    private void WriteCompositionView(Revision revision)
    {
        if (IsReadOnly)
        {
            return;
        }

        var view = new CompositionView(
            "Gerado pelo WallpaperEditor, apenas para consulta. Editar este arquivo não muda nada: a fonte da verdade é a pasta revisions.",
            Name,
            revision.Number,
            _info.Canvas,
            new BackgroundLayer(
                $"{AssetsFolder}/{revision.Document.Background.Source.Asset}",
                revision.Document.Background.Source.OriginalName,
                revision.Document.Background.Fit),
            revision.Document.Items.Select((item, index) => new CompositionLayer(index + 1, item)).ToList());

        try
        {
            var file = Path.Combine(FolderPath, CompositionFile);
            var temporary = $"{file}.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(view, ProjectJson.Options));
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience: the revision itself is already safely written.
        }
    }

    private void Recover()
    {
        if (!IsReadOnly)
        {
            foreach (var leftover in Directory.GetFiles(Path.Combine(FolderPath, RevisionsFolder), ".tmp-*"))
            {
                File.Delete(leftover);
            }
        }

        var newest = RevisionNumbers().OrderDescending().Where(n => Summarize(n).IsValid).Cast<int?>().FirstOrDefault()
            ?? throw new ProjectUnavailableException("The project has no readable revision.");

        if (newest == _info.Head)
        {
            // Also covers projects created before this file existed.
            WriteCompositionView(ReadRevision(newest));
            return;
        }

        // Either a change finished writing its revision but not the pointer to it, or the pointed-to
        // revision is damaged. In both cases the newest readable revision is complete and is the best state.
        RecoveryNote = Summarize(_info.Head).IsValid
            ? $"O projeto foi fechado no meio de uma alteração, e ela foi mantida (revisão {newest})."
            : $"A revisão {_info.Head} está danificada. O projeto abriu na revisão {newest}.";

        if (IsReadOnly)
        {
            _info = _info with { Head = newest };
        }
        else
        {
            SaveInfo(_info with { Head = newest });
            WriteCompositionView(ReadRevision(newest));
        }
    }

    private RevisionSummary Summarize(int number)
    {
        try
        {
            var revision = ReadRevision(number);
            return new RevisionSummary(number, revision.CreatedAt, revision.Change);
        }
        catch (ProjectUnavailableException)
        {
            return new RevisionSummary(number, null, null);
        }
    }

    private IEnumerable<int> RevisionNumbers() =>
        Directory.EnumerateFiles(Path.Combine(FolderPath, RevisionsFolder), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(name => int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0);

    private string RevisionPath(int number) =>
        Path.Combine(FolderPath, RevisionsFolder, $"{number:D6}.json");

    private void SaveInfo(ProjectInfo info)
    {
        var file = Path.Combine(FolderPath, ProjectFile);
        var temporary = $"{file}.tmp";
        WriteDurably(temporary, JsonSerializer.Serialize(info, ProjectJson.Options));
        File.Move(temporary, file, overwrite: true);
        _info = info;
    }

    private void EnsureWritable()
    {
        if (IsReadOnly)
        {
            throw new ProjectReadOnlyException();
        }
    }

    /// <summary>Forces the content to disk before the rename that publishes it.</summary>
    private static void WriteDurably(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private sealed record CanvasInfo(int Width, int Height);

    private sealed record BackgroundLayer(string File, string OriginalName, FitMode Fit);

    /// <summary>Numbered from the bottom: the highest order is drawn on top.</summary>
    private sealed record CompositionLayer(int Order, DocumentItem Item);

    private sealed record CompositionView(string Note, string Name, int Revision, CanvasInfo Canvas, BackgroundLayer Background, IReadOnlyList<CompositionLayer> Layers);

    private sealed record ProjectInfo(int FormatVersion, Guid Id, string Name, CanvasInfo Canvas, int Head, DateTimeOffset CreatedAt);
}
