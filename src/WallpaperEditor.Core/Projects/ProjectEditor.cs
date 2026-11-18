using WallpaperEditor.Core.Wallpapers;

namespace WallpaperEditor.Core.Projects;

/// <summary>
/// Every change a user can make to a dynamic wallpaper. Each one that changes something becomes one
/// revision. Nothing here touches pixels or the wallpaper: publishing is separate.
/// </summary>
/// <param name="project">The open project.</param>
/// <param name="measureHeight">The height a text item needs, so it can grow instead of hiding its content.</param>
public sealed class ProjectEditor(ProjectFolder project, Func<StyledItem, double>? measureHeight = null)
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

    public TextItem AddText(string text, ItemRect rect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var item = (TextItem)GrowToFit(new TextItem { Id = DocumentItem.NewId(), Rect = Sanitize(rect), Text = TextItem.Normalize(text) });
        Apply(WithItems([.. Document.Items, item]), new RevisionChange(ChangeKinds.ItemAdded, $"Adicionou {Describe(item)}") { ItemId = item.Id });
        return item;
    }

    public void SetText(string itemId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var item = Find<TextItem>(itemId) ?? throw MissingItem(itemId);
        var value = TextItem.Normalize(text);
        if (value != item.Text)
        {
            var updated = item with { Text = value };
            ReplaceStyled(updated, ChangeKinds.ItemEdited, $"Editou {Describe(updated)}");
        }
    }

    public CounterItem AddCounter(string label, ItemRect rect) =>
        AddControl(new CounterItem { Id = DocumentItem.NewId(), Rect = Sanitize(rect), Label = label });

    public ProgressItem AddProgress(string label, ItemRect rect) =>
        AddControl(new ProgressItem { Id = DocumentItem.NewId(), Rect = Sanitize(rect), Label = label });

    public ToggleItem AddToggle(string label, ItemRect rect) =>
        AddControl(new ToggleItem { Id = DocumentItem.NewId(), Rect = Sanitize(rect), Label = label });

    public void SetControlLabel(string itemId, string label)
    {
        ArgumentNullException.ThrowIfNull(label);

        var item = Find<ControlItem>(itemId) ?? throw MissingItem(itemId);
        var value = label.Trim();
        if (value != item.Label)
        {
            var updated = item with { Label = value };
            ReplaceStyled(updated, ChangeKinds.ItemEdited, $"Renomeou {Describe(item)} para {Quoted(value)}");
        }
    }

    public void StepCounter(string itemId, int delta)
    {
        var item = Find<CounterItem>(itemId) ?? throw MissingItem(itemId);
        SetCounterValue(itemId, (int)Math.Clamp((long)item.Value + delta, int.MinValue, int.MaxValue));
    }

    public void SetCounterValue(string itemId, int value)
    {
        var item = Find<CounterItem>(itemId) ?? throw MissingItem(itemId);
        if (value != item.Value)
        {
            ReplaceStyled(item with { Value = value }, ChangeKinds.ValueChanged, $"Mudou {Describe(item)} para {value}");
        }
    }

    public void SetProgress(string itemId, int percent)
    {
        var item = Find<ProgressItem>(itemId) ?? throw MissingItem(itemId);
        var value = Math.Clamp(percent, 0, 100);
        if (value != item.Percent)
        {
            ReplaceStyled(item with { Percent = value }, ChangeKinds.ValueChanged, $"Mudou {Describe(item)} para {value}%");
        }
    }

    public void SetToggle(string itemId, bool on)
    {
        var item = Find<ToggleItem>(itemId) ?? throw MissingItem(itemId);
        if (on != item.On)
        {
            var updated = item with { On = on };
            ReplaceStyled(updated, ChangeKinds.ValueChanged, $"Mudou {Describe(item)} para {Quoted(updated.StateText)}");
        }
    }

    /// <summary>The text shown for each state. Neither may be empty, or one state would look like nothing at all.</summary>
    public void SetToggleTexts(string itemId, string onText, string offText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(onText);
        ArgumentException.ThrowIfNullOrWhiteSpace(offText);

        var item = Find<ToggleItem>(itemId) ?? throw MissingItem(itemId);
        var updated = item with { OnText = onText.Trim(), OffText = offText.Trim() };
        if (updated != item)
        {
            ReplaceStyled(updated, ChangeKinds.ItemEdited, $"Mudou os estados {Of(item)}");
        }
    }

    /// <summary>Null goes back to automatic: centered in a thought balloon, on the left anywhere else.</summary>
    public void SetTextAlign(string itemId, TextAlign? align)
    {
        var item = Find<TextItem>(itemId) ?? throw MissingItem(itemId);
        if (align != item.Align)
        {
            var how = align switch
            {
                TextAlign.Left => "à esquerda",
                TextAlign.Center => "ao centro",
                TextAlign.Right => "à direita",
                TextAlign.Justify => "justificado",
                _ => "automaticamente",
            };
            ReplaceStyled(item with { Align = align }, ChangeKinds.ItemEdited, $"Alinhou {Describe(item)} {how}");
        }
    }

    public void SetTextBold(string itemId, bool bold)
    {
        var item = Find<TextItem>(itemId) ?? throw MissingItem(itemId);
        if (bold != item.Bold)
        {
            var label = bold ? $"Pôs {Describe(item)} em negrito" : $"Tirou o negrito {Of(item)}";
            ReplaceStyled(item with { Bold = bold }, ChangeKinds.ItemEdited, label);
        }
    }

    public void SetTextItalic(string itemId, bool italic)
    {
        var item = Find<TextItem>(itemId) ?? throw MissingItem(itemId);
        if (italic != item.Italic)
        {
            var label = italic ? $"Pôs {Describe(item)} em itálico" : $"Tirou o itálico {Of(item)}";
            ReplaceStyled(item with { Italic = italic }, ChangeKinds.ItemEdited, label);
        }
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
            Replace(item with { Opacity = value }, $"Mudou a opacidade {Of(item)}");
        }
    }

    /// <summary>Flips an image left to right, or back.</summary>
    public void ToggleMirror(string itemId) => MirrorItem(itemId, vertically: false);

    /// <summary>
    /// Flips an image as the user sees it, around its center: left to right, or top to bottom. The image mirrors
    /// itself before its rotation, so a turned image also turns the other way to end up mirrored on screen.
    /// </summary>
    public void MirrorItem(string itemId, bool vertically)
    {
        var item = Find<ImageItem>(itemId) ?? throw MissingItem(itemId);
        var undoing = vertically ? item.MirroredVertically : item.Mirrored;
        var label = undoing ? $"Desfez o espelho {Of(item)}"
            : vertically ? $"Espelhou {Describe(item)} na vertical"
            : $"Espelhou {Describe(item)}";
        var flipped = vertically ? item with { MirroredVertically = !item.MirroredVertically } : item with { Mirrored = !item.Mirrored };
        Replace(flipped with { Rotation = NormalizeAngle(-item.Rotation) }, label);
    }

    /// <summary>Turns an item around its center by the given degrees, clockwise.</summary>
    public void RotateItem(string itemId, double degrees)
    {
        var item = Required(itemId);
        var rotation = NormalizeAngle(item.Rotation + degrees);
        if (rotation != item.Rotation)
        {
            Replace(item with { Rotation = rotation }, $"Girou {Describe(item)}");
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
        Apply(WithItems(items), new RevisionChange(ChangeKinds.ItemEdited, $"Mudou a ordem {Of(item)}") { ItemId = itemId });
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

    /// <summary>Rotates or mirrors the background. The description names what changed, for the history.</summary>
    public void SetBackgroundOrientation(ImageOrientation orientation)
    {
        var before = Document.Background.Orientation;
        if (orientation == before)
        {
            return;
        }

        var description = orientation.IsNone ? "Desfez a rotação e o espelhamento do fundo"
            : orientation.FlipHorizontal != before.FlipHorizontal && orientation.Degrees == before.Degrees && orientation.FlipVertical == before.FlipVertical ? "Espelhou o fundo na horizontal"
            : orientation.FlipVertical != before.FlipVertical && orientation.Degrees == before.Degrees && orientation.FlipHorizontal == before.FlipHorizontal ? "Espelhou o fundo na vertical"
            : "Girou o fundo";
        Apply(Document with { Background = Document.Background with { Orientation = orientation } }, new RevisionChange(ChangeKinds.BackgroundChanged, description));
    }

    public void SetChecklistTitle(string itemId, string title)
    {
        var item = Checklist(itemId);
        if (title != item.Title)
        {
            ReplaceStyled(item with { Title = title }, ChangeKinds.ItemEdited, $"Renomeou a checklist para {Quoted(title)}");
        }
    }

    public void SetItemFontSize(string itemId, double size)
    {
        var item = Styled(itemId);
        var value = Math.Clamp(size, 8, 200);
        if (value != item.Style.Size)
        {
            ReplaceStyled(item with { Style = item.Style with { Size = value } }, ChangeKinds.ItemEdited, $"Mudou o tamanho do texto {Of(item)}");
        }
    }

    public void SetItemFont(string itemId, string font)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(font);

        var item = Styled(itemId);
        if (font != item.Style.Font)
        {
            ReplaceStyled(item with { Style = item.Style with { Font = font } }, ChangeKinds.ItemEdited, $"Mudou a fonte {Of(item)} para {font}");
        }
    }

    public void SetItemTextOpacity(string itemId, double opacity)
    {
        var item = Styled(itemId);
        var style = item.Style.WithTextOpacity(opacity);
        if (style != item.Style)
        {
            ReplaceStyled(item with { Style = style }, ChangeKinds.ItemEdited, $"Mudou a opacidade do texto {Of(item)}");
        }
    }

    public void SetItemPanelOpacity(string itemId, double opacity)
    {
        var item = Styled(itemId);
        var style = item.Style.WithPanelOpacity(opacity);
        if (style != item.Style)
        {
            ReplaceStyled(item with { Style = style }, ChangeKinds.ItemEdited, $"Mudou a opacidade do painel {Of(item)}");
        }
    }

    public void SetItemPanelShape(string itemId, PanelShape shape)
    {
        var item = Styled(itemId);
        if (shape != item.Style.Shape)
        {
            var name = shape switch
            {
                PanelShape.Speech => "balão de fala",
                PanelShape.Thought => "balão de pensamento",
                _ => "arredondado",
            };
            ReplaceStyled(item with { Style = item.Style with { Shape = shape } }, ChangeKinds.ItemEdited, $"Mudou o painel {Of(item)} para {name}");
        }
    }

    public void SetItemTail(string itemId, TailSide side)
    {
        var item = Styled(itemId);
        if (side != item.Style.Tail)
        {
            var name = side == TailSide.Right ? "a direita" : "a esquerda";
            ReplaceStyled(item with { Style = item.Style with { Tail = side } }, ChangeKinds.ItemEdited, $"Virou a ponta do balão {Of(item)} para {name}");
        }
    }

    public void AddEntry(string itemId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var item = Checklist(itemId);
        var entry = new ChecklistEntry(DocumentItem.NewId(), text.Trim(), Done: false);
        ReplaceStyled(item with { Entries = [.. item.Entries, entry] }, ChangeKinds.EntryAdded, $"Adicionou a tarefa {Quoted(entry.Text)}");
    }

    public void RemoveEntry(string itemId, string entryId)
    {
        var item = Checklist(itemId);
        var entry = RequiredEntry(item, entryId);
        ReplaceStyled(item with { Entries = [.. item.Entries.Where(e => e.Id != entryId)] }, ChangeKinds.EntryRemoved, $"Removeu a tarefa {Quoted(entry.Text)}");
    }

    public void ToggleEntry(string itemId, string entryId)
    {
        var item = Checklist(itemId);
        var entry = RequiredEntry(item, entryId);
        var verb = entry.Done ? "Desmarcou" : "Marcou";
        ReplaceStyled(
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
            ReplaceStyled(
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

    /// <summary>
    /// Brings back one item as it was in an old revision, leaving the rest of the document alone. It returns to its
    /// place in the stack: right above the nearest item that was below it then and still exists.
    /// </summary>
    public void RestoreItem(int revision, string itemId)
    {
        var source = Project.ReadRevision(revision).Document.Items;
        var item = source.FirstOrDefault(i => i.Id == itemId) ?? throw MissingItem(itemId);

        var items = Document.Items.ToList();
        var existing = items.FindIndex(i => i.Id == itemId);
        if (existing >= 0)
        {
            items[existing] = item;
        }
        else
        {
            items.Insert(PlaceInStack(source, itemId, items), item);
        }

        Apply(WithItems(items), new RevisionChange(ChangeKinds.ItemRestored, $"Restaurou {Describe(item)}") { ItemId = itemId, FromRevision = revision });
    }

    private static int PlaceInStack(IReadOnlyList<DocumentItem> source, string itemId, List<DocumentItem> current)
    {
        var position = source.ToList().FindIndex(i => i.Id == itemId);
        for (var below = position - 1; below >= 0; below--)
        {
            var index = current.FindIndex(i => i.Id == source[below].Id);
            if (index >= 0)
            {
                return index + 1;
            }
        }

        return 0;
    }

    private void Replace(DocumentItem updated, string label) =>
        Apply(
            WithItems([.. Document.Items.Select(i => i.Id == updated.Id ? updated : i)]),
            new RevisionChange(ChangeKinds.ItemEdited, label) { ItemId = updated.Id });

    private T AddControl<T>(T item)
        where T : ControlItem
    {
        var grown = (T)GrowToFit(item);
        Apply(WithItems([.. Document.Items, grown]), new RevisionChange(ChangeKinds.ItemAdded, $"Adicionou {Describe(grown)}") { ItemId = grown.Id });
        return grown;
    }

    private void ReplaceStyled(StyledItem updated, string kind, string label)
    {
        var grown = GrowToFit(updated);
        Apply(
            WithItems([.. Document.Items.Select(i => i.Id == grown.Id ? grown : i)]),
            new RevisionChange(kind, label) { ItemId = grown.Id });
    }

    /// <summary>Text grows to fit its content in one revision, rather than being clipped out of sight.</summary>
    private StyledItem GrowToFit(StyledItem item)
    {
        var needed = measureHeight?.Invoke(item) ?? 0;
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

    private StyledItem Styled(string itemId) => Find<StyledItem>(itemId) ?? throw MissingItem(itemId);

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
        TextItem text => $"o texto {Quoted(text.Label())}",
        CounterItem counter => Named("o contador", counter.Label),
        ProgressItem progress => Named("a barra de progresso", progress.Label),
        ToggleItem toggle => Named("a alternância", toggle.Label),
        _ => "o item",
    };

    private static string Named(string kind, string label) => label.Length > 0 ? $"{kind} {Quoted(label)}" : kind;

    /// <summary>"da imagem", "do texto": Portuguese joins "de" with the article, and "de a imagem" reads wrong.</summary>
    private static string Of(DocumentItem item)
    {
        var described = Describe(item);
        return described.StartsWith("a ", StringComparison.Ordinal) || described.StartsWith("o ", StringComparison.Ordinal)
            ? $"d{described}"
            : $"de {described}";
    }

    private static string Quoted(string text) => $"\"{text}\"";
}
