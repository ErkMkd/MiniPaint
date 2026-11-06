namespace MiniPaint.Core.History;

public sealed class HistoryStack
{
    private readonly List<IEditCommand> _undo = new();
    private readonly List<IEditCommand> _redo = new();
    private readonly int _limit;

    public HistoryStack(int limit = 64)
    {
        _limit = Math.Max(1, limit);
    }

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => CanUndo ? _undo[^1].Name : null;
    public string? RedoName => CanRedo ? _redo[^1].Name : null;

    /// <summary>The command recorded, executed, undone or redone by the latest change (null after Clear).</summary>
    public IEditCommand? LastAffected { get; private set; }

    public event Action? Changed;

    public void Execute(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        Push(command);
    }

    /// <summary>
    /// Record a command that was already applied to the model (no second Execute).
    /// </summary>
    public void Record(IEditCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Push(command);
    }

    private void Push(IEditCommand command)
    {
        _undo.Add(command);
        if (_undo.Count > _limit)
            _undo.RemoveAt(0);
        _redo.Clear();
        LastAffected = command;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        cmd.Undo();
        _redo.Add(cmd);
        LastAffected = cmd;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        cmd.Execute();
        _undo.Add(cmd);
        LastAffected = cmd;
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        LastAffected = null;
        Changed?.Invoke();
    }
}
