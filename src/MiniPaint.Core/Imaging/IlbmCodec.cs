using System.Buffers.Binary;
using System.Text;
using MiniPaint.Core.ColorMode;
using MiniPaint.Core.Document;

namespace MiniPaint.Core.Imaging;

/// <summary>
/// Amiga IFF-ILBM reader/writer (indexed bitplanes, ByteRun1, Extra Half-Brite).
/// Export is only valid for Indexed / ECS / AGA document color modes.
/// </summary>
public static class IlbmCodec
{
    public const uint CamgExtraHalfBrite = 0x80;
    public const uint CamgHires = 0x8000;
    public const uint CamgLace = 0x4;
    public const uint CamgHam = 0x800;

    public sealed class IlbmImage
    {
        public required PixelBuffer Pixels { get; init; }
        public required PaletteRgb[] Palette { get; init; }
        public bool ExtraHalfBrite { get; init; }
        public int Bitplanes { get; init; }
        public uint Camg { get; init; }
    }

    public static bool CanExport(PaintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ColorModePipeline.IsIlbmCompatible(document.ColorSettings);
    }

    public static IlbmImage Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    public static IlbmImage Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        return Parse(data);
    }

    public static PaintDocument LoadDocument(string path)
    {
        var img = Load(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var doc = new PaintDocument(name, img.Pixels.Width, img.Pixels.Height) { FilePath = path };
        doc.AddLayer(new Layer("Background", img.Pixels));
        ApplyLoadedMode(doc, img);
        doc.IsDirty = false;
        return doc;
    }

    public static void Save(PaintDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!CanExport(document))
            throw new InvalidOperationException(
                "IFF-ILBM export requires Image Mode Indexed, ECS, or AGA (not RGBA).");

        using var composite = Composition.CompositionEngine.Composite(document);
        var (indices, palette) = ColorModePipeline.QuantizeToIndexed(composite, document.ColorSettings);
        var bytes = Encode(document.Width, document.Height, indices, palette, document.ColorSettings);
        File.WriteAllBytes(path, bytes);
    }

    private static void ApplyLoadedMode(PaintDocument doc, IlbmImage img)
    {
        int colors = Math.Max(2, img.Palette.Length);
        if (img.ExtraHalfBrite || (img.Camg & CamgExtraHalfBrite) != 0)
        {
            doc.ColorSettings.Mode = ColorModeKind.Ecs;
            doc.ColorSettings.ColorCount = 64;
            doc.ColorSettings.HalfBright = true;
        }
        else if (img.Bitplanes <= 5 && colors <= 32)
        {
            // Likely ECS-range; keep as ECS if palette snaps to 12-bit, else Indexed.
            bool ecsPalette = img.Palette.All(c =>
                AmigaColorSpace.SnapColor(c, 4).Equals(c));
            if (ecsPalette)
            {
                doc.ColorSettings.Mode = ColorModeKind.Ecs;
                doc.ColorSettings.ColorCount = DocumentColorSettings.NormalizePowerOfTwo(colors, 2, 32);
            }
            else
            {
                doc.ColorSettings.Mode = ColorModeKind.Indexed;
                doc.ColorSettings.ColorCount = Math.Clamp(colors, 2, 256);
            }
        }
        else if (colors <= 256)
        {
            bool power = colors > 0 && (colors & (colors - 1)) == 0;
            doc.ColorSettings.Mode = power ? ColorModeKind.Aga : ColorModeKind.Indexed;
            doc.ColorSettings.ColorCount = power
                ? DocumentColorSettings.NormalizePowerOfTwo(colors, 2, 256)
                : Math.Clamp(colors, 2, 256);
        }

        doc.ColorSettings.Quantizer = QuantizerKind.MedianCut;
        doc.ColorSettings.Dither = DitherKind.None;
        doc.ColorSettings.Sanitize();

        // Store editable unique palette (EHB mirrors are derived, not stored).
        var unique = img.ExtraHalfBrite && img.Palette.Length >= 64
            ? img.Palette.Take(32).ToArray()
            : img.Palette;
        if (unique.Length > 0)
            doc.ColorSettings.SetPalette(unique, customized: true);
    }

    private static IlbmImage Parse(byte[] data)
    {
        if (data.Length < 12 || ReadId(data, 0) != "FORM" || ReadId(data, 8) != "ILBM")
            throw new InvalidOperationException("Not an IFF-ILBM file.");

        int pos = 12;
        ushort width = 0, height = 0;
        byte nPlanes = 0, compression = 0, masking = 0;
        ushort transparent = 0;
        uint camg = 0;
        byte[]? cmap = null;
        byte[]? body = null;

        while (pos + 8 <= data.Length)
        {
            string id = ReadId(data, pos);
            int size = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos + 4));
            if (size < 0) throw new InvalidOperationException("Invalid IFF chunk size.");
            int dataStart = pos + 8;
            int dataEnd = dataStart + size;
            if (dataEnd > data.Length) throw new InvalidOperationException("Truncated IFF chunk.");

            switch (id)
            {
                case "BMHD":
                    if (size < 20) throw new InvalidOperationException("Invalid BMHD.");
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(dataStart));
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(dataStart + 2));
                    nPlanes = data[dataStart + 8];
                    masking = data[dataStart + 9];
                    compression = data[dataStart + 10];
                    transparent = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(dataStart + 12));
                    break;
                case "CAMG":
                    if (size >= 4)
                        camg = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(dataStart));
                    break;
                case "CMAP":
                    cmap = data[dataStart..dataEnd];
                    break;
                case "BODY":
                    body = data[dataStart..dataEnd];
                    break;
            }

            pos = dataEnd + (size & 1); // word align
        }

        if (width == 0 || height == 0 || nPlanes == 0 || body is null)
            throw new InvalidOperationException("Incomplete ILBM (missing BMHD/BODY).");
        if ((camg & CamgHam) != 0)
            throw new InvalidOperationException("HAM ILBM is not supported yet.");
        if (nPlanes > 8)
            throw new InvalidOperationException($"Unsupported bitplane count ({nPlanes}).");

        bool ehb = (camg & CamgExtraHalfBrite) != 0;
        var palette = BuildPaletteFromCmap(cmap, nPlanes, ehb);

        byte[] planar = compression switch
        {
            0 => body,
            1 => DecompressByteRun1(body, RowBytes(width) * height * (nPlanes + (masking == 1 ? 1 : 0))),
            _ => throw new InvalidOperationException($"Unsupported ILBM compression {compression}.")
        };

        var pixels = PlanarToRgba(width, height, nPlanes, masking, transparent, planar, palette);
        return new IlbmImage
        {
            Pixels = pixels,
            Palette = palette,
            ExtraHalfBrite = ehb,
            Bitplanes = nPlanes,
            Camg = camg
        };
    }

    private static PaletteRgb[] BuildPaletteFromCmap(byte[]? cmap, int nPlanes, bool ehb)
    {
        int maxColors = 1 << Math.Min(nPlanes, 8);
        var list = new List<PaletteRgb>(maxColors);
        if (cmap is not null)
        {
            int n = cmap.Length / 3;
            for (int i = 0; i < n; i++)
                list.Add(new PaletteRgb(cmap[i * 3], cmap[i * 3 + 1], cmap[i * 3 + 2]));
        }

        while (list.Count < Math.Min(maxColors, ehb ? 32 : maxColors))
            list.Add(new PaletteRgb(0, 0, 0));

        if (ehb)
        {
            // CMAP stores 32 base colors; expand to 64 for decoding.
            while (list.Count < 32) list.Add(new PaletteRgb(0, 0, 0));
            return AmigaColorSpace.ExpandExtraHalfBrite(list.Take(32).ToArray());
        }

        if (list.Count == 0)
            list.Add(new PaletteRgb(0, 0, 0));
        return list.ToArray();
    }

    private static PixelBuffer PlanarToRgba(
        int width, int height, int nPlanes, byte masking, ushort transparent,
        byte[] planar, IReadOnlyList<PaletteRgb> palette)
    {
        int rowBytes = RowBytes(width);
        int planesPerRow = nPlanes + (masking == 1 ? 1 : 0);
        var buf = new PixelBuffer(width, height);
        var px = buf.Pixels;

        for (int y = 0; y < height; y++)
        {
            int rowOff = y * planesPerRow * rowBytes;
            for (int x = 0; x < width; x++)
            {
                int index = 0;
                for (int p = 0; p < nPlanes; p++)
                {
                    int bit = ReadBit(planar, rowOff + p * rowBytes, x);
                    if (bit != 0) index |= 1 << p;
                }

                bool maskOn = true;
                if (masking == 1)
                    maskOn = ReadBit(planar, rowOff + nPlanes * rowBytes, x) != 0;
                else if (masking == 2)
                    maskOn = index != transparent;

                int di = (y * width + x) * 4;
                if (!maskOn || index >= palette.Count)
                {
                    px[di] = px[di + 1] = px[di + 2] = px[di + 3] = 0;
                    continue;
                }

                var c = palette[index];
                px[di] = c.R;
                px[di + 1] = c.G;
                px[di + 2] = c.B;
                px[di + 3] = 255;
            }
        }

        return buf;
    }

    private static byte[] Encode(
        int width, int height, byte[] indices, PaletteRgb[] palette, DocumentColorSettings settings)
    {
        settings.Sanitize();
        bool ehb = settings.Mode == ColorModeKind.Ecs && settings.HalfBright;
        int colorSlots = palette.Length;
        int nPlanes = Math.Max(1, (int)Math.Ceiling(Math.Log2(Math.Max(2, colorSlots))));
        nPlanes = Math.Clamp(nPlanes, 1, 8);

        // CMAP: for EHB store only the 32 base colors.
        PaletteRgb[] cmapColors = ehb && palette.Length >= 64
            ? palette.Take(32).ToArray()
            : palette;

        int rowBytes = RowBytes(width);
        var planar = new byte[height * nPlanes * rowBytes];
        for (int y = 0; y < height; y++)
        {
            for (int p = 0; p < nPlanes; p++)
            {
                int dest = (y * nPlanes + p) * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    int idx = indices[y * width + x];
                    if (((idx >> p) & 1) != 0)
                        SetBit(planar, dest, x);
                }
            }
        }

        byte[] body = CompressByteRun1(planar);
        uint camg = 0;
        if (ehb) camg |= CamgExtraHalfBrite;

        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            WriteId(w, "FORM");
            w.Write(0); // placeholder size
            WriteId(w, "ILBM");

            // BMHD
            WriteId(w, "BMHD");
            WriteChunkSize(w, 20);
            WriteU16(w, (ushort)width);
            WriteU16(w, (ushort)height);
            WriteI16(w, 0); WriteI16(w, 0);
            w.Write((byte)nPlanes);
            w.Write((byte)0); // masking none
            w.Write((byte)1); // ByteRun1
            w.Write((byte)0);
            WriteU16(w, 0); // transparent
            w.Write((byte)1); w.Write((byte)1); // aspect
            WriteU16(w, (ushort)width);
            WriteU16(w, (ushort)height);

            // CAMG
            WriteId(w, "CAMG");
            WriteChunkSize(w, 4);
            WriteU32(w, camg);

            // CMAP
            int cmapSize = cmapColors.Length * 3;
            WriteId(w, "CMAP");
            WriteChunkSize(w, cmapSize);
            foreach (var c in cmapColors)
            {
                w.Write(c.R); w.Write(c.G); w.Write(c.B);
            }
            if ((cmapSize & 1) != 0) w.Write((byte)0);

            // BODY
            WriteId(w, "BODY");
            WriteChunkSize(w, body.Length);
            w.Write(body);
            if ((body.Length & 1) != 0) w.Write((byte)0);
        }

        var bytes = ms.ToArray();
        int formDataSize = bytes.Length - 8;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), formDataSize);
        return bytes;
    }

    private static int RowBytes(int width) => ((width + 15) / 16) * 2;

    private static int ReadBit(byte[] data, int rowStart, int x)
    {
        int byteIndex = rowStart + (x >> 3);
        int bit = 7 - (x & 7);
        return (data[byteIndex] >> bit) & 1;
    }

    private static void SetBit(byte[] data, int rowStart, int x)
    {
        int byteIndex = rowStart + (x >> 3);
        int bit = 7 - (x & 7);
        data[byteIndex] |= (byte)(1 << bit);
    }

    private static byte[] DecompressByteRun1(byte[] src, int expected)
    {
        var dst = new byte[expected];
        int sp = 0, dp = 0;
        while (sp < src.Length && dp < expected)
        {
            sbyte n = (sbyte)src[sp++];
            if (n >= 0)
            {
                int count = n + 1;
                if (sp + count > src.Length || dp + count > expected)
                    throw new InvalidOperationException("Corrupt ByteRun1 literal.");
                Buffer.BlockCopy(src, sp, dst, dp, count);
                sp += count;
                dp += count;
            }
            else if (n != -128)
            {
                int count = 1 - n;
                if (sp >= src.Length || dp + count > expected)
                    throw new InvalidOperationException("Corrupt ByteRun1 repeat.");
                byte v = src[sp++];
                for (int i = 0; i < count; i++)
                    dst[dp++] = v;
            }
        }
        return dst;
    }

    private static byte[] CompressByteRun1(byte[] src)
    {
        using var ms = new MemoryStream(src.Length + src.Length / 8 + 16);
        int i = 0;
        while (i < src.Length)
        {
            // Try run
            int run = 1;
            while (i + run < src.Length && src[i + run] == src[i] && run < 128)
                run++;
            if (run >= 3 || (run >= 2 && (i + run >= src.Length || src[i + run] != src[i])))
            {
                if (run >= 2)
                {
                    ms.WriteByte((byte)(256 - (run - 1))); // - (run-1) as sbyte
                    ms.WriteByte(src[i]);
                    i += run;
                    continue;
                }
            }

            // Literal
            int start = i;
            i++;
            while (i < src.Length && i - start < 128)
            {
                // stop before a profitable run
                int look = 1;
                while (i + look < src.Length && src[i + look] == src[i] && look < 128)
                    look++;
                if (look >= 3) break;
                i++;
            }
            int lit = i - start;
            ms.WriteByte((byte)(lit - 1));
            ms.Write(src, start, lit);
        }
        return ms.ToArray();
    }

    private static string ReadId(byte[] data, int offset) =>
        Encoding.ASCII.GetString(data, offset, 4);

    private static void WriteId(BinaryWriter w, string id)
    {
        w.Write(Encoding.ASCII.GetBytes(id));
    }

    private static void WriteChunkSize(BinaryWriter w, int size)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, size);
        w.Write(b);
    }

    private static void WriteU16(BinaryWriter w, ushort v)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, v);
        w.Write(b);
    }

    private static void WriteI16(BinaryWriter w, short v) => WriteU16(w, unchecked((ushort)v));

    private static void WriteU32(BinaryWriter w, uint v)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, v);
        w.Write(b);
    }
}
