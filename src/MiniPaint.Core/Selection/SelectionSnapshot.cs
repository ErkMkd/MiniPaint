namespace MiniPaint.Core.Selection;

/// <summary>
/// Immutable copy of a selection for undo. The mask is run-length encoded: selection masks are
/// mostly long runs of 0 / 255, so history entries stay small even for large documents.
/// </summary>
public sealed class SelectionSnapshot
{
    private readonly byte[]? _encodedMask;

    public bool HasSelection { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    private SelectionSnapshot(bool hasSelection, int x, int y, int width, int height, byte[]? encodedMask)
    {
        HasSelection = hasSelection;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        _encodedMask = encodedMask;
    }

    internal static SelectionSnapshot Capture(SelectionState state, byte[]? mask) =>
        new(state.HasSelection, state.X, state.Y, state.Width, state.Height,
            mask is null ? null : MaskRle.Encode(mask.AsSpan(0, state.Width * state.Height)));

    /// <summary>Decoded mask, or null for a rectangular (or empty) selection.</summary>
    internal byte[]? DecodeMask() =>
        _encodedMask is null ? null : MaskRle.Decode(_encodedMask, Width * Height);

    public bool SameAs(SelectionSnapshot other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (HasSelection != other.HasSelection) return false;
        if (!HasSelection) return true;
        if (X != other.X || Y != other.Y || Width != other.Width || Height != other.Height) return false;
        if (_encodedMask is null || other._encodedMask is null)
            return _encodedMask is null && other._encodedMask is null;
        return _encodedMask.AsSpan().SequenceEqual(other._encodedMask);
    }
}

/// <summary>Run-length encoding: (value, run length as 7-bit varint) pairs.</summary>
internal static class MaskRle
{
    public static byte[] Encode(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        int i = 0;
        while (i < data.Length)
        {
            byte value = data[i];
            int run = 1;
            while (i + run < data.Length && data[i + run] == value)
                run++;

            output.WriteByte(value);
            uint n = (uint)run;
            while (n >= 0x80)
            {
                output.WriteByte((byte)(n | 0x80));
                n >>= 7;
            }
            output.WriteByte((byte)n);
            i += run;
        }
        return output.ToArray();
    }

    public static byte[] Decode(byte[] encoded, int length)
    {
        var result = new byte[length];
        int pos = 0;
        int i = 0;
        while (i < encoded.Length && pos < length)
        {
            byte value = encoded[i++];
            uint run = 0;
            int shift = 0;
            byte b;
            do
            {
                b = encoded[i++];
                run |= (uint)(b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            int count = (int)Math.Min(run, (uint)(length - pos));
            result.AsSpan(pos, count).Fill(value);
            pos += count;
        }
        return result;
    }
}
