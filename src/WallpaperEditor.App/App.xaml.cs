using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Threading;
using WallpaperEditor.App.Companion;
using WallpaperEditor.App.ViewModels;
using WallpaperEditor.Core.Monitors;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Platform.Windows;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App;

public partial class App : Application
{
    private string _dataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperEditor");

    private CompanionHost? _companion;

    /// <param name="e">
    /// Supports <c>--project &lt;folder&gt;</c> to open a project directly, and <c>--data &lt;folder&gt;</c> to keep
    /// this machine's state, and new projects, somewhere else, so a test run cannot disturb the real ones.
    /// <c>--companion</c> starts the companion instead of the main window, and <c>--interact &lt;monitor&gt;</c>
    /// opens its window for that monitor. <c>--stop-companion</c> ends the companion and takes it off the startup list,
    /// for the uninstaller. <c>--mirror &lt;folder&gt;</c> chooses where the second copy of the state goes.
    /// </param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var testData = ArgumentAfter(e.Args, "--data");
        _dataRoot = testData ?? _dataRoot;

        var mirrorFolder = MirrorFolder(testData, e.Args);
        var mirror = mirrorFolder is null ? null : new DataMirror(_dataRoot, mirrorFolder);

        var windows = new DesktopWallpaperService();
        var renderer = new WpfWallpaperRenderer();
        var publisher = new WallpaperPublisher(
            windows,
            windows,
            renderer,
            new AppStateStore(Path.Combine(_dataRoot, DataMirror.StateFile), mirror),
            Path.Combine(_dataRoot, "published"));

        var companion = new CompanionChannel(_dataRoot, isTestData: testData is not null);

        // For the uninstaller: nothing may keep running from the folder it is about to delete.
        if (e.Args.Contains(CompanionChannel.StopArgument))
        {
            companion.Stop();
            Shutdown();
            return;
        }

        if (e.Args.Contains(CompanionChannel.Argument))
        {
            StartCompanion(e.Args, companion, windows, publisher);
            return;
        }

        // With test data, new projects stay with it instead of landing in the real Documents.
        var projectsRoot = testData is not null
            ? Path.Combine(testData, "projects")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WallpaperEditor");

        var window = new MainWindow();
        var shell = new ShellViewModel(windows, windows, publisher, renderer, new WpfDialogs(window), projectsRoot, companion);
        window.DataContext = shell;
        MainWindow = window;
        window.Show();

        shell.Start(ArgumentAfter(e.Args, "--project"));

        // A companion closed from its menu comes back while a dynamic wallpaper is on screen.
        companion.Sync(publisher);

        // The first run after installing, or after choosing another folder, fills the mirror right away.
        mirror?.Save();
    }

    /// <summary>
    /// The second copy of this machine's state: the folder chosen in the installer, or <c>%USERPROFILE%\.wallpaperEditor</c>.
    /// A test run with <c>--data</c> has none, unless <c>--mirror &lt;folder&gt;</c> gives one.
    /// </summary>
    private static string? MirrorFolder(string? testData, string[] args)
    {
        if (ArgumentAfter(args, "--mirror") is { Length: > 0 } chosen)
        {
            return chosen;
        }

        if (testData is not null)
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\WallpaperEditor");
            if (key?.GetValue("MirrorFolder") is string { Length: > 0 } folder)
            {
                return Environment.ExpandEnvironmentVariables(folder);
            }
        }
        catch (System.Security.SecurityException)
        {
            // Falls back to the default folder.
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wallpaperEditor");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _companion?.Dispose();
        base.OnExit(e);
    }

    /// <summary>The same program without the main window: see <see cref="CompanionHost"/>.</summary>
    private void StartCompanion(string[] args, CompanionChannel channel, DesktopWallpaperService windows, WallpaperPublisher publisher)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var monitor = ArgumentAfter(args, CompanionChannel.InteractArgument) is { Length: > 0 } id ? new MonitorId(id) : null;

        _companion = new CompanionHost(channel, windows, publisher, this);
        if (!_companion.Start(monitor))
        {
            _companion.Dispose();
            _companion = null;
            Shutdown();
        }
    }

    private static string? ArgumentAfter(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>Records what went wrong, since nothing else would survive the app closing.</summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(_dataRoot, "logs")).FullName;
            File.WriteAllText(Path.Combine(folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt"), e.Exception.ToString());
        }
        catch (IOException)
        {
            // Failing to log must not hide the original problem.
        }

        MessageBox.Show(e.Exception.Message, "WallpaperEditor", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
