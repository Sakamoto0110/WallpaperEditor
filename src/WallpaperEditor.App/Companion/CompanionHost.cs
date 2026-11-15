using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using WallpaperEditor.App.ViewModels;
using WallpaperEditor.App.Views;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Projects;
using WallpaperEditor.Core.Publishing;
using Application = System.Windows.Application;

namespace WallpaperEditor.App.Companion;

/// <summary>
/// The companion process: an icon in the notification area and, on request, a window to use a dynamic wallpaper.
/// Nothing runs between clicks; it waits on a pipe and on the icon, and holds no project while no window is open.
/// </summary>
public sealed class CompanionHost : IDisposable
{
    private readonly CompanionChannel _channel;
    private readonly IMonitorService _monitors;
    private readonly WallpaperPublisher _publisher;
    private readonly Application _app;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, InteractionWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _closing = [];
    private readonly CancellationTokenSource _stop = new();
    private Mutex? _mutex;
    private NotifyIcon? _tray;

    public CompanionHost(CompanionChannel channel, IMonitorService monitors, WallpaperPublisher publisher, Application app)
    {
        _channel = channel;
        _monitors = monitors;
        _publisher = publisher;
        _app = app;
        _dispatcher = app.Dispatcher;
    }

    /// <summary>False if this process has nothing to do and should end: another companion took the request, or
    /// no monitor shows a dynamic wallpaper any more.</summary>
    public bool Start(MonitorId? interactWith)
    {
        _mutex = new Mutex(initiallyOwned: true, _channel.MutexName, out var isFirst);
        if (!isFirst)
        {
            _mutex.Dispose();
            _mutex = null;
            _channel.TrySend(interactWith is null ? "refresh" : $"interact {interactWith.Value}");
            return false;
        }

        // Started with Windows after the wallpaper was replaced: nothing left to serve, and no reason to start again.
        var shown = ShownProjects().Count > 0;
        if (interactWith is null && !shown)
        {
            _channel.SetStartWithWindows(false);
            return false;
        }

        // Registered from where it runs now: after an install or an update, the entry follows the new folder.
        if (shown)
        {
            _channel.SetStartWithWindows(true);
        }

        _ = Task.Run(() => ListenAsync(_stop.Token));
        ShowTrayIcon();

        if (interactWith is not null)
        {
            OpenInteraction(interactWith);
        }
        else
        {
            // Started to wait, as after signing in: let go of what starting up needed.
            _dispatcher.BeginInvoke(Trim, DispatcherPriority.ApplicationIdle);
        }

        return true;
    }

    /// <summary>Safe to call twice: once on "Sair", and again as the app exits.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }

        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
    }

    private void ShowTrayIcon()
    {
        var menu = new ContextMenuStrip();
        _tray = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            ContextMenuStrip = menu,
            Visible = true,
        };

        // Built now and again on every opening, so it is never empty: an empty menu cancels its own opening.
        BuildMenu(menu);
        menu.Opening += (_, e) =>
        {
            BuildMenu(menu);
            e.Cancel = false;
        };

        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OnIconClicked();
            }
        };
    }

    /// <summary>What the icon says when hovered: which wallpaper it serves.</summary>
    private void UpdateTooltip(IReadOnlyList<MonitorAssignment> shown, Dictionary<string, string> names)
    {
        if (_tray is null)
        {
            return;
        }

        var text = shown.Count switch
        {
            0 => "WallpaperEditor\nNenhum wallpaper dinâmico na tela",
            1 => $"WallpaperEditor\n{Describe(shown[0], names)}\nClique para interagir",
            _ => $"WallpaperEditor\n{shown.Count} wallpapers dinâmicos na tela",
        };

        // The limit Windows puts on the text of a notification icon.
        _tray.Text = text.Length <= 127 ? text : text[..126] + "…";
    }

    private static string Describe(MonitorAssignment assignment, Dictionary<string, string> names) =>
        $"{Path.GetFileName(assignment.ProjectPath)} em {names.GetValueOrDefault(assignment.MonitorId) ?? assignment.LastKnownName ?? "um monitor"}";

    /// <summary>With one dynamic wallpaper, a click goes straight to it; with several, the menu asks which.</summary>
    private void OnIconClicked()
    {
        var shown = ShownProjects();
        if (shown.Count == 1)
        {
            OpenInteraction(new MonitorId(shown[0].MonitorId));
            return;
        }

        // NotifyIcon has no public way to open its menu; this is the method a right click uses.
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_tray, null);
    }

    /// <summary>
    /// Abrir and Editar wallpaper for each dynamic wallpaper on screen, then Sair, which ends this process. With
    /// several, each pair names its wallpaper.
    /// </summary>
    private void BuildMenu(ContextMenuStrip menu)
    {
        menu.Items.Clear();

        var shown = ShownProjects();
        var names = MonitorNames();
        UpdateTooltip(shown, names);

        if (shown.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("Nenhum wallpaper dinâmico na tela") { Enabled = false });
            var editor = new ToolStripMenuItem("Abrir o editor");
            editor.Click += (_, _) => _channel.OpenEditor();
            menu.Items.Add(editor);
        }

        foreach (var assignment in shown)
        {
            var suffix = shown.Count == 1 ? string.Empty : $": {Describe(assignment, names)}";
            var id = new MonitorId(assignment.MonitorId);
            var path = assignment.ProjectPath!;

            // In bold, as what a click on the icon does.
            var open = new ToolStripMenuItem($"Abrir{suffix}") { Font = new Font(menu.Font, System.Drawing.FontStyle.Bold) };
            open.Click += (_, _) => OpenInteraction(id);
            menu.Items.Add(open);

            var edit = new ToolStripMenuItem($"Editar wallpaper{suffix}");
            edit.Click += (_, _) => OpenInEditor(id, path);
            menu.Items.Add(edit);
        }

        menu.Items.Add(new ToolStripSeparator());

        var quit = new ToolStripMenuItem("Sair");
        quit.Click += (_, _) => _ = ExitAsync();
        menu.Items.Add(quit);
    }

    /// <summary>An open window lets the project go first, or the editor would open it read-only.</summary>
    private void OpenInEditor(MonitorId monitorId, string projectPath)
    {
        if (_windows.TryGetValue(monitorId.Value, out var open))
        {
            open.CloseForEditor();
            return;
        }

        _channel.OpenEditor(projectPath);
    }

    private void OpenInteraction(MonitorId monitorId)
    {
        if (_windows.TryGetValue(monitorId.Value, out var open))
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        var monitor = _monitors.GetMonitors().FirstOrDefault(m => m.Id == monitorId);
        if (monitor is null || _publisher.GetAssignment(monitorId)?.ProjectPath is not { } path)
        {
            Notify("Este monitor não mostra mais um wallpaper dinâmico.");
            return;
        }

        ProjectFolder project;
        try
        {
            project = ProjectFolder.Open(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectUnavailableException)
        {
            Notify($"Não foi possível abrir o projeto: {ex.Message}");
            return;
        }

        var model = new InteractionViewModel(project, monitor, _publisher);
        var window = new InteractionWindow(model);
        window.Closed += (_, _) =>
        {
            _windows.Remove(monitorId.Value);
            _closing.Add(LetGoAsync(model, window.EditorRequested ? path : null));
        };

        _windows[monitorId.Value] = window;
        window.Show();
        window.Activate();
    }

    /// <summary>A change on its way still reaches the monitor, then the project is free for the editor.</summary>
    private async Task LetGoAsync(InteractionViewModel model, string? openInEditor)
    {
        await model.FinishAsync();
        model.Dispose();
        if (openInEditor is not null)
        {
            _channel.OpenEditor(openInEditor);
        }

        if (_windows.Count == 0)
        {
            Trim();
        }
    }

    /// <summary>
    /// Between uses the companion only waits, so the decoded images and everything else the window needed go back
    /// to Windows instead of sitting in memory until the next click.
    /// </summary>
    private static void Trim()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        EmptyWorkingSet(self.Handle);
    }

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr process);

    private async Task ExitAsync()
    {
        foreach (var window in _windows.Values.ToList())
        {
            window.Close();
        }

        await Task.WhenAll(_closing);
        Dispose();
        _app.Shutdown();
    }

    private async Task ListenAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _channel.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellation);
                using var reader = new StreamReader(server);
                if (await reader.ReadLineAsync(cancellation) is { } line)
                {
                    await _dispatcher.InvokeAsync(() => Handle(line));
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // A client that went away mid-message; wait for the next one.
            }
        }
    }

    private void Handle(string command)
    {
        var (verb, argument) = command.IndexOf(' ') is var space and > 0
            ? (command[..space], command[(space + 1)..])
            : (command, string.Empty);

        switch (verb)
        {
            case "interact" when argument.Length > 0:
                OpenInteraction(new MonitorId(argument));
                break;
            case "refresh" when _tray?.ContextMenuStrip is { } menu:
                // The app published or restored something: the tooltip and menu follow.
                BuildMenu(menu);
                break;
            case "exit":
                _ = ExitAsync();
                break;
        }
    }

    private List<MonitorAssignment> ShownProjects()
    {
        try
        {
            return [.. _publisher.ShownProjects()];
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException)
        {
            return [];
        }
    }

    private Dictionary<string, string> MonitorNames()
    {
        try
        {
            return _monitors.GetMonitors().ToDictionary(m => m.Id.Value, m => m.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }

    private void Notify(string message) => _tray?.ShowBalloonTip(5000, "WallpaperEditor", message, ToolTipIcon.Info);
}
