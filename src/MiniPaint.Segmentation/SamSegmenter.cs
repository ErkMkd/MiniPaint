using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MiniPaint.Core.Selection;

namespace MiniPaint.Segmentation;

/// <summary>Image embeddings of one image state: encode once, then decode any number of prompts.</summary>
public sealed class SamEmbedding
{
    internal SamEmbedding(DenseTensor<float> embeddings, int width, int height, int resizedWidth, int resizedHeight)
    {
        Embeddings = embeddings;
        Width = width;
        Height = height;
        ResizedWidth = resizedWidth;
        ResizedHeight = resizedHeight;
    }

    internal DenseTensor<float> Embeddings { get; }
    public int Width { get; }
    public int Height { get; }
    public int ResizedWidth { get; }
    public int ResizedHeight { get; }
}

/// <summary>
/// MobileSAM object selection on the CPU: the image encoder runs once per image (~0.3 s), the
/// prompt decoder per click or box (~30 ms). Sessions are thread-safe; one instance can serve
/// every open document.
/// </summary>
public sealed class SamSegmenter : IDisposable
{
    public const string EncoderFile = "mobile_sam_image_encoder.onnx";
    public const string DecoderFile = "sam_mask_decoder_multi.onnx";

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;

    private SamSegmenter(InferenceSession encoder, InferenceSession decoder)
    {
        _encoder = encoder;
        _decoder = decoder;
    }

    public static bool ModelsExist(string directory) =>
        File.Exists(Path.Combine(directory, EncoderFile)) && File.Exists(Path.Combine(directory, DecoderFile));

    public static SamSegmenter Load(string directory)
    {
        if (!ModelsExist(directory))
            throw new FileNotFoundException($"MobileSAM models not found in {directory} (run scripts\\Get-Models.ps1).");

        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        var encoder = new InferenceSession(Path.Combine(directory, EncoderFile), options);
        try
        {
            return new SamSegmenter(encoder, new InferenceSession(Path.Combine(directory, DecoderFile), options));
        }
        catch
        {
            encoder.Dispose();
            throw;
        }
    }

    public SamEmbedding Encode(SamEncoderInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var results = _encoder.Run([NamedOnnxValue.CreateFromTensor("input_image", input.Pixels)]);
        var embeddings = results.First().AsTensor<float>().ToDenseTensor();
        return new SamEmbedding(embeddings, input.Width, input.Height, input.ResizedWidth, input.ResizedHeight);
    }

    /// <summary>Selection of the object at a click or inside a box (document-sized coverage mask).</summary>
    public SelectionState Segment(SamEmbedding embedding, SamPrompt prompt, bool softEdges)
    {
        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(prompt);
        int rw = embedding.ResizedWidth;
        int rh = embedding.ResizedHeight;
        var (coords, labels) = SamProcessing.EncodePrompt(prompt, embedding.Width, embedding.Height, rw, rh);

        // orig_im_size = resized size: the model returns masks at ≤ 1024 px, we upscale only the
        // chosen one (4 masks at full document size would weigh hundreds of MB on large images).
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("image_embeddings", embedding.Embeddings),
            NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(coords, [1, labels.Length, 2])),
            NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>(labels, [1, labels.Length])),
            NamedOnnxValue.CreateFromTensor("mask_input", new DenseTensor<float>([1, 1, 256, 256])),
            NamedOnnxValue.CreateFromTensor("has_mask_input", new DenseTensor<float>(new float[] { 0 }, [1])),
            NamedOnnxValue.CreateFromTensor("orig_im_size", new DenseTensor<float>(new float[] { rh, rw }, [2])),
        };

        using var results = _decoder.Run(inputs);
        var masks = results.First(r => r.Name == "masks").AsTensor<float>();
        var iou = results.First(r => r.Name == "iou_predictions").AsTensor<float>().ToArray();
        int chosen = SamProcessing.ChooseMask(iou, prompt.IsBox);

        int mh = masks.Dimensions[2];
        int mw = masks.Dimensions[3];
        var dense = masks as DenseTensor<float> ?? masks.ToDenseTensor();
        var logits = dense.Buffer.Span.Slice(chosen * mw * mh, mw * mh).ToArray();

        var coverage = SamProcessing.LogitsToCoverage(logits, mw, mh, embedding.Width, embedding.Height, softEdges);
        var selection = new SelectionState();
        selection.SetMask(0, 0, embedding.Width, embedding.Height, coverage);
        return selection;
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}
