using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperEditor.App.Companion;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.ViewModels;

// The options of a list override ToString: it is what UI Automation, and so a screen reader, announces for an item
// shown through DisplayMemberPath. A record's own ToString would read out its fields.
public sealed record MonitorOption(MonitorInfo Info)
{
    public string Label => $"{Info.Name} ({Info.Bounds.Width} × {Info.Bounds.Height})";

    public override string ToString() => Label;
}

public sealed record PanelShapeOption(PanelShape Shape, string Label)
{
    public override string ToString() => Label;
}

public sealed record TailSideOption(TailSide Side, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A null <see cref="Align"/> is automatic, which is why the list binds the option and not its value.</summary>
public sealed record TextAlignOption(TextAlign? Align, string Label)
{
    public override string ToString() => Label;
}

public sealed record HistoryRow(int Number, string Label, string When, bool IsHead, bool IsValid, string? RemovedItemId);

/// <summary>An item in the list, which reaches items hidden behind others, and works without a mouse.</summary>
public sealed record ItemRow(string Id, string Label);

/// <summary>A checklist entry in the inspector. Editing the text commits when the box loses focus.</summary>
public sealed partial class EntryRow(ChecklistEntry initial, Action<EntryRow, string> commitText, Action<EntryRow> toggle) : ObservableObject
{
    private ChecklistEntry entry = initial;

    public string Id { get; } = initial.Id;

    /// <summary>Updates in place: recreating rows on every click would lose focus and jump the scroll position.</summary>
    public void Update(ChecklistEntry updated)
    {
        entry = updated;
        OnPropertyChanged(nameof(Done));
        OnPropertyChanged(nameof(Text));
    }

    /// <summary>
    /// A two-way binding rather than a command: UI Automation, which screen readers use, toggles a checkbox
    /// without raising the click that would run a command.
    /// </summary>
    public bool Done
    {
        get => entry.Done;
        set
        {
            if (value != entry.Done)
            {
                toggle(this);
            }
        }
    }

    public string Text
    {
        get => entry.Text;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && value != entry.Text)
            {
                commitText(this, value);
            }
        }
    }
}

public sealed partial class EditorViewModel : ObservableObject, IDisposable
{
    private const double DefaultImageShare = 0.6;

    private readonly IMonitorService _monitors;
    private readonly IWallpaperService _wallpapers;
    private readonly WallpaperPublisher _publisher;
    private readonly WpfWallpaperRenderer _renderer;
    private readonly IDialogs _dialogs;
    private readonly Action _back;
    private readonly CompanionChannel _companion;
    private readonly ProjectEditor _editor;
    private readonly CoalescingRunner _publishRunner;
    private readonly int _headAtOpen;
    private CancellationTokenSource? _thumbnailCancellation;
    private bool _syncing;
    private bool _refreshingItems;

    public EditorViewModel(
        ProjectFolder project,
        IMonitorService monitors,
        IWallpaperService wallpapers,
        WallpaperPublisher publisher,
        WpfWallpaperRenderer renderer,
        IDialogs dialogs,
        Action back,
        CompanionChannel companion)
    {
        _companion = companion;
        _monitors = monitors;
        _wallpapers = wallpapers;
        _publisher = publisher;
        _renderer = renderer;
        _dialogs = dialogs;
        _back = back;
        _editor = new ProjectEditor(project, StyledItemPainter.RequiredHeight);
        _publishRunner = new CoalescingRunner(PublishLatestAsync);
        _headAtOpen = project.Head;
        Images = new DocumentImages(project.ResolveAsset);

        Status = project.IsReadOnly
            ? "Este projeto está aberto em outra janela. Aqui ele fica somente para leitura."
            : project.RecoveryNote;

        _editor.Changed += (_, _) => OnDocumentChanged();

        RefreshMonitors();
        RefreshHistory();
        RefreshItems();
        SyncBackground();
    }

    public event EventHandler? DocumentChanged;

    public event EventHandler? SelectionChanged;

    public DocumentImages Images { get; }

    public string ProjectName => _editor.Project.Name;

    public string ProjectPath => _editor.Project.FolderPath;

    public PixelSize Canvas => _editor.Project.Canvas;

    public WallpaperDocument Document => _editor.Document;

    public bool IsEditable => !_editor.Project.IsReadOnly;

    public ObservableCollection<MonitorOption> Monitors { get; } = [];

    public ObservableCollection<HistoryRow> History { get; } = [];

    public ObservableCollection<EntryRow> Entries { get; } = [];

    /// <summary>Topmost first, the way a person reads a stack of things.</summary>
    public ObservableCollection<ItemRow> Items { get; } = [];

    [ObservableProperty]
    public partial ItemRow? SelectedRow { get; set; }

    public IReadOnlyList<FitOption> FitOptions { get; } =
    [
        new(FitMode.Fill, "Preencher"),
        new(FitMode.Fit, "Ajustar"),
        new(FitMode.Center, "Centralizar"),
        new(FitMode.Stretch, "Esticar"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    public partial MonitorOption? PublishMonitor { get; set; }

    [ObservableProperty]
    public partial string? PublishState { get; set; }

    [ObservableProperty]
    public partial bool HasUnpublishedChanges { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    public partial bool IsPublishing { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial FitMode BackgroundFit { get; set; }

    [ObservableProperty]
    public partial string? BackgroundName { get; set; }

    /// <summary>The selected item. Drags report back through <see cref="CommitPlacement"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageSelected))]
    [NotifyPropertyChangedFor(nameof(IsChecklistSelected))]
    [NotifyPropertyChangedFor(nameof(IsTextSelected))]
    [NotifyPropertyChangedFor(nameof(IsStyledSelected))]
    [NotifyPropertyChangedFor(nameof(IsControlSelected))]
    [NotifyPropertyChangedFor(nameof(IsCounterSelected))]
    [NotifyPropertyChangedFor(nameof(IsProgressSelected))]
    [NotifyPropertyChangedFor(nameof(IsToggleSelected))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(BringForwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendBackwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(RealSizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(MirrorCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    public partial DocumentItem? SelectedItem { get; set; }

    public bool HasSelection => SelectedItem is not null;

    public bool IsImageSelected => SelectedItem is ImageItem;

    public bool IsChecklistSelected => SelectedItem is ChecklistItem;

    public bool IsTextSelected => SelectedItem is TextItem;

    public bool IsStyledSelected => SelectedItem is StyledItem;

    public bool IsControlSelected => SelectedItem is ControlItem;

    public bool IsCounterSelected => SelectedItem is CounterItem;

    public bool IsProgressSelected => SelectedItem is ProgressItem;

    public bool IsToggleSelected => SelectedItem is ToggleItem;

    /// <summary>The label of a counter, progress bar or switch.</summary>
    [ObservableProperty]
    public partial string ControlLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int CounterValue { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool ToggleOn { get; set; }

    [ObservableProperty]
    public partial string ToggleOnText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ToggleOffText { get; set; } = string.Empty;

    /// <summary>The content of a text item. Commits when the box loses focus.</summary>
    [ObservableProperty]
    public partial string ItemText { get; set; } = string.Empty;

    public IReadOnlyList<TextAlignOption> TextAlignOptions { get; } =
    [
        new(null, "Automático"),
        new(TextAlign.Left, "Esquerda"),
        new(TextAlign.Center, "Centro"),
        new(TextAlign.Right, "Direita"),
        new(TextAlign.Justify, "Justificado"),
    ];

    [ObservableProperty]
    public partial TextAlignOption? ItemTextAlign { get; set; }

    [ObservableProperty]
    public partial bool ItemBold { get; set; }

    [ObservableProperty]
    public partial bool ItemItalic { get; set; }

    [ObservableProperty]
    public partial double ItemX { get; set; }

    [ObservableProperty]
    public partial double ItemY { get; set; }

    [ObservableProperty]
    public partial double ItemWidth { get; set; }

    [ObservableProperty]
    public partial double ItemHeight { get; set; }

    [ObservableProperty]
    public partial double ItemRotation { get; set; }

    [ObservableProperty]
    public partial double ItemOpacity { get; set; } = 100;

    [ObservableProperty]
    public partial string? ScaleNote { get; set; }

    [ObservableProperty]
    public partial bool IsScaleReduced { get; set; }

    [ObservableProperty]
    public partial string ChecklistTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ItemFontSize { get; set; }

    /// <summary>Installed fonts, filled the first time a text item is selected, so opening the app does not pay for it.</summary>
    public ObservableCollection<string> FontOptions { get; } = [];

    [ObservableProperty]
    public partial string? ItemFont { get; set; }

    /// <summary>Says so when a project came from a machine that has a font this one lacks.</summary>
    [ObservableProperty]
    public partial string? FontNote { get; set; }

    [ObservableProperty]
    public partial double ItemTextOpacity { get; set; } = 100;

    [ObservableProperty]
    public partial double ItemPanelOpacity { get; set; } = 100;

    public IReadOnlyList<PanelShapeOption> PanelShapeOptions { get; } =
    [
        new(PanelShape.Rounded, "Arredondado"),
        new(PanelShape.Speech, "Balão de fala"),
        new(PanelShape.Thought, "Balão de pensamento"),
    ];

    public IReadOnlyList<TailSideOption> TailSideOptions { get; } =
    [
        new(TailSide.Left, "Esquerda"),
        new(TailSide.Right, "Direita"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTail))]
    public partial PanelShape ItemPanelShape { get; set; }

    [ObservableProperty]
    public partial TailSide ItemTail { get; set; }

    /// <summary>Only balloons have a tail to turn.</summary>
    public bool HasTail => ItemPanelShape != PanelShape.Rounded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    public partial string NewEntryText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreRemovedItemCommand))]
    public partial HistoryRow? SelectedHistory { get; set; }

    [ObservableProperty]
    public partial ImageSource? HistoryThumbnail { get; set; }

    public void Dispose()
    {
        _thumbnailCancellation?.Cancel();
        _editor.Project.Dispose();
    }

    public void RefreshMonitors()
    {
        var keep = PublishMonitor?.Info.Id;
        var assigned = _publisher.AssignmentsOf(ProjectPath).Select(a => a.MonitorId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<MonitorInfo> current;
        try
        {
            current = [.. _monitors.GetMonitors()];
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            Status = $"Não foi possível ler os monitores: {ex.Message}";
            return;
        }

        Monitors.Clear();
        foreach (var info in current)
        {
            Monitors.Add(new MonitorOption(info));
        }

        PublishMonitor = Monitors.FirstOrDefault(m => m.Info.Id == keep)
            ?? Monitors.FirstOrDefault(m => assigned.Contains(m.Info.Id.Value))
            ?? Monitors.FirstOrDefault(m => m.Info.Bounds.Size == Canvas)
            ?? Monitors.FirstOrDefault(m => m.Info.IsPrimary)
            ?? Monitors.FirstOrDefault();

        UpdatePublishState();
    }

    /// <summary>Called when a drag on the surface ends. One drag is one revision.</summary>
    public void CommitPlacement(string itemId, ItemRect rect, double rotation) =>
        Edit(() => _editor.Place(itemId, rect, rotation));

    /// <summary>A click on a control drawn in the preview. It changes the project, never the monitor by itself.</summary>
    public void Interact(string itemId, Hotspot hotspot)
    {
        ArgumentNullException.ThrowIfNull(hotspot);

        Edit(() =>
        {
            switch (hotspot.Action)
            {
                case HotspotAction.ToggleEntry when hotspot.EntryId is { } entryId:
                    _editor.ToggleEntry(itemId, entryId);
                    break;
                case HotspotAction.Decrement:
                    _editor.StepCounter(itemId, -1);
                    break;
                case HotspotAction.Increment:
                    _editor.StepCounter(itemId, 1);
                    break;
                case HotspotAction.Toggle when _editor.Find<ToggleItem>(itemId) is { } toggle:
                    _editor.SetToggle(itemId, !toggle.On);
                    break;
            }
        });
    }

    /// <summary>Called when a drag along a progress bar ends. One drag is one revision.</summary>
    public void SetProgress(string itemId, int percent) => Edit(() => _editor.SetProgress(itemId, percent));

    public void Select(string? itemId) =>
        SelectedItem = itemId is null ? null : Document.Items.FirstOrDefault(i => i.Id == itemId);

    partial void OnSelectedItemChanged(DocumentItem? value)
    {
        SelectedRow = value is null ? null : Items.FirstOrDefault(r => r.Id == value.Id);
        SyncInspector();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedRowChanged(ItemRow? value)
    {
        // Rebuilding the list clears its selection for a moment, which must not deselect the item.
        if (!_refreshingItems && value?.Id != SelectedItem?.Id)
        {
            Select(value?.Id);
        }
    }

    private void RefreshItems()
    {
        _refreshingItems = true;
        try
        {
            Items.Clear();
            foreach (var item in Document.Items.Reverse())
            {
                Items.Add(new ItemRow(item.Id, item switch
                {
                    ImageItem image => image.Source.OriginalName,
                    ChecklistItem list => list.Title.Length > 0 ? list.Title : "Checklist",
                    TextItem text => text.Label(),
                    CounterItem counter => counter.Label.Length > 0 ? counter.Label : "Contador",
                    ProgressItem progress => progress.Label.Length > 0 ? progress.Label : "Progresso",
                    ToggleItem toggle => toggle.Label.Length > 0 ? toggle.Label : "Alternância",
                    _ => "Item",
                }));
            }

            SelectedRow = Items.FirstOrDefault(r => r.Id == SelectedItem?.Id);
        }
        finally
        {
            _refreshingItems = false;
        }
    }

    partial void OnPublishMonitorChanged(MonitorOption? value) => UpdatePublishState();

    partial void OnBackgroundFitChanged(FitMode value)
    {
        if (!_syncing)
        {
            Edit(() => _editor.SetBackgroundFit(value));
        }
    }

    partial void OnItemXChanged(double value) => CommitFields();

    partial void OnItemYChanged(double value) => CommitFields();

    partial void OnItemWidthChanged(double value) => CommitFields();

    partial void OnItemHeightChanged(double value) => CommitFields();

    partial void OnItemRotationChanged(double value) => CommitFields();

    partial void OnItemOpacityChanged(double value)
    {
        if (!_syncing && SelectedItem is ImageItem image)
        {
            Edit(() => _editor.SetOpacity(image.Id, value / 100));
        }
    }

    partial void OnChecklistTitleChanged(string value)
    {
        if (!_syncing && SelectedItem is ChecklistItem list)
        {
            Edit(() => _editor.SetChecklistTitle(list.Id, value));
        }
    }

    partial void OnItemFontSizeChanged(double value)
    {
        if (!_syncing && SelectedItem is StyledItem item)
        {
            Edit(() => _editor.SetItemFontSize(item.Id, value));
        }
    }

    partial void OnItemFontChanged(string? value)
    {
        if (!_syncing && SelectedItem is StyledItem item && !string.IsNullOrWhiteSpace(value))
        {
            Edit(() => _editor.SetItemFont(item.Id, value));
        }
    }

    partial void OnItemTextOpacityChanged(double value)
    {
        if (!_syncing && SelectedItem is StyledItem item)
        {
            Edit(() => _editor.SetItemTextOpacity(item.Id, value / 100));
        }
    }

    partial void OnItemPanelOpacityChanged(double value)
    {
        if (!_syncing && SelectedItem is StyledItem item)
        {
            Edit(() => _editor.SetItemPanelOpacity(item.Id, value / 100));
        }
    }

    partial void OnItemTextAlignChanged(TextAlignOption? value)
    {
        if (!_syncing && value is not null && SelectedItem is TextItem item)
        {
            Edit(() => _editor.SetTextAlign(item.Id, value.Align));
        }
    }

    partial void OnItemBoldChanged(bool value)
    {
        if (!_syncing && SelectedItem is TextItem item)
        {
            Edit(() => _editor.SetTextBold(item.Id, value));
        }
    }

    partial void OnItemItalicChanged(bool value)
    {
        if (!_syncing && SelectedItem is TextItem item)
        {
            Edit(() => _editor.SetTextItalic(item.Id, value));
        }
    }

    partial void OnItemPanelShapeChanged(PanelShape value)
    {
        if (!_syncing && SelectedItem is StyledItem item)
        {
            Edit(() => _editor.SetItemPanelShape(item.Id, value));
        }
    }

    partial void OnItemTailChanged(TailSide value)
    {
        if (!_syncing && SelectedItem is StyledItem item)
        {
            Edit(() => _editor.SetItemTail(item.Id, value));
        }
    }

    partial void OnItemTextChanged(string value)
    {
        if (_syncing || SelectedItem is not TextItem item)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            // Text nobody can see or select is a trap. Removing the layer is what "Remover" is for.
            Status = "O texto não pode ficar vazio. Para tirar esta camada, use Remover.";
            SyncInspector();
            return;
        }

        Edit(() => _editor.SetText(item.Id, value));
    }

    partial void OnControlLabelChanged(string value)
    {
        if (!_syncing && SelectedItem is ControlItem item)
        {
            Edit(() => _editor.SetControlLabel(item.Id, value));
        }
    }

    partial void OnCounterValueChanged(int value)
    {
        if (!_syncing && SelectedItem is CounterItem item)
        {
            Edit(() => _editor.SetCounterValue(item.Id, value));
        }
    }

    partial void OnProgressPercentChanged(double value)
    {
        if (!_syncing && SelectedItem is ProgressItem item)
        {
            Edit(() => _editor.SetProgress(item.Id, (int)Math.Round(value)));
        }
    }

    partial void OnToggleOnChanged(bool value)
    {
        if (!_syncing && SelectedItem is ToggleItem item)
        {
            Edit(() => _editor.SetToggle(item.Id, value));
        }
    }

    partial void OnToggleOnTextChanged(string value) => CommitToggleTexts(value);

    partial void OnToggleOffTextChanged(string value) => CommitToggleTexts(value);

    private void CommitToggleTexts(string value)
    {
        if (_syncing || SelectedItem is not ToggleItem item)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            // A state with no text would look like nothing on the wallpaper.
            Status = "Os dois estados precisam de um texto.";
            SyncInspector();
            return;
        }

        Edit(() => _editor.SetToggleTexts(item.Id, ToggleOnText, ToggleOffText));
    }

    partial void OnSelectedHistoryChanged(HistoryRow? value) => _ = LoadHistoryThumbnailAsync(value);

    [RelayCommand]
    private async Task BackAsync()
    {
        if (await ConfirmLeaveAsync())
        {
            _back();
        }
    }

    /// <summary>
    /// Asks before leaving with changes the monitor does not show. The project itself is always saved; what would be
    /// lost is only the moment to update the wallpaper. Opening and closing without changing anything never asks.
    /// </summary>
    /// <returns>True to go on leaving.</returns>
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!IsEditable || _editor.Project.Head == _headAtOpen || !HasUnpublishedChanges || PublishMonitor is not { } monitor)
        {
            return true;
        }

        var onMonitor = _publisher.AssignmentsOf(ProjectPath)
            .Any(a => string.Equals(a.MonitorId, monitor.Info.Id.Value, StringComparison.OrdinalIgnoreCase));
        var message = onMonitor
            ? $"O wallpaper de {monitor.Info.Name} ainda não mostra as alterações feitas agora.\n\nO projeto já está salvo: só o wallpaper ficaria desatualizado."
            : $"Este projeto ainda não está em {monitor.Info.Name}.\n\nO projeto já está salvo: atualizar coloca ele como wallpaper desse monitor.";

        switch (_dialogs.Choose("Wallpaper desatualizado", message, "Atualizar e sair", "Sair sem atualizar", "Cancelar"))
        {
            case 0:
                await PublishQuietlyAsync();

                // If it failed, stay: the status line says why, and leaving would hide it.
                return !HasUnpublishedChanges;
            case 1:
                return true;
            default:
                return false;
        }
    }

    [RelayCommand]
    private void AddImage()
    {
        if (_dialogs.ChooseImage("Imagem para adicionar") is not { } file)
        {
            return;
        }

        var size = new DocumentImages(path => path).SizeOf(new AssetRef(file, Path.GetFileName(file)));
        if (size is not { } natural)
        {
            Status = "Não foi possível ler essa imagem.";
            return;
        }

        Edit(() =>
        {
            var item = _editor.AddImage(file, InitialRect(natural));
            Select(item.Id);
        });
    }

    [RelayCommand]
    private void AddChecklist()
    {
        var width = Math.Min(460, Canvas.Width * 0.8);
        var height = Math.Min(320, Canvas.Height * 0.8);
        var rect = new ItemRect(Canvas.Width - width - 40, 40, width, height);

        Edit(() =>
        {
            var item = _editor.AddChecklist("Tarefas", rect);
            Select(item.Id);
        });
    }

    [RelayCommand]
    private void AddText()
    {
        var width = Math.Min(600, Canvas.Width * 0.8);
        var height = Math.Min(120, Canvas.Height * 0.8);
        var rect = new ItemRect((Canvas.Width - width) / 2, (Canvas.Height - height) / 2, width, height);

        Edit(() =>
        {
            var item = _editor.AddText("Escreva aqui", rect);
            Select(item.Id);
        });
    }

    [RelayCommand]
    private void AddCounter() => AddControl(400, rect => _editor.AddCounter("Contador", rect));

    [RelayCommand]
    private void AddProgress() => AddControl(480, rect => _editor.AddProgress("Progresso", rect));

    [RelayCommand]
    private void AddToggle() => AddControl(400, rect => _editor.AddToggle("Modo", rect));

    /// <summary>
    /// Centered, and as tall as its content: the editor grows it to fit. Each one added in a row lands a little
    /// further down, instead of exactly on top of the last, where it would hide it.
    /// </summary>
    private void AddControl(double preferredWidth, Func<ItemRect, ControlItem> add)
    {
        const double Cascade = 40;

        var width = Math.Round(Math.Min(preferredWidth, Canvas.Width * 0.8));
        var rect = new ItemRect(Math.Round((Canvas.Width - width) / 2), Math.Round(Canvas.Height * 0.4), width, 40);
        while (Document.Items.Any(i => i.Rect.X == rect.X && i.Rect.Y == rect.Y))
        {
            rect = rect with { X = rect.X + Cascade, Y = rect.Y + Cascade };
        }

        Edit(() => Select(add(rect).Id));
    }

    [RelayCommand]
    private void IncrementCounter() => StepSelectedCounter(1);

    [RelayCommand]
    private void DecrementCounter() => StepSelectedCounter(-1);

    private void StepSelectedCounter(int delta)
    {
        if (SelectedItem is CounterItem counter)
        {
            Edit(() => _editor.StepCounter(counter.Id, delta));
        }
    }

    [RelayCommand]
    private void ChangeBackground()
    {
        if (_dialogs.ChooseImage("Nova imagem de fundo") is not { } file)
        {
            return;
        }

        if (new DocumentImages(path => path).SizeOf(new AssetRef(file, Path.GetFileName(file))) is null)
        {
            Status = "Não foi possível ler essa imagem.";
            return;
        }

        Edit(() => _editor.SetBackground(file, BackgroundFit));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveSelected()
    {
        if (SelectedItem is { } item)
        {
            Edit(() =>
            {
                _editor.RemoveItem(item.Id);
                SelectedItem = null;
            });
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void BringForward()
    {
        if (SelectedItem is { } item)
        {
            Edit(() => _editor.Reorder(item.Id, 1));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SendBackward()
    {
        if (SelectedItem is { } item)
        {
            Edit(() => _editor.Reorder(item.Id, -1));
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseRealSize))]
    private void Mirror()
    {
        if (SelectedItem is ImageItem image)
        {
            Edit(() => _editor.ToggleMirror(image.Id));
        }
    }

    private bool CanUseRealSize => SelectedItem is ImageItem;

    /// <summary>One image pixel per monitor pixel, which keeps the small print of a cheatsheet legible.</summary>
    [RelayCommand(CanExecute = nameof(CanUseRealSize))]
    private void RealSize()
    {
        if (SelectedItem is ImageItem image && Images.SizeOf(image.Source) is { } size)
        {
            var center = ItemGeometry.Center(image.Rect);
            Edit(() => _editor.Place(
                image.Id,
                new ItemRect(center.X - (size.Width / 2.0), center.Y - (size.Height / 2.0), size.Width, size.Height),
                image.Rotation));
        }
    }

    private bool CanAddEntry => SelectedItem is ChecklistItem && !string.IsNullOrWhiteSpace(NewEntryText);

    [RelayCommand(CanExecute = nameof(CanAddEntry))]
    private void AddEntry()
    {
        if (SelectedItem is ChecklistItem list)
        {
            Edit(() => _editor.AddEntry(list.Id, NewEntryText));
            NewEntryText = string.Empty;
        }
    }

    [RelayCommand]
    private void RemoveEntry(EntryRow row)
    {
        if (SelectedItem is ChecklistItem list)
        {
            Edit(() => _editor.RemoveEntry(list.Id, row.Id));
        }
    }

    /// <summary>Like every change, marking a task only reaches the monitor through "Atualizar wallpaper".</summary>
    private void ToggleEntry(EntryRow row)
    {
        if (SelectedItem is ChecklistItem list)
        {
            Edit(() => _editor.ToggleEntry(list.Id, row.Id));
        }
    }

    private bool CanPublish => !IsPublishing && PublishMonitor is not null;

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private Task PublishAsync() => PublishQuietlyAsync();

    private bool CanRevert => IsEditable && SelectedHistory is { IsHead: false, IsValid: true };

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private void Revert()
    {
        if (SelectedHistory is { } row)
        {
            Edit(() =>
            {
                _editor.Revert(row.Number);
                SelectedItem = null;
            });
        }
    }

    private bool CanRestoreRemovedItem => IsEditable && SelectedHistory is { RemovedItemId: not null };

    [RelayCommand(CanExecute = nameof(CanRestoreRemovedItem))]
    private void RestoreRemovedItem()
    {
        if (SelectedHistory is not { RemovedItemId: { } itemId } row)
        {
            return;
        }

        Edit(() =>
        {
            var parent = _editor.Project.ReadRevision(row.Number).Parent
                ?? throw new InvalidOperationException("The first revision removed nothing.");
            _editor.RestoreItem(parent, itemId);
            Select(itemId);
        });
    }

    /// <summary>Runs a change, reporting what can go wrong with files instead of closing the app.</summary>
    private bool Edit(Action change)
    {
        try
        {
            change();
            return true;
        }
        catch (ProjectReadOnlyException)
        {
            Status = "Este projeto está aberto em outra janela. Aqui ele fica somente para leitura.";
        }
        catch (ProjectUnavailableException ex)
        {
            Status = $"Não foi possível concluir: {ex.Message}";
        }
        catch (IOException ex)
        {
            Status = $"Não foi possível salvar: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = $"Não foi possível salvar: {ex.Message}";
        }

        return false;
    }

    private void OnDocumentChanged()
    {
        if (SelectedItem is { } selected)
        {
            // The revision may have replaced the item, so the reference is refreshed by id.
            SelectedItem = Document.Items.FirstOrDefault(i => i.Id == selected.Id);
        }

        RefreshItems();
        SyncInspector();
        SyncBackground();
        RefreshHistory();
        UpdatePublishState();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommitFields()
    {
        if (!_syncing && SelectedItem is { } item)
        {
            Edit(() => _editor.Place(item.Id, new ItemRect(ItemX, ItemY, ItemWidth, ItemHeight), ItemRotation));
        }
    }

    private void SyncInspector()
    {
        _syncing = true;
        try
        {
            ScaleNote = null;
            IsScaleReduced = false;

            if (SelectedItem is not ChecklistItem)
            {
                Entries.Clear();
            }

            if (SelectedItem is not { } item)
            {
                return;
            }

            ItemX = item.Rect.X;
            ItemY = item.Rect.Y;
            ItemWidth = item.Rect.Width;
            ItemHeight = item.Rect.Height;
            ItemRotation = item.Rotation;

            if (item is StyledItem styled)
            {
                ItemFontSize = styled.Style.Size;
                ItemTextOpacity = Math.Round(styled.Style.TextOpacity * 100);
                ItemPanelOpacity = Math.Round(styled.Style.PanelOpacity * 100);
                ItemPanelShape = styled.Style.Shape;
                ItemTail = styled.Style.Tail;
                SyncFont(styled.Style.Font);
            }

            switch (item)
            {
                case ImageItem image:
                    ItemOpacity = image.Opacity * 100;
                    UpdateScaleNote(image);
                    break;

                case ChecklistItem list:
                    ChecklistTitle = list.Title;
                    SyncEntries(list);
                    break;

                case TextItem text:
                    ItemText = text.Text;
                    ItemTextAlign = TextAlignOptions.First(o => o.Align == text.Align);
                    ItemBold = text.Bold;
                    ItemItalic = text.Italic;
                    break;
            }

            if (item is ControlItem control)
            {
                ControlLabel = control.Label;
            }

            switch (item)
            {
                case CounterItem counter:
                    CounterValue = counter.Value;
                    break;

                case ProgressItem progress:
                    ProgressPercent = progress.Percent;
                    break;

                case ToggleItem toggle:
                    ToggleOn = toggle.On;
                    ToggleOnText = toggle.OnText;
                    ToggleOffText = toggle.OffText;
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncFont(string font)
    {
        if (FontOptions.Count == 0)
        {
            foreach (var name in Fonts.SystemFontFamilies.Select(f => f.Source).Order(StringComparer.CurrentCultureIgnoreCase))
            {
                FontOptions.Add(name);
            }
        }

        var installed = FontOptions.Contains(font);
        if (!installed)
        {
            // Kept in the list so the selection shows what the project really asks for.
            FontOptions.Insert(0, font);
        }

        ItemFont = font;
        FontNote = installed ? null : $"A fonte {font} não está instalada aqui, e o texto usa uma alternativa.";
    }

    /// <summary>Keeps existing rows when the same entries are still there, as after marking one.</summary>
    private void SyncEntries(ChecklistItem list)
    {
        if (Entries.Select(e => e.Id).SequenceEqual(list.Entries.Select(e => e.Id)))
        {
            for (var i = 0; i < Entries.Count; i++)
            {
                Entries[i].Update(list.Entries[i]);
            }

            return;
        }

        Entries.Clear();
        foreach (var entry in list.Entries)
        {
            Entries.Add(new EntryRow(entry, CommitEntryText, ToggleEntry));
        }
    }

    private void CommitEntryText(EntryRow row, string text)
    {
        if (SelectedItem is ChecklistItem list)
        {
            Edit(() => _editor.EditEntry(list.Id, row.Id, text));
        }
    }

    private void UpdateScaleNote(ImageItem image)
    {
        if (Images.SizeOf(image.Source) is not { } size)
        {
            ScaleNote = "Não foi possível ler esta imagem.";
            IsScaleReduced = true;
            return;
        }

        var scale = Math.Min(image.Rect.Width / size.Width, image.Rect.Height / size.Height);
        var percent = (int)Math.Round(scale * 100);
        IsScaleReduced = percent < 100;
        ScaleNote = IsScaleReduced
            ? $"Em {percent}% do tamanho real. Texto pequeno pode ficar difícil de ler."
            : $"Em {percent}% do tamanho real.";
    }

    private void SyncBackground()
    {
        _syncing = true;
        try
        {
            BackgroundFit = Document.Background.Fit;
            BackgroundName = Document.Background.Source.OriginalName;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RefreshHistory()
    {
        var head = _editor.Project.Head;
        History.Clear();
        foreach (var summary in _editor.Project.ReadHistory())
        {
            History.Add(new HistoryRow(
                summary.Number,
                summary.Change?.Label ?? "Revisão danificada",
                summary.CreatedAt?.LocalDateTime.ToString("dd/MM HH:mm") ?? string.Empty,
                summary.Number == head,
                summary.IsValid,
                summary.Change is { Kind: ChangeKinds.ItemRemoved } change ? change.ItemId : null));
        }
    }

    private async Task LoadHistoryThumbnailAsync(HistoryRow? row)
    {
        _thumbnailCancellation?.Cancel();
        _thumbnailCancellation = new CancellationTokenSource();
        var cancellation = _thumbnailCancellation.Token;
        HistoryThumbnail = null;

        if (row is not { IsValid: true })
        {
            return;
        }

        try
        {
            var document = _editor.Project.ReadRevision(row.Number).Document;
            var scale = 300.0 / Math.Max(Canvas.Width, Canvas.Height);
            var size = new PixelSize(Math.Max(1, (int)(Canvas.Width * scale)), Math.Max(1, (int)(Canvas.Height * scale)));
            var bitmap = await _renderer.RenderDocumentPreviewAsync(
                new DocumentRenderRequest(document, Canvas, _editor.Project.ResolveAsset, size), cancellation);

            if (!cancellation.IsCancellationRequested)
            {
                HistoryThumbnail = bitmap;
            }
        }
        catch (OperationCanceledException)
        {
            // Another row was selected.
        }
        catch (ProjectUnavailableException)
        {
            HistoryThumbnail = null;
        }
    }

    private ItemRect InitialRect(PixelSize natural)
    {
        // Real size when it fits, since that is what keeps a cheatsheet legible.
        var scale = Math.Min(1, DefaultImageShare * Math.Min((double)Canvas.Width / natural.Width, (double)Canvas.Height / natural.Height));
        if (natural.Width <= Canvas.Width && natural.Height <= Canvas.Height)
        {
            scale = 1;
        }

        var width = natural.Width * scale;
        var height = natural.Height * scale;
        return new ItemRect((Canvas.Width - width) / 2, (Canvas.Height - height) / 2, width, height);
    }

    private void UpdatePublishState()
    {
        if (PublishMonitor is not { } monitor)
        {
            PublishState = "Nenhum monitor disponível.";
            HasUnpublishedChanges = false;
            return;
        }

        var assignment = _publisher.AssignmentsOf(ProjectPath)
            .FirstOrDefault(a => string.Equals(a.MonitorId, monitor.Info.Id.Value, StringComparison.OrdinalIgnoreCase));

        var isCurrent = assignment?.ProjectRevision == _editor.Project.Head
            && string.Equals(_wallpapers.GetWallpaper(monitor.Info.Id), assignment.PublishedFile, StringComparison.OrdinalIgnoreCase);

        HasUnpublishedChanges = !isCurrent;
        PublishState = (assignment, isCurrent) switch
        {
            (null, _) => "Ainda não está neste monitor.",
            (_, true) => "O wallpaper mostra a versão atual.",
            _ => "Há alterações que o wallpaper ainda não mostra.",
        };
    }

    /// <summary>Always ends with the monitor showing the head at that moment, however many clicks came in.</summary>
    private async Task PublishQuietlyAsync()
    {
        IsPublishing = true;
        try
        {
            await _publishRunner.RequestAsync();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Status = Describe(ex);
        }
        finally
        {
            IsPublishing = _publishRunner.IsRunning;
            UpdatePublishState();
        }
    }

    private async Task PublishLatestAsync()
    {
        if (PublishMonitor is not { } monitor)
        {
            return;
        }

        var revision = _editor.Project.Head;
        var document = _editor.Document;

        try
        {
            await PublishRevisionAsync(monitor, revision, document, skipUnreadable: false);
        }
        catch (ImageLoadException ex)
        {
            if (!_dialogs.Confirm(
                "Imagem ilegível",
                $"Não foi possível ler {ex.ImagePath}.\n\nAtualizar o wallpaper sem ela? O projeto não muda, e ela continua no histórico."))
            {
                Status = "O wallpaper não mudou.";
                return;
            }

            await PublishRevisionAsync(monitor, revision, document, skipUnreadable: true);
        }
        catch (SpanModeActiveException)
        {
            if (!_dialogs.Confirm(
                "Modo span ativo",
                "O Windows está estendendo uma única imagem por todos os monitores, e assim não há wallpaper por monitor.\n\n"
                + "Mudar o Windows para o modo Preencher? Isso afeta os outros monitores."))
            {
                Status = "O wallpaper não mudou.";
                return;
            }

            _wallpapers.SetPosition(WallpaperPosition.Fill);
            await PublishRevisionAsync(monitor, revision, document, skipUnreadable: false);
        }

        Status = $"Wallpaper atualizado em {monitor.Info.Name}.";

        // A dynamic wallpaper is on screen now: the companion comes up to make it usable without the editor.
        _companion.Sync(_publisher);
    }

    private Task PublishRevisionAsync(MonitorOption monitor, int revision, WallpaperDocument document, bool skipUnreadable) =>
        _publisher.PublishProjectAsync(
            monitor.Info.Id,
            ProjectPath,
            revision,
            document,
            Canvas,
            _editor.Project.ResolveAsset,
            skipUnreadable);

    private static bool IsExpected(Exception ex) =>
        ex is ImageLoadException or MonitorUnavailableException or COMException or IOException or UnauthorizedAccessException;

    private static string Describe(Exception ex) => ex switch
    {
        MonitorUnavailableException => "Este monitor não está mais conectado. O wallpaper não mudou.",
        _ => $"Não foi possível atualizar o wallpaper: {ex.Message}",
    };
}
