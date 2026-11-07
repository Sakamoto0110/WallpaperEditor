using System.Windows.Controls;
using WallpaperEditor.App.ViewModels;

namespace WallpaperEditor.App.Views;

public partial class EditorView : UserControl
{
    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Surface.Attach(e.NewValue as EditorViewModel);
    }
}
