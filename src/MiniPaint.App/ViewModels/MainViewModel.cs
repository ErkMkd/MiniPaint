using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniPaint.App.Controls;
using MiniPaint.Core.Document;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;

namespace MiniPaint.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly WorkspaceManager _manager = new();
    private Window? _window;

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new();
    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    [ObservableProperty] public partial DocumentTabViewModel? SelectedTab { get; set; }
    [ObservableProperty] public partial LayerItemViewModel? SelectedLayer { get; set; }
    [ObservableProperty] public partial CanvasTool CurrentTool { get; set; } = CanvasTool.RectSelect;
    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";
    [ObservableProperty] public partial string ZoomText { get; set; } = "100%";
    [ObservableProperty] public partial string CursorText { get; set; } = "";
    [ObservableProperty] public partial string SizeText { get; set; } = "";
    [ObservableProperty] public partial bool IsTransforming { get; set; }
    [ObservableProperty] public partial string TransformInfo { get; set; } = "";

    public DocumentWorkspace? ActiveWorkspace => SelectedTab?.Workspace;

    public MainViewModel()
    {
        _manager.Changed += RefreshFromManager;
        // Demo document on first run is created from the window Opened handler.
    }

    public void AttachWindow(Window window)
    {
        _window = window;
        WireClipboard();
    }

    private void WireClipboard()
    {
        _manager.Clipboard.WriteSystemPng = bytes =>
        {
            // Best-effort: Avalonia clipboard text fallback is not ideal for PNG;
            // UI layer can also expose bytes via platform. Stored internally regardless.
            _ = bytes;
        };
        _manager.Clipboard.ReadSystemPng = () => null;
    }

    partial void OnSelectedTabChanged(DocumentTabViewModel? value)
    {
        if (value is not null)
            _manager.Activate(value.Workspace);
        OnPropertyChanged(nameof(ActiveWorkspace));
        RefreshLayers();
        UpdateStatus();
    }

    partial void OnSelectedLayerChanged(LayerItemViewModel? value)
    {
        if (value is null || ActiveWorkspace is null) return;
        ActiveWorkspace.Document.ActiveLayerId = value.Id;
        foreach (var l in Layers)
            l.IsActive = l.Id == value.Id;
        ActiveWorkspace.InvalidateComposite();
        UpdateStatus();
    }

    private void RefreshFromManager()
    {
        // Sync tabs
        var existing = Tabs.Select(t => t.Workspace).ToHashSet();
        foreach (var ws in _manager.Workspaces)
        {
            if (!existing.Contains(ws))
                Tabs.Add(new DocumentTabViewModel(ws));
        }

        for (int i = Tabs.Count - 1; i >= 0; i--)
        {
            if (!_manager.Workspaces.Contains(Tabs[i].Workspace))
                Tabs.RemoveAt(i);
        }

        if (_manager.Active is { } active)
        {
            var tab = Tabs.FirstOrDefault(t => t.Workspace == active);
            if (!ReferenceEquals(SelectedTab, tab))
                SelectedTab = tab;
        }

        RefreshLayers();
        UpdateStatus();
    }

    private void RefreshLayers()
    {
        Layers.Clear();
        var ws = ActiveWorkspace;
        if (ws is null) return;

        // Show top → bottom in panel
        for (int i = ws.Document.Layers.Count - 1; i >= 0; i--)
        {
            var layer = ws.Document.Layers[i];
            Layers.Add(new LayerItemViewModel(layer, layer.Id == ws.Document.ActiveLayerId));
        }

        SelectedLayer = Layers.FirstOrDefault(l => l.IsActive) ?? Layers.FirstOrDefault();
    }

    public void UpdateStatus(int? cursorX = null, int? cursorY = null)
    {
        var ws = ActiveWorkspace;
        if (ws is null)
        {
            StatusText = "No document";
            ZoomText = "";
            CursorText = "";
            SizeText = "";
            IsTransforming = false;
            TransformInfo = "";
            return;
        }

        SizeText = $"{ws.Document.Width} × {ws.Document.Height}";
        ZoomText = $"{ws.Zoom * 100:0.#}%";
        if (cursorX is int cx && cursorY is int cy)
            CursorText = $"{cx}, {cy}";
        IsTransforming = ws.IsTransforming;
        if (ws.ActiveTransform is { } t)
        {
            TransformInfo =
                $"W:{t.ScaleX * 100:0.#}% H:{t.ScaleY * 100:0.#}%  ∠ {t.RotationDegrees:0.#}°  —  Enter apply · Esc cancel · Shift constrain · Alt from center";
            StatusText = "Free Transform";
        }
        else
        {
            TransformInfo = "";
            StatusText = ws.Document.IsDirty ? "Modified" : "Saved";
        }
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (_window is null) return;
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image or Project",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg"] },
                new FilePickerFileType("MiniPaint Project") { Patterns = ["*.minipaint"] },
                new FilePickerFileType("All") { Patterns = ["*.*"] }
            ]
        });

        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path is null) continue;
            if (path.EndsWith(ProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase))
                _manager.OpenProject(path);
            else
                _manager.OpenImage(path);
        }
    }

    [RelayCommand]
    private void NewDocument()
    {
        _manager.NewDocument($"Untitled {_manager.Workspaces.Count + 1}", 800, 600);
    }

    [RelayCommand]
    private async Task SaveProjectAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        var path = ws.Document.ProjectPath;
        if (string.IsNullOrEmpty(path))
        {
            var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save MiniPaint Project",
                DefaultExtension = "minipaint",
                SuggestedFileName = ws.Document.Name + ".minipaint",
                FileTypeChoices =
                [
                    new FilePickerFileType("MiniPaint Project") { Patterns = ["*.minipaint"] }
                ]
            });
            path = file?.TryGetLocalPath();
            if (path is null) return;
        }

        ws.SaveProject(path);
        UpdateStatus();
    }

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export PNG",
            DefaultExtension = "png",
            SuggestedFileName = ws.Document.Name + ".png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG") { Patterns = ["*.png"] }
            ]
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return;
        ws.ExportPng(path);
    }

    [RelayCommand]
    private void CloseDocument()
    {
        if (SelectedTab is null) return;
        _manager.Close(SelectedTab.Workspace);
    }

    [RelayCommand]
    private void Copy()
    {
        ActiveWorkspace?.Copy(_manager.Clipboard);
    }

    [RelayCommand]
    private void Cut()
    {
        ActiveWorkspace?.Cut(_manager.Clipboard);
        RefreshLayers();
    }

    [RelayCommand]
    private void Paste()
    {
        ActiveWorkspace?.Paste(_manager.Clipboard);
        RefreshLayers();
    }

    [RelayCommand]
    private void SelectAll() => ActiveWorkspace?.SelectAll();

    [RelayCommand]
    private void Deselect() => ActiveWorkspace?.Deselect();

    [RelayCommand]
    private void Undo()
    {
        ActiveWorkspace?.History.Undo();
        RefreshLayers();
    }

    [RelayCommand]
    private void Redo()
    {
        ActiveWorkspace?.History.Redo();
        RefreshLayers();
    }

    [RelayCommand]
    private void AddLayer()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        ws.History.Execute(new AddEmptyLayerCommand(ws.Document, $"Layer {ws.Document.Layers.Count + 1}", ws.NotifyChanged));
        RefreshLayers();
    }

    [RelayCommand]
    private void DeleteLayer()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveLayerId is not Guid id) return;
        if (ws.Document.Layers.Count <= 1) return;
        ws.History.Execute(new DeleteLayerCommand(ws.Document, id, ws.NotifyChanged));
        RefreshLayers();
    }

    [RelayCommand]
    private void DuplicateLayer()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveLayerId is not Guid id) return;
        ws.Document.DuplicateLayer(id);
        ws.NotifyChanged();
        RefreshLayers();
    }

    [RelayCommand]
    private void MoveLayerUp()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveLayer is not { } layer) return;
        int idx = IndexOf(ws.Document, layer.Id);
        if (idx < 0 || idx >= ws.Document.Layers.Count - 1) return;
        ws.History.Execute(new ReorderLayerCommand(ws.Document, layer.Id, idx, idx + 1, ws.NotifyChanged));
        RefreshLayers();
    }

    [RelayCommand]
    private void MoveLayerDown()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveLayer is not { } layer) return;
        int idx = IndexOf(ws.Document, layer.Id);
        if (idx <= 0) return;
        ws.History.Execute(new ReorderLayerCommand(ws.Document, layer.Id, idx, idx - 1, ws.NotifyChanged));
        RefreshLayers();
    }

    [RelayCommand]
    private void ToggleLayerVisibility(LayerItemViewModel? item)
    {
        var ws = ActiveWorkspace;
        if (ws is null || item is null) return;
        // Set visibility to the checkbox-bound VM value (already flipped by binding).
        bool target = item.IsVisible;
        // Revert VM briefly so command captures correct "before"
        item.Layer.IsVisible = !target;
        ws.History.Execute(LayerPropertyCommand.Visibility(item.Layer, target, ws.NotifyChanged));
        item.SyncFromModel();
    }

    [RelayCommand]
    private void RenameSelectedLayer(string? name)
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveLayer is not { } layer || string.IsNullOrWhiteSpace(name)) return;
        ws.History.Execute(LayerPropertyCommand.Rename(layer, name.Trim(), ws.NotifyChanged));
        RefreshLayers();
    }

    [RelayCommand]
    private void SetToolSelect()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private void SetToolMove()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        CurrentTool = CanvasTool.MoveLayer;
        SyncTransformUi();
    }

    [RelayCommand]
    private void SetToolPan()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        CurrentTool = CanvasTool.Pan;
        SyncTransformUi();
    }

    [RelayCommand]
    private void ToggleFreeTransform()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        if (ws.IsTransforming)
        {
            ws.CancelTransform();
            CurrentTool = CanvasTool.RectSelect;
        }
        else
        {
            if (ws.BeginTransform())
                CurrentTool = CanvasTool.FreeTransform;
        }
        SyncTransformUi();
    }

    [RelayCommand]
    private void CommitTransform()
    {
        ActiveWorkspace?.CommitTransform();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private void CancelTransform()
    {
        ActiveWorkspace?.CancelTransform();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    public void SyncTransformUi()
    {
        IsTransforming = ActiveWorkspace?.IsTransforming == true;
        UpdateStatus();
        OnPropertyChanged(nameof(ActiveWorkspace));
    }

    [RelayCommand]
    private void FitToWindow()
    {
        // Viewport size applied from view code-behind via helper
        FitRequested?.Invoke();
    }

    [RelayCommand]
    private void Zoom100() => Zoom100Requested?.Invoke();

    public event Action? FitRequested;
    public event Action? Zoom100Requested;

    public void ApplyLayerOpacity(LayerItemViewModel item, float opacity)
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        ws.History.Execute(LayerPropertyCommand.Opacity(item.Layer, opacity, ws.NotifyChanged));
        item.SyncFromModel();
    }

    private static int IndexOf(PaintDocument doc, Guid id)
    {
        for (int i = 0; i < doc.Layers.Count; i++)
            if (doc.Layers[i].Id == id) return i;
        return -1;
    }

    public void Dispose()
    {
        _manager.Dispose();
        GC.SuppressFinalize(this);
    }
}
