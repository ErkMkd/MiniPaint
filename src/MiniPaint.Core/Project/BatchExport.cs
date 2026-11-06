using MiniPaint.Core.Document;

namespace MiniPaint.Core.Project;

public enum ExportFormat
{
    Png,
    Jpeg
}

/// <summary>
/// File → Export All Images: one merged file per open document into a single folder.
/// </summary>
public static class BatchExport
{
    public static string Extension(ExportFormat format) => format switch
    {
        ExportFormat.Png => ".png",
        ExportFormat.Jpeg => ".jpg",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
    };

    public static void Export(PaintDocument document, string path, ExportFormat format, int jpegQuality)
    {
        switch (format)
        {
            case ExportFormat.Png:
                ProjectSerializer.ExportPng(document, path);
                break;
            case ExportFormat.Jpeg:
                ProjectSerializer.ExportJpeg(document, path, jpegQuality);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, null);
        }
    }

    /// <summary>
    /// One output path per document name, in order. Invalid file-name characters become '_';
    /// names repeated within the batch get " (2)", " (3)"… When <paramref name="fileExists"/> is
    /// given, names already taken on disk are skipped the same way (no overwrite).
    /// </summary>
    public static IReadOnlyList<string> PlanPaths(
        IEnumerable<string> documentNames,
        string folder,
        ExportFormat format,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(documentNames);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        string ext = Extension(format);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var name in documentNames)
        {
            string stem = SanitizeFileName(name);
            string path = Path.Combine(folder, stem + ext);
            for (int n = 2; taken.Contains(path) || fileExists?.Invoke(path) == true; n++)
                path = Path.Combine(folder, $"{stem} ({n}){ext}");

            taken.Add(path);
            result.Add(path);
        }
        return result;
    }

    public static string SanitizeFileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (name ?? "").Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        // Windows silently drops trailing dots and spaces.
        string stem = new string(chars).Trim().TrimEnd('.');
        return stem.Length == 0 ? "Untitled" : stem;
    }
}
