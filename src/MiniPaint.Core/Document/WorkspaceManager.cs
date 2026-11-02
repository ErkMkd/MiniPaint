using MiniPaint.Core.Clipboard;
using MiniPaint.Core.Project;

namespace MiniPaint.Core.Document;

/// <summary>
/// Manages multiple open document workspaces (tabs).
/// </summary>
public sealed class WorkspaceManager : IDisposable
{
    private readonly List<DocumentWorkspace> _workspaces = new();
    private bool _disposed;

    public ImageClipboard Clipboard { get; } = new();

    public IReadOnlyList<DocumentWorkspace> Workspaces => _workspaces;

    public DocumentWorkspace? Active { get; private set; }

    public event Action? Changed;

    public DocumentWorkspace OpenImage(string path)
    {
        var doc = PaintDocument.FromImage(path);
        return Add(new DocumentWorkspace(doc));
    }

    public DocumentWorkspace OpenProject(string path)
    {
        var doc = ProjectSerializer.Load(path);
        return Add(new DocumentWorkspace(doc));
    }

    public DocumentWorkspace NewDocument(string name, int width, int height)
    {
        var doc = PaintDocument.CreateBlank(name, width, height, transparent: true);
        return Add(new DocumentWorkspace(doc));
    }

    public void Activate(DocumentWorkspace workspace)
    {
        if (!_workspaces.Contains(workspace))
            throw new ArgumentException("Workspace is not open.");
        Active = workspace;
        Changed?.Invoke();
    }

    public bool Close(DocumentWorkspace workspace)
    {
        if (!_workspaces.Remove(workspace)) return false;
        if (Active == workspace)
            Active = _workspaces.Count > 0 ? _workspaces[^1] : null;
        workspace.Dispose();
        Changed?.Invoke();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (var ws in _workspaces)
            ws.Dispose();
        _workspaces.Clear();
        Clipboard.Clear();
        Active = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private DocumentWorkspace Add(DocumentWorkspace workspace)
    {
        _workspaces.Add(workspace);
        workspace.Changed += () => Changed?.Invoke();
        Active = workspace;
        Changed?.Invoke();
        return workspace;
    }
}
