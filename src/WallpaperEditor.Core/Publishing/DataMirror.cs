namespace WallpaperEditor.Core.Publishing;

/// <summary>
/// A second copy of what this machine cannot rebuild: <c>state.json</c> and the copies of the original wallpapers.
/// The app always works on its own folder; the mirror is only read when that folder lost its state.
/// </summary>
/// <remarks>
/// Published images are left out, since any project can render them again. The mirror keeps the same relative paths
/// as the data folder, so <c>state.json</c> is copied as is and its paths stay right after a restore. Failing to
/// write the mirror never fails what the app was doing.
/// </remarks>
public sealed class DataMirror(string dataRoot, string mirrorRoot)
{
    public const string StateFile = "state.json";
    private const string PublishedFolder = "published";
    private const string OriginalFolder = "original";

    public string DataRoot { get; } = Path.GetFullPath(dataRoot);

    public string MirrorRoot { get; } = Path.GetFullPath(mirrorRoot);

    /// <summary>Brings the mirror up to date: new and changed files are copied, and ones gone from the data folder go.</summary>
    /// <returns>False if the mirror could not be written; the data folder is untouched either way.</returns>
    public bool Save()
    {
        try
        {
            Copy(DataRoot, MirrorRoot);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Puts the mirrored files back when the data folder has no state, for example after it was deleted, or after a
    /// damaged <c>state.json</c> was set aside.
    /// </summary>
    /// <returns>True if something was restored.</returns>
    public bool RestoreIfMissing()
    {
        if (File.Exists(Path.Combine(DataRoot, StateFile)) || !File.Exists(Path.Combine(MirrorRoot, StateFile)))
        {
            return false;
        }

        try
        {
            Copy(MirrorRoot, DataRoot);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The files that matter, relative to a root: the state, and every monitor's original copies.</summary>
    private static IEnumerable<string> Kept(string root)
    {
        if (File.Exists(Path.Combine(root, StateFile)))
        {
            yield return StateFile;
        }

        var published = Path.Combine(root, PublishedFolder);
        if (!Directory.Exists(published))
        {
            yield break;
        }

        foreach (var monitor in Directory.GetDirectories(published))
        {
            var originals = Path.Combine(monitor, OriginalFolder);
            if (!Directory.Exists(originals))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(originals).Where(f => !Path.GetFileName(f).StartsWith(".tmp-", StringComparison.Ordinal)))
            {
                yield return Path.GetRelativePath(root, file);
            }
        }
    }

    private static void Copy(string from, string to)
    {
        var wanted = Kept(from).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in wanted)
        {
            var source = Path.Combine(from, relative);
            var target = Path.Combine(to, relative);
            if (IsSame(source, target))
            {
                continue;
            }

            // Through a temporary file, so a mirror interrupted halfway still holds the previous complete copy.
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = $"{target}.{Guid.NewGuid():N}.tmp";
            File.Copy(source, temporary);
            File.Move(temporary, target, overwrite: true);
        }

        // Only within what is mirrored: anything else someone put in the folder stays.
        foreach (var stale in Kept(to).Where(r => !wanted.Contains(r)))
        {
            File.Delete(Path.Combine(to, stale));
        }
    }

    private static bool IsSame(string source, string target)
    {
        if (!File.Exists(target))
        {
            return false;
        }

        var a = new FileInfo(source);
        var b = new FileInfo(target);
        return a.Length == b.Length && a.LastWriteTimeUtc == b.LastWriteTimeUtc;
    }
}
