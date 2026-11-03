namespace MiniPaint.Core.Composition;

/// <summary>
/// Channel blend functions + alpha-aware compositing (Photoshop/PDF style).
/// Colors are 0..1; results clamped.
/// </summary>
public static class BlendOps
{
    public static void BlendPixel(
        BlendMode mode,
        byte sr, byte sg, byte sb, byte sa,
        ref byte dr, ref byte dg, ref byte db, ref byte da,
        float opacity)
    {
        opacity = Math.Clamp(opacity, 0f, 1f);
        if (opacity <= 0f || sa == 0) return;

        // Dissolve: approximate with Normal (true dissolve needs dither/noise).
        if (mode == BlendMode.Dissolve)
            mode = BlendMode.Normal;

        float as_ = (sa / 255f) * opacity;
        float ab = da / 255f;
        float ao = as_ + ab * (1f - as_);
        if (ao <= 1e-6f)
        {
            dr = dg = db = da = 0;
            return;
        }

        float csR = sr / 255f, csG = sg / 255f, csB = sb / 255f;
        float cbR = dr / 255f, cbG = dg / 255f, cbB = db / 255f;

        float bR, bG, bB;
        if (mode is BlendMode.Hue or BlendMode.Saturation or BlendMode.Color or BlendMode.Luminosity)
        {
            (bR, bG, bB) = NonSeparable(mode, cbR, cbG, cbB, csR, csG, csB);
        }
        else
        {
            bR = Separable(mode, cbR, csR);
            bG = Separable(mode, cbG, csG);
            bB = Separable(mode, cbB, csB);
        }

        // co = ((1-as)*ab*Cb + (1-ab)*as*Cs + as*ab*B) / ao
        float coR = ((1f - as_) * ab * cbR + (1f - ab) * as_ * csR + as_ * ab * bR) / ao;
        float coG = ((1f - as_) * ab * cbG + (1f - ab) * as_ * csG + as_ * ab * bG) / ao;
        float coB = ((1f - as_) * ab * cbB + (1f - ab) * as_ * csB + as_ * ab * bB) / ao;

        dr = ToByte(coR);
        dg = ToByte(coG);
        db = ToByte(coB);
        da = ToByte(ao);
    }

    public static float Separable(BlendMode mode, float cb, float cs) => mode switch
    {
        BlendMode.Normal => cs,
        BlendMode.Darken => Math.Min(cb, cs),
        BlendMode.Multiply => cb * cs,
        BlendMode.ColorBurn => ColorBurn(cb, cs),
        BlendMode.LinearBurn => Clamp01(cb + cs - 1f),
        BlendMode.Lighten => Math.Max(cb, cs),
        BlendMode.Screen => 1f - (1f - cb) * (1f - cs),
        BlendMode.ColorDodge => ColorDodge(cb, cs),
        BlendMode.LinearDodge => Clamp01(cb + cs),
        BlendMode.Overlay => Overlay(cb, cs),
        BlendMode.SoftLight => SoftLight(cb, cs),
        BlendMode.HardLight => Overlay(cs, cb),
        BlendMode.VividLight => VividLight(cb, cs),
        BlendMode.LinearLight => Clamp01(cb + 2f * cs - 1f),
        BlendMode.PinLight => PinLight(cb, cs),
        BlendMode.HardMix => (cb + cs) < 1f ? 0f : 1f,
        BlendMode.Difference => Math.Abs(cb - cs),
        BlendMode.Exclusion => Clamp01(cb + cs - 2f * cb * cs),
        _ => cs
    };

    private static float ColorBurn(float cb, float cs)
    {
        if (cs <= 0f) return 0f;
        if (cb >= 1f) return 1f;
        return Clamp01(1f - (1f - cb) / cs);
    }

    private static float ColorDodge(float cb, float cs)
    {
        if (cs >= 1f) return 1f;
        if (cb <= 0f) return 0f;
        return Clamp01(cb / (1f - cs));
    }

    private static float Overlay(float cb, float cs) =>
        cb <= 0.5f ? Clamp01(2f * cb * cs) : Clamp01(1f - 2f * (1f - cb) * (1f - cs));

    private static float SoftLight(float cb, float cs)
    {
        if (cs <= 0.5f)
            return Clamp01(cb - (1f - 2f * cs) * cb * (1f - cb));
        float d = cb <= 0.25f
            ? ((16f * cb - 12f) * cb + 4f) * cb
            : MathF.Sqrt(cb);
        return Clamp01(cb + (2f * cs - 1f) * (d - cb));
    }

    private static float VividLight(float cb, float cs) =>
        cs <= 0.5f ? ColorBurn(cb, Math.Max(2f * cs, 1e-6f)) : ColorDodge(cb, 2f * cs - 1f);

    private static float PinLight(float cb, float cs) =>
        cs <= 0.5f ? Math.Min(cb, 2f * cs) : Math.Max(cb, 2f * cs - 1f);

    private static (float r, float g, float b) NonSeparable(
        BlendMode mode, float cbR, float cbG, float cbB, float csR, float csG, float csB)
    {
        return mode switch
        {
            BlendMode.Hue => ApplyLum(SetSat(csR, csG, csB, Sat(cbR, cbG, cbB)), Lum(cbR, cbG, cbB)),
            BlendMode.Saturation => ApplyLum(SetSat(cbR, cbG, cbB, Sat(csR, csG, csB)), Lum(cbR, cbG, cbB)),
            BlendMode.Color => SetLum(csR, csG, csB, Lum(cbR, cbG, cbB)),
            BlendMode.Luminosity => SetLum(cbR, cbG, cbB, Lum(csR, csG, csB)),
            _ => (csR, csG, csB)
        };
    }

    private static (float r, float g, float b) ApplyLum((float r, float g, float b) c, float l) =>
        SetLum(c.r, c.g, c.b, l);

    private static float Lum(float r, float g, float b) => 0.3f * r + 0.59f * g + 0.11f * b;

    private static float Sat(float r, float g, float b) =>
        Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));

    private static (float r, float g, float b) SetLum(float r, float g, float b, float l)
    {
        float d = l - Lum(r, g, b);
        return ClipColor(r + d, g + d, b + d);
    }

    private static (float r, float g, float b) SetSat(float r, float g, float b, float s)
    {
        float[] v = [r, g, b];
        int iMin = 0, iMid = 1, iMax = 2;
        if (v[iMin] > v[iMid]) (iMin, iMid) = (iMid, iMin);
        if (v[iMid] > v[iMax]) (iMid, iMax) = (iMax, iMid);
        if (v[iMin] > v[iMid]) (iMin, iMid) = (iMid, iMin);

        if (v[iMax] > v[iMin])
        {
            v[iMid] = ((v[iMid] - v[iMin]) * s) / (v[iMax] - v[iMin]);
            v[iMax] = s;
        }
        else
        {
            v[iMid] = 0f;
            v[iMax] = 0f;
        }
        v[iMin] = 0f;
        return (v[0], v[1], v[2]);
    }

    private static (float r, float g, float b) ClipColor(float r, float g, float b)
    {
        float l = Lum(r, g, b);
        float n = Math.Min(r, Math.Min(g, b));
        float x = Math.Max(r, Math.Max(g, b));
        if (n < 0f)
        {
            r = l + ((r - l) * l) / (l - n);
            g = l + ((g - l) * l) / (l - n);
            b = l + ((b - l) * l) / (l - n);
        }
        if (x > 1f)
        {
            r = l + ((r - l) * (1f - l)) / (x - l);
            g = l + ((g - l) * (1f - l)) / (x - l);
            b = l + ((b - l) * (1f - l)) / (x - l);
        }
        return (Clamp01(r), Clamp01(g), Clamp01(b));
    }

    private static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);
    private static byte ToByte(float v) => (byte)Math.Clamp((int)Math.Round(v * 255f), 0, 255);
}
