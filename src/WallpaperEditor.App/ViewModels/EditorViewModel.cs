using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.ViewModels;

public sealed record MonitorOption(MonitorInfo Info)
{
    public string Label => $"{Info.Name} ({Info.Bounds.Width} × {Info.Bounds.Height})";
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
    private readonly ProjectEditor _editor;
    private readonly CoalescingRunner _publishRunner;
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
        Action back)
    {
        _monitors = monitors;
        _wallpapers = wallpapers;
        _publisher = publisher;
        _renderer = renderer;
        _dialogs = dialogs;
        _back = back;
        _editor = new ProjectEditor(project, ChecklistPainter.RequiredHeight);
        _publishRunner = new CoalescingRunner(PublishLatestAsync);
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
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(BringForwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendBackwardCommand))]
    [NotifyCanExecuteChangedFor(nameof(RealSizeCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    public partial DocumentItem? SelectedItem { get; set; }

    public bool HasSelection => SelectedItem is not null;

    public bool IsImageSelected => SelectedItem is ImageItem;

    public bool IsChecklistSelected => SelectedItem is ChecklistItem;

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
    public partial double ChecklistFontSize { get; set; }

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

    partial void OnChecklistFontSizeChanged(double value)
    {
        if (!_syncing && SelectedItem is ChecklistItem list)
        {
            Edit(() => _editor.SetChecklistFontSize(list.Id, value));
        }
    }

    partial void OnSelectedHistoryChanged(HistoryRow? value) => _ = LoadHistoryThumbnailAsync(value);

    [RelayCommand]
    private void Back() => _back();

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

    /// <summary>Marking a task is the one change that also reaches the monitor at once, if the project is on one.</summary>
    private void ToggleEntry(EntryRow row)
    {
        if (SelectedItem is not ChecklistItem list)
        {
            return;
        }

        if (Edit(() => _editor.ToggleEntry(list.Id, row.Id)) && IsOnSelectedMonitor())
        {
            _ = PublishQuietlyAsync();
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

            switch (item)
            {
                case ImageItem image:
                    ItemOpacity = image.Opacity * 100;
                    UpdateScaleNote(image);
                    break;

                case ChecklistItem list:
                    ChecklistTitle = list.Title;
                    ChecklistFontSize = list.Style.Size;
                    SyncEntries(list);
                    break;
            }
        }
        finally
        {
            _syncing = false;
        }
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

    private bool IsOnSelectedMonitor() =>
        PublishMonitor is { } monitor
        && _publisher.AssignmentsOf(ProjectPath).Any(a => string.Equals(a.MonitorId, monitor.Info.Id.Value, StringComparison.OrdinalIgnoreCase));

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
            (_, true) => "O monitor mostra a versão atual.",
            _ => "Há alterações que o monitor ainda não mostra.",
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
                $"Não foi possível ler {ex.ImagePath}.\n\nPublicar sem ela? O projeto não muda, e ela continua no histórico."))
            {
                Status = "Nada foi publicado.";
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
                Status = "Nada foi publicado.";
                return;
            }

            _wallpapers.SetPosition(WallpaperPosition.Fill);
            await PublishRevisionAsync(monitor, revision, document, skipUnreadable: false);
        }

        Status = $"Publicado em {monitor.Info.Name}.";
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
        _ => $"Não foi possível publicar: {ex.Message}",
    };
}
