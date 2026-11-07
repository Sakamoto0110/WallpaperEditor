using System.IO;
using System.Windows;
using System.Windows.Threading;
using WallpaperEditor.App.ViewModels;
using WallpaperEditor.Core.Publishing;
using WallpaperEditor.Platform.Windows;
using WallpaperEditor.Rendering;

namespace WallpaperEditor.App;

public partial class App : Application
{
    private string _dataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WallpaperEditor");

    /// <param name="e">
    /// Supports <c>--project &lt;folder&gt;</c> to open a project directly, and <c>--data &lt;folder&gt;</c> to keep
    /// this machine's state somewhere else, so a test run cannot disturb the real one.
    /// </param>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        _dataRoot = ArgumentAfter(e.Args, "--data") ?? _dataRoot;

        var windows = new DesktopWallpaperService();
        var renderer = new WpfWallpaperRenderer();
        var publisher = new WallpaperPublisher(
            windows,
            windows,
            renderer,
            new AppStateStore(Path.Combine(_dataRoot, "state.json")),
            Path.Combine(_dataRoot, "published"));

        var projectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "WallpaperEditor");

        var window = new MainWindow();
        var shell = new ShellViewModel(windows, windows, publisher, renderer, new WpfDialogs(window), projectsRoot);
        window.DataContext = shell;
        MainWindow = window;
        window.Show();

        shell.Start(ArgumentAfter(e.Args, "--project"));
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
