using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniPaint.App.Controls;
using MiniPaint.App.Logging;
using MiniPaint.App.ViewModels;
using MiniPaint.Core.Composition;

namespace MiniPaint.App.Views;

public partial class MainWindow : Window
{
    private float _opacityDragStart = 1f;
    private bool _opacityDragging;
    private bool _opacityHandlersWired;
    private bool _suppressOpacityEvents;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        if (Vm is null) return;
        AppLog.Info($"MainWindow opened size={Bounds.Width}x{Bounds.Height}");
        Vm.AttachWindow(this);
        WireOpacitySliders();
        Vm.FitRequested += () =>
        {
            if (Vm.ActiveWorkspace is { } ws)
                ws.FitToWindow(Canvas.Bounds.Width, Canvas.Bounds.Height);
        };
        Vm.Zoom100Requested += () =>
        {
            if (Vm.ActiveWorkspace is { } ws)
                ws.Zoom100(Canvas.Bounds.Width, Canvas.Bounds.Height);
        };
        Canvas.StatusChanged += () =>
        {
            Vm.UpdateStatus(Canvas.CursorDocX, Canvas.CursorDocY);
            Vm.ZoomText = Vm.ActiveWorkspace is { } w ? $"{w.Zoom * 100:0.#}%" : "";
        };
        Canvas.TransformModeChanged += () => Vm.SyncTransformUi();

        // Start with a blank document so the app is immediately usable.
        if (Vm.Tabs.Count == 0)
            Vm.NewDocumentCommand.Execute(null);
    }

    /// <summary>
    /// Slider thumb marks pointer events Handled — must use handledEventsToo or opacity never commits.
    /// </summary>
    private void WireOpacitySliders()
    {
        if (_opacityHandlersWired) return;
        _opacityHandlersWired = true;

        WireOpacitySlider(LayerOpacitySlider);
        WireOpacitySlider(TransformOpacitySlider);
    }

    private void WireOpacitySlider(Slider? slider)
    {
        if (slider is null) return;

        slider.AddHandler(PointerPressedEvent, OnOpacityPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        slider.AddHandler(PointerReleasedEvent, OnOpacityPointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        slider.AddHandler(PointerCaptureLostEvent, OnOpacityPointerCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        slider.ValueChanged += OnOpacityValueChanged;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Transform / selection-edit modes take priority for Enter / Esc
        if (vm.IsTransforming)
        {
            if (e.Key == Key.Enter) { vm.CommitTransformCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.Escape) { vm.CancelTransformCommand.Execute(null); e.Handled = true; return; }
        }
        if (vm.IsEditingSelection)
        {
            if (e.Key == Key.Enter) { vm.CommitSelectionEditCommand.Execute(null); e.Handled = true; return; }
            if (e.Key == Key.Escape) { vm.CancelSelectionEditCommand.Execute(null); e.Handled = true; return; }
        }

        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (ctrl && alt && e.Key == Key.T) { vm.SetToolSelectionEditCommand.Execute(null); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.I) { vm.ImageSizeCommand.Execute(null); e.Handled = true; }
        else if (ctrl && alt && e.Key == Key.C) { vm.CanvasSizeCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.T) { vm.ToggleFreeTransformCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { vm.CopyCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.X) { vm.CutCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { vm.PasteCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.A) { vm.SelectAllCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.D) { vm.DeselectCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.Z) { vm.UndoCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.Y) { vm.RedoCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.O) { vm.OpenCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { vm.SaveProjectCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.N) { vm.NewDocumentCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.W) { vm.CloseDocumentCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.D0) { vm.FitToWindowCommand.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.D1) { vm.Zoom100Command.Execute(null); e.Handled = true; }
        else if (e.Key == Key.V && !vm.IsTransforming) { vm.SetToolSelectCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.M && !vm.IsTransforming) { vm.SetToolMoveCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.H && !vm.IsTransforming) { vm.SetToolPanCommand.Execute(null); e.Handled = true; }
    }

    private void OnVisibilityClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: LayerItemViewModel item } && Vm is not null)
        {
            // Checkbox already toggled binding; sync via command from previous value inverted
            // Force model update through history using current checkbox intent:
            Vm.ToggleLayerVisibilityCommand.Execute(item);
            // Re-sync checkbox to model (command toggles model from current model value)
            if (sender is CheckBox cb)
                cb.IsChecked = item.IsVisible;
            e.Handled = true;
        }
    }

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Vm is null) return;
        if (sender is TextBox tb)
        {
            Vm.RenameSelectedLayerCommand.Execute(tb.Text);
            tb.Text = "";
            e.Handled = true;
        }
    }

    private void OnOpacityPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Capture model opacity (not VM binding) so undo restores the real previous value.
        var modelOpacity = Vm?.SelectedLayer?.Layer.Opacity ?? 1f;
        BeginOpacityDrag(modelOpacity);
    }

    private void OnOpacityValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressOpacityEvents) return;
        if (Vm?.SelectedLayer is not { } layer) return;

        float value = (float)e.NewValue;
        // Binding updates when switching layers should not start a phantom drag.
        if (!_opacityDragging)
        {
            if (Math.Abs(value - layer.Layer.Opacity) < 0.0005f)
                return;
            BeginOpacityDrag(layer.Layer.Opacity);
        }

        Vm.PreviewLayerOpacity(layer, value);
    }

    private void OnOpacityPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider)
            EndOpacityDrag((float)slider.Value);
        else if (Vm is not null)
            EndOpacityDrag(Vm.SelectedLayer?.Opacity ?? _opacityDragStart);
    }

    private void OnOpacityPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_opacityDragging || Vm?.SelectedLayer is not { } layer) return;
        EndOpacityDrag(layer.Opacity);
    }

    private void BeginOpacityDrag(float start)
    {
        _opacityDragStart = start;
        _opacityDragging = true;
    }

    private void EndOpacityDrag(float value)
    {
        if (!_opacityDragging) return;
        _opacityDragging = false;
        if (Vm?.SelectedLayer is not { } layer) return;
        _suppressOpacityEvents = true;
        try
        {
            Vm.CommitLayerOpacity(layer, _opacityDragStart, value);
        }
        finally
        {
            _suppressOpacityEvents = false;
        }
    }

    private void OnBlendSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Vm?.SelectedLayer is not { } layer) return;
        if (sender is not ComboBox { SelectedItem: BlendModeOption option }) return;
        if (layer.Layer.BlendMode == option.Mode) return;
        Vm.ApplyLayerBlendMode(layer, option.Mode);
    }
}
