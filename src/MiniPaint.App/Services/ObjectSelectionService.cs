using System.Runtime.CompilerServices;
using MiniPaint.App.Logging;
using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Selection;
using MiniPaint.Segmentation;

namespace MiniPaint.App.Services;

/// <summary>
/// Object Selection backend: loads MobileSAM once, keeps one image embedding per document
/// (re-encoded only when its pixels change) and turns clicks / boxes into selections.
/// Heavy work runs off the UI thread; call the public methods from the UI thread.
/// </summary>
public sealed class ObjectSelectionService : IDisposable
{
    private sealed record CachedEmbedding(int CompositeVersion, Task<SamEmbedding> Embedding);

    private readonly string _modelDirectory = Path.Combine(AppContext.BaseDirectory, "Models", "MobileSAM");
    private readonly ConditionalWeakTable<DocumentWorkspace, CachedEmbedding> _embeddings = new();
    private Task<SamSegmenter>? _segmenter;
    private bool _disposed;

    public bool IsAvailable => SamSegmenter.ModelsExist(_modelDirectory);

    /// <summary>True when the document's current image still has to be analyzed (first click is slower).</summary>
    public bool NeedsAnalysis(DocumentWorkspace workspace) =>
        !_embeddings.TryGetValue(workspace, out var cached) ||
        cached.CompositeVersion != workspace.CompositeVersion ||
        !cached.Embedding.IsCompletedSuccessfully;

    /// <summary>Start loading the model and analyzing the image so the first click is instant.</summary>
    public void Prewarm(DocumentWorkspace workspace)
    {
        if (!IsAvailable) return;
        _ = GetEmbeddingAsync(workspace).ContinueWith(
            t => AppLog.Warn($"Object Selection prewarm failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    public async Task<SelectionState> SelectAsync(DocumentWorkspace workspace, SamPrompt prompt, bool softEdges)
    {
        var embedding = await GetEmbeddingAsync(workspace);
        var segmenter = await GetSegmenterAsync();
        return await Task.Run(() => segmenter.Segment(embedding, prompt, softEdges));
    }

    private Task<SamSegmenter> GetSegmenterAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _segmenter ??= Task.Run(() =>
        {
            var started = Environment.TickCount64;
            var segmenter = SamSegmenter.Load(_modelDirectory);
            AppLog.Info($"MobileSAM loaded in {Environment.TickCount64 - started} ms");
            return segmenter;
        });
    }

    /// <summary>UI thread: the composite is copied here; resizing and encoding run in the background.</summary>
    private Task<SamEmbedding> GetEmbeddingAsync(DocumentWorkspace workspace)
    {
        var composite = workspace.GetComposite(); // may recompute: CompositeVersion is read after
        int version = workspace.CompositeVersion;
        if (_embeddings.TryGetValue(workspace, out var cached) && cached.CompositeVersion == version &&
            !cached.Embedding.IsFaulted)
            return cached.Embedding;

        var task = EncodeAsync(composite.Clone());
        _embeddings.AddOrUpdate(workspace, new CachedEmbedding(version, task));
        return task;
    }

    private async Task<SamEmbedding> EncodeAsync(PixelBuffer image)
    {
        using (image)
        {
            var segmenter = await GetSegmenterAsync();
            return await Task.Run(() =>
            {
                var started = Environment.TickCount64;
                var embedding = segmenter.Encode(SamProcessing.ToEncoderInput(image));
                AppLog.Info($"Object Selection: image {image.Width}x{image.Height} analyzed in {Environment.TickCount64 - started} ms");
                return embedding;
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_segmenter is { IsCompletedSuccessfully: true } loaded)
            loaded.Result.Dispose();
    }
}
