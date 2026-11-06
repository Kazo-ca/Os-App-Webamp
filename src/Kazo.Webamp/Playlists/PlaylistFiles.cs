using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.Player;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.Playlists;

/// <summary>
/// Opens and saves playlist files through the Kazo OS file system (decision 10 of doc/en/plan.md):
/// what the window runs for a <c>.m3u</c>/<c>.m3u8</c>/<c>.pls</c> it opens, for "Load list" and
/// for "Save list".
/// </summary>
public sealed class PlaylistFiles
{
    private readonly Func<string, CancellationToken, Task<byte[]>> _readAllBytes;
    private readonly Func<string, byte[], CancellationToken, Task> _writeAllBytes;

    /// <summary>Creates the service on <see cref="KazoFile"/>.</summary>
    public PlaylistFiles()
        : this(KazoFile.ReadAllBytesAsync, KazoFile.WriteAllBytesAsync)
    {
    }

    /// <summary>Creates the service with its own file access (the tests).</summary>
    /// <param name="readAllBytes">Reads a whole file; throws an <see cref="IOException"/> or an
    /// <see cref="UnauthorizedAccessException"/> when it cannot.</param>
    /// <param name="writeAllBytes">Writes a whole file, replacing it.</param>
    public PlaylistFiles(
        Func<string, CancellationToken, Task<byte[]>> readAllBytes,
        Func<string, byte[], CancellationToken, Task> writeAllBytes)
    {
        ArgumentNullException.ThrowIfNull(readAllBytes);
        ArgumentNullException.ThrowIfNull(writeAllBytes);
        _readAllBytes = readAllBytes;
        _writeAllBytes = writeAllBytes;
    }

    /// <summary>Reads a playlist file.</summary>
    /// <param name="path">The list.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Its tracks, and what was left out.</returns>
    /// <exception cref="IOException">The list cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The app may not read it.</exception>
    public async Task<PlaylistReadResult> ReadAsync(VirtualPath path, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await _readAllBytes(path.Value, cancellationToken);
        return PlaylistReader.Read(path, bytes);
    }

    /// <summary>
    /// Turns paths to open into tracks: an audio file is one track, a playlist its tracks (opening a
    /// list adds its tracks, decision 5), in order.
    /// </summary>
    /// <param name="paths">Files to open.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The tracks, and what the lists left out or could not read.</returns>
    public async Task<PlaylistExpansion> ExpandAsync(IReadOnlyList<VirtualPath> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<PlaylistEntry> entries = [];
        List<string> ignoredUrls = [];
        List<string> unresolved = [];
        List<TrackLoadFailure> failures = [];
        foreach (VirtualPath path in paths)
        {
            if (!PlaylistFormats.IsPlaylist(path.Value))
            {
                entries.Add(new PlaylistEntry(path));
                continue;
            }

            try
            {
                PlaylistReadResult list = await ReadAsync(path, cancellationToken);
                entries.AddRange(list.Entries);
                ignoredUrls.AddRange(list.IgnoredUrls);
                unresolved.AddRange(list.Unresolved);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new TrackLoadFailure(path, exception.Message));
            }
        }

        return new PlaylistExpansion(entries, ignoredUrls, unresolved, failures);
    }

    /// <summary>
    /// The tracks to save from Webamp's list: the ones <paramref name="tracks"/> knows the file of,
    /// with Webamp's title and duration.
    /// </summary>
    /// <param name="tracks">The library that gave Webamp its tracks.</param>
    /// <param name="listed">Webamp's list, as the host module reports it.</param>
    public static IReadOnlyList<PlaylistEntry> ToEntries(TrackLibrary tracks, IReadOnlyList<WebampListEntry> listed)
    {
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(listed);
        List<PlaylistEntry> entries = [];
        foreach (WebampListEntry entry in listed)
        {
            if (tracks.TryGetPath(entry.Key, out VirtualPath path))
            {
                TimeSpan? duration = entry.Duration is > 0 and var seconds && double.IsFinite(seconds) ? TimeSpan.FromSeconds(seconds) : null;
                entries.Add(new PlaylistEntry(path, entry.Title, duration));
            }
        }

        return entries;
    }

    /// <summary>Writes the tracks as a <c>.m3u8</c> list (<see cref="PlaylistWriter"/>).</summary>
    /// <param name="path">Where to save; relative paths start from its folder.</param>
    /// <param name="entries">The tracks, in order.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="IOException">The list cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The app may not write there.</exception>
    public Task SaveAsync(VirtualPath path, IReadOnlyList<PlaylistEntry> entries, CancellationToken cancellationToken = default) =>
        _writeAllBytes(path.Value, PlaylistWriter.WriteM3u8(path, entries), cancellationToken);
}

/// <summary>The tracks of files opened together, playlists expanded.</summary>
/// <param name="Entries">The tracks, in order.</param>
/// <param name="IgnoredUrls">Web addresses the lists held (not played, decision 9).</param>
/// <param name="Unresolved">Locations of the lists that are not a Kazo OS path.</param>
/// <param name="Failures">Lists that could not be read.</param>
public sealed record PlaylistExpansion(
    IReadOnlyList<PlaylistEntry> Entries,
    IReadOnlyList<string> IgnoredUrls,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<TrackLoadFailure> Failures);
