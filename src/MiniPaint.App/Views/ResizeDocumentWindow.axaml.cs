using Avalonia.Controls;
using Avalonia.Interactivity;
using MiniPaint.App.ViewModels;
using MiniPaint.Core.Document;

namespace MiniPaint.App.Views;

public partial class ResizeDocumentWindow : Window
{
    public ResizeDocumentWindow()
    {
        InitializeComponent();
    }

    public ResizeDocumentWindow(ResizeDocumentViewModel vm) : this()
    {
        DataContext = vm;
    }

    private ResizeDocumentViewModel? Vm => DataContext as ResizeDocumentViewModel;

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || !Vm.TryCommit(out _))
            return;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnAnchorClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button { Tag: string tag }) return;
        if (Enum.TryParse<CanvasAnchor>(tag, out var anchor))
            Vm.Anchor = anchor;
    }
}
