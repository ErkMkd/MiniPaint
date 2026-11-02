using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;
using MiniPaint.Core.Selection;
using MiniPaint.Core.Clipboard;

namespace MiniPaint.Core.Tests;

public class SerializationTests
{
    [Fact]
    public void Project_RoundTrip_PreservesLayersAndProps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"minipaint_test_{Guid.NewGuid():N}.minipaint");
        try
        {
            var doc = PaintDocument.CreateBlank("RoundTrip", 16, 12, transparent: true);
            doc.Layers[0].Pixels.FillRect(0, 0, 8, 8, 255, 0, 0, 200);
            doc.Layers[0].Opacity = 0.75f;
            doc.SetLayerOffset(doc.Layers[0].Id, 1, 2);

            var overlay = new PixelBuffer(4, 4);
            overlay.FillRect(0, 0, 4, 4, 0, 255, 0, 255);
            var layer2 = new Layer("Overlay", overlay, 5, 5) { IsVisible = false };
            doc.AddLayer(layer2);

            ProjectSerializer.Save(doc, path);
            doc.Dispose();

            using var loaded = ProjectSerializer.Load(path);
            Assert.Equal(16, loaded.Width);
            Assert.Equal(12, loaded.Height);
            Assert.Equal(2, loaded.Layers.Count);
            Assert.Equal(0.75f, loaded.Layers[0].Opacity, 3);
            Assert.Equal(1, loaded.Layers[0].OffsetX);
            Assert.Equal(2, loaded.Layers[0].OffsetY);
            Assert.False(loaded.Layers[1].IsVisible);
            Assert.Equal("Overlay", loaded.Layers[1].Name);

            var px = loaded.Layers[0].Pixels.PixelsReadOnly;
            Assert.Equal(255, px[0]);
            Assert.Equal(200, px[3]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportPng_DoesNotDestroyDocumentStructure()
    {
        var path = Path.Combine(Path.GetTempPath(), $"minipaint_export_{Guid.NewGuid():N}.png");
        try
        {
            var doc = PaintDocument.CreateBlank("Export", 8, 8, transparent: true);
            doc.Layers[0].Pixels.FillRect(0, 0, 8, 8, 10, 20, 30, 255);
            var layerCount = doc.Layers.Count;
            ProjectSerializer.ExportPng(doc, path);
            Assert.Equal(layerCount, doc.Layers.Count);
            Assert.True(File.Exists(path));
            using var loaded = ImageCodec.Load(path);
            Assert.Equal(8, loaded.Width);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CopyPaste_CrossDocument_CreatesTransparentLayer()
    {
        using var mgr = new WorkspaceManager();
        var a = mgr.NewDocument("A", 10, 10);
        var b = mgr.NewDocument("B", 20, 20);
        a.Document.Layers[0].Pixels.FillRect(2, 2, 3, 3, 255, 128, 0, 255);
        a.Selection.Set(2, 2, 3, 3);
        a.Copy(mgr.Clipboard);

        mgr.Activate(b);
        var pasted = b.Paste(mgr.Clipboard);
        Assert.NotNull(pasted);
        Assert.Equal(2, b.Document.Layers.Count);
        Assert.Equal(2, pasted!.OffsetX);
        Assert.Equal(2, pasted.OffsetY);
        Assert.Equal(3, pasted.Pixels.Width);
        Assert.Equal(255, pasted.Pixels.PixelsReadOnly[0]);
    }

    [Fact]
    public void Selection_ClampToDocument()
    {
        var sel = new SelectionState();
        sel.Set(-5, -5, 20, 20);
        sel.ClampToDocument(10, 10);
        Assert.Equal(0, sel.X);
        Assert.Equal(0, sel.Y);
        Assert.Equal(10, sel.Width);
        Assert.Equal(10, sel.Height);
    }
}
