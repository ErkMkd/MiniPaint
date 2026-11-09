using MiniPaint.App.Logging;
using MiniPaint.Inpainting;

namespace MiniPaint.App.Services;

/// <summary>
/// Content-Aware Fill backend: loads LaMa on first use (kept for the session, ~2.5 s) and
/// rebuilds selected areas off the UI thread (~2 s each on the CPU).
/// </summary>
public sealed class ContentAwareFillService : IDisposable
{
    private readonly string _modelDirectory = Path.Combine(AppContext.BaseDirectory, "Models", "LaMa");
    private Task<LamaInpainter>? _inpainter;
    private bool _disposed;

    public bool IsAvailable => LamaInpainter.ModelExists(_modelDirectory);

    public bool IsLoaded => _inpainter is { IsCompletedSuccessfully: true };

    /// <summary>Load the model now (no-op once loaded); a failed load is retried on the next call.</summary>
    public Task LoadAsync() => GetInpainterAsync();

    /// <summary>Rebuilt pixels for the request, or null when nothing around the selection is left to rebuild from.</summary>
    public async Task<InpaintResult?> FillAsync(InpaintRequest request)
    {
        var inpainter = await GetInpainterAsync();
        return await Task.Run(() =>
        {
            var started = Environment.TickCount64;
            var result = inpainter.Inpaint(request);
            var plan = request.Plan;
            AppLog.Info($"Content-Aware Fill: region {plan.Region.Width}x{plan.Region.Height}, context " +
                        $"{plan.Crop.Width}x{plan.Crop.Height} -> {plan.ModelWidth}x{plan.ModelHeight}, " +
                        $"{Environment.TickCount64 - started} ms");
            return result;
        });
    }

    private Task<LamaInpainter> GetInpainterAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inpainter is { IsFaulted: true })
            _inpainter = null;
        return _inpainter ??= Task.Run(() =>
        {
            var started = Environment.TickCount64;
            var inpainter = LamaInpainter.Load(_modelDirectory);
            AppLog.Info($"LaMa loaded in {Environment.TickCount64 - started} ms");
            return inpainter;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_inpainter is { IsCompletedSuccessfully: true } loaded)
            loaded.Result.Dispose();
    }
}
