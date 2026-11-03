namespace MiniPaint.Core.ColorMode;

public enum ColorModeKind
{
    Rgba,
    Indexed,
    Ecs,
    Aga
}

public enum QuantizerKind
{
    MedianCut,
    Octree,
    Wu,
    KMeansPlusPlus,
    NeuQuant
}

public enum DitherKind
{
    None,
    FloydSteinberg,
    Bayer4x4,
    Bayer8x8,
    Atkinson,
    Stucki,
    BlueNoise
}
