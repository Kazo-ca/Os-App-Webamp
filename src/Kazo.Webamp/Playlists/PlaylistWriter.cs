using System.Globalization;
using System.Text;
using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.Playlists;

/// <summary>
/// Writes a <c>.m3u8</c> list (decision 10 of doc/en/plan.md): UTF-8 without byte order mark,
/// <c>#EXTM3U</c>, then for each track <c>#EXTINF:seconds,title</c> and its path relative to the
/// list's folder.
/// </summary>
/// <remarks>
/// Winamp and most players read this form; <see cref="PlaylistReader"/> reads it back unchanged.
/// An unknown duration is written <c>-1</c>, as Winamp does; a track without a title gets its file
/// name without extension.
/// </remarks>
public static class PlaylistWriter
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Returns the text of the list.</summary>
    /// <param name="playlistPath">Where the list is saved: the paths are relative to its folder.</param>
    /// <param name="entries">The tracks, in order.</param>
    public static string WriteM3u8Text(VirtualPath playlistPath, IReadOnlyList<PlaylistEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string folder = PlaylistPaths.FolderOf(playlistPath);
        StringBuilder text = new("#EXTM3U\n");
        foreach (PlaylistEntry entry in entries)
        {
            long seconds = entry.Duration is { } duration ? (long)Math.Round(duration.TotalSeconds) : -1;
            string title = OneLine(entry.Title) ?? Path.GetFileNameWithoutExtension(entry.Path.Value);
            text.Append("#EXTINF:").Append(seconds.ToString(CultureInfo.InvariantCulture)).Append(',').Append(title).Append('\n');
            text.Append(PlaylistPaths.GetRelativePath(folder, entry.Path)).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Returns the bytes of the list (UTF-8, no byte order mark).</summary>
    /// <param name="playlistPath">Where the list is saved.</param>
    /// <param name="entries">The tracks, in order.</param>
    public static byte[] WriteM3u8(VirtualPath playlistPath, IReadOnlyList<PlaylistEntry> entries) =>
        Utf8.GetBytes(WriteM3u8Text(playlistPath, entries));

    // A title is one line of the file: a line break in a tag would start a new "path".
    private static string? OneLine(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return string.Join(' ', title.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
