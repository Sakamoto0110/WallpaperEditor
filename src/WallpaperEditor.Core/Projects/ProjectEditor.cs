using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Projects;

/// <summary>
/// Every change a user can make to a dynamic wallpaper. Each one that changes something becomes one
/// revision. Nothing here touches pixels or the wallpaper: publishing is separate.
/// </summary>
/// <param name="project">The open project.</param>
/// <param name="measureChecklist">The height a checklist needs, so it can grow instead of hiding entries.</param>
public sealed class ProjectEditor(ProjectFolder project, Func<ChecklistItem, double>? measureChecklist = null)
{
    private const double MinimumItemSize = 16;

    public ProjectFolder Project { get; } = project;

    public WallpaperDocument Document { get; private set; } = project.ReadRevision(project.Head).Document;

    public event EventHandler? Changed;

    public T? Find<T>(string itemId)
        where T : DocumentItem => Document.Items.OfType<T>().FirstOrDefault(i => i.Id == itemId);

    public ImageItem AddImage(string file, ItemRect rect)
    {
        var source = Project.ImportAsset(file);
        var item = new ImageItem { Id = DocumentItem.NewId(), Rect = Sanitize(rect), Source = source };
        Apply(WithItems([.. Document.Items, item]), new RevisionChange(ChangeKinds.ItemAdded, $"Adicionou a imagem {source.OriginalName}") { ItemId = item.Id });
        return item;
    }

    public ChecklistItem AddChecklist(string title, ItemRect rect)
    {
        var item = new ChecklistItem { Id = DocumentItem.NewId(), Rect = Sanitize(rect), Title = title };
        Apply(WithItems([.. Document.Items, item]), new RevisionChange(ChangeKinds.ItemAdded, $"Adicionou a checklist {Quoted(title)}") { ItemId = item.Id });
        return item;
    }

    public void RemoveItem(string itemId)
    {
        var item = Required(itemId);
        Apply(WithItems([.. Document.Items.Where(i => i.Id != itemId)]), new RevisionChange(ChangeKinds.ItemRemoved, $"Removeu {Describe(item)}") { ItemId = itemId });
    }

    /// <summary>Called when a drag ends, so one drag is one revision however far the item travelled.</summary>
    public void Place(string itemId, ItemRect rect, double rotation)
    {
        var item = Required(itemId);
        var updated = item with { Rect = Sanitize(rect), Rotation = NormalizeAngle(rotation) };
        if (updated.Rect != item.Rect || updated.Rotation != item.Rotation)
        {
            Replace(updated, $"Moveu ou redimensionou {Describe(item)}");
        }
    }

    public void SetOpacity(string itemId, double opacity)
    {
        var item = Find<ImageItem>(itemId) ?? throw MissingItem(itemId);
        var value = Math.Clamp(opacity, 0, 1);
        if (value != item.Opacity)
        {
            Replace(item with { Opacity = value }, $"Mudou a opacidade de {Describe(item)}");
        }
    }

    /// <summary>Moves an item up (positive) or down (negative) in the drawing order.</summary>
    public void Reorder(string itemId, int steps)
    {
        var item = Required(itemId);
        var items = Document.Items.ToList();
        var from = items.IndexOf(item);
        var to = Math.Clamp(from + steps, 0, items.Count - 1);
        if (to == from)
        {
            return;
        }

        items.RemoveAt(from);
        items.Insert(to, item);
        Apply(WithItems(items), new RevisionChange(ChangeKinds.ItemEdited, $"Mudou a ordem de {Describe(item)}") { ItemId = itemId });
    }

    public void SetBackground(string file, FitMode fit)
    {
        var source = Project.ImportAsset(file);
        Apply(Document with { Background = new BackgroundSpec(source, fit) }, new RevisionChange(ChangeKinds.BackgroundChanged, $"Trocou o fundo por {source.OriginalName}"));
    }

    public void SetBackgroundFit(FitMode fit)
    {
        if (fit != Document.Background.Fit)
        {
            Apply(Document with { Background = Document.Background with { Fit = fit } }, new RevisionChange(ChangeKinds.BackgroundChanged, "Mudou o ajuste do fundo"));
        }
    }

    public void SetChecklistTitle(string itemId, string title)
    {
        var item = Checklist(itemId);
        if (title != item.Title)
        {
            ReplaceChecklist(item with { Title = title }, ChangeKinds.ItemEdited, $"Renomeou a checklist para {Quoted(title)}");
        }
    }

    public void SetChecklistFontSize(string itemId, double size)
    {
        var item = Checklist(itemId);
        var value = Math.Clamp(size, 8, 200);
        if (value != item.Style.Size)
        {
            ReplaceChecklist(item with { Style = item.Style with { Size = value } }, ChangeKinds.ItemEdited, $"Mudou o tamanho do texto de {Describe(item)}");
        }
    }

    public void AddEntry(string itemId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var item = Checklist(itemId);
        var entry = new ChecklistEntry(DocumentItem.NewId(), text.Trim(), Done: false);
        ReplaceChecklist(item with { Entries = [.. item.Entries, entry] }, ChangeKinds.EntryAdded, $"Adicionou a tarefa {Quoted(entry.Text)}");
    }

    public void RemoveEntry(string itemId, string entryId)
    {
        var item = Checklist(itemId);
        var entry = RequiredEntry(item, entryId);
        ReplaceChecklist(item with { Entries = [.. item.Entries.Where(e => e.Id != entryId)] }, ChangeKinds.EntryRemoved, $"Removeu a tarefa {Quoted(entry.Text)}");
    }

    public void ToggleEntry(string itemId, string entryId)
    {
        var item = Checklist(itemId);
        var entry = RequiredEntry(item, entryId);
        var verb = entry.Done ? "Desmarcou" : "Marcou";
        ReplaceChecklist(
            item with { Entries = [.. item.Entries.Select(e => e.Id == entryId ? e with { Done = !e.Done } : e)] },
            ChangeKinds.EntryToggled,
            $"{verb} a tarefa {Quoted(entry.Text)}");
    }

    public void EditEntry(string itemId, string entryId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var item = Checklist(itemId);
        var entry = RequiredEntry(item, entryId);
        var value = text.Trim();
        if (value != entry.Text)
        {
            ReplaceChecklist(
                item with { Entries = [.. item.Entries.Select(e => e.Id == entryId ? e with { Text = value } : e)] },
                ChangeKinds.EntryEdited,
                $"Editou a tarefa {Quoted(value)}");
        }
    }

    /// <summary>Makes a new revision equal to an old one. Nothing is lost: later revisions stay in the history.</summary>
    public void Revert(int revision)
    {
        var target = Project.ReadRevision(revision);
        Apply(target.Document, new RevisionChange(ChangeKinds.Reverted, $"Voltou à revisão {revision}") { Target = revision });
    }

    /// <summary>Brings back one item as it was in an old revision, leaving the rest of the document alone.</summary>
    public void RestoreItem(int revision, string itemId)
    {
        var item = Project.ReadRevision(revision).Document.Items.FirstOrDefault(i => i.Id == itemId) ?? throw MissingItem(itemId);
        var items = Document.Items.Any(i => i.Id == itemId)
            ? Document.Items.Select(i => i.Id == itemId ? item : i).ToList()
            : [.. Document.Items, item];

        Apply(WithItems(items), new RevisionChange(ChangeKinds.ItemRestored, $"Restaurou {Describe(item)}") { ItemId = itemId, FromRevision = revision });
    }

    private void Replace(DocumentItem updated, string label) =>
        Apply(
            WithItems([.. Document.Items.Select(i => i.Id == updated.Id ? updated : i)]),
            new RevisionChange(ChangeKinds.ItemEdited, label) { ItemId = updated.Id });

    private void ReplaceChecklist(ChecklistItem updated, string kind, string label)
    {
        var grown = GrowToFit(updated);
        Apply(
            WithItems([.. Document.Items.Select(i => i.Id == grown.Id ? grown : i)]),
            new RevisionChange(kind, label) { ItemId = grown.Id });
    }

    /// <summary>A checklist grows to fit new entries in one revision, rather than clipping them out of sight.</summary>
    private ChecklistItem GrowToFit(ChecklistItem item)
    {
        var needed = measureChecklist?.Invoke(item) ?? 0;
        return needed > item.Rect.Height ? item with { Rect = item.Rect with { Height = Math.Ceiling(needed) } } : item;
    }

    private void Apply(WallpaperDocument document, RevisionChange change)
    {
        Project.Commit(document, change);
        Document = document;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private WallpaperDocument WithItems(IReadOnlyList<DocumentItem> items) => Document with { Items = items };

    private DocumentItem Required(string itemId) =>
        Document.Items.FirstOrDefault(i => i.Id == itemId) ?? throw MissingItem(itemId);

    private ChecklistItem Checklist(string itemId) => Find<ChecklistItem>(itemId) ?? throw MissingItem(itemId);

    private static ChecklistEntry RequiredEntry(ChecklistItem item, string entryId) =>
        item.Entries.FirstOrDefault(e => e.Id == entryId) ?? throw new InvalidOperationException($"No entry {entryId}.");

    private static InvalidOperationException MissingItem(string itemId) => new($"No item {itemId}.");

    private static ItemRect Sanitize(ItemRect rect) => new(
        Math.Round(rect.X),
        Math.Round(rect.Y),
        Math.Max(MinimumItemSize, Math.Round(rect.Width)),
        Math.Max(MinimumItemSize, Math.Round(rect.Height)));

    private static double NormalizeAngle(double degrees)
    {
        var value = Math.Round(degrees, 2) % 360;
        return value < 0 ? value + 360 : value;
    }

    private static string Describe(DocumentItem item) => item switch
    {
        ImageItem image => $"a imagem {image.Source.OriginalName}",
        ChecklistItem checklist => $"a checklist {Quoted(checklist.Title)}",
        _ => "o item",
    };

    private static string Quoted(string text) => $"\"{text}\"";
}
