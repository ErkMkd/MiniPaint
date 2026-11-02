using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MiniPaint.App.Controls;
using MiniPaint.App.ViewModels;

namespace MiniPaint.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnOpened(object? sender, EventArgs e)
    {
        if (Vm is null) return;
        Vm.AttachWindow(this);
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

        // Start with a blank document so the app is immediately usable.
        if (Vm.Tabs.Count == 0)
            Vm.NewDocumentCommand.Execute(null);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = Vm;
        if (vm is null) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl && e.Key == Key.C) { vm.CopyCommand.Execute(null); e.Handled = true; }
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
        else if (e.Key == Key.V) { vm.SetToolSelectCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.M) { vm.SetToolMoveCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.H) { vm.SetToolPanCommand.Execute(null); e.Handled = true; }
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

    private void OnOpacityPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (Vm?.SelectedLayer is not { } layer) return;
        if (sender is Slider slider)
            Vm.ApplyLayerOpacity(layer, (float)slider.Value);
    }
}
