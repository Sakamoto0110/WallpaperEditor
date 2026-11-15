using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Core.Rendering;
using WallpaperEditor.Core.Wallpapers;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App.ViewModels;

/// <summary>
/// The quick way to use a dynamic wallpaper: its components respond, nothing else does, and every change goes to
/// the monitor at once. Arranging items stays in the editor.
/// </summary>
/// <remarks>
/// The project is held only while this is open, so the editor can open it again as soon as the window closes.
/// </remarks>
public sealed partial class InteractionViewModel : ObservableObject, IDisposable
{
    private const string Ready = "Clique nos componentes: cada mudança vai direto para o wallpaper.";

    private readonly WallpaperPublisher _publisher;
    private readonly ProjectEditor _editor;
    private readonly CoalescingRunner _publishRunner;
    private Task _lastPublish = Task.CompletedTask;

    public InteractionViewModel(ProjectFolder project, MonitorInfo monitor, WallpaperPublisher publisher)
    {
        Monitor = monitor;
        _publisher = publisher;
        _editor = new ProjectEditor(project, StyledItemPainter.RequiredHeight);
        _publishRunner = new CoalescingRunner(PublishLatestAsync);
        Images = new DocumentImages(project.ResolveAsset);
        _editor.Changed += (_, _) => DocumentChanged?.Invoke(this, EventArgs.Empty);

        Status = project.IsReadOnly
            ? "Este projeto está aberto no editor. Feche-o lá para interagir por aqui."
            : Ready;
    }

    public event EventHandler? DocumentChanged;

    public MonitorInfo Monitor { get; }

    public string Title => $"{_editor.Project.Name} · {Monitor.Name}";

    public DocumentImages Images { get; }

    public PixelSize Canvas => _editor.Project.Canvas;

    public WallpaperDocument Document => _editor.Document;

    public bool IsInteractive => !_editor.Project.IsReadOnly;

    [ObservableProperty]
    public partial string Status { get; set; }

    [ObservableProperty]
    public partial bool IsPublishing { get; set; }

    /// <summary>A click on a component. The wallpaper follows right after.</summary>
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

    /// <summary>Called when a drag along a progress bar ends: one drag is one revision, and one change.</summary>
    public void SetProgress(string itemId, int percent) => Edit(() => _editor.SetProgress(itemId, percent));

    /// <summary>Lets a change still on its way to the monitor arrive before the project is let go.</summary>
    public async Task FinishAsync()
    {
        try
        {
            await _lastPublish;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            // Already reported while the window was open.
        }
    }

    public void Dispose() => _editor.Project.Dispose();

    private void Edit(Action change)
    {
        var head = _editor.Project.Head;
        try
        {
            change();
        }
        catch (ProjectReadOnlyException)
        {
            Status = "Este projeto está aberto no editor. Feche-o lá para interagir por aqui.";
        }
        catch (Exception ex) when (ex is ProjectUnavailableException or IOException or UnauthorizedAccessException)
        {
            Status = $"Não foi possível salvar: {ex.Message}";
        }

        if (_editor.Project.Head != head)
        {
            _lastPublish = PublishAsync();
        }
    }

    /// <summary>Always ends with the monitor showing the latest click, however many came in.</summary>
    private async Task PublishAsync()
    {
        IsPublishing = true;
        Status = "Atualizando o wallpaper...";
        try
        {
            await _publishRunner.RequestAsync();
        }
        catch (SpanModeActiveException)
        {
            Status = "O Windows está estendendo uma imagem por todos os monitores. Atualize pelo editor, que pode mudar isso.";
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Status = ex is MonitorUnavailableException
                ? "Este monitor não está mais conectado. A mudança ficou salva no projeto."
                : $"Não foi possível atualizar o wallpaper: {ex.Message}";
        }
        finally
        {
            IsPublishing = _publishRunner.IsRunning;
        }
    }

    private async Task PublishLatestAsync()
    {
        var revision = _editor.Project.Head;
        var document = _editor.Document;

        try
        {
            await Publish(revision, document, skipUnreadable: false);
            Status = Ready;
        }
        catch (ImageLoadException ex)
        {
            // No dialog here: this window is for quick clicks. The editor is where a missing image gets fixed.
            await Publish(revision, document, skipUnreadable: true);
            Status = $"Atualizado sem {Path.GetFileName(ex.ImagePath)}, que não pôde ser lida.";
        }
    }

    private Task Publish(int revision, WallpaperDocument document, bool skipUnreadable) =>
        _publisher.PublishProjectAsync(
            Monitor.Id,
            _editor.Project.FolderPath,
            revision,
            document,
            Canvas,
            _editor.Project.ResolveAsset,
            skipUnreadable);

    private static bool IsExpected(Exception ex) =>
        ex is ImageLoadException or MonitorUnavailableException or SpanModeActiveException
            or COMException or IOException or UnauthorizedAccessException;
}
