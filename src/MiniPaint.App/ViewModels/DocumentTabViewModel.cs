using CommunityToolkit.Mvvm.ComponentModel;
using MiniPaint.Core.Document;

namespace MiniPaint.App.ViewModels;

public partial class DocumentTabViewModel : ViewModelBase
{
    public DocumentWorkspace Workspace { get; }

    [ObservableProperty] public partial string Title { get; set; }

    public DocumentTabViewModel(DocumentWorkspace workspace)
    {
        Workspace = workspace;
        Title = workspace.TabTitle;
        workspace.Changed += () => Title = workspace.TabTitle;
    }
}
