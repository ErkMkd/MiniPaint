using System.Numerics;

namespace MiniPaint.Core.Selection;

public enum SelectionCombineMode
{
    Replace,
    Add,
    Subtract,
    Intersect
}

/// <summary>
/// Selection in document coordinates: a bounding box plus an optional per-pixel coverage mask
/// (0 = outside, 255 = fully selected, in between = soft edge). Without a mask the whole box is
/// selected (plain rectangle). Empty when HasSelection is false.
/// </summary>
public sealed class SelectionState
{
    /// <summary>Coverage threshold for "inside" when drawing the outline (Photoshop: 50%).</summary>
    public const byte OutlineThreshold = 128;

    private byte[]? _mask;

    public bool HasSelection { get; private set; }
    public int X { get; private set; }
    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public int Right => X + Width;
    public int Bottom => Y + Height;

    /// <summary>True when the selection is the whole bounding box (no mask).</summary>
    public bool IsRectangular => _mask is null;

    /// <summary>Coverage per pixel of the bounds, row-major Width × Height. Empty when rectangular.</summary>
    public ReadOnlySpan<byte> Mask => _mask;

    /// <summary>Incremented on every change, so render caches (outline geometry) know when to rebuild.</summary>
    public int Version { get; private set; }

    /// <summary>Like <see cref="Version"/> but unchanged by <see cref="MoveTo"/> (same shape, new place).</summary>
    public int ShapeVersion { get; private set; }

    public void Set(int x, int y, int width, int height)
    {
        if (width < 0)
        {
            x += width;
            width = -width;
        }
        if (height < 0)
        {
            y += height;
            height = -height;
        }

        if (width == 0 || height == 0)
        {
            Clear();
            return;
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
        _mask = null;
        HasSelection = true;
        Version++;
        ShapeVersion++;
    }

    /// <summary>
    /// Masked selection. Takes ownership of <paramref name="mask"/> (width × height coverage).
    /// Empty borders are trimmed unless <paramref name="trim"/> is false; a fully selected
    /// mask becomes a plain rectangle; an empty one clears the selection.
    /// </summary>
    public void SetMask(int x, int y, int width, int height, byte[] mask, bool trim = true)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (width <= 0 || height <= 0)
        {
            Clear();
            return;
        }
        if (mask.Length < width * height)
            throw new ArgumentException("Mask is too small.", nameof(mask));

        int x0 = 0, y0 = 0, x1 = width, y1 = height;
        if (trim && !TryGetCoverageBounds(mask, width, height, out x0, out y0, out x1, out y1))
        {
            Clear();
            return;
        }

        int w = x1 - x0;
        int h = y1 - y0;
        if (w != width || h != height)
            mask = CropMask(mask, width, x0, y0, w, h);

        X = x + x0;
        Y = y + y0;
        Width = w;
        Height = h;
        _mask = IsFullySelected(mask, w * h) ? null : mask;
        HasSelection = true;
        Version++;
        ShapeVersion++;
    }

    /// <summary>Move the selection (and its mask) so its top-left corner is at x, y.</summary>
    public void MoveTo(int x, int y)
    {
        if (!HasSelection || (x == X && y == Y)) return;
        X = x;
        Y = y;
        Version++;
    }

    public void Offset(int dx, int dy)
    {
        if (!HasSelection) return;
        MoveTo(X + dx, Y + dy);
    }

    public void ClampToDocument(int docWidth, int docHeight)
    {
        if (!HasSelection) return;

        int x0 = Math.Clamp(X, 0, docWidth);
        int y0 = Math.Clamp(Y, 0, docHeight);
        int x1 = Math.Clamp(Right, 0, docWidth);
        int y1 = Math.Clamp(Bottom, 0, docHeight);
        int w = x1 - x0;
        int h = y1 - y0;
        if (w <= 0 || h <= 0)
        {
            Clear();
            return;
        }
        if (x0 == X && y0 == Y && w == Width && h == Height)
            return;

        if (_mask is null)
        {
            Set(x0, y0, w, h);
            return;
        }

        SetMask(x0, y0, w, h, CropMask(_mask, Width, x0 - X, y0 - Y, w, h));
    }

    public void SelectAll(int docWidth, int docHeight) => Set(0, 0, docWidth, docHeight);

    /// <summary>Coverage 0–255 at a document pixel (0 outside the selection).</summary>
    public byte CoverageAt(int docX, int docY)
    {
        if (!HasSelection) return 0;
        int lx = docX - X;
        int ly = docY - Y;
        if ((uint)lx >= (uint)Width || (uint)ly >= (uint)Height) return 0;
        return _mask is null ? (byte)255 : _mask[ly * Width + lx];
    }

    /// <summary>True when the pixel is (at least partly) selected.</summary>
    public bool Contains(int docX, int docY) => CoverageAt(docX, docY) > 0;

    public bool ContainsDoc(double docX, double docY) =>
        Contains((int)Math.Floor(docX), (int)Math.Floor(docY));

    /// <summary>True when the point is inside the bounding box (handles / transform hit tests).</summary>
    public bool BoundsContainDoc(double docX, double docY) =>
        HasSelection && docX >= X && docX < Right && docY >= Y && docY < Bottom;

    public void Clear()
    {
        HasSelection = false;
        X = Y = Width = Height = 0;
        _mask = null;
        Version++;
        ShapeVersion++;
    }

    public SelectionState Clone()
    {
        var copy = new SelectionState();
        copy.CopyFrom(this);
        return copy;
    }

    public void CopyFrom(SelectionState other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) return;
        HasSelection = other.HasSelection;
        X = other.X;
        Y = other.Y;
        Width = other.Width;
        Height = other.Height;
        _mask = other._mask is null ? null : (byte[])other._mask.Clone();
        Version++;
        ShapeVersion++;
    }

    /// <summary>
    /// Combine <paramref name="other"/> into this selection. Soft edges combine like Photoshop:
    /// add = a + b − ab, subtract = a(1 − b), intersect = ab (coverage as 0–1).
    /// </summary>
    public void Combine(SelectionState other, SelectionCombineMode mode)
    {
        ArgumentNullException.ThrowIfNull(other);
        switch (mode)
        {
            case SelectionCombineMode.Replace:
                CopyFrom(other);
                return;
            case SelectionCombineMode.Add when !HasSelection:
                CopyFrom(other);
                return;
            case SelectionCombineMode.Add when !other.HasSelection:
            case SelectionCombineMode.Subtract when !HasSelection || !other.HasSelection:
                return;
            case SelectionCombineMode.Intersect when !HasSelection || !other.HasSelection:
                Clear();
                return;
        }

        int x0, y0, x1, y1;
        switch (mode)
        {
            case SelectionCombineMode.Add:
                x0 = Math.Min(X, other.X);
                y0 = Math.Min(Y, other.Y);
                x1 = Math.Max(Right, other.Right);
                y1 = Math.Max(Bottom, other.Bottom);
                break;
            case SelectionCombineMode.Subtract:
                (x0, y0, x1, y1) = (X, Y, Right, Bottom);
                break;
            case SelectionCombineMode.Intersect:
                x0 = Math.Max(X, other.X);
                y0 = Math.Max(Y, other.Y);
                x1 = Math.Min(Right, other.Right);
                y1 = Math.Min(Bottom, other.Bottom);
                if (x1 <= x0 || y1 <= y0)
                {
                    Clear();
                    return;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        int w = x1 - x0;
        int h = y1 - y0;
        var result = new byte[w * h];
        ForEachRow(h, w, yy =>
        {
            var row = result.AsSpan(yy * w, w);
            CopyCoverageRow(x0, y0 + yy, row);
            other.CombineIntoRow(row, x0, y0 + yy, mode);
        });

        SetMask(x0, y0, w, h, result);
    }

    /// <summary>Coverage of document row <paramref name="docY"/> from <paramref name="docX"/> into <paramref name="row"/>.</summary>
    private void CopyCoverageRow(int docX, int docY, Span<byte> row)
    {
        row.Clear();
        if (!HasSelection || docY < Y || docY >= Bottom) return;
        int x0 = Math.Max(docX, X);
        int x1 = Math.Min(docX + row.Length, Right);
        if (x1 <= x0) return;

        var dst = row.Slice(x0 - docX, x1 - x0);
        if (_mask is null)
            dst.Fill(255);
        else
            _mask.AsSpan((docY - Y) * Width + (x0 - X), x1 - x0).CopyTo(dst);
    }

    /// <summary>Combine this selection's coverage (b) into <paramref name="row"/> (a) in place.</summary>
    private void CombineIntoRow(Span<byte> row, int docX, int docY, SelectionCombineMode mode)
    {
        bool rowHit = docY >= Y && docY < Bottom;
        int x0 = Math.Max(docX, X);
        int x1 = Math.Min(docX + row.Length, Right);
        if (!rowHit || x1 <= x0)
        {
            if (mode == SelectionCombineMode.Intersect)
                row.Clear(); // b = 0 everywhere on this row
            return;
        }

        int start = x0 - docX;
        int length = x1 - x0;
        if (mode == SelectionCombineMode.Intersect)
        {
            row[..start].Clear();
            row[(start + length)..].Clear();
        }

        var a = row.Slice(start, length);
        if (_mask is null)
        {
            // b = 255: add fills, subtract clears, intersect keeps a.
            if (mode == SelectionCombineMode.Add) a.Fill(255);
            else if (mode == SelectionCombineMode.Subtract) a.Clear();
            return;
        }

        var b = _mask.AsSpan((docY - Y) * Width + (x0 - X), length);
        switch (mode)
        {
            case SelectionCombineMode.Add:
                for (int i = 0; i < length; i++)
                    a[i] = (byte)(a[i] + b[i] - (a[i] * b[i] + 127) / 255);
                break;
            case SelectionCombineMode.Subtract:
                for (int i = 0; i < length; i++)
                    a[i] = (byte)((a[i] * (255 - b[i]) + 127) / 255);
                break;
            default:
                for (int i = 0; i < length; i++)
                    a[i] = (byte)((a[i] * b[i] + 127) / 255);
                break;
        }
    }

    /// <summary>Select everything that was not selected (within the document). No-op when empty.</summary>
    public void Invert(int docWidth, int docHeight)
    {
        if (!HasSelection) return;
        var result = new byte[docWidth * docHeight];
        ForEachRow(docHeight, docWidth, y =>
        {
            var row = result.AsSpan(y * docWidth, docWidth);
            CopyCoverageRow(0, y, row);
            InvertBytes(row);
        });
        SetMask(0, 0, docWidth, docHeight, result);
    }

    /// <summary>
    /// Resample <paramref name="source"/> into the box x, y, width × height (nearest neighbour).
    /// Bounds are kept exactly (no trim) so Transform Selection handles stay under the cursor.
    /// </summary>
    public void SetScaled(SelectionState source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.HasSelection || width <= 0 || height <= 0)
        {
            Clear();
            return;
        }
        if (source._mask is null)
        {
            Set(x, y, width, height);
            return;
        }

        var src = source._mask;
        int sw = source.Width;
        int sh = source.Height;
        var result = new byte[width * height];
        var columns = new int[width];
        for (int xx = 0; xx < width; xx++)
            columns[xx] = Math.Min(sw - 1, (int)((xx + 0.5) * sw / width));
        ForEachRow(height, width, yy =>
        {
            int srcRow = Math.Min(sh - 1, (int)((yy + 0.5) * sh / height)) * sw;
            var dst = result.AsSpan(yy * width, width);
            for (int xx = 0; xx < width; xx++)
                dst[xx] = src[srcRow + columns[xx]];
        });
        SetMask(x, y, width, height, result, trim: false);
    }

    /// <summary>Scale position and size (Image Size), e.g. 0.5 halves the selection.</summary>
    public void ScaleBy(double sx, double sy)
    {
        if (!HasSelection) return;
        int x = (int)Math.Round(X * sx);
        int y = (int)Math.Round(Y * sy);
        int w = Math.Max(1, (int)Math.Round(Width * sx));
        int h = Math.Max(1, (int)Math.Round(Height * sy));
        SetScaled(Clone(), x, y, w, h);
    }

    public SelectionSnapshot Capture() => SelectionSnapshot.Capture(this, _mask);

    public void Restore(SelectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasSelection)
        {
            Clear();
            return;
        }
        var mask = snapshot.DecodeMask();
        if (mask is null)
            Set(snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height);
        else
            SetMask(snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height, mask, trim: false);
    }

    private static bool TryGetCoverageBounds(byte[] mask, int width, int height,
        out int x0, out int y0, out int x1, out int y1)
    {
        x0 = width;
        y0 = height;
        x1 = 0;
        y1 = 0;
        for (int y = 0; y < height; y++)
        {
            var row = mask.AsSpan(y * width, width);
            int first = row.IndexOfAnyExcept((byte)0);
            if (first < 0) continue;
            int last = row.LastIndexOfAnyExcept((byte)0);
            if (first < x0) x0 = first;
            if (last + 1 > x1) x1 = last + 1;
            if (y < y0) y0 = y;
            y1 = y + 1;
        }
        return x1 > x0 && y1 > y0;
    }

    private static byte[] CropMask(byte[] mask, int maskWidth, int x0, int y0, int w, int h)
    {
        var result = new byte[w * h];
        for (int y = 0; y < h; y++)
            Array.Copy(mask, (y0 + y) * maskWidth + x0, result, y * w, w);
        return result;
    }

    private static bool IsFullySelected(byte[] mask, int length) =>
        mask.AsSpan(0, length).IndexOfAnyExcept((byte)255) < 0;

    private static void InvertBytes(Span<byte> data)
    {
        // 255 - x == ~x for bytes.
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= data.Length - Vector<byte>.Count; i += Vector<byte>.Count)
                (~new Vector<byte>(data[i..])).CopyTo(data[i..]);
        }
        for (; i < data.Length; i++)
            data[i] = (byte)~data[i];
    }

    /// <summary>Run <paramref name="body"/> for rows 0..height-1, in parallel when the area is large.</summary>
    private static void ForEachRow(int height, int width, Action<int> body)
    {
        if ((long)height * width < ParallelThreshold)
        {
            for (int y = 0; y < height; y++)
                body(y);
            return;
        }
        Parallel.For(0, height, body);
    }

    private const long ParallelThreshold = 256 * 1024;
}
