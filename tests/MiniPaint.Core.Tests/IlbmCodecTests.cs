using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Document;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class IlbmCodecTests
{
    [Fact]
    public void CanExport_OnlyIndexedFamily()
    {
        var doc = PaintDocument.CreateBlank("t", 8, 8);
        Assert.False(IlbmCodec.CanExport(doc));
        doc.ColorSettings.Mode = ColorModeKind.Aga;
        doc.ColorSettings.ColorCount = 16;
        doc.ColorSettings.Sanitize();
        Assert.True(IlbmCodec.CanExport(doc));
    }

    [Fact]
    public void RoundTrip_IndexedIlbm_PreservesSizeAndOpaquePixels()
    {
        var doc = PaintDocument.CreateBlank("ilbm", 32, 24, transparent: true);
        doc.ActiveLayer!.Pixels.FillRect(4, 4, 16, 12, 255, 0, 0, 255);
        doc.ActiveLayer.Pixels.FillRect(10, 8, 8, 8, 0, 0, 255, 255);
        doc.ColorSettings.Mode = ColorModeKind.Indexed;
        doc.ColorSettings.ColorCount = 16;
        doc.ColorSettings.Quantizer = QuantizerKind.MedianCut;
        doc.ColorSettings.Dither = DitherKind.None;
        doc.ColorSettings.Sanitize();

        var path = Path.Combine(Path.GetTempPath(), $"minipaint_{Guid.NewGuid():N}.iff");
        try
        {
            IlbmCodec.Save(doc, path);
            using var loaded = IlbmCodec.LoadDocument(path);
            Assert.Equal(32, loaded.Width);
            Assert.Equal(24, loaded.Height);
            Assert.NotEqual(ColorModeKind.Rgba, loaded.ColorSettings.Mode);

            var dst = loaded.ActiveLayer!.Pixels.PixelsReadOnly.ToArray();
            bool hasRed = false, hasBlue = false;
            for (int i = 0; i < dst.Length; i += 4)
            {
                if (dst[i + 3] != 255) continue;
                if (dst[i] > 200 && dst[i + 1] < 40) hasRed = true;
                if (dst[i + 2] > 200 && dst[i] < 40) hasBlue = true;
            }
            Assert.True(hasRed);
            Assert.True(hasBlue);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_Rgba_Throws()
    {
        var doc = PaintDocument.CreateBlank("t", 4, 4);
        var path = Path.Combine(Path.GetTempPath(), $"minipaint_{Guid.NewGuid():N}.iff");
        Assert.Throws<InvalidOperationException>(() => IlbmCodec.Save(doc, path));
    }
}
