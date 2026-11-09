using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiniPaint.App.Controls;
using MiniPaint.App.Logging;
using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Filters;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;
using MiniPaint.App.Services;
using MiniPaint.Core.Selection;
using MiniPaint.Inpainting;
using MiniPaint.Segmentation;

namespace MiniPaint.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly WorkspaceManager _manager = new();
    private Window? _window;

    // Remembered for the session so repeated exports / batch resizes start from the last choice.
    private int _jpegQuality = ImageCodec.DefaultJpegQuality;
    private string? _lastExportFolder;
    private ExportFormat _lastExportFormat = ExportFormat.Png;
    private BatchResizeOptions? _lastResizeAllOptions;
    private int _colorRangeFuzziness = 40;

    private readonly ObjectSelectionService _objectSelection = new();
    private bool _objectSelectionBusy;

    private readonly ContentAwareFillService _contentAwareFill = new();
    /// <summary>Shown in the status bar instead of Modified / Saved while a long task runs.</summary>
    private string? _busyStatus;

    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new();
    public ObservableCollection<LayerItemViewModel> Layers { get; } = new();

    /// <summary>Filters of the active layer or group, in the order they apply.</summary>
    public ObservableCollection<FilterItemViewModel> NodeFilters { get; } = new();

    /// <summary>Header of the filters list (names the active layer or group).</summary>
    [ObservableProperty] public partial string FiltersHeader { get; set; } = "Filters";

    [ObservableProperty] public partial bool HasNodeFilters { get; set; }

    private LayerNode? _filtersNode;
    private IReadOnlyList<LayerFilter>? _filtersShown;

    [ObservableProperty] public partial DocumentTabViewModel? SelectedTab { get; set; }
    [ObservableProperty] public partial LayerItemViewModel? SelectedLayer { get; set; }
    [ObservableProperty] public partial CanvasTool CurrentTool { get; set; } = CanvasTool.RectSelect;
    [ObservableProperty] public partial string StatusText { get; set; } = "Ready";
    [ObservableProperty] public partial string ZoomText { get; set; } = "100%";
    [ObservableProperty] public partial string CursorText { get; set; } = "";
    [ObservableProperty] public partial string SizeText { get; set; } = "";
    [ObservableProperty] public partial bool IsTransforming { get; set; }
    [ObservableProperty] public partial bool IsEditingSelection { get; set; }
    [ObservableProperty] public partial string TransformInfo { get; set; } = "";
    [ObservableProperty] public partial string SelectionEditInfo { get; set; } = "";
    [ObservableProperty] public partial float TransformOpacity { get; set; } = 1f;
    [ObservableProperty] public partial BlendModeOption? SelectedBlendOption { get; set; }

    public BlendModeOption[] BlendModes { get; } = BlendModeNames.All;

    // --- Selection tools options bar (Rectangular Selection + Magic Wand) ---

    /// <summary>Mode used by a plain click; Shift / Alt / Shift+Alt override it for one gesture.</summary>
    [ObservableProperty] public partial SelectionCombineMode SelectionMode { get; set; }
    [ObservableProperty] public partial decimal WandToleranceValue { get; set; } = 32;
    [ObservableProperty] public partial bool WandContiguous { get; set; } = true;
    [ObservableProperty] public partial bool WandAntiAlias { get; set; }
    [ObservableProperty] public partial WandSampleOption SelectedWandSample { get; set; }

    public WandSampleOption[] WandSamples { get; } =
    [
        new(true, "Visible image"),
        new(false, "Current layer")
    ];

    public MagicWandOptions WandOptions => new(
        (int)Math.Clamp(Math.Round(WandToleranceValue), 0, 255),
        WandContiguous,
        WandAntiAlias,
        SelectedWandSample.VisibleImage);

    public bool IsSelectionModeReplace
    {
        get => SelectionMode == SelectionCombineMode.Replace;
        set { if (value) SelectionMode = SelectionCombineMode.Replace; }
    }

    public bool IsSelectionModeAdd
    {
        get => SelectionMode == SelectionCombineMode.Add;
        set { if (value) SelectionMode = SelectionCombineMode.Add; }
    }

    public bool IsSelectionModeSubtract
    {
        get => SelectionMode == SelectionCombineMode.Subtract;
        set { if (value) SelectionMode = SelectionCombineMode.Subtract; }
    }

    public bool IsSelectionModeIntersect
    {
        get => SelectionMode == SelectionCombineMode.Intersect;
        set { if (value) SelectionMode = SelectionCombineMode.Intersect; }
    }

    public bool IsMagicWandTool => CurrentTool == CanvasTool.MagicWand;

    public bool IsPolygonLassoTool => CurrentTool == CanvasTool.PolygonLasso;

    /// <summary>Polygonal Lasso: anti-aliased edges (photos); off gives hard pixel edges.</summary>
    [ObservableProperty] public partial bool LassoAntiAlias { get; set; } = true;

    public bool ShowSelectionOptions =>
        (CurrentTool is CanvasTool.RectSelect or CanvasTool.PolygonLasso or CanvasTool.MagicWand or CanvasTool.ObjectSelect) &&
        !IsTransforming && !IsEditingSelection;

    // --- Object Selection (MobileSAM, local) ---

    /// <summary>Anti-aliased object edges (photos); off gives hard pixel edges.</summary>
    [ObservableProperty] public partial bool ObjectSoftEdges { get; set; } = true;

    public bool IsObjectSelectTool => CurrentTool == CanvasTool.ObjectSelect;

    public bool IsObjectSelectionAvailable => _objectSelection.IsAvailable;

    public string ObjectSelectionTip => _objectSelection.IsAvailable
        ? "Object Selection (Q) — click an object or drag a box around it · Shift add · Alt subtract · Shift+Alt intersect"
        : "Object Selection — AI model not installed: run scripts\\Get-Models.ps1, then rebuild";

    // --- Content-Aware Fill (LaMa, local) ---

    public bool IsContentAwareFillAvailable => _contentAwareFill.IsAvailable;

    public string ContentAwareFillTip => _contentAwareFill.IsAvailable
        ? "Rebuild the selected area from its surroundings on a new layer above the active one (AI runs locally)"
        : "Content-Aware Fill — AI model not installed: run scripts\\Get-Models.ps1, then rebuild";

    // --- Mask Brush (layer masks) ---

    /// <summary>Brush diameter in document pixels.</summary>
    [ObservableProperty] public partial decimal MaskBrushSizeValue { get; set; } = 60;

    /// <summary>0 = soft edge across the whole radius, 100 = hard edge.</summary>
    [ObservableProperty] public partial double MaskBrushHardness { get; set; }

    /// <summary>Most a stroke changes the mask, in percent.</summary>
    [ObservableProperty] public partial double MaskBrushOpacity { get; set; } = 100;

    /// <summary>False paints black (hides the layer), true paints white (reveals it).</summary>
    [ObservableProperty] public partial bool MaskBrushReveal { get; set; }

    public bool IsMaskBrushHide
    {
        get => !MaskBrushReveal;
        set { if (value) MaskBrushReveal = false; }
    }

    public bool IsMaskBrushReveal
    {
        get => MaskBrushReveal;
        set { if (value) MaskBrushReveal = true; }
    }

    public MaskBrushOptions MaskBrushOptions =>
        new((double)MaskBrushSizeValue, MaskBrushHardness / 100, MaskBrushOpacity / 100, MaskBrushReveal);

    public bool IsMaskBrushTool => CurrentTool == CanvasTool.MaskBrush;

    partial void OnMaskBrushSizeValueChanged(decimal value) => OnPropertyChanged(nameof(MaskBrushOptions));
    partial void OnMaskBrushHardnessChanged(double value) => OnPropertyChanged(nameof(MaskBrushOptions));
    partial void OnMaskBrushOpacityChanged(double value) => OnPropertyChanged(nameof(MaskBrushOptions));

    partial void OnMaskBrushRevealChanged(bool value)
    {
        OnPropertyChanged(nameof(MaskBrushOptions));
        OnPropertyChanged(nameof(IsMaskBrushHide));
        OnPropertyChanged(nameof(IsMaskBrushReveal));
    }

    partial void OnSelectionModeChanged(SelectionCombineMode value)
    {
        OnPropertyChanged(nameof(IsSelectionModeReplace));
        OnPropertyChanged(nameof(IsSelectionModeAdd));
        OnPropertyChanged(nameof(IsSelectionModeSubtract));
        OnPropertyChanged(nameof(IsSelectionModeIntersect));
    }

    partial void OnWandToleranceValueChanged(decimal value) => OnPropertyChanged(nameof(WandOptions));
    partial void OnWandContiguousChanged(bool value) => OnPropertyChanged(nameof(WandOptions));
    partial void OnWandAntiAliasChanged(bool value) => OnPropertyChanged(nameof(WandOptions));
    partial void OnSelectedWandSampleChanged(WandSampleOption value) => OnPropertyChanged(nameof(WandOptions));

    partial void OnCurrentToolChanged(CanvasTool value)
    {
        OnPropertyChanged(nameof(IsMagicWandTool));
        OnPropertyChanged(nameof(IsPolygonLassoTool));
        OnPropertyChanged(nameof(IsObjectSelectTool));
        OnPropertyChanged(nameof(IsMaskBrushTool));
        OnPropertyChanged(nameof(ShowSelectionOptions));
    }

    partial void OnIsTransformingChanged(bool value) => OnPropertyChanged(nameof(ShowSelectionOptions));
    partial void OnIsEditingSelectionChanged(bool value) => OnPropertyChanged(nameof(ShowSelectionOptions));

    public DocumentWorkspace? ActiveWorkspace => SelectedTab?.Workspace;

    public MainViewModel()
    {
        SelectedWandSample = WandSamples[0];
        _manager.Changed += RefreshFromManager;
        // Demo document on first run is created from the window Opened handler.
    }

    public void AttachWindow(Window window)
    {
        _window = window;
        WireClipboard();
        AppLog.Info("MainViewModel attached to MainWindow");
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        AppLog.Info("OpenLogFolder requested");
        AppLog.OpenLogFolder();
    }

    [RelayCommand]
    private async Task AboutAsync()
    {
        if (_window is null) return;
        AppLog.Info("About dialog opened");
        var about = new Views.AboutWindow();
        await about.ShowDialog(_window);
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
        if (value is not null && CurrentTool == CanvasTool.ObjectSelect)
            _objectSelection.Prewarm(value.Workspace);
        OnPropertyChanged(nameof(ActiveWorkspace));
        RefreshLayers();
        UpdateStatus();
    }

    partial void OnSelectedLayerChanged(LayerItemViewModel? value)
    {
        if (value is null || ActiveWorkspace is null)
        {
            SelectedBlendOption = null;
            return;
        }
        ActiveWorkspace.Document.ActiveLayerId = value.Id;
        foreach (var l in Layers)
            l.IsActive = l.Id == value.Id;
        RefreshNodeFilters();
        SelectedBlendOption = BlendModeNames.GetOption(value.BlendMode);
        TransformOpacity = value.Opacity;
        // No InvalidateComposite: the active layer does not change the merged image, and
        // recompositing here re-ran the whole pipeline (dither included) on every UI refresh.
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
        var ws = ActiveWorkspace;
        RefreshNodeFilters();
        if (ws is not null && SameLayerRows(ws.Document))
        {
            // Runs on every workspace change (selection, zoom…): only refresh the existing rows.
            foreach (var item in Layers)
            {
                item.SyncFromModel();
                item.IsActive = item.Id == ws.Document.ActiveLayerId;
            }
            var activeRow = Layers.FirstOrDefault(l => l.IsActive) ?? Layers.FirstOrDefault();
            if (!ReferenceEquals(SelectedLayer, activeRow))
                SelectedLayer = activeRow;
            SelectedBlendOption = SelectedLayer is null
                ? null
                : BlendModeNames.GetOption(SelectedLayer.BlendMode);
            TransformOpacity = SelectedLayer?.Opacity ?? 1f;
            return;
        }

        Layers.Clear();
        if (ws is null) return;

        foreach (var (node, depth) in PanelRows(ws.Document.Root, 0))
            Layers.Add(new LayerItemViewModel(node, depth, node.Id == ws.Document.ActiveLayerId));

        SelectedLayer = Layers.FirstOrDefault(l => l.IsActive) ?? Layers.FirstOrDefault();
        SelectedBlendOption = SelectedLayer is null
            ? null
            : BlendModeNames.GetOption(SelectedLayer.BlendMode);
        TransformOpacity = SelectedLayer?.Opacity ?? 1f;
    }

    /// <summary>The panel rows: the tree top → bottom, the content of open groups under them, indented.</summary>
    private static IEnumerable<(LayerNode Node, int Depth)> PanelRows(LayerGroup group, int depth)
    {
        for (int i = group.Children.Count - 1; i >= 0; i--)
        {
            var node = group.Children[i];
            yield return (node, depth);
            if (node is LayerGroup { IsExpanded: true } open)
            {
                foreach (var row in PanelRows(open, depth + 1))
                    yield return row;
            }
        }
    }

    /// <summary>True when the panel rows are exactly the document's current rows.</summary>
    private bool SameLayerRows(PaintDocument document)
    {
        int i = 0;
        foreach (var (node, depth) in PanelRows(document.Root, 0))
        {
            if (i >= Layers.Count || !ReferenceEquals(Layers[i].Node, node) || Layers[i].Depth != depth)
                return false;
            i++;
        }
        return i == Layers.Count && i > 0;
    }

    /// <summary>Rebuild the filters list when the active entry or its filters changed.</summary>
    private void RefreshNodeFilters()
    {
        var node = ActiveWorkspace?.Document.ActiveNode;
        var filters = node?.Filters;
        if (ReferenceEquals(node, _filtersNode) && ReferenceEquals(filters, _filtersShown)) return;
        _filtersNode = node;
        _filtersShown = filters;
        NodeFilters.Clear();
        if (filters is not null)
        {
            for (int i = 0; i < filters.Count; i++)
                NodeFilters.Add(new FilterItemViewModel(filters[i], i));
        }
        HasNodeFilters = NodeFilters.Count > 0;
        FiltersHeader = node is null ? "Filters" : $"Filters — {node.Name}";
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
            IsEditingSelection = false;
            TransformInfo = "";
            SelectionEditInfo = "";
            return;
        }

        SizeText = $"{ws.Document.Width} × {ws.Document.Height} · {FormatColorMode(ws.Document.ColorSettings)}";
        ZoomText = $"{ws.Zoom * 100:0.#}%";
        if (cursorX is int cx && cursorY is int cy)
            CursorText = $"{cx}, {cy}";
        IsTransforming = ws.IsTransforming;
        IsEditingSelection = ws.IsEditingSelection;
        if (ws.ActiveTransform is { } t)
        {
            TransformOpacity = ws.IsSelectionTransform
                ? 1f
                : ws.Document.ActiveLayer?.Opacity ?? SelectedLayer?.Opacity ?? 1f;
            TransformInfo =
                $"W:{t.ScaleX * 100:0.#}% H:{t.ScaleY * 100:0.#}%  ∠ {t.RotationDegrees:0.#}°  —  Enter apply · Esc cancel · Shift ratio · Alt = center (sinon ancre opposée)";
            SelectionEditInfo = "";
            StatusText = ws.IsSelectionTransform ? "Free Transform Selection" : "Free Transform";
        }
        else if (ws.IsEditingSelection && ws.Selection.HasSelection)
        {
            var s = ws.Selection;
            SelectionEditInfo =
                $"Selection {s.Width}×{s.Height} @ {s.X},{s.Y}  —  Enter apply · Esc cancel · Shift ratio · Alt = center (sinon ancre opposée)";
            TransformInfo = "";
            StatusText = "Transform Selection";
        }
        else
        {
            TransformInfo = "";
            SelectionEditInfo = "";
            StatusText = _busyStatus ??
                (CurrentTool == CanvasTool.MaskBrush && ws.Document.ActiveNode is LayerGroup
                    ? "Mask Brush paints a layer's mask — select a layer, not a group"
                    : ws.Document.IsDirty ? "Modified" : "Saved");
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
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.iff", "*.ilbm", "*.lbm"] },
                new FilePickerFileType("IFF ILBM") { Patterns = ["*.iff", "*.ilbm", "*.lbm"] },
                new FilePickerFileType("MiniPaint Project") { Patterns = ["*.minipaint"] },
                new FilePickerFileType("All") { Patterns = ["*.*"] }
            ]
        });

        OpenPaths(files.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    /// <summary>
    /// Open local image / IFF / .minipaint paths (File → Open and drag-and-drop).
    /// </summary>
    public void OpenPaths(IEnumerable<string?> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        bool opened = false;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsSupportedOpenPath(path))
                continue;

            try
            {
                AppLog.Info($"Opening: {path}");
                if (path.EndsWith(ProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase))
                    _manager.OpenProject(path);
                else
                    _manager.OpenImage(path);
                opened = true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to open {path}", ex);
                StatusText = $"Failed to open {Path.GetFileName(path)} — see log";
            }
        }

        if (opened)
            FitRequested?.Invoke();
    }

    public static bool IsSupportedOpenPath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".iff", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".ilbm", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".lbm", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(ProjectSerializer.Extension, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void NewDocument()
    {
        _manager.NewDocument($"Untitled {_manager.Workspaces.Count + 1}", 800, 600);
    }

    /// <summary>
    /// Right-click layer → new document cropped to that layer's opaque content.
    /// </summary>
    [RelayCommand]
    private void CreateImageFromLayer(LayerItemViewModel? item)
    {
        item ??= SelectedLayer;
        if (item is null) return;

        if (item.Node is not Layer layer)
        {
            StatusText = "New Image from Layer Content works on a layer, not a group.";
            return;
        }
        string name = item.Name;
        var ws = _manager.NewDocumentFromLayerContent(layer, name);
        if (ws is null)
        {
            StatusText = "Layer has no visible content.";
            AppLog.Info($"CreateImageFromLayer: empty layer '{name}'");
            return;
        }

        AppLog.Info($"CreateImageFromLayer: '{name}' → {ws.Document.Width}x{ws.Document.Height}");
        FitRequested?.Invoke();
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

    /// <summary>
    /// Save picker → local path, or null if cancelled. Releases the picker handle first — on Windows
    /// it often keeps an exclusive lock on the chosen path (especially when overwriting the file
    /// still "open" in the app).
    /// </summary>
    private async Task<string?> PickSavePathAsync(string title, string suggestedFileName, FilePickerFileType fileType)
    {
        if (_window is null) return null;

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = Path.GetExtension(suggestedFileName).TrimStart('.'),
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [fileType]
        });
        if (file is null) return null;

        string? path;
        try
        {
            path = file.TryGetLocalPath();
        }
        finally
        {
            try
            {
                if (file is IAsyncDisposable asyncDisp)
                    await asyncDisp.DisposeAsync();
                else if (file is IDisposable disp)
                    disp.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"StorageFile dispose: {ex.Message}");
            }
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            AppLog.Error($"{title}: no local path from save picker");
            StatusText = "Export failed (no path)";
            return null;
        }
        return path;
    }

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        try
        {
            var path = await PickSavePathAsync("Export PNG", ws.Document.Name + ".png",
                new FilePickerFileType("PNG") { Patterns = ["*.png"] });
            if (path is null) return;

            AppLog.Info($"Export PNG begin path={path} doc={ws.Document.Width}x{ws.Document.Height}");
            StatusText = "Exporting PNG…";

            // Encode off the UI thread so the window stays responsive.
            await Task.Run(() => ws.ExportPng(path));

            AppLog.Info($"Export PNG done: {path}");
            StatusText = "Exported PNG";
            UpdateStatus();
        }
        catch (Exception ex)
        {
            AppLog.Error("Export PNG failed", ex);
            StatusText = "Export failed — see log";
        }
    }

    [RelayCommand]
    private async Task ExportJpgAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        try
        {
            var path = await PickSavePathAsync("Export JPG", ws.Document.Name + ".jpg",
                new FilePickerFileType("JPEG") { Patterns = ["*.jpg", "*.jpeg"] });
            if (path is null) return;

            var options = new JpegOptionsViewModel(_jpegQuality);
            var ok = await new Views.JpegOptionsWindow(options).ShowDialog<bool?>(_window);
            if (ok != true) return;
            int quality = _jpegQuality = options.Quality;

            AppLog.Info($"Export JPG begin path={path} quality={quality} doc={ws.Document.Width}x{ws.Document.Height}");
            StatusText = "Exporting JPG…";

            await Task.Run(() => ws.ExportJpeg(path, quality));

            AppLog.Info($"Export JPG done: {path}");
            StatusText = $"Exported JPG (quality {quality})";
        }
        catch (Exception ex)
        {
            AppLog.Error("Export JPG failed", ex);
            StatusText = "Export failed — see log";
        }
    }

    /// <summary>
    /// One merged PNG or JPG per open tab into a single folder; JPG quality applies to every image.
    /// </summary>
    [RelayCommand]
    private async Task ExportAllAsync()
    {
        if (_window is null || _manager.Workspaces.Count == 0) return;

        var workspaces = _manager.Workspaces.ToList();
        var vm = new ExportAllViewModel(
            workspaces.Count, _lastExportFolder ?? DefaultExportFolder(), _lastExportFormat, _jpegQuality);
        var ok = await new Views.ExportAllWindow(vm).ShowDialog<bool?>(_window);
        if (ok != true) return;

        _lastExportFolder = vm.Folder;
        _lastExportFormat = vm.Format;
        _jpegQuality = vm.Jpeg.Quality;

        var format = vm.Format;
        int quality = vm.Jpeg.Quality;
        IReadOnlyList<string> paths;
        try
        {
            paths = BatchExport.PlanPaths(
                workspaces.Select(w => w.Document.Name),
                vm.Folder,
                format,
                vm.OverwriteExisting ? null : File.Exists);
        }
        catch (Exception ex)
        {
            AppLog.Error("Export All: invalid output folder", ex);
            StatusText = "Export All failed — see log";
            return;
        }

        AppLog.Info($"Export All begin count={workspaces.Count} format={format} quality={quality} " +
                    $"folder={vm.Folder} overwrite={vm.OverwriteExisting}");

        int failed = 0;
        for (int i = 0; i < workspaces.Count; i++)
        {
            var ws = workspaces[i];
            var path = paths[i];
            StatusText = $"Exporting {i + 1}/{workspaces.Count}: {Path.GetFileName(path)}…";
            try
            {
                await Task.Run(() => BatchExport.Export(ws.Document, path, format, quality));
                AppLog.Info($"Export All: {path}");
            }
            catch (Exception ex)
            {
                failed++;
                AppLog.Error($"Export All failed for '{ws.Document.Name}' → {path}", ex);
            }
        }

        int done = workspaces.Count - failed;
        StatusText = failed == 0
            ? $"Exported {done} image(s) to {vm.Folder}"
            : $"Exported {done} of {workspaces.Count} image(s) — {failed} failed, see log";
        AppLog.Info($"Export All done ok={done} failed={failed}");
    }

    private string DefaultExportFolder()
    {
        var doc = ActiveWorkspace?.Document;
        var source = doc?.FilePath ?? doc?.ProjectPath;
        var dir = source is null ? null : Path.GetDirectoryName(source);
        return !string.IsNullOrEmpty(dir)
            ? dir
            : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }

    [RelayCommand]
    private async Task ImageModeAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        if (ws.IsTransforming) ws.CancelTransform();
        if (ws.IsEditingSelection) ws.CancelSelectionEdit();

        var before = ws.Document.ColorSettings.Clone();
        var vm = new ColorModeViewModel(before, preview =>
        {
            ws.Document.ColorSettings.CopyFrom(preview);
            ws.NotifyChanged();
            UpdateStatus();
        });
        var dlg = new Views.ColorModeWindow(vm);

        bool? ok = false;
        try
        {
            ok = await dlg.ShowDialog<bool?>(_window);
        }
        finally
        {
            // Always leave the document on the pre-dialog settings before commit/cancel.
            ws.Document.ColorSettings.CopyFrom(before);
            ws.NotifyChanged();
            UpdateStatus();
        }

        if (ok != true) return;

        var after = vm.ToSettings(before);
        if (before.Equals(after)) return;

        try
        {
            StatusText = "Applying image mode…";
            var cmd = new ColorSettingsCommand(ws.Document, before, after, ws.NotifyChanged);
            ws.History.Execute(cmd);
            UpdateStatus();
            AppLog.Info($"Image mode → {after.Mode} colors={after.ColorCount} q={after.Quantizer} dither={after.Dither}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Image mode failed", ex);
            StatusText = "Image mode failed — see log";
        }
    }

    [RelayCommand]
    private async Task EditPaletteAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        ws.Document.ColorSettings.Sanitize();
        if (ws.Document.ColorSettings.Mode == ColorModeKind.Rgba)
        {
            StatusText = "Palette editor requires Indexed / ECS / AGA mode.";
            return;
        }

        if (ws.IsTransforming) ws.CancelTransform();
        if (ws.IsEditingSelection) ws.CancelSelectionEdit();

        // Ensure a palette exists before editing (quantizes once if needed).
        using var composite = CompositionEngine.Composite(ws.Document);
        ColorModePipeline.EnsureUniquePalette(composite, ws.Document.ColorSettings);
        var before = ws.Document.ColorSettings.Clone();

        var vm = new PaletteEditorViewModel(ws.Document.ColorSettings, composite);
        var dlg = new Views.PaletteEditorWindow(vm);
        var ok = await dlg.ShowDialog<bool?>(_window);
        if (ok != true) return;

        var after = vm.ResultSettings;
        if (before.Equals(after)) return;

        try
        {
            ws.History.Execute(new ColorSettingsCommand(ws.Document, before, after, ws.NotifyChanged));
            UpdateStatus();
            AppLog.Info($"Palette edited ({after.EffectivePaletteSlots} colors, customized={after.PaletteCustomized})");
        }
        catch (Exception ex)
        {
            AppLog.Error("Palette edit failed", ex);
            StatusText = "Palette edit failed — see log";
        }
    }

    [RelayCommand]
    private async Task ExportIlbmAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        if (!IlbmCodec.CanExport(ws.Document))
        {
            StatusText = "IFF-ILBM export needs Indexed / ECS / AGA mode (Image → Mode…).";
            return;
        }

        try
        {
            var path = await PickSavePathAsync("Export IFF-ILBM", ws.Document.Name + ".iff",
                new FilePickerFileType("IFF ILBM") { Patterns = ["*.iff", "*.ilbm"] });
            if (path is null) return;

            StatusText = "Exporting IFF-ILBM…";
            await Task.Run(() => IlbmCodec.Save(ws.Document, path));
            StatusText = "Exported IFF-ILBM";
            AppLog.Info($"Export ILBM done: {path}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Export ILBM failed", ex);
            StatusText = "IFF export failed — see log";
        }
    }

    [RelayCommand]
    private async Task ImageSizeAsync() => await ShowResizeDialogAsync(ResizeDialogMode.ImageSize);

    [RelayCommand]
    private async Task CanvasSizeAsync() => await ShowResizeDialogAsync(ResizeDialogMode.CanvasSize);

    private async Task ShowResizeDialogAsync(ResizeDialogMode mode)
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;

        if (ws.IsTransforming) ws.CancelTransform();
        if (ws.IsEditingSelection) ws.CancelSelectionEdit();

        var vm = new ResizeDocumentViewModel(mode, ws.Document.Width, ws.Document.Height);
        var dlg = new Views.ResizeDocumentWindow(vm);
        var ok = await dlg.ShowDialog<bool?>(_window);
        if (ok != true) return;

        try
        {
            int oldWidth = ws.Document.Width;
            int oldHeight = ws.Document.Height;
            AppLog.Info($"{mode}: {ws.Document.Width}x{ws.Document.Height} → {vm.ResultWidth}x{vm.ResultHeight}" +
                        (mode == ResizeDialogMode.CanvasSize ? $" anchor={vm.Anchor}" : ""));
            StatusText = mode == ResizeDialogMode.ImageSize ? "Resizing image…" : "Resizing canvas…";

            // Heavy pixel work off the UI thread so the window does not appear frozen.
            var cmd = await Task.Run(() => mode == ResizeDialogMode.CanvasSize
                ? DocumentResizeCommand.CanvasSize(
                    ws.Document, vm.ResultWidth, vm.ResultHeight, vm.Anchor, ws.NotifyChanged)
                : DocumentResizeCommand.ImageSize(
                    ws.Document, vm.ResultWidth, vm.ResultHeight, ws.NotifyChanged));

            // Factory already applied the mutation; record for undo without re-executing.
            RecordResize(ws, cmd, oldWidth, oldHeight,
                mode == ResizeDialogMode.CanvasSize ? vm.Anchor : null);
            ws.NotifyChanged();
            UpdateStatus();
            FitRequested?.Invoke();
            AppLog.Info($"{mode} done");
        }
        catch (Exception ex)
        {
            AppLog.Error($"{mode} failed", ex);
            StatusText = $"{mode} failed — see log";
        }
    }

    /// <summary>
    /// Image Size applied to every open tab with one rule (percentage / fit within / exact size).
    /// Each tab records its own undo step.
    /// </summary>
    [RelayCommand]
    private async Task ResizeAllAsync()
    {
        if (_window is null || _manager.Workspaces.Count == 0) return;

        var workspaces = _manager.Workspaces.ToList();
        var box = ActiveWorkspace?.Document ?? workspaces[0].Document;
        var vm = new ResizeAllViewModel(
            workspaces.Select(w => new ResizeAllDocument(w.Document.Name, w.Document.Width, w.Document.Height)).ToList(),
            _lastResizeAllOptions,
            box.Width,
            box.Height);
        var ok = await new Views.ResizeAllWindow(vm).ShowDialog<bool?>(_window);
        if (ok != true) return;

        var options = vm.Options;
        _lastResizeAllOptions = options;
        AppLog.Info($"Resize All begin count={workspaces.Count} {options}");

        int resized = 0, unchanged = 0, failed = 0;
        for (int i = 0; i < workspaces.Count; i++)
        {
            var ws = workspaces[i];
            if (!_manager.Workspaces.Contains(ws)) continue; // tab closed while resizing

            try
            {
                var (w, h) = BatchResize.ComputeTargetSize(ws.Document.Width, ws.Document.Height, options);
                if (w == ws.Document.Width && h == ws.Document.Height)
                {
                    unchanged++;
                    continue;
                }

                if (ws.IsTransforming) ws.CancelTransform();
                if (ws.IsEditingSelection) ws.CancelSelectionEdit();

                StatusText = $"Resizing {i + 1}/{workspaces.Count}: {ws.Document.Name}…";
                AppLog.Info($"Resize All: '{ws.Document.Name}' {ws.Document.Width}x{ws.Document.Height} → {w}x{h}");

                // Heavy pixel work off the UI thread; the factory applies the mutation, record for undo.
                int oldWidth = ws.Document.Width;
                int oldHeight = ws.Document.Height;
                var cmd = await Task.Run(() => DocumentResizeCommand.ImageSize(ws.Document, w, h, ws.NotifyChanged));
                RecordResize(ws, cmd, oldWidth, oldHeight, anchor: null);
                ws.NotifyChanged();
                resized++;
            }
            catch (Exception ex)
            {
                failed++;
                AppLog.Error($"Resize All failed for '{ws.Document.Name}'", ex);
            }
        }

        SyncTransformUi();
        FitAllRequested?.Invoke();

        var summary = $"Resized {resized} image(s)";
        if (unchanged > 0) summary += $", {unchanged} unchanged";
        if (failed > 0) summary += $" — {failed} failed, see log";
        StatusText = summary;
        AppLog.Info($"Resize All done resized={resized} unchanged={unchanged} failed={failed}");
    }

    /// <summary>
    /// Record an already-applied resize. The selection follows the pixels — shifted with the anchor
    /// for Canvas Size, scaled for Image Size (<paramref name="anchor"/> null) — and undo restores both.
    /// </summary>
    private static void RecordResize(
        DocumentWorkspace ws, DocumentResizeCommand cmd, int oldWidth, int oldHeight, CanvasAnchor? anchor)
    {
        var selection = ws.Selection;
        if (!selection.HasSelection)
        {
            ws.History.Record(cmd);
            return;
        }

        var before = selection.Capture();
        int newWidth = ws.Document.Width;
        int newHeight = ws.Document.Height;
        if (anchor is { } a)
        {
            var (dx, dy) = CanvasAnchorMath.OffsetDelta(a, oldWidth, oldHeight, newWidth, newHeight);
            selection.Offset(dx, dy);
        }
        else
        {
            selection.ScaleBy(newWidth / (double)oldWidth, newHeight / (double)oldHeight);
        }
        selection.ClampToDocument(newWidth, newHeight);

        ws.History.Record(new CompoundCommand(cmd.Name, cmd,
            new SelectionCommand(cmd.Name, selection, before, selection.Capture())));
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
    private void SelectAll()
    {
        if (ActiveWorkspace?.IsEditingSelection == true)
            ActiveWorkspace.CancelSelectionEdit();
        ActiveWorkspace?.SelectAll();
        SyncTransformUi();
    }

    [RelayCommand]
    private void Deselect()
    {
        ActiveWorkspace?.Deselect();
        if (CurrentTool == CanvasTool.SelectionEdit)
            CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private void InverseSelection()
    {
        ActiveWorkspace?.InvertSelection();
        if (CurrentTool == CanvasTool.SelectionEdit)
            CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private async Task ColorRangeAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;
        if (ws.IsTransforming) ws.CancelTransform();
        if (ws.IsEditingSelection) ws.CancelSelectionEdit();

        using var vm = new ColorRangeViewModel(ws.GetComposite(), _colorRangeFuzziness, SelectionMode);
        var ok = await new Views.ColorRangeWindow(vm).ShowDialog<bool?>(_window);
        _colorRangeFuzziness = vm.Fuzziness;
        if (ok != true || !vm.HasColors) return;

        try
        {
            var options = vm.Options;
            ws.ColorRangeSelect(options, vm.SelectedMode.Mode);
            if (CurrentTool == CanvasTool.SelectionEdit)
                CurrentTool = CanvasTool.RectSelect;
            SyncTransformUi();
            AppLog.Info($"Color Range: {options.Include.Count} color(s), {options.Exclude.Count} excluded, " +
                        $"fuzziness {options.Fuzziness}, {vm.SelectedMode.Mode}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Color Range failed", ex);
            StatusText = "Color Range failed — see log";
        }
    }

    [RelayCommand]
    private void Undo()
    {
        // Photoshop: undo during a transform cancels it instead of stepping back through history.
        if (CancelPendingTransform()) return;
        ActiveWorkspace?.History.Undo();
        RefreshLayers();
    }

    [RelayCommand]
    private void Redo()
    {
        if (CancelPendingTransform()) return;
        ActiveWorkspace?.History.Redo();
        RefreshLayers();
    }

    private bool CancelPendingTransform()
    {
        var ws = ActiveWorkspace;
        if (ws is null || (!ws.IsTransforming && !ws.IsEditingSelection)) return false;
        if (ws.IsTransforming) ws.CancelTransform();
        if (ws.IsEditingSelection) ws.CancelSelectionEdit();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
        return true;
    }

    [RelayCommand]
    private void AddLayer()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        ws.AddLayerAboveActive($"Layer {ws.Document.Layers.Count + 1}");
        RefreshLayers();
    }

    [RelayCommand]
    private void DeleteLayer()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not { } node) return;
        if (!ws.DeleteNode(node))
            StatusText = "The image needs at least one layer.";
        RefreshLayers();
    }

    [RelayCommand]
    private void DuplicateLayer()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not { } node) return;
        ws.DuplicateNode(node);
        RefreshLayers();
    }

    [RelayCommand]
    private void MoveLayerUp() => MoveActiveNode(up: true);

    [RelayCommand]
    private void MoveLayerDown() => MoveActiveNode(up: false);

    /// <summary>Through the stack, in and out of open groups (a closed group is passed as one entry).</summary>
    private void MoveActiveNode(bool up)
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not { } node) return;
        if (ws.MoveNode(node, up))
            RefreshLayers();
    }

    // --- Groups ---

    [RelayCommand]
    private void NewGroup()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        ws.AddGroupAboveActive(NextGroupName(ws.Document));
        RefreshLayers();
    }

    /// <summary>Ctrl+G: put the active layer (or group) into a new group.</summary>
    [RelayCommand]
    private void GroupLayer()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not { } node) return;
        if (ws.GroupNode(node, NextGroupName(ws.Document)) is not null)
            RefreshLayers();
    }

    /// <summary>Ctrl+Shift+G: the active group's content takes its place.</summary>
    [RelayCommand]
    private void Ungroup()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not LayerGroup group)
        {
            StatusText = "Select a group to ungroup.";
            return;
        }
        if (ws.Ungroup(group))
            RefreshLayers();
    }

    /// <summary>Open or close a group in the layers panel.</summary>
    public void ToggleGroupExpanded(LayerItemViewModel item)
    {
        if (item.Node is not LayerGroup group) return;
        group.IsExpanded = !group.IsExpanded;
        RefreshLayers();
    }

    private static string NextGroupName(PaintDocument document) =>
        $"Group {document.AllNodes.OfType<LayerGroup>().Count() + 1}";

    // --- Filters (non-destructive, on the active layer or group) ---

    [RelayCommand]
    private Task AddBrightnessContrastAsync() => AddFilterAsync(new BrightnessContrastFilter(0, 0));

    [RelayCommand]
    private Task AddHueSaturationAsync() => AddFilterAsync(new HueSaturationFilter(0, 0, 0));

    [RelayCommand]
    private Task AddColorBalanceAsync() =>
        AddFilterAsync(new ColorBalanceFilter(default, default, default));

    private Task AddFilterAsync(LayerFilter filter)
    {
        if (ActiveWorkspace?.Document.ActiveNode is not { } node)
        {
            StatusText = "Select a layer or a group first.";
            return Task.CompletedTask;
        }
        return EditFilterAsync(node, -1, filter);
    }

    public Task EditFilterAsync(FilterItemViewModel item) =>
        ActiveWorkspace?.Document.ActiveNode is { } node && item.Index < node.Filters.Count
            ? EditFilterAsync(node, item.Index, node.Filters[item.Index])
            : Task.CompletedTask;

    /// <summary>
    /// Open the filter dialog for a new filter (<paramref name="index"/> −1) or an existing one,
    /// previewing on the canvas; OK records one undo step, Cancel leaves the filters as they were.
    /// </summary>
    private async Task EditFilterAsync(LayerNode node, int index, LayerFilter initial)
    {
        var ws = ActiveWorkspace;
        if (ws is null || _window is null) return;
        if (ws.IsTransforming) ws.CancelTransform();
        if (ws.IsEditingSelection) ws.CommitSelectionEdit();

        var original = node.Filters;
        IReadOnlyList<LayerFilter> With(LayerFilter filter) =>
            index < 0 ? [.. original, filter] : original.Select((f, i) => i == index ? filter : f).ToArray();

        var vm = new FilterDialogViewModel(initial, node.Name);
        vm.Changed += () => ws.PreviewFilters(node, vm.Preview ? With(vm.Result) : original);
        ws.PreviewFilters(node, With(vm.Result));
        bool? ok;
        try
        {
            ok = await new Views.FilterWindow(vm).ShowDialog<bool?>(_window);
        }
        finally
        {
            ws.PreviewFilters(node, original);
        }

        var result = vm.Result;
        if (ok == true && (index < 0 || result != original[index]))
        {
            ws.SetFilters(node, With(result), index < 0 ? $"Add {result.Name}" : $"Edit {result.Name}");
            AppLog.Info($"Filter {(index < 0 ? "added" : "edited")} on '{node.Name}': {result.Name} ({result.Summary})");
        }
        RefreshLayers();
    }

    /// <summary>Switch a filter off or back on (its settings are kept).</summary>
    public void ToggleFilter(FilterItemViewModel item)
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not { } node || item.Index >= node.Filters.Count) return;
        var filter = node.Filters[item.Index];
        var toggled = filter with { Enabled = !filter.Enabled };
        ws.SetFilters(node, node.Filters.Select((f, i) => i == item.Index ? toggled : f).ToArray(),
            toggled.Enabled ? $"Show {filter.Name}" : $"Hide {filter.Name}");
        RefreshLayers();
    }

    public void RemoveFilter(FilterItemViewModel item)
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not { } node || item.Index >= node.Filters.Count) return;
        ws.SetFilters(node, node.Filters.Where((_, i) => i != item.Index).ToArray(), $"Remove {item.Name}");
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
        item.Node.IsVisible = !target;
        ws.History.Execute(LayerPropertyCommand.Visibility(item.Node, target, ws.NotifyChanged));
        item.SyncFromModel();
    }

    [RelayCommand]
    private void RenameSelectedLayer(string? name)
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveNode is not { } layer || string.IsNullOrWhiteSpace(name)) return;
        ws.History.Execute(LayerPropertyCommand.Rename(layer, name.Trim(), ws.NotifyChanged));
        RefreshLayers();
    }

    [RelayCommand]
    private void SetToolSelect()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        if (ActiveWorkspace?.IsEditingSelection == true) ActiveWorkspace.CommitSelectionEdit();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private void SetToolObjectSelect()
    {
        if (!_objectSelection.IsAvailable)
        {
            StatusText = "Object Selection needs the AI model: run scripts\\Get-Models.ps1, then rebuild.";
            return;
        }
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        if (ActiveWorkspace?.IsEditingSelection == true) ActiveWorkspace.CommitSelectionEdit();
        CurrentTool = CanvasTool.ObjectSelect;
        SyncTransformUi();
        // Load the model and analyze the image now, so the first click answers at once.
        if (ActiveWorkspace is { } ws)
            _objectSelection.Prewarm(ws);
    }

    /// <summary>
    /// Object Selection click (point) or drag (box) from the canvas, combined with the selection.
    /// Ignored while a previous request is still running.
    /// </summary>
    public async Task ObjectSelectAsync(SamPrompt prompt, SelectionCombineMode mode)
    {
        var ws = ActiveWorkspace;
        if (ws is null || _objectSelectionBusy || !_objectSelection.IsAvailable) return;

        _objectSelectionBusy = true;
        StatusText = _objectSelection.NeedsAnalysis(ws) ? "Analyzing image…" : "Selecting object…";
        try
        {
            var picked = await _objectSelection.SelectAsync(ws, prompt, ObjectSoftEdges);
            if (!_manager.Workspaces.Contains(ws)) return; // tab closed meanwhile

            ws.ApplySelection(picked, mode, "Object Selection");
            SyncTransformUi();
            StatusText = picked.HasSelection ? "Object selected" : "No object found there";
        }
        catch (Exception ex)
        {
            AppLog.Error("Object Selection failed", ex);
            StatusText = "Object Selection failed — see log";
        }
        finally
        {
            _objectSelectionBusy = false;
        }
    }

    /// <summary>
    /// Edit → Content-Aware Fill (Shift+Backspace): rebuild the selected area with LaMa from what
    /// shows at the active layer's level, onto a new layer just above it (the original stays
    /// intact underneath; paint the new layer's mask to bring parts of it back). One undo step;
    /// the selection stays for another pass.
    /// </summary>
    [RelayCommand]
    private async Task ContentAwareFillAsync()
    {
        var ws = ActiveWorkspace;
        if (ws is null || _busyStatus is not null) return;
        if (!_contentAwareFill.IsAvailable)
        {
            StatusText = "Content-Aware Fill needs the AI model: run scripts\\Get-Models.ps1, then rebuild.";
            return;
        }
        if (ws.IsTransforming)
        {
            StatusText = "Apply or cancel the transform first (Enter / Esc).";
            return;
        }
        if (ws.IsEditingSelection)
        {
            ws.CommitSelectionEdit();
            SyncTransformUi();
        }
        var layer = ws.Document.ActiveNode;
        if (layer is null) return;
        if (!ws.Selection.HasSelection)
        {
            StatusText = "Select what to remove first (Q: Object Selection), then Content-Aware Fill.";
            return;
        }

        using var request = CreateFillRequest(ws, layer);
        if (request is null) return;
        var documentSize = (ws.Document.Width, ws.Document.Height);

        string? outcome = null;
        try
        {
            if (!_contentAwareFill.IsLoaded)
            {
                SetBusyStatus("Loading AI model…");
                await _contentAwareFill.LoadAsync();
            }
            SetBusyStatus("Content-Aware Fill…");
            using var result = await _contentAwareFill.FillAsync(request);
            if (!_manager.Workspaces.Contains(ws)) return; // tab closed meanwhile

            if (result is null)
            {
                outcome = "Nothing left around the selection to rebuild it from";
            }
            else
            {
                var fill = new Layer("Content-Aware Fill", result.Pixels.Clone(), result.Region.X, result.Region.Y);
                if ((ws.Document.Width, ws.Document.Height) == documentSize &&
                    ws.InsertNodeAbove(layer, fill, "Content-Aware Fill"))
                {
                    RefreshLayers();
                    outcome = "Content-Aware Fill done on a new layer — Mask Brush (B) brings parts of the original back";
                }
                else
                {
                    fill.Dispose();
                    outcome = "The document changed during Content-Aware Fill — try again";
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Content-Aware Fill failed", ex);
            outcome = "Content-Aware Fill failed — see log";
        }
        finally
        {
            _busyStatus = null;
            if (outcome is null) UpdateStatus();
            else StatusText = outcome;
        }
    }

    /// <summary>Context for the fill: the visible stack up to the active entry (not what is above it).</summary>
    private static InpaintRequest? CreateFillRequest(DocumentWorkspace ws, LayerNode layer)
    {
        using var visible = CompositionEngine.CompositeThrough(ws.Document, layer);
        return InpaintProcessing.CreateRequest(visible, 0, 0, ws.Selection, ws.Document.Width, ws.Document.Height);
    }

    private void SetBusyStatus(string text)
    {
        _busyStatus = text;
        StatusText = text;
    }

    // --- Layer masks ---

    [RelayCommand]
    private void SetToolMaskBrush()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        if (ActiveWorkspace?.IsEditingSelection == true) ActiveWorkspace.CommitSelectionEdit();
        CurrentTool = CanvasTool.MaskBrush;
        SyncTransformUi();
    }

    /// <summary>X: swap Hide (black) and Reveal (white).</summary>
    [RelayCommand]
    private void SwapMaskBrushColor() => MaskBrushReveal = !MaskBrushReveal;

    /// <summary>[ / ]: brush size down / up by about 15%.</summary>
    public void StepMaskBrushSize(int direction)
    {
        double size = (double)MaskBrushSizeValue;
        size = direction > 0 ? Math.Max(size + 1, size * 1.15) : Math.Min(size - 1, size / 1.15);
        MaskBrushSizeValue = (decimal)Math.Round(Math.Clamp(size, MaskBrushOptions.MinSize, MaskBrushOptions.MaxSize));
    }

    /// <summary>{ / } (Shift+[ / ]): brush hardness down / up by 25%.</summary>
    public void StepMaskBrushHardness(int direction) =>
        MaskBrushHardness = Math.Clamp(MaskBrushHardness + 25 * Math.Sign(direction), 0, 100);

    /// <summary>The canvas finished a Mask Brush stroke: the layer may have just got a mask.</summary>
    public void OnMaskStrokeFinished() => RefreshLayers();

    [RelayCommand]
    private void AddLayerMask()
    {
        var ws = ActiveWorkspace;
        if (ws?.Document.ActiveLayer is not { } layer) return;
        if (layer.HasMask)
        {
            StatusText = "The layer already has a mask — paint it with the Mask Brush (B).";
            return;
        }
        if (!ws.AddLayerMask()) return;
        RefreshLayers();
        StatusText = ws.Selection.HasSelection
            ? "Layer mask added: only the selection shows"
            : "Layer mask added — Mask Brush (B): black hides, white reveals";
    }

    [RelayCommand]
    private void DeleteLayerMask()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        if (!ws.DeleteLayerMask())
        {
            StatusText = "The active layer has no mask.";
            return;
        }
        RefreshLayers();
    }

    [RelayCommand]
    private void ApplyLayerMask()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        if (!ws.ApplyLayerMask())
        {
            StatusText = "The active layer has no mask.";
            return;
        }
        RefreshLayers();
        StatusText = "Layer mask applied to the layer's transparency";
    }

    [RelayCommand]
    private void SetToolPolygonLasso()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        if (ActiveWorkspace?.IsEditingSelection == true) ActiveWorkspace.CommitSelectionEdit();
        CurrentTool = CanvasTool.PolygonLasso;
        SyncTransformUi();
    }

    [RelayCommand]
    private void SetToolMagicWand()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        if (ActiveWorkspace?.IsEditingSelection == true) ActiveWorkspace.CommitSelectionEdit();
        CurrentTool = CanvasTool.MagicWand;
        SyncTransformUi();
    }

    [RelayCommand]
    private void SetToolSelectionEdit()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        if (ws.IsTransforming) ws.CancelTransform();
        if (!ws.Selection.HasSelection)
        {
            AppLog.Info("Transform Selection: no active selection");
            StatusText = "Create a selection first (V), then Transform Selection";
            return;
        }
        if (ws.BeginSelectionEdit())
        {
            CurrentTool = CanvasTool.SelectionEdit;
            AppLog.Info("Transform Selection begin");
        }
        SyncTransformUi();
    }

    [RelayCommand]
    private void SetToolMove()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        if (ActiveWorkspace?.IsEditingSelection == true) ActiveWorkspace.CommitSelectionEdit();
        CurrentTool = CanvasTool.MoveLayer;
        SyncTransformUi();
    }

    [RelayCommand]
    private void SetToolPan()
    {
        if (ActiveWorkspace?.IsTransforming == true) ActiveWorkspace.CancelTransform();
        if (ActiveWorkspace?.IsEditingSelection == true) ActiveWorkspace.CommitSelectionEdit();
        CurrentTool = CanvasTool.Pan;
        SyncTransformUi();
    }

    [RelayCommand]
    private void CommitSelectionEdit()
    {
        ActiveWorkspace?.CommitSelectionEdit();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private void CancelSelectionEdit()
    {
        ActiveWorkspace?.CancelSelectionEdit();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private void ToggleFreeTransform()
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        try
        {
            if (ws.IsTransforming)
            {
                AppLog.Info("FreeTransform cancel via toggle");
                ws.CancelTransform();
                CurrentTool = CanvasTool.RectSelect;
            }
            else
            {
                if (ws.BeginTransform())
                {
                    AppLog.Info(ws.IsSelectionTransform
                        ? $"FreeTransform selection begin layer={ws.Document.ActiveLayer?.Name} sel={ws.Selection.Width}x{ws.Selection.Height}@{ws.Selection.X},{ws.Selection.Y}"
                        : $"FreeTransform begin layer={ws.Document.ActiveLayer?.Name}");
                    CurrentTool = CanvasTool.FreeTransform;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("ToggleFreeTransform failed", ex);
        }
        SyncTransformUi();
    }

    [RelayCommand]
    private void CommitTransform()
    {
        try
        {
            AppLog.Info("FreeTransform commit");
            ActiveWorkspace?.CommitTransform();
        }
        catch (Exception ex)
        {
            AppLog.Error("CommitTransform failed", ex);
        }
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    [RelayCommand]
    private void CancelTransform()
    {
        AppLog.Info("FreeTransform cancel");
        ActiveWorkspace?.CancelTransform();
        CurrentTool = CanvasTool.RectSelect;
        SyncTransformUi();
    }

    public void SyncTransformUi()
    {
        IsTransforming = ActiveWorkspace?.IsTransforming == true;
        IsEditingSelection = ActiveWorkspace?.IsEditingSelection == true;
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
    /// <summary>Fit every open tab (after Resize All, so switching tabs shows the whole image).</summary>
    public event Action? FitAllRequested;
    public event Action? Zoom100Requested;

    public void PreviewLayerOpacity(LayerItemViewModel item, float opacity)
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        opacity = Math.Clamp(opacity, 0f, 1f);
        item.Node.Opacity = opacity;
        item.Opacity = opacity;
        TransformOpacity = opacity;
        // NotifyChanged (not InvalidateComposite alone) so ImageCanvas rebuilds the bitmap.
        ws.NotifyChanged();
    }

    public void CommitLayerOpacity(LayerItemViewModel item, float from, float to)
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        from = Math.Clamp(from, 0f, 1f);
        to = Math.Clamp(to, 0f, 1f);
        if (Math.Abs(from - to) < 0.0005f)
        {
            item.SyncFromModel();
            return;
        }

        // History command captures before/after explicitly (preview may already have set layer).
        item.Node.Opacity = from;
        ws.History.Execute(LayerPropertyCommand.Opacity(item.Node, from, to, ws.NotifyChanged));
        item.SyncFromModel();
        TransformOpacity = item.Opacity;
    }

    public void ApplyLayerOpacity(LayerItemViewModel item, float opacity)
    {
        CommitLayerOpacity(item, item.Node.Opacity, opacity);
    }

    public void ApplyLayerBlendMode(LayerItemViewModel item, BlendMode blendMode)
    {
        var ws = ActiveWorkspace;
        if (ws is null) return;
        if (item.Node.BlendMode == blendMode)
        {
            item.SyncFromModel();
            return;
        }

        ws.History.Execute(LayerPropertyCommand.BlendModeChange(item.Node, blendMode, ws.NotifyChanged));
        item.SyncFromModel();
        SelectedBlendOption = BlendModeNames.GetOption(item.BlendMode);
    }

    private static string FormatColorMode(DocumentColorSettings s)
    {
        s.Sanitize();
        return s.Mode switch
        {
            ColorModeKind.Rgba => "RGBA",
            ColorModeKind.Indexed => $"Indexed {s.ColorCount}",
            ColorModeKind.Ecs => s.HalfBright ? "ECS 64 EHB" : $"ECS {s.ColorCount}",
            ColorModeKind.Aga => $"AGA {s.ColorCount}",
            _ => s.Mode.ToString()
        };
    }

    public void Dispose()
    {
        _manager.Dispose();
        _objectSelection.Dispose();
        _contentAwareFill.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed record WandSampleOption(bool VisibleImage, string Label)
{
    public override string ToString() => Label;
}
