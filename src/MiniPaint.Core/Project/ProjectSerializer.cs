using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Filters;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Project;

/// <summary>
/// Native .minipaint format: ZIP containing manifest.json + layer_N.png files (+ mask_N.png,
/// grayscale, for layers with a mask). Preserves layers, offsets, opacity, blend mode,
/// visibility, layer masks, groups, filters and document dimensions.
/// </summary>
public static class ProjectSerializer
{
    public const string Extension = ".minipaint";

    /// <summary>
    /// Version 2 adds layer masks, version 3 groups and filters (a "nodes" tree instead of the flat
    /// "layers" list). A file is written with the lowest version that holds it, so projects that
    /// do not use the newer features still open in older MiniPaint builds.
    /// </summary>
    private const int FormatVersion = 3;

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
            var cs = document.ColorSettings;
            cs.Sanitize();
            var layers = document.Layers;
            var allNodes = document.AllNodes.ToList();
            bool tree = allNodes.Any(n => n is LayerGroup || n.Filters.Count > 0);
            var manifest = new ProjectManifest
            {
                Version = tree ? 3 : layers.Any(l => l.HasMask) ? 2 : 1,
                Name = document.Name,
                Width = document.Width,
                Height = document.Height,
                ActiveLayerIndex = tree
                    ? allNodes.FindIndex(n => n.Id == document.ActiveLayerId)
                    : IndexOf(layers, document.ActiveLayerId),
                ColorMode = cs.Mode.ToString(),
                ColorCount = cs.ColorCount,
                HalfBright = cs.HalfBright,
                Quantizer = cs.Quantizer.ToString(),
                Dither = cs.Dither.ToString(),
                PaletteCustomized = cs.PaletteCustomized,
                Palette = cs.Palette?.Select(c => $"{c.R:X2}{c.G:X2}{c.B:X2}").ToList(),
                Layers = tree ? null : layers.Select((l, i) => new LayerManifest
                {
                    Name = l.Name,
                    Visible = l.IsVisible,
                    Opacity = l.Opacity,
                    BlendMode = l.BlendMode.ToString(),
                    OffsetX = l.OffsetX,
                    OffsetY = l.OffsetY,
                    File = LayerFile(i),
                    Mask = l.HasMask ? MaskFile(i) : null
                }).ToList(),
                Nodes = tree ? NodesOf(document.Root, layers) : null
            };

            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
            {
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));
            }

            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                var entry = zip.CreateEntry(LayerFile(i), CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                    ImageCodec.SavePng(layer.Pixels, entryStream);

                if (layer.Mask is { } mask)
                {
                    var maskEntry = zip.CreateEntry(MaskFile(i), CompressionLevel.Optimal);
                    using var maskStream = maskEntry.Open();
                    using var gray = MaskToGray(mask, layer.Pixels.Width, layer.Pixels.Height);
                    ImageCodec.SavePng(gray, maskStream);
                }
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

        if (manifest.Nodes is { } nodes)
        {
            foreach (var node in nodes)
                doc.AddNode(LoadNode(zip, node), doc.Root);
        }
        else
        {
            foreach (var layerInfo in manifest.Layers ?? [])
            {
                doc.AddLayer(LoadLayer(zip, new NodeManifest
                {
                    Name = layerInfo.Name,
                    Visible = layerInfo.Visible,
                    Opacity = layerInfo.Opacity,
                    BlendMode = layerInfo.BlendMode,
                    OffsetX = layerInfo.OffsetX,
                    OffsetY = layerInfo.OffsetY,
                    File = layerInfo.File,
                    Mask = layerInfo.Mask
                }));
            }
        }

        if (doc.Layers.Count == 0)
            throw new InvalidOperationException("Project contains no layers.");

        var active = manifest.Nodes is not null
            ? doc.AllNodes.ElementAtOrDefault(manifest.ActiveLayerIndex)
            : manifest.ActiveLayerIndex >= 0 && manifest.ActiveLayerIndex < doc.Layers.Count
                ? doc.Layers[manifest.ActiveLayerIndex]
                : null;
        doc.ActiveLayerId = (active ?? doc.Layers[^1]).Id;

        if (Enum.TryParse<ColorModeKind>(manifest.ColorMode, ignoreCase: true, out var mode))
            doc.ColorSettings.Mode = mode;
        if (manifest.ColorCount is int cc)
            doc.ColorSettings.ColorCount = cc;
        doc.ColorSettings.HalfBright = manifest.HalfBright ?? false;
        if (Enum.TryParse<QuantizerKind>(manifest.Quantizer, ignoreCase: true, out var q))
            doc.ColorSettings.Quantizer = q;
        if (Enum.TryParse<DitherKind>(manifest.Dither, ignoreCase: true, out var d))
            doc.ColorSettings.Dither = d;
        doc.ColorSettings.Sanitize();
        if (manifest.Palette is { Count: > 0 })
        {
            var colors = new List<PaletteRgb>(manifest.Palette.Count);
            foreach (var hex in manifest.Palette)
            {
                if (hex is null || hex.Length < 6) continue;
                if (byte.TryParse(hex.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                    byte.TryParse(hex.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                    byte.TryParse(hex.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                    colors.Add(new PaletteRgb(r, g, b));
            }
            if (colors.Count > 0)
                doc.ColorSettings.SetPalette(colors, manifest.PaletteCustomized ?? true);
        }

        doc.IsDirty = false;
        return doc;
    }

    public static void ExportPng(PaintDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var composite = CompositeForExport(document);
        ImageCodec.SavePng(composite, path);
    }

    public static void ExportPng(PaintDocument document, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        using var composite = CompositeForExport(document);
        ImageCodec.SavePng(composite, stream);
    }

    /// <summary>Merged JPEG; transparency is flattened onto white (JPEG has no alpha).</summary>
    public static void ExportJpeg(PaintDocument document, string path, int quality = ImageCodec.DefaultJpegQuality)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var composite = CompositeForExport(document);
        ImageCodec.SaveJpeg(composite, path, quality);
    }

    public static void ExportJpeg(PaintDocument document, Stream stream, int quality = ImageCodec.DefaultJpegQuality)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stream);
        using var composite = CompositeForExport(document);
        ImageCodec.SaveJpeg(composite, stream, quality);
    }

    /// <summary>Merged layers with the document's image mode applied, as shown on the canvas.</summary>
    private static PixelBuffer CompositeForExport(PaintDocument document)
    {
        var composite = Composition.CompositionEngine.Composite(document);
        if (document.ColorSettings.Mode != ColorModeKind.Rgba)
            ColorModePipeline.ApplyInPlace(composite, document.ColorSettings);
        return composite;
    }

    private static string LayerFile(int index) => $"layers/layer_{index:D3}.png";
    private static string MaskFile(int index) => $"layers/mask_{index:D3}.png";

    private static List<NodeManifest> NodesOf(LayerGroup group, IReadOnlyList<Layer> layers)
    {
        var list = new List<NodeManifest>(group.Children.Count);
        foreach (var node in group.Children)
        {
            var info = new NodeManifest
            {
                Name = node.Name,
                Visible = node.IsVisible,
                Opacity = node.Opacity,
                BlendMode = node.BlendMode.ToString(),
                Filters = node.Filters.Count > 0 ? node.Filters.Select(FilterManifest.From).ToList() : null
            };
            if (node is Layer layer)
            {
                int i = IndexOf(layers, layer.Id);
                info.OffsetX = layer.OffsetX;
                info.OffsetY = layer.OffsetY;
                info.File = LayerFile(i);
                info.Mask = layer.HasMask ? MaskFile(i) : null;
            }
            else if (node is LayerGroup child)
            {
                info.Type = "group";
                info.Expanded = child.IsExpanded;
                info.Children = NodesOf(child, layers);
            }
            list.Add(info);
        }
        return list;
    }

    private static LayerNode LoadNode(ZipArchive zip, NodeManifest info)
    {
        if (info.Type != "group")
            return LoadLayer(zip, info);

        BlendModeNames.TryParse(info.BlendMode, out var blendMode);
        var group = new LayerGroup(info.Name)
        {
            IsVisible = info.Visible,
            Opacity = info.Opacity,
            BlendMode = blendMode,
            IsExpanded = info.Expanded ?? true,
            Filters = LoadFilters(info.Filters)
        };
        foreach (var child in info.Children ?? [])
            group.Insert(group.Children.Count, LoadNode(zip, child));
        return group;
    }

    private static Layer LoadLayer(ZipArchive zip, NodeManifest info)
    {
        if (info.File is not { } file)
            throw new InvalidOperationException($"Layer '{info.Name}' has no image file.");
        var pixels = LoadImage(zip, file, "layer");
        BlendModeNames.TryParse(info.BlendMode, out var blendMode);
        var layer = new Layer(info.Name, pixels, info.OffsetX, info.OffsetY)
        {
            IsVisible = info.Visible,
            Opacity = info.Opacity,
            BlendMode = blendMode,
            Filters = LoadFilters(info.Filters)
        };
        if (info.Mask is { } maskFile)
        {
            using var gray = LoadImage(zip, maskFile, "mask");
            if (gray.Width != pixels.Width || gray.Height != pixels.Height)
                throw new InvalidOperationException($"Mask {maskFile} does not match its layer size.");
            layer.SetMask(GrayToMask(gray));
        }
        return layer;
    }

    /// <summary>Filters of a kind this build does not know are left out.</summary>
    private static LayerFilter[] LoadFilters(List<FilterManifest>? filters) =>
        filters?.Select(f => f.ToFilter()).OfType<LayerFilter>().ToArray() ?? [];

    private static PixelBuffer LoadImage(ZipArchive zip, string file, string kind)
    {
        var entry = zip.GetEntry(file)
            ?? throw new InvalidOperationException($"Missing {kind} file: {file}");
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        return ImageCodec.Load(ms);
    }

    /// <summary>Opaque gray image of a mask (white shows, black hides), readable in any viewer.</summary>
    private static PixelBuffer MaskToGray(byte[] mask, int width, int height)
    {
        var gray = new PixelBuffer(width, height);
        var px = gray.Pixels;
        for (int i = 0; i < mask.Length; i++)
        {
            int o = i * 4;
            px[o] = px[o + 1] = px[o + 2] = mask[i];
            px[o + 3] = 255;
        }
        return gray;
    }

    private static byte[] GrayToMask(PixelBuffer gray)
    {
        var px = gray.PixelsReadOnly;
        var mask = new byte[gray.Width * gray.Height];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = px[i * 4];
        return mask;
    }

    private static int IndexOf(IReadOnlyList<Layer> layers, Guid? id)
    {
        if (id is null) return -1;
        for (int i = 0; i < layers.Count; i++)
            if (layers[i].Id == id) return i;
        return -1;
    }

    private sealed class ProjectManifest
    {
        public int Version { get; set; }
        public string? Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ActiveLayerIndex { get; set; }
        public string? ColorMode { get; set; }
        public int? ColorCount { get; set; }
        public bool? HalfBright { get; set; }
        public string? Quantizer { get; set; }
        public string? Dither { get; set; }
        public bool? PaletteCustomized { get; set; }
        public List<string>? Palette { get; set; }

        /// <summary>Versions 1–2: the layers, bottom → top.</summary>
        public List<LayerManifest>? Layers { get; set; }

        /// <summary>Version 3: the layer tree, bottom → top (ActiveLayerIndex counts its entries depth-first).</summary>
        public List<NodeManifest>? Nodes { get; set; }
    }

    private sealed class NodeManifest
    {
        /// <summary>"layer" or "group".</summary>
        public string Type { get; set; } = "layer";
        public string Name { get; set; } = "";
        public bool Visible { get; set; } = true;
        public float Opacity { get; set; } = 1f;
        public string? BlendMode { get; set; }
        public List<FilterManifest>? Filters { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public string? File { get; set; }
        public string? Mask { get; set; }
        public bool? Expanded { get; set; }
        public List<NodeManifest>? Children { get; set; }
    }

    private sealed class FilterManifest
    {
        /// <summary>"brightnessContrast", "hueSaturation" or "colorBalance".</summary>
        public string Type { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public int? Brightness { get; set; }
        public int? Contrast { get; set; }
        public int? Hue { get; set; }
        public int? Saturation { get; set; }
        public int? Value { get; set; }
        /// <summary>Color balance per range: cyan–red, magenta–green, yellow–blue.</summary>
        public int[]? Shadows { get; set; }
        public int[]? Midtones { get; set; }
        public int[]? Highlights { get; set; }
        public bool? PreserveLuminosity { get; set; }

        public static FilterManifest From(LayerFilter filter) => filter switch
        {
            BrightnessContrastFilter f => new() { Type = "brightnessContrast", Enabled = f.Enabled, Brightness = f.Brightness, Contrast = f.Contrast },
            HueSaturationFilter f => new() { Type = "hueSaturation", Enabled = f.Enabled, Hue = f.Hue, Saturation = f.Saturation, Value = f.Value },
            ColorBalanceFilter f => new()
            {
                Type = "colorBalance",
                Enabled = f.Enabled,
                Shadows = Shift(f.Shadows),
                Midtones = Shift(f.Midtones),
                Highlights = Shift(f.Highlights),
                PreserveLuminosity = f.PreserveLuminosity
            },
            _ => throw new NotSupportedException($"Cannot save filter {filter.GetType().Name}.")
        };

        public LayerFilter? ToFilter() => Type switch
        {
            "brightnessContrast" => new BrightnessContrastFilter(Brightness ?? 0, Contrast ?? 0, Enabled),
            "hueSaturation" => new HueSaturationFilter(Hue ?? 0, Saturation ?? 0, Value ?? 0, Enabled),
            "colorBalance" => new ColorBalanceFilter(Shift(Shadows), Shift(Midtones), Shift(Highlights), PreserveLuminosity ?? true, Enabled),
            _ => null
        };

        private static int[] Shift(ToneShift shift) => [shift.CyanRed, shift.MagentaGreen, shift.YellowBlue];

        private static ToneShift Shift(int[]? values) =>
            values is { Length: 3 } ? new ToneShift(values[0], values[1], values[2]) : default;
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
        public string? Mask { get; set; }
    }
}
