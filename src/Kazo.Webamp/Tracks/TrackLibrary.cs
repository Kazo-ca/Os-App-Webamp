using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.IO;

namespace Kazo.Webamp.Tracks;

/// <summary>
/// The files in Webamp's list (decision 14 of doc/en/plan.md): each one is read whole with
/// <see cref="KazoFile.ReadAllBytesAsync"/> when it is added, handed to Webamp as a <c>Blob</c>,
/// and released when it leaves the list.
/// </summary>
/// <remarks>
/// <para>
/// The library gives every track a key. The host module (<c>wwwroot/js/webamp-host.js</c>) keeps the
/// <c>Blob</c> address of each key and reports, through <see cref="OnPlaylistChanged"/>, the keys
/// still in Webamp's list, in its order: a key that is no longer there has left the list (removed,
/// cleared, replaced), its address was given back to the browser, and the library forgets it.
/// </para>
/// <para>
/// Tracks the user drops from the computer onto Webamp are Webamp's own: they have no key and no
/// path, so they never appear in <see cref="Paths"/>.
/// </para>
/// <para>Not thread-safe: the window calls it from its render thread.</para>
/// </remarks>
public sealed class TrackLibrary
{
    private readonly ITrackPlayer _player;
    private readonly Func<string, CancellationToken, Task<byte[]>> _readAllBytes;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private List<string> _order = [];
    private long _nextKey;

    /// <summary>Creates a library that reads files through the Kazo OS file system.</summary>
    /// <param name="player">Where the files go: Webamp.</param>
    public TrackLibrary(ITrackPlayer player)
        : this(player, KazoFile.ReadAllBytesAsync)
    {
    }

    /// <summary>Creates a library with its own file reader (the tests).</summary>
    /// <param name="player">Where the files go.</param>
    /// <param name="readAllBytes">Reads a whole file; throws an <see cref="IOException"/> or an
    /// <see cref="UnauthorizedAccessException"/> when it cannot.</param>
    public TrackLibrary(ITrackPlayer player, Func<string, CancellationToken, Task<byte[]>> readAllBytes)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(readAllBytes);
        _player = player;
        _readAllBytes = readAllBytes;
    }

    /// <summary>Raised when <see cref="Paths"/> changed.</summary>
    public event Action? Changed;

    /// <summary>Gets the files in Webamp's list, in its order.</summary>
    public IReadOnlyList<VirtualPath> Paths => [.. _order.Select(key => _entries[key].Path)];

    /// <summary>
    /// Gets the keys of the files in Webamp's list, in its order (the same order as <see cref="Paths"/>):
    /// the host module names the current track by its key.
    /// </summary>
    public IReadOnlyList<string> Keys => [.. _order];

    /// <summary>Gets the total size of the files held in memory for Webamp, in bytes.</summary>
    public long LoadedBytes => _entries.Values.Sum(entry => entry.Size);

    /// <summary>Adds files at the end of Webamp's list, in order.</summary>
    /// <param name="paths">The files.</param>
    /// <param name="play">Whether the first file read starts playing.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The files added and the ones that could not be read.</returns>
    public async Task<TrackLoadResult> AddAsync(IReadOnlyList<VirtualPath> paths, bool play, CancellationToken cancellationToken = default)
    {
        (List<TrackData> tracks, TrackLoadResult result) = await ReadAsync(paths, cancellationToken);
        if (tracks.Count > 0)
        {
            // Known before Webamp hears of them: its list changes are reported while it adds them.
            Remember(tracks, result.Added);
            try
            {
                await _player.AddAsync(tracks, play, cancellationToken);
            }
            catch
            {
                Forget(tracks);
                throw;
            }

            _order = [.. _order.Where(_entries.ContainsKey).Except(tracks.Select(track => track.Key)), .. tracks.Select(track => track.Key)];
            Changed?.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Replaces Webamp's list with these files. When none can be read the list stays as it was, so a
    /// broken file never empties the user's list.
    /// </summary>
    /// <param name="paths">The files.</param>
    /// <param name="play">Whether the first file read starts playing.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The files now in the list and the ones that could not be read.</returns>
    public async Task<TrackLoadResult> ReplaceAsync(IReadOnlyList<VirtualPath> paths, bool play, CancellationToken cancellationToken = default)
    {
        (List<TrackData> tracks, TrackLoadResult result) = await ReadAsync(paths, cancellationToken);
        if (tracks.Count == 0 && paths.Count > 0)
        {
            return result;
        }

        Remember(tracks, result.Added);
        try
        {
            await _player.ReplaceAsync(tracks, play, cancellationToken);
        }
        catch
        {
            Forget(tracks);
            throw;
        }

        HashSet<string> keys = [.. tracks.Select(track => track.Key)];
        foreach (string key in _entries.Keys.Where(key => !keys.Contains(key)).ToList())
        {
            _entries.Remove(key);
        }

        _order = [.. tracks.Select(track => track.Key)];
        Changed?.Invoke();
        return result;
    }

    /// <summary>
    /// Called with the keys still in Webamp's list, in its order: the others left it and their
    /// memory was given back.
    /// </summary>
    /// <param name="keys">The keys reported by the host module.</param>
    public void OnPlaylistChanged(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        List<string> order = [.. keys.Where(_entries.ContainsKey)];
        HashSet<string> kept = [.. order];
        foreach (string key in _entries.Keys.Where(key => !kept.Contains(key)).ToList())
        {
            _entries.Remove(key);
        }

        if (!order.SequenceEqual(_order))
        {
            _order = order;
            Changed?.Invoke();
        }
    }

    private async Task<(List<TrackData> Tracks, TrackLoadResult Result)> ReadAsync(IReadOnlyList<VirtualPath> paths, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<TrackData> tracks = [];
        List<VirtualPath> added = [];
        List<TrackLoadFailure> failures = [];
        foreach (VirtualPath path in paths)
        {
            try
            {
                byte[] bytes = await _readAllBytes(path.Value, cancellationToken);
                string name = KazoPath.GetFileName(path.Value) ?? path.Value;
                tracks.Add(new TrackData($"t{++_nextKey}", name, AudioMediaTypes.ForFile(name), bytes));
                added.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new TrackLoadFailure(path, exception.Message));
            }
        }

        return (tracks, new TrackLoadResult(added, failures));
    }

    private void Remember(List<TrackData> tracks, IReadOnlyList<VirtualPath> paths)
    {
        for (int index = 0; index < tracks.Count; index++)
        {
            _entries[tracks[index].Key] = new Entry(paths[index], tracks[index].Bytes.LongLength);
        }
    }

    private void Forget(List<TrackData> tracks)
    {
        foreach (TrackData track in tracks)
        {
            _entries.Remove(track.Key);
        }
    }

    private readonly record struct Entry(VirtualPath Path, long Size);
}
