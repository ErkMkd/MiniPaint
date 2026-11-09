using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace MiniPaint.Inpainting;

/// <summary>
/// LaMa (big-lama, fixed 512 × 512 input) on the CPU: rebuilds the selected area from what
/// surrounds it. The session is thread-safe; one instance can serve every open document.
/// </summary>
public sealed class LamaInpainter : IDisposable
{
    public const string ModelFile = "lama_fp32.onnx";

    private readonly InferenceSession _session;

    private LamaInpainter(InferenceSession session) => _session = session;

    public static bool ModelExists(string directory) => File.Exists(Path.Combine(directory, ModelFile));

    public static LamaInpainter Load(string directory)
    {
        if (!ModelExists(directory))
            throw new FileNotFoundException($"LaMa model not found in {directory} (run scripts\\Get-Models.ps1).");

        // The export has ~10k nodes (FFT written out as plain ops): the sequential planner needs
        // ~12 s to load it, the parallel one ~2.5 s, for the same output and a similar run time.
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_PARALLEL,
        };
        return new LamaInpainter(new InferenceSession(Path.Combine(directory, ModelFile), options));
    }

    /// <summary>Rebuilt pixels for the request's selection, or null when nothing around it is left to rebuild from.</summary>
    public InpaintResult? Inpaint(InpaintRequest request) => InpaintProcessing.Inpaint(request, Run);

    private float[] Run(DenseTensor<float> image, DenseTensor<float> mask)
    {
        using var results = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor("image", image),
            NamedOnnxValue.CreateFromTensor("mask", mask),
        ]);
        return results.First().AsTensor<float>().ToArray();
    }

    public void Dispose() => _session.Dispose();
}
