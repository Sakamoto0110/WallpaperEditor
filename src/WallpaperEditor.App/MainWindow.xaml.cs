using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using WallpaperEditor.App.ViewModels;

namespace WallpaperEditor.App;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
    }

    private ShellViewModel Shell => (ShellViewModel)DataContext;

    /// <summary>
    /// The question is asked inside this handler, so a plain answer decides at once. Updating the wallpaper first
    /// takes a moment: the close is held, and done again once the update is over.
    /// </summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        var decision = Shell.ConfirmCloseAsync();
        if (decision.IsCompleted)
        {
            e.Cancel = !decision.Result;
            return;
        }

        e.Cancel = true;
        if (await decision)
        {
            _closeConfirmed = true;
            await Dispatcher.InvokeAsync(Close);
        }
    }

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
