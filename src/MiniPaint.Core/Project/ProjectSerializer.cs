using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Project;

/// <summary>
/// Native .minipaint format: ZIP containing manifest.json + layer_N.png files.
/// Preserves layers, offsets, opacity, blend mode, visibility, and document dimensions.
/// </summary>
public static class ProjectSerializer
{
    public const string Extension = ".minipaint";
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Save(PaintDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var temp = path + ".tmp";
        if (File.Exists(temp)) File.Delete(temp);

        using (var fs = File.Create(temp))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var manifest = new ProjectManifest
            {
                Version = FormatVersion,
                Name = document.Name,
                Width = document.Width,
                Height = document.Height,
                ActiveLayerIndex = IndexOf(document, document.ActiveLayerId),
                Layers = document.Layers.Select((l, i) => new LayerManifest
                {
                    Name = l.Name,
                    Visible = l.IsVisible,
                    Opacity = l.Opacity,
                    BlendMode = l.BlendMode.ToString(),
                    OffsetX = l.OffsetX,
                    OffsetY = l.OffsetY,
                    File = $"layers/layer_{i:D3}.png"
                }).ToList()
            };

            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
            }

            for (int i = 0; i < document.Layers.Count; i++)
            {
                var layer = document.Layers[i];
                var entry = zip.CreateEntry($"layers/layer_{i:D3}.png", CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                ImageCodec.SavePng(layer.Pixels, entryStream);
            }
        }

        if (File.Exists(path)) File.Delete(path);
        File.Move(temp, path);

        document.ProjectPath = path;
        document.Name = Path.GetFileNameWithoutExtension(path);
        document.IsDirty = false;
    }

    public static PaintDocument Load(string path)
    {
        using var fs = File.OpenRead(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("Invalid MiniPaint project: missing manifest.json");

        ProjectManifest manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
        {
            var json = reader.ReadToEnd();
            manifest = JsonSerializer.Deserialize<ProjectManifest>(json, JsonOptions)
                ?? throw new InvalidOperationException("Invalid MiniPaint project: bad manifest.");
        }

        if (manifest.Version > FormatVersion)
            throw new InvalidOperationException($"Unsupported project version {manifest.Version}.");

        var doc = new PaintDocument(manifest.Name ?? Path.GetFileNameWithoutExtension(path), manifest.Width, manifest.Height)
        {
            ProjectPath = path
        };

        // Clear default — PaintDocument starts empty; we add from file.
        foreach (var layerInfo in manifest.Layers)
        {
            var entry = zip.GetEntry(layerInfo.File)
                ?? throw new InvalidOperationException($"Missing layer file: {layerInfo.File}");
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            var pixels = ImageCodec.Load(ms);
            BlendModeNames.TryParse(layerInfo.BlendMode, out var blendMode);
            var layer = new Layer(layerInfo.Name, pixels, layerInfo.OffsetX, layerInfo.OffsetY)
            {
                IsVisible = layerInfo.Visible,
                Opacity = layerInfo.Opacity,
                BlendMode = blendMode
            };
            doc.AddLayer(layer);
        }

        if (doc.Layers.Count == 0)
            throw new InvalidOperationException("Project contains no layers.");

        if (manifest.ActiveLayerIndex >= 0 && manifest.ActiveLayerIndex < doc.Layers.Count)
            doc.ActiveLayerId = doc.Layers[manifest.ActiveLayerIndex].Id;
        else
            doc.ActiveLayerId = doc.Layers[^1].Id;

        doc.IsDirty = false;
        return doc;
    }

    public static void ExportPng(PaintDocument document, string path)
    {
        using var composite = Composition.CompositionEngine.Composite(document);
        ImageCodec.SavePng(composite, path);
    }

    private static int IndexOf(PaintDocument document, Guid? id)
    {
        if (id is null) return -1;
        for (int i = 0; i < document.Layers.Count; i++)
            if (document.Layers[i].Id == id) return i;
        return -1;
    }

    private sealed class ProjectManifest
    {
        public int Version { get; set; }
        public string? Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ActiveLayerIndex { get; set; }
        public List<LayerManifest> Layers { get; set; } = new();
    }

    private sealed class LayerManifest
    {
        public string Name { get; set; } = "";
        public bool Visible { get; set; } = true;
        public float Opacity { get; set; } = 1f;
        public string? BlendMode { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public string File { get; set; } = "";
    }
}
