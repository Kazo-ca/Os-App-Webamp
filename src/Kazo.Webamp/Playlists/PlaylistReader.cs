using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.Playlists;

/// <summary>
/// Reads <c>.m3u</c>, <c>.m3u8</c> and <c>.pls</c> files (decision 10 of doc/en/plan.md), the way
/// Winamp wrote and read them.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Paths may be absolute or relative; a relative path is resolved from the folder of the
/// list. Windows separators (<c>\</c>) are accepted, so a list made on Windows with relative paths
/// works as it is.</item>
/// <item><c>#EXTINF:seconds,title</c> gives the title and the duration of the next track (the
/// attributes some players add before the comma are skipped).</item>
/// <item>Web addresses are left out and reported in <see cref="PlaylistReadResult.IgnoredUrls"/>:
/// no music from the internet in the first version (decision 9). A <c>file://</c> address is a
/// path.</item>
/// <item>A Windows drive (<c>C:\</c>) or a network share (<c>\\server</c>) is not a path of the
/// Kazo OS file system: reported in <see cref="PlaylistReadResult.Unresolved"/>.</item>
/// <item>Encoding: a byte order mark wins; otherwise <c>.m3u8</c> is UTF-8, and <c>.m3u</c> and
/// <c>.pls</c> are UTF-8 when they are valid UTF-8, Latin-1 when not (lists of the Winamp era).</item>
/// </list>
/// The reader does not look at the files: one that is missing fails when the track is read
/// (<see cref="Tracks.TrackLibrary"/>), like any other file.
/// </remarks>
public static partial class PlaylistReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads a playlist file.</summary>
    /// <param name="playlistPath">Where the list is: relative paths start from its folder, its
    /// extension gives the format.</param>
    /// <param name="bytes">The whole file.</param>
    /// <returns>The tracks, and what was left out.</returns>
    /// <exception cref="ArgumentException"><paramref name="playlistPath"/> is not a playlist.</exception>
    public static PlaylistReadResult Read(VirtualPath playlistPath, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        PlaylistKind kind = PlaylistFormats.KindOf(playlistPath.Value)
            ?? throw new ArgumentException($"'{playlistPath.Value}' is not a playlist.", nameof(playlistPath));
        string text = Decode(bytes, kind);
        string folder = PlaylistPaths.FolderOf(playlistPath);
        return kind == PlaylistKind.Pls ? ReadPls(text, folder) : ReadM3u(text, folder);
    }

    /// <summary>Decodes the text of a list: byte order mark, then UTF-8, then Latin-1 (not for <c>.m3u8</c>).</summary>
    /// <param name="bytes">The whole file.</param>
    /// <param name="kind">Its format.</param>
    /// <returns>The text, without its byte order mark.</returns>
    public static string Decode(byte[] bytes, PlaylistKind kind)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return Encoding.UTF8.GetString(span[3..]);
        }

        if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(span[2..]);
        }

        if (span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(span[2..]);
        }

        if (kind == PlaylistKind.M3u8)
        {
            return Encoding.UTF8.GetString(span);
        }

        try
        {
            return StrictUtf8.GetString(span);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(span);
        }
    }

    private static PlaylistReadResult ReadM3u(string text, string folder)
    {
        Collector collector = new(folder);
        string? title = null;
        TimeSpan? duration = null;
        foreach (string rawLine in SplitLines(text))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('#'))
            {
                if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                {
                    (duration, title) = ParseExtInf(line["#EXTINF:".Length..]);
                }

                // #EXTM3U and the other comments say nothing about the tracks.
                continue;
            }

            collector.Add(line, title, duration);
            title = null;
            duration = null;
        }

        return collector.Result();
    }

    private static PlaylistReadResult ReadPls(string text, string folder)
    {
        SortedDictionary<int, PlsEntry> entries = [];
        foreach (string rawLine in SplitLines(text))
        {
            Match match = PlsLine().Match(rawLine.Trim());
            if (!match.Success
                || !int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            {
                // [playlist], NumberOfEntries, Version, comments: nothing about one track.
                continue;
            }

            string value = match.Groups["value"].Value.Trim();
            PlsEntry entry = entries.GetValueOrDefault(number);
            entries[number] = match.Groups["key"].Value.ToUpperInvariant() switch
            {
                "FILE" => entry with { File = value },
                "TITLE" => entry with { Title = value.Length > 0 ? value : null },
                _ => entry with { Duration = ParseSeconds(value) },
            };
        }

        Collector collector = new(folder);
        foreach (PlsEntry entry in entries.Values)
        {
            if (!string.IsNullOrWhiteSpace(entry.File))
            {
                collector.Add(entry.File, entry.Title, entry.Duration);
            }
        }

        return collector.Result();
    }

    // "123,Artist - Title", or "-1 tvg-name="x",Title" from other players.
    private static (TimeSpan? Duration, string? Title) ParseExtInf(string value)
    {
        int comma = value.IndexOf(',');
        string head = comma < 0 ? value : value[..comma];
        string? title = comma < 0 ? null : value[(comma + 1)..].Trim();
        string seconds = head.Trim().Split(' ', 2)[0];
        return (ParseSeconds(seconds), string.IsNullOrEmpty(title) ? null : title);
    }

    // -1 (and 0) mean "unknown" in Winamp's lists.
    private static TimeSpan? ParseSeconds(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) && seconds > 0 && double.IsFinite(seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static string[] SplitLines(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

    [GeneratedRegex(@"^(?<key>File|Title|Length)(?<number>[0-9]+)\s*=(?<value>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlsLine();

    // "http://", "https://", "rtsp://"…: two letters or more before "://" (one letter is a drive).
    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]+://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebAddress();

    [GeneratedRegex(@"^[a-z]:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WindowsDrive();

    private readonly record struct PlsEntry(string? File, string? Title, TimeSpan? Duration);

    /// <summary>Turns the locations of a list into paths, and keeps what it left out.</summary>
    private sealed class Collector(string folder)
    {
        private readonly List<PlaylistEntry> _entries = [];
        private readonly List<string> _ignoredUrls = [];
        private readonly List<string> _unresolved = [];

        public void Add(string location, string? title, TimeSpan? duration)
        {
            string path;
            if (location.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryFileUri(location, out string? local))
                {
                    _unresolved.Add(location);
                    return;
                }

                path = local;
            }
            else if (WebAddress().IsMatch(location))
            {
                _ignoredUrls.Add(location);
                return;
            }
            else
            {
                path = location.Replace('\\', '/');
            }

            if (WindowsDrive().IsMatch(path) || path.StartsWith("//", StringComparison.Ordinal))
            {
                _unresolved.Add(location);
                return;
            }

            try
            {
                VirtualPath resolved = VirtualPath.Parse(path.StartsWith('/') ? path : $"{folder}/{path}");
                _entries.Add(new PlaylistEntry(resolved, title, duration));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                _unresolved.Add(location);
            }
        }

        public PlaylistReadResult Result() => new(_entries, _ignoredUrls, _unresolved);

        // file:///home/neo/a%20b.mp3 → /home/neo/a b.mp3; file:///C:/… and file://server/… stay unresolved.
        private static bool TryFileUri(string location, [NotNullWhen(true)] out string? path)
        {
            path = null;
            if (!Uri.TryCreate(location, UriKind.Absolute, out Uri? uri) || !string.IsNullOrEmpty(uri.Host))
            {
                return false;
            }

            string local = Uri.UnescapeDataString(uri.AbsolutePath);
            if (!local.StartsWith('/') || WindowsDrive().IsMatch(local.TrimStart('/')))
            {
                return false;
            }

            path = local;
            return true;
        }
    }
}
