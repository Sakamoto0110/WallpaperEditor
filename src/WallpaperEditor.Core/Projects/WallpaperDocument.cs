using System.Globalization;
using System.Text.Encodings.Web;
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
[JsonDerivedType(typeof(TextItem), "text")]
[JsonDerivedType(typeof(CounterItem), "counter")]
[JsonDerivedType(typeof(ProgressItem), "progress")]
[JsonDerivedType(typeof(ToggleItem), "toggle")]
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

    /// <summary>Flipped left to right, before the rotation is applied.</summary>
    public bool Mirrored { get; init; }
}

/// <summary>An item made of text on a panel, which shares its look with every other such item.</summary>
public abstract record StyledItem : DocumentItem
{
    public TextStyle Style { get; init; } = TextStyle.Default;
}

public sealed record ChecklistItem : StyledItem
{
    public string Title { get; init; } = string.Empty;

    public IReadOnlyList<ChecklistEntry> Entries { get; init; } = [];
}

/// <summary>Free text. Line breaks are always stored as <c>\n</c>, so a project reads the same anywhere.</summary>
public sealed record TextItem : StyledItem
{
    private const int LabelLength = 30;

    public required string Text { get; init; }

    /// <summary>Null is automatic: centered in a thought balloon, on the left anywhere else.</summary>
    public TextAlign? Align { get; init; }

    /// <summary>The whole text, not a word at a time: formatted text is out of scope.</summary>
    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.ReplaceLineEndings("\n");
    }

    /// <summary>The start of the text, short enough to name this item in a list or in the history.</summary>
    public string Label()
    {
        var firstLine = Text.TrimStart().Split('\n', 2)[0].TrimEnd();
        return firstLine.Length <= LabelLength ? firstLine : $"{firstLine[..LabelLength]}…";
    }
}

public sealed record ChecklistEntry(string Id, string Text, bool Done);

/// <summary>
/// A value the user changes by clicking in the editor, such as a counter. On the wallpaper it is only a picture of
/// that value: what makes it interactive is the editor, never the desktop.
/// </summary>
public abstract record ControlItem : StyledItem
{
    /// <summary>Shown above the value. May be empty.</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>A whole number, changed one step at a time.</summary>
public sealed record CounterItem : ControlItem
{
    public int Value { get; init; }
}

/// <summary>A bar filled from 0 to 100 percent.</summary>
public sealed record ProgressItem : ControlItem
{
    public int Percent { get; init; }
}

/// <summary>A switch with two states, each with its own text, such as "Foco" and "Livre".</summary>
public sealed record ToggleItem : ControlItem
{
    public bool On { get; init; }

    public string OnText { get; init; } = "Ligado";

    public string OffText { get; init; } = "Desligado";

    [JsonIgnore]
    public string StateText => On ? OnText : OffText;
}

/// <summary>
/// How text on a panel looks. Colors are <c>#RRGGBB</c> or <c>#AARRGGBB</c>, and the alpha is what the opacity
/// controls change, so there is one source of truth for transparency.
/// </summary>
public sealed record TextStyle(string Font, double Size, string Color, string Panel)
{
    public static readonly TextStyle Default = new("Segoe UI", 22, "#FFFFFF", "#BE101218");

    /// <summary>The outline of the panel. Projects saved before shapes existed read as rounded.</summary>
    public PanelShape Shape { get; init; } = PanelShape.Rounded;

    /// <summary>Which way a bubble's tail points. Ignored by the rounded panel.</summary>
    public TailSide Tail { get; init; } = TailSide.Left;

    [JsonIgnore]
    public double TextOpacity => ColorAlpha.Of(Color);

    [JsonIgnore]
    public double PanelOpacity => ColorAlpha.Of(Panel);

    public TextStyle WithTextOpacity(double opacity) => this with { Color = ColorAlpha.With(Color, opacity) };

    public TextStyle WithPanelOpacity(double opacity) => this with { Panel = ColorAlpha.With(Panel, opacity) };
}

/// <summary>How the lines of a text sit across its width.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,

    /// <summary>Every line but the last stretched to the full width.</summary>
    Justify,
}

public enum PanelShape
{
    /// <summary>A rectangle with rounded corners.</summary>
    Rounded,

    /// <summary>A speech balloon, with a pointed tail below.</summary>
    Speech,

    /// <summary>A thought balloon: a cloud, with small circles trailing below.</summary>
    Thought,
}

/// <summary>Toward which bottom corner a balloon's tail goes, to point at whoever is "speaking".</summary>
public enum TailSide
{
    Left,
    Right,
}

/// <summary>Reads and changes the alpha of a color written as <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
public static class ColorAlpha
{
    /// <summary>The alpha from 0 to 1. Anything else, such as a named color, counts as opaque.</summary>
    public static double Of(string color)
    {
        ArgumentNullException.ThrowIfNull(color);

        return color is ['#', _, _, _, _, _, _, _, _]
            && byte.TryParse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var alpha)
                ? alpha / 255.0
                : 1;
    }

    /// <summary>The same color with another alpha. A color it cannot read is returned as it was.</summary>
    public static string With(string color, double opacity)
    {
        ArgumentNullException.ThrowIfNull(color);

        var rgb = color switch
        {
            ['#', _, _, _, _, _, _] => color[1..],
            ['#', _, _, _, _, _, _, _, _] => color[3..],
            _ => null,
        };

        if (rgb is null)
        {
            return color;
        }

        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);

        // #FFFFFF and #FFFFFFFF are the same color: rewriting it would record a change that changes nothing.
        return alpha == (byte)Math.Round(Of(color) * 255) ? color : $"#{alpha:X2}{rgb}";
    }
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

    /// <summary>A counter, a progress bar or a switch changed its value.</summary>
    public const string ValueChanged = "value-changed";
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

        // These files are meant to be readable: "Demonstração", not "Demonstração". The stricter default
        // guards against JSON embedded in HTML, which does not apply to a local file.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowOutOfOrderMetadataProperties = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
