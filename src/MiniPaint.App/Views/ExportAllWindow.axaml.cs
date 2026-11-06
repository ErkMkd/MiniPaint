using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MiniPaint.App.Logging;
using MiniPaint.App.ViewModels;

namespace MiniPaint.App.Views;

public partial class ExportAllWindow : Window
{
    public ExportAllWindow() : this(new ExportAllViewModel())
    {
    }

    public ExportAllWindow(ExportAllViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private ExportAllViewModel? Vm => DataContext as ExportAllViewModel;

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null || !Vm.TryCommit())
            return;
        Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        try
        {
            IStorageFolder? start = Directory.Exists(Vm.Folder)
                ? await StorageProvider.TryGetFolderFromPathAsync(Vm.Folder)
                : null;
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Export All Images — Output Folder",
                AllowMultiple = false,
                SuggestedStartLocation = start
            });

            var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrEmpty(path))
                Vm.Folder = path;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Export All folder picker: {ex.Message}");
        }
    }
}
