namespace MiniPaint.Core.History;

public interface IEditCommand
{
    string Name { get; }
    void Execute();
    void Undo();
}
