using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniPaint.App.ViewModels;

namespace MiniPaint.App.Views;

public partial class PaletteEditorWindow : Window
{
    public PaletteEditorWindow() 
    {
        InitializeComponent();
    }

    public PaletteEditorWindow(PaletteEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is PaletteEditorViewModel vm &&
            sender is Button { Tag: PaletteSwatchItem item })
        {
            vm.SelectedSwatch = item;
        }
    }
}
