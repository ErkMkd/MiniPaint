using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;
using MiniPaint.Core.Project;

namespace MiniPaint.Core.Tests;

public class JpegExportTests
{
    [Fact]
    public void SaveJpeg_RoundTrip_KeepsSizeAndApproximateColor()
    {
        using var buffer = new PixelBuffer(32, 24);
        buffer.FillRect(0, 0, 32, 24, 200, 60, 30, 255);

        var bytes = ImageCodec.EncodeJpegBytes(buffer, 95);
        using var ms = new MemoryStream(bytes);
        using var decoded = ImageCodec.Load(ms);

        Assert.Equal(32, decoded.Width);
        Assert.Equal(24, decoded.Height);
        var px = decoded.PixelsReadOnly;
        int center = (12 * 32 + 16) * 4;
        Assert.InRange(px[center], 192, 208);
        Assert.InRange(px[center + 1], 52, 68);
        Assert.InRange(px[center + 2], 22, 38);
        Assert.Equal(255, px[center + 3]);
    }

    [Fact]
    public void SaveJpeg_FlattensTransparencyOntoWhite()
    {
        using var buffer = new PixelBuffer(16, 16); // fully transparent

        using var ms = new MemoryStream(ImageCodec.EncodeJpegBytes(buffer, 90));
        using var decoded = ImageCodec.Load(ms);

        var px = decoded.PixelsReadOnly;
        for (int i = 0; i < px.Length; i += 4)
        {
            Assert.True(px[i] >= 250 && px[i + 1] >= 250 && px[i + 2] >= 250,
                $"Pixel {i / 4} not white: {px[i]},{px[i + 1]},{px[i + 2]}");
            Assert.Equal(255, px[i + 3]);
        }
    }

    [Fact]
    public void SaveJpeg_LowerQuality_ProducesSmallerFile()
    {
        using var buffer = new PixelBuffer(128, 128);
        var rng = new Random(1234);
        rng.NextBytes(buffer.Pixels);
        var px = buffer.Pixels;
        for (int i = 3; i < px.Length; i += 4)
            px[i] = 255;

        int high = ImageCodec.EncodeJpegBytes(buffer, 95).Length;
        int low = ImageCodec.EncodeJpegBytes(buffer, 20).Length;

        Assert.True(low < high, $"quality 20 = {low} bytes, quality 95 = {high} bytes");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(150)]
    public void SaveJpeg_OutOfRangeQuality_IsClamped(int quality)
    {
        using var buffer = new PixelBuffer(8, 8);
        buffer.FillRect(0, 0, 8, 8, 10, 20, 30, 255);

        var bytes = ImageCodec.EncodeJpegBytes(buffer, quality);

        Assert.True(bytes.Length > 0);
    }

    [Fact]
    public void ExportJpeg_WritesDecodableMergedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"minipaint_test_{Guid.NewGuid():N}.jpg");
        try
        {
            using var doc = PaintDocument.CreateBlank("Jpeg", 40, 30, transparent: false);
            doc.Layers[0].Pixels.FillRect(0, 0, 40, 30, 0, 0, 255, 255);

            ProjectSerializer.ExportJpeg(doc, path, 80);

            using var loaded = ImageCodec.Load(path);
            Assert.Equal(40, loaded.Width);
            Assert.Equal(30, loaded.Height);
            Assert.Single(doc.Layers); // export does not touch the document
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
