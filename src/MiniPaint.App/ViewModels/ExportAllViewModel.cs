using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Project;

namespace MiniPaint.App.ViewModels;

public partial class ExportAllViewModel : ViewModelBase
{
    public ExportFormatOption[] Formats { get; } =
    [
        new(ExportFormat.Png, "PNG (lossless, keeps transparency)"),
        new(ExportFormat.Jpeg, "JPG (compressed)")
    ];

    [ObservableProperty] public partial ExportFormatOption SelectedFormat { get; set; }
    [ObservableProperty] public partial string Folder { get; set; }
    [ObservableProperty] public partial bool OverwriteExisting { get; set; }
    [ObservableProperty] public partial string ErrorText { get; set; } = "";

    /// <summary>Quality applied to every image when exporting as JPG.</summary>
    public JpegOptionsViewModel Jpeg { get; }

    public int DocumentCount { get; }

    public string Title => "Export All Images";

    public string Subtitle => DocumentCount == 1
        ? "Export the open image (layers merged, image mode applied), named after its tab."
        : $"Export all {DocumentCount} open images (layers merged, image mode applied), one file per tab, named after the tab.";

    public ExportFormat Format => SelectedFormat.Format;

    public bool ShowJpegOptions => SelectedFormat.Format == ExportFormat.Jpeg;

    public ExportAllViewModel() : this(1, "", ExportFormat.Png, Core.Imaging.ImageCodec.DefaultJpegQuality)
    {
    }

    public ExportAllViewModel(int documentCount, string folder, ExportFormat format, int jpegQuality)
    {
        DocumentCount = documentCount;
        Folder = folder;
        SelectedFormat = Formats.First(f => f.Format == format);
        Jpeg = new JpegOptionsViewModel(jpegQuality);
    }

    partial void OnSelectedFormatChanged(ExportFormatOption value) =>
        OnPropertyChanged(nameof(ShowJpegOptions));

    partial void OnFolderChanged(string value) => ErrorText = "";

    public bool TryCommit()
    {
        var folder = Folder?.Trim() ?? "";
        if (folder.Length == 0)
        {
            ErrorText = "Choose an output folder.";
            return false;
        }

        if (!Path.IsPathFullyQualified(folder))
        {
            ErrorText = @"Use a full folder path (for example D:\Images).";
            return false;
        }

        Folder = folder;
        return true;
    }
}

public sealed record ExportFormatOption(ExportFormat Format, string Label)
{
    public override string ToString() => Label;
}
