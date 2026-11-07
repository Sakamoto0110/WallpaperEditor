using System.Windows;
using System.Windows.Controls;
using WallpaperEditor.App.ViewModels;

namespace WallpaperEditor.App.Views;

public partial class MonitorsView : UserControl
{
    public MonitorsView()
    {
        InitializeComponent();
    }

    private void View_Loaded(object sender, RoutedEventArgs e) => (DataContext as MainViewModel)?.Refresh();
}
