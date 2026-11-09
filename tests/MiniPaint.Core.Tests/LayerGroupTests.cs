using System.IO.Compression;
using MiniPaint.Core.Composition;
using MiniPaint.Core.Document;
using MiniPaint.Core.Filters;
using MiniPaint.Core.History;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;

namespace MiniPaint.Core.Tests;

public class LayerGroupTests
{
    private static Layer Solid(string name, int w, int h, byte r, byte g, byte b, byte a = 255)
    {
        var layer = new Layer(name, w, h);
        layer.Pixels.Clear(r, g, b, a);
        return layer;
    }

    private static (int R, int G, int B, int A) At(PixelBuffer px, int x, int y)
    {
        var p = px.PixelsReadOnly;
        int i = px.Offset(x, y);
        return (p[i], p[i + 1], p[i + 2], p[i + 3]);
    }

    /// <summary>Names of the top-level entries, groups as "Name[child,child]", bottom → top.</summary>
    private static string Describe(LayerGroup group) =>
        string.Join(",", group.Children.Select(n => n is LayerGroup g ? $"{g.Name}[{Describe(g)}]" : n.Name));

    [Fact]
    public void Group_OpacityAppliesToItsContent()
    {
        using var doc = new PaintDocument("t", 1, 1);
        doc.AddLayer(Solid("red", 1, 1, 255, 0, 0));
        var group = new LayerGroup("G") { Opacity = 0.5f };
        doc.AddNode(group);
        doc.AddNode(Solid("blue", 1, 1, 0, 0, 255), group);

        using var composite = CompositionEngine.Composite(doc);

        var (r, _, b, a) = At(composite, 0, 0);
        Assert.InRange(r, 120, 135);
        Assert.InRange(b, 120, 135);
        Assert.Equal(255, a);
    }

    [Fact]
    public void GroupFilter_ChangesOnlyTheGroupContent()
    {
        using var doc = new PaintDocument("t", 2, 1);
        doc.AddLayer(Solid("blue", 2, 1, 0, 0, 255));
        var group = new LayerGroup("G") { Filters = [new HueSaturationFilter(0, -100, 0)] };
        doc.AddNode(group);
        doc.AddNode(Solid("red", 1, 1, 255, 0, 0), group); // covers the left pixel only

        using var composite = CompositionEngine.Composite(doc);

        var left = At(composite, 0, 0);
        Assert.Equal(left.R, left.G);          // the red pixel went gray
        Assert.Equal(left.G, left.B);
        Assert.Equal((0, 0, 255, 255), At(composite, 1, 0)); // the blue layer below is untouched
    }

    [Fact]
    public void LayerFilter_ChangesOnlyItsLayer_AndCanBeSwitchedOff()
    {
        using var doc = new PaintDocument("t", 2, 1);
        doc.AddLayer(Solid("bottom", 2, 1, 0, 0, 255));
        var top = Solid("top", 1, 1, 255, 0, 0);
        top.Filters = [new HueSaturationFilter(120, 0, 0)]; // red → green
        doc.AddLayer(top);

        using (var composite = CompositionEngine.Composite(doc))
        {
            Assert.Equal((0, 255, 0, 255), At(composite, 0, 0));
            Assert.Equal((0, 0, 255, 255), At(composite, 1, 0));
        }

        top.Filters = [top.Filters[0] with { Enabled = false }];
        using var off = CompositionEngine.Composite(doc);
        Assert.Equal((255, 0, 0, 255), At(off, 0, 0));
    }

    [Fact]
    public void Group_IsComposedOnItsOwn_BeforeBlendingWithWhatIsBelow()
    {
        using var doc = new PaintDocument("t", 1, 1);
        doc.AddLayer(Solid("red", 1, 1, 255, 0, 0));
        var group = new LayerGroup("G");
        doc.AddNode(group);
        doc.AddNode(new Layer("multiply", new PixelBuffer(1, 1)) { BlendMode = BlendMode.Multiply }, group);
        ((Layer)group.Children[0]).Pixels.Clear(0, 0, 255, 255);

        using var composite = CompositionEngine.Composite(doc);

        // Isolated: the Multiply layer only meets the group's (empty) content, so blue shows.
        Assert.Equal((0, 0, 255, 255), At(composite, 0, 0));
    }

    [Fact]
    public void MoveUpAndDown_GoInAndOutOfOpenGroups_AndPastClosedOnes()
    {
        using var ws = new DocumentWorkspace(new PaintDocument("t", 1, 1));
        var doc = ws.Document;
        var a = doc.AddLayer(Solid("A", 1, 1, 0, 0, 0));
        var g = new LayerGroup("G");
        doc.AddNode(g);
        var b = Solid("B", 1, 1, 0, 0, 0);
        doc.AddNode(b, g);
        var c = doc.AddLayer(Solid("C", 1, 1, 0, 0, 0));
        Assert.Equal("A,G[B],C", Describe(doc.Root));

        Assert.True(ws.MoveNode(a, up: true));  // into the open group, at its bottom
        Assert.Equal("G[A,B],C", Describe(doc.Root));
        Assert.True(ws.MoveNode(b, up: true));  // already on top of the group: out, just above it
        Assert.Equal("G[A],B,C", Describe(doc.Root));
        Assert.True(ws.MoveNode(c, up: false)); // swap with B
        Assert.Equal("G[A],C,B", Describe(doc.Root));
        Assert.True(ws.MoveNode(c, up: false)); // into the open group, on top
        Assert.Equal("G[A,C],B", Describe(doc.Root));

        g.IsExpanded = false;
        Assert.True(ws.MoveNode(b, up: false)); // a closed group is passed as one entry
        Assert.Equal("B,G[A,C]", Describe(doc.Root));
        Assert.False(ws.MoveNode(b, up: false)); // bottom of the stack

        ws.History.Undo();
        ws.History.Undo();
        Assert.Equal("G[A],C,B", Describe(doc.Root));
    }

    [Fact]
    public void AddLayer_GoesAboveTheActiveLayer_OrInsideTheActiveGroup()
    {
        using var ws = new DocumentWorkspace(new PaintDocument("t", 1, 1));
        var doc = ws.Document;
        var a = doc.AddLayer(Solid("A", 1, 1, 0, 0, 0));
        var g = new LayerGroup("G");
        doc.AddNode(g);
        doc.AddNode(Solid("B", 1, 1, 0, 0, 0), g);

        doc.ActiveLayerId = g.Id;
        ws.AddLayerAboveActive("In");
        doc.ActiveLayerId = a.Id;
        ws.AddLayerAboveActive("Above A");

        Assert.Equal("A,Above A,G[B,In]", Describe(doc.Root));
        ws.History.Undo();
        Assert.Equal("A,G[B,In]", Describe(doc.Root));
    }

    [Fact]
    public void GroupLayer_AndUngroup_AreUndoable()
    {
        using var ws = new DocumentWorkspace(new PaintDocument("t", 1, 1));
        var doc = ws.Document;
        doc.AddLayer(Solid("A", 1, 1, 0, 0, 0));
        var b = doc.AddLayer(Solid("B", 1, 1, 0, 0, 0));

        var group = ws.GroupNode(b, "G")!;
        Assert.Equal("A,G[B]", Describe(doc.Root));
        Assert.Same(group, doc.ActiveNode);

        ws.History.Undo();
        Assert.Equal("A,B", Describe(doc.Root));
        Assert.Same(b, doc.ActiveNode);
        ws.History.Redo();
        Assert.Equal("A,G[B]", Describe(doc.Root));

        Assert.True(ws.Ungroup(group));
        Assert.Equal("A,B", Describe(doc.Root));
        ws.History.Undo();
        Assert.Equal("A,G[B]", Describe(doc.Root));
        Assert.Same(group, b.Parent);
    }

    [Fact]
    public void DeleteGroup_TakesItsContent_AndUndoBringsTheSameEntriesBack()
    {
        using var ws = new DocumentWorkspace(new PaintDocument("t", 1, 1));
        var doc = ws.Document;
        doc.AddLayer(Solid("A", 1, 1, 0, 0, 0));
        var g = new LayerGroup("G");
        doc.AddNode(g);
        var b = Solid("B", 1, 1, 0, 0, 0);
        doc.AddNode(b, g);

        Assert.True(ws.DeleteNode(g));
        Assert.Single(doc.Layers);

        ws.History.Undo();
        Assert.Equal("A,G[B]", Describe(doc.Root));
        Assert.Same(b, doc.Layers[1]);
    }

    [Fact]
    public void DeleteNode_KeepsTheLastLayer()
    {
        using var ws = new DocumentWorkspace(new PaintDocument("t", 1, 1));
        var g = new LayerGroup("G");
        ws.Document.AddNode(g);
        ws.Document.AddNode(Solid("A", 1, 1, 0, 0, 0), g);

        Assert.False(ws.DeleteNode(g));
        Assert.False(ws.DeleteNode(ws.Document.Layers[0]));
    }

    [Fact]
    public void DuplicateGroup_CopiesItsContent()
    {
        using var ws = new DocumentWorkspace(new PaintDocument("t", 1, 1));
        var g = new LayerGroup("G") { Filters = [new BrightnessContrastFilter(10, 0)] };
        ws.Document.AddNode(g);
        var a = Solid("A", 1, 1, 1, 2, 3);
        ws.Document.AddNode(a, g);

        var copy = (LayerGroup)ws.DuplicateNode(g)!;

        Assert.Equal("G[A],G copy[A]", Describe(ws.Document.Root));
        Assert.NotSame(a, copy.Children[0]);
        Assert.Equal(g.Filters, copy.Filters);
        ws.History.Undo();
        Assert.Equal("G[A]", Describe(ws.Document.Root));
    }

    [Fact]
    public void CompositeThrough_InsideAGroup_StopsAtTheLayer()
    {
        using var doc = new PaintDocument("t", 4, 1);
        doc.AddLayer(Solid("A", 1, 1, 255, 0, 0));
        var g = new LayerGroup("G");
        doc.AddNode(g);
        var b = new Layer("B", Solid("tmp", 1, 1, 0, 255, 0).Pixels, 1, 0);
        doc.AddNode(b, g);
        doc.AddNode(new Layer("C", Solid("tmp", 1, 1, 0, 0, 255).Pixels, 2, 0), g);
        doc.AddLayer(new Layer("D", Solid("tmp", 1, 1, 9, 9, 9).Pixels, 3, 0));

        using var through = CompositionEngine.CompositeThrough(doc, b);

        Assert.Equal(255, At(through, 0, 0).A); // A
        Assert.Equal(255, At(through, 1, 0).A); // B
        Assert.Equal(0, At(through, 2, 0).A);   // C (above B in the group): left out
        Assert.Equal(0, At(through, 3, 0).A);   // D (above the group): left out
    }

    [Fact]
    public void NodeFiltersCommand_UndoesAndRedoes()
    {
        using var ws = new DocumentWorkspace(PaintDocument.CreateBlank("t", 1, 1));
        var layer = ws.Document.Layers[0];
        LayerFilter[] filters = [new BrightnessContrastFilter(20, 0)];

        ws.SetFilters(layer, filters, "Brightness/Contrast");
        Assert.Equal(filters, layer.Filters);
        ws.History.Undo();
        Assert.Empty(layer.Filters);
        ws.History.Redo();
        Assert.Equal(filters, layer.Filters);
    }

    [Fact]
    public void Project_RoundTrip_KeepsGroupsFiltersAndTheActiveEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MiniPaintTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var doc = new PaintDocument("t", 2, 2);
            doc.AddLayer(Solid("A", 2, 2, 10, 20, 30));
            var g = new LayerGroup("G")
            {
                Opacity = 0.5f,
                BlendMode = BlendMode.Screen,
                IsExpanded = false,
                Filters = [new ColorBalanceFilter(new ToneShift(1, 2, 3), default, new ToneShift(-4, 0, 5), PreserveLuminosity: false)]
            };
            doc.AddNode(g);
            var inner = new LayerGroup("Inner");
            doc.AddNode(inner, g);
            var b = Solid("B", 2, 2, 40, 50, 60);
            b.Filters = [new HueSaturationFilter(30, -20, 10, Enabled: false), new BrightnessContrastFilter(5, -5)];
            b.SetMask([0, 85, 170, 255]);
            doc.AddNode(b, inner);
            doc.ActiveLayerId = inner.Id;

            var path = Path.Combine(dir, "tree.minipaint");
            ProjectSerializer.Save(doc, path);
            using (var zip = ZipFile.OpenRead(path))
            using (var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open()))
                Assert.Contains("\"version\": 3", reader.ReadToEnd());

            using var loaded = ProjectSerializer.Load(path);
            Assert.Equal("A,G[Inner[B]]", Describe(loaded.Root));
            var lg = (LayerGroup)loaded.Root.Children[1];
            Assert.Equal(0.5f, lg.Opacity);
            Assert.Equal(BlendMode.Screen, lg.BlendMode);
            Assert.False(lg.IsExpanded);
            Assert.Equal(g.Filters, lg.Filters);
            var lb = loaded.Layers[1];
            Assert.Equal(b.Filters, lb.Filters);
            Assert.Equal(new byte[] { 0, 85, 170, 255 }, lb.Mask);
            Assert.Equal("Inner", loaded.ActiveNode!.Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ImageSize_ResizesLayersInsideGroups()
    {
        using var ws = new DocumentWorkspace(new PaintDocument("t", 10, 10));
        ws.Document.AddLayer(Solid("A", 10, 10, 0, 0, 0));
        var g = new LayerGroup("G");
        ws.Document.AddNode(g);
        var b = Solid("B", 10, 10, 0, 0, 0);
        ws.Document.AddNode(b, g);

        ws.History.Execute(DocumentResizeCommand.ImageSize(ws.Document, 20, 20));

        Assert.Equal(20, b.Pixels.Width);
    }
}
