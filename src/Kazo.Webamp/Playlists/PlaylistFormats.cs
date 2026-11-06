namespace Kazo.Webamp.Playlists;

/// <summary>
/// The playlist files Webamp opens (decision 10 of doc/en/plan.md): <c>.m3u</c>, <c>.m3u8</c> and
/// <c>.pls</c>. It saves <c>.m3u8</c> only.
/// </summary>
public static class PlaylistFormats
{
    /// <summary>Media type of <c>.m3u</c> (the usual one; browsers and servers also use others).</summary>
    public const string M3uMediaType = "audio/x-mpegurl";

    /// <summary>Media type of <c>.m3u8</c>.</summary>
    public const string M3u8MediaType = "application/vnd.apple.mpegurl";

    /// <summary>Media type of <c>.pls</c>.</summary>
    public const string PlsMediaType = "audio/x-scpls";

    /// <summary>The extension Webamp saves.</summary>
    public const string SaveExtension = ".m3u8";

    private static readonly Dictionary<string, PlaylistKind> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".m3u"] = PlaylistKind.M3u,
        [".m3u8"] = PlaylistKind.M3u8,
        [".pls"] = PlaylistKind.Pls,
    };

    /// <summary>Gets the playlist extensions, dot included, lowercase.</summary>
    public static IReadOnlyCollection<string> Extensions => ByExtension.Keys;

    /// <summary>Returns the kind of playlist a file name is, or <see langword="null"/> for another file.</summary>
    /// <param name="fileName">A file name or path.</param>
    public static PlaylistKind? KindOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return ByExtension.TryGetValue(Path.GetExtension(fileName), out PlaylistKind kind) ? kind : null;
    }

    /// <summary>Returns whether a file name is a playlist Webamp opens.</summary>
    /// <param name="fileName">A file name or path.</param>
    public static bool IsPlaylist(string fileName) => KindOf(fileName) is not null;
}

/// <summary>The playlist formats.</summary>
public enum PlaylistKind
{
    /// <summary><c>.m3u</c>: lines of paths, <c>#EXTINF</c> optional; UTF-8, or Latin-1 for old lists.</summary>
    M3u,

    /// <summary><c>.m3u8</c>: the same, always UTF-8.</summary>
    M3u8,

    /// <summary><c>.pls</c>: an INI file, <c>FileN</c>, <c>TitleN</c>, <c>LengthN</c>.</summary>
    Pls,
}
