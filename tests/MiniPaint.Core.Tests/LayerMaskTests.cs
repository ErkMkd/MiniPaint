using System.IO.Compression;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;

namespace MiniPaint.Core.Tests;

public class LayerMaskTests
{
    private static DocumentWorkspace Workspace(int w, int h, byte r = 200, byte g = 10, byte b = 10)
    {
        var doc = PaintDocument.CreateBlank("t", w, h, transparent: true);
        doc.Layers[0].Pixels.FillRect(0, 0, w, h, r, g, b, 255);
        doc.IsDirty = false;
        return new DocumentWorkspace(doc);
    }

    private static int Alpha(PixelBuffer px, int x, int y) => px.PixelsReadOnly[px.Offset(x, y) + 3];

    private static MaskBrushOptions Hard(double size, double opacity = 1, bool reveal = false) =>
        new(size, Hardness: 1, opacity, reveal);

    private static void Stroke(DocumentWorkspace ws, MaskBrushOptions options, params (double X, double Y)[] points)
    {
        Assert.True(ws.BeginMaskStroke(options));
        foreach (var (x, y) in points)
            ws.ContinueMaskStroke(x, y);
        ws.EndMaskStroke();
    }

    [Fact]
    public void Composite_MaskScalesTheLayerAlpha()
    {
        using var ws = Workspace(4, 1);
        ws.Document.Layers[0].SetMask([255, 128, 0, 255]);

        using var composite = CompositionEngine.Composite(ws.Document);

        Assert.Equal(255, Alpha(composite, 0, 0));
        Assert.InRange(Alpha(composite, 1, 0), 127, 129);
        Assert.Equal(0, Alpha(composite, 2, 0));
    }

    [Fact]
    public void CompositeThrough_LeavesOutTheLayersAbove()
    {
        using var ws = Workspace(2, 1);
        var bottom = ws.Document.Layers[0];
        var top = new Layer("top", 2, 1);
        top.Pixels.Clear(0, 0, 255, 255);
        ws.Document.AddLayer(top);

        using var below = CompositionEngine.CompositeThrough(ws.Document, bottom);

        var px = below.PixelsReadOnly;
        Assert.Equal((200, 10, 10), (px[0], px[1], px[2])); // the red layer, not the blue one above
    }

    [Fact]
    public void HardBrush_HidesUnderTheBrushOnly_AndAddsTheMask()
    {
        using var ws = Workspace(40, 40);
        var layer = ws.Document.Layers[0];

        Stroke(ws, Hard(10), (20, 20));

        Assert.NotNull(layer.Mask);
        Assert.Equal(0, layer.Mask![20 * 40 + 20]);
        Assert.Equal(255, layer.Mask[20 * 40 + 28]);
        Assert.Equal("Add Layer Mask", ws.History.UndoName);

        ws.History.Undo();
        Assert.False(layer.HasMask);
        ws.History.Redo();
        Assert.Equal(0, layer.Mask![20 * 40 + 20]);
    }

    [Fact]
    public void Opacity_CapsOneStroke_TheNextStrokeGoesFurther()
    {
        using var ws = Workspace(40, 40);
        var layer = ws.Document.Layers[0];

        // Back and forth over the same spot: still half hidden, not more.
        Stroke(ws, Hard(10, opacity: 0.5), (20, 20), (24, 20), (16, 20), (20, 20));
        Assert.InRange(layer.Mask![20 * 40 + 20], 126, 129);
        Assert.Equal("Add Layer Mask", ws.History.UndoName);

        Stroke(ws, Hard(10, opacity: 0.5), (20, 20));
        Assert.InRange(layer.Mask![20 * 40 + 20], 62, 66);
        Assert.Equal("Mask Brush", ws.History.UndoName);
    }

    [Fact]
    public void SoftBrush_FadesFromCenterToEdge()
    {
        using var ws = Workspace(60, 60);
        var layer = ws.Document.Layers[0];

        Stroke(ws, new MaskBrushOptions(40, Hardness: 0, Opacity: 1, Reveal: false), (30, 30));

        var mask = layer.Mask!;
        int center = mask[30 * 60 + 30];
        int half = mask[30 * 60 + 40];  // 10 px out of a 20 px radius
        int edge = mask[30 * 60 + 48];
        Assert.InRange(center, 0, 2);
        Assert.InRange(half, 100, 160);
        Assert.True(edge > half && edge < 255, $"{center} {half} {edge}");
    }

    [Fact]
    public void Reveal_PaintsWhite()
    {
        using var ws = Workspace(20, 20);
        var layer = ws.Document.Layers[0];
        layer.SetMask(LayerMask.Filled(20, 20, LayerMask.Hide));

        Stroke(ws, Hard(6, reveal: true), (10, 10));

        Assert.Equal(255, layer.Mask![10 * 20 + 10]);
        Assert.Equal(0, layer.Mask[0]);
    }

    [Fact]
    public void Stroke_LeavesNoGapAlongItsPath()
    {
        using var ws = Workspace(60, 20);
        var layer = ws.Document.Layers[0];

        Stroke(ws, Hard(4), (5, 10), (55, 10));

        for (int x = 6; x < 54; x++)
            Assert.Equal(0, layer.Mask![10 * 60 + x]);
    }

    [Fact]
    public void Stroke_OutsideTheLayer_ChangesNothing()
    {
        using var ws = Workspace(20, 20);

        Stroke(ws, Hard(4), (-30, -30), (-40, -10));

        Assert.False(ws.Document.Layers[0].HasMask);
        Assert.False(ws.History.CanUndo);
    }

    [Fact]
    public void AddLayerMask_WithASelection_ShowsOnlyTheSelection()
    {
        using var ws = Workspace(10, 10);
        ws.Selection.Set(2, 2, 3, 3);

        Assert.True(ws.AddLayerMask());

        var mask = ws.Document.Layers[0].Mask!;
        Assert.Equal(255, mask[3 * 10 + 3]);
        Assert.Equal(0, mask[0]);
        ws.History.Undo();
        Assert.False(ws.Document.Layers[0].HasMask);
    }

    [Fact]
    public void ApplyLayerMask_BakesIntoAlpha_AndUndoes()
    {
        using var ws = Workspace(2, 1);
        var layer = ws.Document.Layers[0];
        layer.SetMask([255, 0]);

        Assert.True(ws.ApplyLayerMask());
        Assert.False(layer.HasMask);
        Assert.Equal(255, Alpha(layer.Pixels, 0, 0));
        Assert.Equal(0, Alpha(layer.Pixels, 1, 0));

        ws.History.Undo();
        Assert.Equal(new byte[] { 255, 0 }, layer.Mask);
        Assert.Equal(255, Alpha(layer.Pixels, 1, 0));
    }

    [Fact]
    public void DeleteLayerMask_IsUndoable()
    {
        using var ws = Workspace(2, 1);
        var layer = ws.Document.Layers[0];
        layer.SetMask([10, 20]);

        Assert.True(ws.DeleteLayerMask());
        Assert.False(layer.HasMask);
        ws.History.Undo();
        Assert.Equal(new byte[] { 10, 20 }, layer.Mask);
    }

    [Fact]
    public void FreeTransform_MovesTheMaskWithThePixels()
    {
        using var ws = Workspace(32, 16);
        var layer = ws.Document.Layers[0];
        var mask = LayerMask.Filled(32, 16, LayerMask.Reveal);
        for (int y = 0; y < 16; y++)
            mask.AsSpan(y * 32, 16).Clear(); // left half hidden
        layer.SetMask(mask);

        Assert.True(ws.BeginTransform());
        ws.ActiveTransform!.ScaleX = 0.5; // the hidden half shrinks to the left quarter
        ws.RefreshTransformPreview();
        ws.CommitTransform();

        Assert.Equal(layer.Pixels.Width * layer.Pixels.Height, layer.Mask!.Length);
        using (var composite = CompositionEngine.Composite(ws.Document))
        {
            Assert.Equal(0, Alpha(composite, 10, 8));   // left of center: hidden
            Assert.Equal(255, Alpha(composite, 20, 8)); // right of center: shown
            Assert.Equal(0, Alpha(composite, 30, 8));   // beyond the shrunk layer
        }

        ws.History.Undo();
        Assert.Equal(32 * 16, layer.Mask!.Length);
        Assert.Equal(0, layer.Mask[8 * 32 + 4]);
    }

    [Fact]
    public void ImageSize_ScalesTheMask_AndUndoRestoresIt()
    {
        using var ws = Workspace(10, 10);
        var layer = ws.Document.Layers[0];
        var mask = LayerMask.Filled(10, 10, LayerMask.Reveal);
        for (int y = 0; y < 10; y++)
            mask.AsSpan(y * 10, 5).Clear();
        layer.SetMask(mask);

        ws.History.Execute(DocumentResizeCommand.ImageSize(ws.Document, 20, 20));

        Assert.Equal(400, layer.Mask!.Length);
        Assert.True(layer.Mask[10 * 20 + 2] < 30);
        Assert.True(layer.Mask[10 * 20 + 17] > 225);
        ws.History.Undo();
        Assert.Equal(mask, layer.Mask);
    }

    [Fact]
    public void ReplacePixels_WithAnotherSize_KeepsTheMaskInStep()
    {
        var layer = new Layer("L", 4, 4);
        layer.SetMask(LayerMask.Filled(4, 4, 255));

        layer.ReplacePixels(new PixelBuffer(8, 2));

        Assert.Equal(16, layer.Mask!.Length);
    }

    [Fact]
    public void DeleteLayer_Undo_BringsTheMaskBack()
    {
        using var ws = Workspace(2, 1);
        var top = new Layer("top", 2, 1);
        top.SetMask([1, 2]);
        ws.Document.AddLayer(top);

        ws.History.Execute(new DeleteNodeCommand(ws.Document, top));
        ws.History.Undo();

        Assert.Equal(new byte[] { 1, 2 }, ws.Document.Layers[^1].Mask);
    }

    [Fact]
    public void Duplicate_CopiesTheMask()
    {
        using var ws = Workspace(2, 1);
        ws.Document.Layers[0].SetMask([3, 4]);

        var copy = (Layer)ws.DuplicateNode(ws.Document.Layers[0])!;
        ws.Document.Layers[0].Mask![0] = 99;

        Assert.Equal(new byte[] { 3, 4 }, copy.Mask);
    }

    [Fact]
    public void CreateFromLayerContent_TrimsWhatTheMaskHides()
    {
        var layer = new Layer("L", 10, 1);
        layer.Pixels.Clear(1, 2, 3, 255);
        var mask = new byte[10];
        mask.AsSpan(6).Fill(255);
        layer.SetMask(mask);

        using var doc = PaintDocument.CreateFromLayerContent(layer)!;

        Assert.Equal(4, doc.Width);
    }

    [Fact]
    public void InsertLayer_UndoRedo_KeepsTheSameLayer_ForLaterSteps()
    {
        using var ws = Workspace(20, 20);
        var bottom = ws.Document.Layers[0];
        var fill = new Layer("fill", 20, 20);
        fill.Pixels.Clear(0, 255, 0, 255);

        Assert.True(ws.InsertNodeAbove(bottom, fill, "Content-Aware Fill"));
        Assert.Same(fill, ws.Document.ActiveLayer);
        Stroke(ws, Hard(4), (10, 10)); // a mask stroke on the new layer

        ws.History.Undo();
        ws.History.Undo();
        Assert.Single(ws.Document.Layers);
        Assert.Same(bottom, ws.Document.ActiveLayer);

        ws.History.Redo();
        ws.History.Redo();
        Assert.Same(fill, ws.Document.Layers[1]);
        Assert.Equal(0, fill.Mask![10 * 20 + 10]);
    }

    [Fact]
    public void Project_RoundTrip_KeepsMasks_AndStaysVersion1WithoutThem()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MiniPaintTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var ws = Workspace(3, 2);
            var plain = Path.Combine(dir, "plain.minipaint");
            ProjectSerializer.Save(ws.Document, plain);
            Assert.Contains("\"version\": 1", ManifestText(plain));

            ws.Document.Layers[0].SetMask([0, 50, 100, 150, 200, 255]);
            var masked = Path.Combine(dir, "masked.minipaint");
            ProjectSerializer.Save(ws.Document, masked);
            Assert.Contains("\"version\": 2", ManifestText(masked));

            using var loaded = ProjectSerializer.Load(masked);
            Assert.Equal(new byte[] { 0, 50, 100, 150, 200, 255 }, loaded.Layers[0].Mask);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    private static string ManifestText(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        using var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        return reader.ReadToEnd();
    }
}
