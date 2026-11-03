using MiniPaint.Core.Imaging;

namespace MiniPaint.Core.ColorMode;

/// <summary>Palette builders for live color-mode remapping.</summary>
public static class Quantizers
{
    public static PaletteRgb[] BuildPalette(
        PixelBuffer source,
        int colorCount,
        int bitsPerChannel,
        QuantizerKind kind)
    {
        ArgumentNullException.ThrowIfNull(source);
        colorCount = Math.Clamp(colorCount, 2, 256);
        bitsPerChannel = Math.Clamp(bitsPerChannel, 1, 8);

        PaletteRgb[] palette = kind switch
        {
            QuantizerKind.MedianCut => MedianCut.Build(source, colorCount, bitsPerChannel),
            QuantizerKind.Octree => Octree.Build(source, colorCount, bitsPerChannel),
            QuantizerKind.Wu => Wu.Build(source, colorCount, bitsPerChannel),
            QuantizerKind.KMeansPlusPlus => KMeans.Build(source, colorCount, bitsPerChannel),
            QuantizerKind.NeuQuant => NeuQuant.Build(source, colorCount, bitsPerChannel),
            _ => MedianCut.Build(source, colorCount, bitsPerChannel)
        };

        for (int i = 0; i < palette.Length; i++)
            palette[i] = AmigaColorSpace.SnapColor(palette[i], bitsPerChannel);

        return DedupAndPad(palette, colorCount, bitsPerChannel);
    }

    private static PaletteRgb[] DedupAndPad(PaletteRgb[] palette, int colorCount, int bitsPerChannel)
    {
        var unique = new List<PaletteRgb>(colorCount);
        var seen = new HashSet<PaletteRgb>();
        foreach (var c in palette)
        {
            var s = AmigaColorSpace.SnapColor(c, bitsPerChannel);
            if (seen.Add(s))
                unique.Add(s);
            if (unique.Count >= colorCount) break;
        }

        while (unique.Count < colorCount)
        {
            // Fill missing slots with a stepped gray ramp in the mode's color space.
            int t = unique.Count;
            byte v = AmigaColorSpace.SnapChannel((byte)(t * 255 / Math.Max(1, colorCount - 1)), bitsPerChannel);
            var fill = new PaletteRgb(v, v, v);
            if (seen.Add(fill))
                unique.Add(fill);
            else
                unique.Add(AmigaColorSpace.SnapColor((byte)(t * 37), (byte)(t * 59), (byte)(t * 97), bitsPerChannel));
        }

        return unique.Take(colorCount).ToArray();
    }

    // --- Median Cut ---
    private static class MedianCut
    {
        private sealed class Box
        {
            public List<(byte r, byte g, byte b)> Colors = new();
            public byte MinR, MaxR, MinG, MaxG, MinB, MaxB;

            public void Recompute()
            {
                if (Colors.Count == 0) return;
                MinR = MaxR = Colors[0].r;
                MinG = MaxG = Colors[0].g;
                MinB = MaxB = Colors[0].b;
                foreach (var (r, g, b) in Colors)
                {
                    if (r < MinR) MinR = r; if (r > MaxR) MaxR = r;
                    if (g < MinG) MinG = g; if (g > MaxG) MaxG = g;
                    if (b < MinB) MinB = b; if (b > MaxB) MaxB = b;
                }
            }

            public int LongChannel()
            {
                int dr = MaxR - MinR, dg = MaxG - MinG, db = MaxB - MinB;
                if (dr >= dg && dr >= db) return 0;
                if (dg >= db) return 1;
                return 2;
            }

            public PaletteRgb Average()
            {
                if (Colors.Count == 0) return new PaletteRgb(0, 0, 0);
                long r = 0, g = 0, b = 0;
                foreach (var c in Colors) { r += c.r; g += c.g; b += c.b; }
                int n = Colors.Count;
                return PaletteRgb.FromRgb((int)(r / n), (int)(g / n), (int)(b / n));
            }
        }

        public static PaletteRgb[] Build(PixelBuffer source, int colorCount, int bits)
        {
            var samples = PixelSampler.CollectOpaque(source, bits, maxSamples: 50000);
            var root = new Box { Colors = samples };
            root.Recompute();
            var boxes = new List<Box> { root };

            while (boxes.Count < colorCount)
            {
                Box? split = null;
                int bestRange = -1;
                foreach (var box in boxes)
                {
                    if (box.Colors.Count < 2) continue;
                    int range = Math.Max(box.MaxR - box.MinR, Math.Max(box.MaxG - box.MinG, box.MaxB - box.MinB));
                    if (range > bestRange)
                    {
                        bestRange = range;
                        split = box;
                    }
                }
                if (split is null) break;

                int ch = split.LongChannel();
                split.Colors.Sort((a, b) => ch switch
                {
                    0 => a.r.CompareTo(b.r),
                    1 => a.g.CompareTo(b.g),
                    _ => a.b.CompareTo(b.b)
                });
                int mid = split.Colors.Count / 2;
                var left = new Box { Colors = split.Colors.GetRange(0, mid) };
                var right = new Box { Colors = split.Colors.GetRange(mid, split.Colors.Count - mid) };
                left.Recompute();
                right.Recompute();
                boxes.Remove(split);
                boxes.Add(left);
                boxes.Add(right);
            }

            return boxes.Select(b => b.Average()).ToArray();
        }
    }

    // --- Octree ---
    // Previous version reduced one parent at a time and re-walked the whole tree each
    // step (O(leaves²)) — that froze the UI on Preview. Now we batch-collapse the
    // deepest internal level until we fit the budget.
    private static class Octree
    {
        private const int MaxDepth = 6;

        private sealed class Node
        {
            public bool IsLeaf;
            public int PixelCount;
            public long R, G, B;
            public Node?[] Children = new Node?[8];
        }

        public static PaletteRgb[] Build(PixelBuffer source, int colorCount, int bits)
        {
            var samples = PixelSampler.CollectOpaque(source, bits, maxSamples: 25000);
            var root = new Node();
            int leafCount = 0;
            foreach (var (r, g, b) in samples)
                Insert(root, r, g, b, 0, ref leafCount);

            int guard = 64;
            while (leafCount > colorCount && guard-- > 0)
            {
                int deepest = FindDeepestInternalDepth(root);
                if (deepest < 0) break;
                int before = leafCount;
                CollapseDepth(root, 0, deepest, ref leafCount);
                if (leafCount >= before) break; // no progress
            }

            var leaves = new List<(PaletteRgb color, int weight)>(Math.Max(16, leafCount));
            CollectWeighted(root, leaves);
            if (leaves.Count > colorCount)
            {
                leaves.Sort((a, b) => b.weight.CompareTo(a.weight));
                leaves.RemoveRange(colorCount, leaves.Count - colorCount);
            }

            return leaves.Select(l => l.color).ToArray();
        }

        private static void Insert(Node node, byte r, byte g, byte b, int depth, ref int leafCount)
        {
            if (depth >= MaxDepth)
            {
                if (!node.IsLeaf)
                {
                    node.IsLeaf = true;
                    leafCount++;
                }
                node.PixelCount++;
                node.R += r;
                node.G += g;
                node.B += b;
                return;
            }

            int idx = BitIndex(r, g, b, depth);
            node.Children[idx] ??= new Node();
            Insert(node.Children[idx]!, r, g, b, depth + 1, ref leafCount);
        }

        private static int FindDeepestInternalDepth(Node node) =>
            FindDeepestInternalDepth(node, 0);

        private static int FindDeepestInternalDepth(Node node, int depth)
        {
            if (node.IsLeaf) return -1;
            bool anyChild = false;
            int best = -1;
            for (int i = 0; i < 8; i++)
            {
                if (node.Children[i] is not { } c) continue;
                anyChild = true;
                int d = FindDeepestInternalDepth(c, depth + 1);
                if (d > best) best = d;
            }
            // An internal node at `depth` is itself a candidate if it has children.
            return anyChild ? Math.Max(best, depth) : -1;
        }

        private static void CollapseDepth(Node node, int depth, int targetDepth, ref int leafCount)
        {
            if (node.IsLeaf) return;

            if (depth == targetDepth)
            {
                int leavesRemoved = 0;
                long r = 0, g = 0, b = 0;
                int count = 0;
                for (int i = 0; i < 8; i++)
                {
                    var child = node.Children[i];
                    if (child is null) continue;
                    leavesRemoved += CountLeaves(child);
                    count += child.PixelCount;
                    r += child.R;
                    g += child.G;
                    b += child.B;
                    node.Children[i] = null;
                }

                node.PixelCount = count;
                node.R = r;
                node.G = g;
                node.B = b;
                node.IsLeaf = true;
                leafCount = leafCount - leavesRemoved + 1;
                return;
            }

            for (int i = 0; i < 8; i++)
            {
                if (node.Children[i] is { } c)
                    CollapseDepth(c, depth + 1, targetDepth, ref leafCount);
            }
        }

        private static int CountLeaves(Node n)
        {
            if (n.IsLeaf) return 1;
            int c = 0;
            for (int i = 0; i < 8; i++)
                if (n.Children[i] is { } ch)
                    c += CountLeaves(ch);
            return c;
        }

        private static int BitIndex(byte r, byte g, byte b, int depth)
        {
            int shift = 7 - depth;
            return ((r >> shift) & 1) << 2 | ((g >> shift) & 1) << 1 | ((b >> shift) & 1);
        }

        private static void CollectWeighted(Node n, List<(PaletteRgb color, int weight)> palette)
        {
            if (n.IsLeaf)
            {
                if (n.PixelCount <= 0) return;
                palette.Add((
                    PaletteRgb.FromRgb(
                        (int)(n.R / n.PixelCount),
                        (int)(n.G / n.PixelCount),
                        (int)(n.B / n.PixelCount)),
                    n.PixelCount));
                return;
            }
            for (int i = 0; i < 8; i++)
                if (n.Children[i] is { } c)
                    CollectWeighted(c, palette);
        }
    }

    // --- Wu (simplified variance cut using median-cut-like with moment bias) ---
    private static class Wu
    {
        public static PaletteRgb[] Build(PixelBuffer source, int colorCount, int bits)
        {
            // Practical Wu-inspired: histogram in 32^3 then variance-based median cut on unique colors.
            const int q = 32;
            var hist = new int[q * q * q];
            var sumR = new long[q * q * q];
            var sumG = new long[q * q * q];
            var sumB = new long[q * q * q];

            var px = source.PixelsReadOnly;
            for (int i = 0; i < px.Length; i += 4)
            {
                if (px[i + 3] == 0) continue;
                var c = AmigaColorSpace.SnapColor(px[i], px[i + 1], px[i + 2], bits);
                int ri = c.R * (q - 1) / 255;
                int gi = c.G * (q - 1) / 255;
                int bi = c.B * (q - 1) / 255;
                int idx = (ri * q + gi) * q + bi;
                hist[idx]++;
                sumR[idx] += c.R; sumG[idx] += c.G; sumB[idx] += c.B;
            }

            var colors = new List<(byte r, byte g, byte b, int w)>();
            for (int i = 0; i < hist.Length; i++)
            {
                if (hist[i] == 0) continue;
                colors.Add((
                    (byte)(sumR[i] / hist[i]),
                    (byte)(sumG[i] / hist[i]),
                    (byte)(sumB[i] / hist[i]),
                    hist[i]));
            }
            if (colors.Count == 0)
                return Enumerable.Repeat(new PaletteRgb(0, 0, 0), colorCount).ToArray();

            var boxes = new List<List<(byte r, byte g, byte b, int w)>> { colors };
            while (boxes.Count < colorCount)
            {
                int bi = -1;
                double bestVar = -1;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count < 2) continue;
                    double v = ChannelVariance(boxes[i]);
                    if (v > bestVar) { bestVar = v; bi = i; }
                }
                if (bi < 0) break;

                var box = boxes[bi];
                int ch = DominantChannel(box);
                box.Sort((a, b) => ch switch
                {
                    0 => a.r.CompareTo(b.r),
                    1 => a.g.CompareTo(b.g),
                    _ => a.b.CompareTo(b.b)
                });
                int totalW = box.Sum(x => x.w);
                int acc = 0;
                int mid = 1;
                for (int i = 0; i < box.Count - 1; i++)
                {
                    acc += box[i].w;
                    if (acc >= totalW / 2) { mid = i + 1; break; }
                }
                var left = box.GetRange(0, mid);
                var right = box.GetRange(mid, box.Count - mid);
                boxes.RemoveAt(bi);
                boxes.Add(left);
                boxes.Add(right);
            }

            return boxes.Select(WeightedAverage).ToArray();
        }

        private static double ChannelVariance(List<(byte r, byte g, byte b, int w)> box)
        {
            long w = 0, r = 0, g = 0, b = 0;
            foreach (var c in box) { w += c.w; r += c.r * c.w; g += c.g * c.w; b += c.b * c.w; }
            if (w == 0) return 0;
            double mr = (double)r / w, mg = (double)g / w, mb = (double)b / w;
            double v = 0;
            foreach (var c in box)
            {
                double dr = c.r - mr, dg = c.g - mg, db = c.b - mb;
                v += c.w * (dr * dr + dg * dg + db * db);
            }
            return v;
        }

        private static int DominantChannel(List<(byte r, byte g, byte b, int w)> box)
        {
            byte minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
            foreach (var c in box)
            {
                if (c.r < minR) minR = c.r; if (c.r > maxR) maxR = c.r;
                if (c.g < minG) minG = c.g; if (c.g > maxG) maxG = c.g;
                if (c.b < minB) minB = c.b; if (c.b > maxB) maxB = c.b;
            }
            int dr = maxR - minR, dg = maxG - minG, db = maxB - minB;
            if (dr >= dg && dr >= db) return 0;
            if (dg >= db) return 1;
            return 2;
        }

        private static PaletteRgb WeightedAverage(List<(byte r, byte g, byte b, int w)> box)
        {
            long w = 0, r = 0, g = 0, b = 0;
            foreach (var c in box) { w += c.w; r += c.r * c.w; g += c.g * c.w; b += c.b * c.w; }
            if (w == 0) return new PaletteRgb(0, 0, 0);
            return PaletteRgb.FromRgb((int)(r / w), (int)(g / w), (int)(b / w));
        }
    }

    // --- K-Means++ ---
    private static class KMeans
    {
        public static PaletteRgb[] Build(PixelBuffer source, int colorCount, int bits)
        {
            var samples = PixelSampler.CollectOpaque(source, bits, maxSamples: 20000);
            if (samples.Count == 0)
                return Enumerable.Repeat(new PaletteRgb(0, 0, 0), colorCount).ToArray();

            var rng = new Random(0x4D696E69); // stable seed "Mini"
            var centroids = new (double r, double g, double b)[colorCount];

            // k-means++ init
            int first = rng.Next(samples.Count);
            centroids[0] = (samples[first].r, samples[first].g, samples[first].b);
            var dist = new double[samples.Count];
            for (int k = 1; k < colorCount; k++)
            {
                double sum = 0;
                for (int i = 0; i < samples.Count; i++)
                {
                    double best = double.MaxValue;
                    for (int c = 0; c < k; c++)
                    {
                        double d = Dist2(samples[i], centroids[c]);
                        if (d < best) best = d;
                    }
                    dist[i] = best;
                    sum += best;
                }
                double pick = rng.NextDouble() * sum;
                double acc = 0;
                int chosen = samples.Count - 1;
                for (int i = 0; i < samples.Count; i++)
                {
                    acc += dist[i];
                    if (acc >= pick) { chosen = i; break; }
                }
                centroids[k] = (samples[chosen].r, samples[chosen].g, samples[chosen].b);
            }

            var assign = new int[samples.Count];
            for (int iter = 0; iter < 12; iter++)
            {
                for (int i = 0; i < samples.Count; i++)
                {
                    int best = 0;
                    double bestD = double.MaxValue;
                    for (int c = 0; c < colorCount; c++)
                    {
                        double d = Dist2(samples[i], centroids[c]);
                        if (d < bestD) { bestD = d; best = c; }
                    }
                    assign[i] = best;
                }

                var sumR = new double[colorCount];
                var sumG = new double[colorCount];
                var sumB = new double[colorCount];
                var count = new int[colorCount];
                for (int i = 0; i < samples.Count; i++)
                {
                    int a = assign[i];
                    sumR[a] += samples[i].r;
                    sumG[a] += samples[i].g;
                    sumB[a] += samples[i].b;
                    count[a]++;
                }
                for (int c = 0; c < colorCount; c++)
                {
                    if (count[c] == 0) continue;
                    centroids[c] = (sumR[c] / count[c], sumG[c] / count[c], sumB[c] / count[c]);
                }
            }

            return centroids.Select(c => PaletteRgb.FromRgb((int)Math.Round(c.r), (int)Math.Round(c.g), (int)Math.Round(c.b))).ToArray();
        }

        private static double Dist2((byte r, byte g, byte b) p, (double r, double g, double b) c)
        {
            double dr = p.r - c.r, dg = p.g - c.g, db = p.b - c.b;
            return dr * dr + dg * dg + db * db;
        }
    }

    // --- NeuQuant (Dekker-style, generalized network size) ---
    private static class NeuQuant
    {
        public static PaletteRgb[] Build(PixelBuffer source, int colorCount, int bits)
        {
            var samples = PixelSampler.CollectOpaque(source, bits, maxSamples: 30000);
            int netsize = colorCount;
            var network = new double[netsize, 3];
            for (int i = 0; i < netsize; i++)
            {
                double v = i * 255.0 / Math.Max(1, netsize - 1);
                network[i, 0] = v; network[i, 1] = v; network[i, 2] = v;
            }

            int samplefac = Math.Max(1, samples.Count / 1000);
            int alphadec = 30 + (samplefac - 1) / 3;
            int lengthcount = samples.Count;
            int samplepixels = lengthcount / samplefac;
            int delta = Math.Max(1, samplepixels / 100);
            double alpha = 1.0;
            double radius = netsize / 8.0;

            int rad = (int)radius;
            if (rad <= 1) rad = 0;

            int step = primesStep(lengthcount);
            int pos = 0;
            for (int i = 0; i < samplepixels; i++)
            {
                var (b, g, r) = (samples[pos].b, samples[pos].g, samples[pos].r); // NeuQuant historically BGR-ish; we use RGB equally
                r = samples[pos].r; g = samples[pos].g; b = samples[pos].b;
                int j = Contest(network, netsize, r, g, b);
                AlterSingle(network, alpha, j, r, g, b);
                if (rad > 0)
                    AlterNeighbour(network, netsize, rad, j, r, g, b, alpha);

                pos += step;
                while (pos >= lengthcount) pos -= lengthcount;

                if (i % delta == 0)
                {
                    alpha -= alpha / alphadec;
                    radius *= 0.95;
                    rad = (int)radius;
                    if (rad <= 1) rad = 0;
                }
            }

            var palette = new PaletteRgb[netsize];
            for (int i = 0; i < netsize; i++)
                palette[i] = PaletteRgb.FromRgb(
                    (int)Math.Round(network[i, 0]),
                    (int)Math.Round(network[i, 1]),
                    (int)Math.Round(network[i, 2]));
            return palette;
        }

        private static int primesStep(int n)
        {
            int[] primes = [499, 491, 487, 503];
            foreach (var p in primes)
                if (n % p != 0) return p;
            return 503;
        }

        private static int Contest(double[,] network, int netsize, int r, int g, int b)
        {
            int best = 0;
            double bestd = double.MaxValue;
            for (int i = 0; i < netsize; i++)
            {
                double dr = network[i, 0] - r;
                double dg = network[i, 1] - g;
                double db = network[i, 2] - b;
                double d = dr * dr + dg * dg + db * db;
                if (d < bestd) { bestd = d; best = i; }
            }
            return best;
        }

        private static void AlterSingle(double[,] network, double alpha, int i, int r, int g, int b)
        {
            network[i, 0] -= alpha * (network[i, 0] - r);
            network[i, 1] -= alpha * (network[i, 1] - g);
            network[i, 2] -= alpha * (network[i, 2] - b);
        }

        private static void AlterNeighbour(double[,] network, int netsize, int rad, int i, int r, int g, int b, double alpha)
        {
            int lo = Math.Max(0, i - rad);
            int hi = Math.Min(netsize - 1, i + rad);
            for (int j = lo; j <= hi; j++)
            {
                if (j == i) continue;
                double a = alpha * (1.0 - Math.Abs(j - i) / (double)(rad + 1));
                network[j, 0] -= a * (network[j, 0] - r);
                network[j, 1] -= a * (network[j, 1] - g);
                network[j, 2] -= a * (network[j, 2] - b);
            }
        }
    }
}
