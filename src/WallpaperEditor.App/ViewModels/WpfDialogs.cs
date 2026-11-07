using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WallpaperEditor.App.ViewModels;

public interface IDialogs
{
    /// <summary>Returns the chosen image file, or null if the user cancelled.</summary>
    string? ChooseImage(string title);

    /// <summary>Returns the chosen folder, or null if the user cancelled.</summary>
    string? ChooseFolder(string title);

    /// <summary>Returns the typed text, or null if the user cancelled.</summary>
    string? AskText(string title, string label, string initial);

    bool Confirm(string title, string message);

    void Inform(string title, string message);
}

public sealed class WpfDialogs(Window owner) : IDialogs
{
    public string? ChooseImage(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic|Todos os arquivos|*.*",
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    public string? ChooseFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public string? AskText(string title, string label, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 14) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var content = new StackPanel { Margin = new Thickness(20) };
        content.Children.Add(new TextBlock { Text = label });
        content.Children.Add(box);
        content.Children.Add(buttons);

        var window = new Window
        {
            Title = title,
            Owner = owner,
            Content = content,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        ok.Click += (_, _) => window.DialogResult = true;
        window.Loaded += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };

        return window.ShowDialog() == true ? box.Text : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Inform(string title, string message) =>
        MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
