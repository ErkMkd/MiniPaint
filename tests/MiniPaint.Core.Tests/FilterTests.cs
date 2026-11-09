using MiniPaint.Core.Filters;
using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.Tests;

public class FilterTests
{
    private static PixelBuffer Pixel(byte r, byte g, byte b, byte a = 255)
    {
        var px = new PixelBuffer(1, 1);
        px.Clear(r, g, b, a);
        return px;
    }

    private static (int R, int G, int B, int A) Run(LayerFilter filter, byte r, byte g, byte b, byte a = 255)
    {
        using var px = Pixel(r, g, b, a);
        LayerFilter.ApplyAll([filter], px);
        var p = px.PixelsReadOnly;
        return (p[0], p[1], p[2], p[3]);
    }

    private static double Luma((int R, int G, int B, int A) c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    [Fact]
    public void BrightnessContrast_ZeroIsIdentity()
    {
        var curve = new BrightnessContrastFilter(0, 0).BuildCurve();
        for (int v = 0; v < 256; v++)
            Assert.Equal(v, curve[v]);
    }

    [Fact]
    public void Brightness_LiftsTheMidtones_KeepingBlackAndWhite()
    {
        var curve = new BrightnessContrastFilter(50, 0).BuildCurve();
        Assert.Equal(0, curve[0]);
        Assert.Equal(255, curve[255]);
        Assert.True(curve[128] > 170, $"{curve[128]}");

        var darker = new BrightnessContrastFilter(-50, 0).BuildCurve();
        Assert.True(darker[128] < 80, $"{darker[128]}");
    }

    [Fact]
    public void Contrast_SpreadsOrFlattensAroundGray()
    {
        var more = new BrightnessContrastFilter(0, 50).BuildCurve();
        Assert.True(more[64] < 64 && more[192] > 192);

        var less = new BrightnessContrastFilter(0, -100).BuildCurve();
        Assert.InRange(less[0], 110, 128);
        Assert.InRange(less[255], 128, 145);
    }

    [Fact]
    public void Hue_TurnsRedIntoGreen()
    {
        Assert.Equal((0, 255, 0, 255), Run(new HueSaturationFilter(120, 0, 0), 255, 0, 0));
        Assert.Equal((0, 0, 255, 255), Run(new HueSaturationFilter(-120, 0, 0), 255, 0, 0));
    }

    [Fact]
    public void Saturation_MinusHundredGivesGray_ValueMinusHundredGivesBlack()
    {
        var gray = Run(new HueSaturationFilter(0, -100, 0), 200, 100, 50);
        Assert.Equal(gray.R, gray.G);
        Assert.Equal(gray.G, gray.B);

        Assert.Equal((0, 0, 0, 255), Run(new HueSaturationFilter(0, 0, -100), 200, 100, 50));
    }

    [Fact]
    public void Saturation_LeavesGraysAlone()
    {
        Assert.Equal((128, 128, 128, 255), Run(new HueSaturationFilter(90, 100, 0), 128, 128, 128));
    }

    [Fact]
    public void Filters_KeepAlpha_AndSkipTransparentPixels()
    {
        Assert.Equal(77, Run(new HueSaturationFilter(120, 0, 0), 255, 0, 0, 77).A);
        Assert.Equal((255, 0, 0, 0), Run(new HueSaturationFilter(120, 0, 0), 255, 0, 0, 0));
    }

    [Fact]
    public void ColorBalance_MidtonesTowardRed_KeepsLuminosityWhenAsked()
    {
        var shift = new ToneShift(60, 0, 0);
        var kept = Run(new ColorBalanceFilter(default, shift, default, PreserveLuminosity: true), 128, 128, 128);
        var free = Run(new ColorBalanceFilter(default, shift, default, PreserveLuminosity: false), 128, 128, 128);

        Assert.True(kept.R > kept.G && kept.R > kept.B);
        Assert.InRange(Luma(kept), 125, 131);
        Assert.True(Luma(free) > Luma(kept));
    }

    [Fact]
    public void ColorBalance_ShadowsBarelyTouchHighlights()
    {
        var filter = new ColorBalanceFilter(new ToneShift(0, 0, 100), default, default, PreserveLuminosity: false);

        var dark = Run(filter, 20, 20, 20);
        var light = Run(filter, 235, 235, 235);

        Assert.True(dark.B - dark.R > 80, $"{dark}");
        Assert.True(light.B - light.R < 5, $"{light}");
    }

    [Fact]
    public void DisabledOrNeutralFilters_ChangeNothing()
    {
        Assert.Equal((200, 100, 50, 255), Run(new HueSaturationFilter(120, 0, 0, Enabled: false), 200, 100, 50));
        Assert.Equal((200, 100, 50, 255), Run(new ColorBalanceFilter(default, default, default), 200, 100, 50));
        Assert.False(LayerFilter.AnyActive([new BrightnessContrastFilter(0, 0), new HueSaturationFilter(360, 0, 0)]));
    }
}
