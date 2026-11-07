using System.Text.Json;
using System.Text.Json.Serialization;
using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Projects;

/// <summary>The complete state of a dynamic wallpaper: a background and items, last drawn on top.</summary>
public sealed record WallpaperDocument(BackgroundSpec Background, IReadOnlyList<DocumentItem> Items)
{
    /// <summary>True if both describe the same picture. Records compare lists by reference, not content.</summary>
    public bool HasSameContentAs(WallpaperDocument other) =>
        JsonSerializer.Serialize(this, ProjectJson.Options) == JsonSerializer.Serialize(other, ProjectJson.Options);
}

/// <summary>A file in the project's <c>assets</c> folder, and the name it had when it was imported.</summary>
public sealed record AssetRef(string Asset, string OriginalName);

public sealed record BackgroundSpec(AssetRef Source, FitMode Fit);

/// <summary>Position and size in canvas pixels, before rotation.</summary>
public readonly record struct ItemRect(double X, double Y, double Width, double Height);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ImageItem), "image")]
[JsonDerivedType(typeof(ChecklistItem), "checklist")]
public abstract record DocumentItem
{
    public required string Id { get; init; }

    public required ItemRect Rect { get; init; }

    /// <summary>Degrees clockwise, around the center of <see cref="Rect"/>.</summary>
    public double Rotation { get; init; }

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];
}

public sealed record ImageItem : DocumentItem
{
    public required AssetRef Source { get; init; }

    public double Opacity { get; init; } = 1;
}

public sealed record ChecklistItem : DocumentItem
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<ChecklistEntry> Entries { get; init; } = [];

    public ChecklistStyle Style { get; init; } = ChecklistStyle.Default;
}

public sealed record ChecklistEntry(string Id, string Text, bool Done);

public sealed record ChecklistStyle(string Font, double Size, string Color, string Panel)
{
    public static readonly ChecklistStyle Default = new("Segoe UI", 22, "#FFFFFF", "#BE101218");
}

/// <summary>What changed in a revision, for people reading the history.</summary>
public sealed record RevisionChange(string Kind, string Label)
{
    public string? ItemId { get; init; }

    /// <summary>For a revert: the revision whose document was restored.</summary>
    public int? Target { get; init; }

    /// <summary>For a restored item: the revision it came from.</summary>
    public int? FromRevision { get; init; }
}

public static class ChangeKinds
{
    public const string Created = "created";
    public const string ItemAdded = "item-added";
    public const string ItemRemoved = "item-removed";
    public const string ItemEdited = "item-edited";
    public const string ItemRestored = "item-restored";
    public const string EntryAdded = "checklist-entry-added";
    public const string EntryRemoved = "checklist-entry-removed";
    public const string EntryToggled = "checklist-entry-toggled";
    public const string EntryEdited = "checklist-entry-edited";
    public const string BackgroundChanged = "background-changed";
    public const string Reverted = "reverted";
}

/// <summary>An immutable, complete state of the document, with how it came to be.</summary>
public sealed record Revision(int Number, int? Parent, DateTimeOffset CreatedAt, RevisionChange Change, WallpaperDocument Document);

/// <summary>A history row. <see cref="Change"/> is null if the revision file could not be read.</summary>
public sealed record RevisionSummary(int Number, DateTimeOffset? CreatedAt, RevisionChange? Change)
{
    public bool IsValid => Change is not null;
}

internal static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
