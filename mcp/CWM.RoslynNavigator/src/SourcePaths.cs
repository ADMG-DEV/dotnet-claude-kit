namespace CWM.RoslynNavigator;

/// <summary>
/// Matches a file path supplied by a caller against the absolute paths Roslyn stores.
/// Callers write paths the way the tools print them — relative and forward-slashed — while
/// Roslyn stores native paths (backslashed on Windows), so a plain EndsWith misses every
/// multi-segment hint there. A miss is not harmless: hints only narrow, so a missed hint
/// falls back to a wider set and can quietly answer for a different file.
/// </summary>
internal static class SourcePaths
{
    /// <summary>
    /// True when <paramref name="hint"/> names <paramref name="filePath"/>: the hint matches
    /// the path's trailing segments, with either separator on either side. Matching whole
    /// segments keeps 'Codes.cs' from also matching 'GameCodes.cs'.
    /// </summary>
    public static bool EndsWithPath(string? filePath, string hint)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrWhiteSpace(hint)) return false;

        var path = Normalize(filePath);
        var tail = Normalize(hint);
        while (tail.StartsWith("./", StringComparison.Ordinal)) tail = tail[2..];
        tail = tail.TrimStart('/');
        if (tail.Length == 0) return false;

        if (!path.EndsWith(tail, StringComparison.OrdinalIgnoreCase)) return false;

        return path.Length == tail.Length || path[path.Length - tail.Length - 1] == '/';
    }

    /// <summary>
    /// True when <paramref name="hint"/> occurs anywhere in <paramref name="filePath"/>, with
    /// either separator on either side. For filters that deliberately accept a folder or partial name.
    /// </summary>
    public static bool ContainsPath(string? filePath, string hint)
    {
        if (string.IsNullOrEmpty(filePath) || string.IsNullOrWhiteSpace(hint)) return false;

        return Normalize(filePath).Contains(Normalize(hint), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => path.Trim().Replace('\\', '/');
}
