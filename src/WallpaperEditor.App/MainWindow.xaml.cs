using System.Windows;
using Microsoft.Win32;
using WallpaperEditor.App.ViewModels;

namespace WallpaperEditor.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    private void Window_Loaded(object sender, RoutedEventArgs e) =>
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

    /// <summary>A static event: left subscribed, it would keep this window alive.</summary>
    private void Window_Closed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Shell.Dispose();
    }

    /// <summary>Raised on another thread when a monitor is plugged in, rotated or changes resolution.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(Shell.OnDisplayChanged);
}
