using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;

namespace MiniPaint.Core.Tests;

public class BatchExportTests
{
    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "out");

    [Fact]
    public void PlanPaths_UsesFormatExtension()
    {
        var png = BatchExport.PlanPaths(["cat"], Folder, ExportFormat.Png);
        var jpg = BatchExport.PlanPaths(["cat"], Folder, ExportFormat.Jpeg);

        Assert.Equal(Path.Combine(Folder, "cat.png"), png[0]);
        Assert.Equal(Path.Combine(Folder, "cat.jpg"), jpg[0]);
    }

    [Fact]
    public void PlanPaths_DeduplicatesNamesWithinBatch()
    {
        var paths = BatchExport.PlanPaths(["photo", "Photo", "photo", "other"], Folder, ExportFormat.Jpeg);

        Assert.Equal(
        [
            Path.Combine(Folder, "photo.jpg"),
            Path.Combine(Folder, "Photo (2).jpg"),
            Path.Combine(Folder, "photo (3).jpg"),
            Path.Combine(Folder, "other.jpg")
        ], paths);
    }

    [Fact]
    public void PlanPaths_SkipsExistingFilesWhenAsked()
    {
        var onDisk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(Folder, "a.png"),
            Path.Combine(Folder, "a (2).png")
        };

        var keep = BatchExport.PlanPaths(["a"], Folder, ExportFormat.Png, onDisk.Contains);
        var overwrite = BatchExport.PlanPaths(["a"], Folder, ExportFormat.Png);

        Assert.Equal(Path.Combine(Folder, "a (3).png"), keep[0]);
        Assert.Equal(Path.Combine(Folder, "a.png"), overwrite[0]);
    }

    [Theory]
    [InlineData("a/b:c*d?", "a_b_c_d_")]
    [InlineData("  name.  ", "name")]
    [InlineData("", "Untitled")]
    [InlineData("...", "Untitled")]
    public void SanitizeFileName_ProducesValidStem(string input, string expected)
    {
        Assert.Equal(expected, BatchExport.SanitizeFileName(input));
    }

    [Fact]
    public void Export_WritesEveryFormat()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"minipaint_batch_{Guid.NewGuid():N}");
        try
        {
            using var doc = PaintDocument.CreateBlank("Batch", 12, 10, transparent: false);
            var paths = BatchExport.PlanPaths([doc.Name, doc.Name], dir, ExportFormat.Png);

            BatchExport.Export(doc, paths[0], ExportFormat.Png, jpegQuality: 90);
            BatchExport.Export(doc, Path.ChangeExtension(paths[1], ".jpg"), ExportFormat.Jpeg, jpegQuality: 40);

            using var png = ImageCodec.Load(Path.Combine(dir, "Batch.png"));
            using var jpg = ImageCodec.Load(Path.Combine(dir, "Batch (2).jpg"));
            Assert.Equal((12, 10), (png.Width, png.Height));
            Assert.Equal((12, 10), (jpg.Width, jpg.Height));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
