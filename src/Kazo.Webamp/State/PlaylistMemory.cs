namespace Kazo.Webamp.State;

/// <summary>
/// The saved list beside Webamp's own (decision 13 of doc/en/plan.md): a file that has disappeared
/// (a network drive not mounted, a file moved) <b>stays in the list</b> and is skipped when playing.
/// Webamp only holds the files that could be read; this class keeps the missing ones at their place,
/// after the track that preceded them, so the next run finds them again when they are back.
/// </summary>
/// <remarks>
/// Missing files are forgotten when Webamp's list is emptied (the user cleared it, or replaced it
/// with another list). A missing file whose preceding track left the list goes to the end.
/// </remarks>
public sealed class PlaylistMemory
{
    private readonly List<MissingTrack> _missing;

    private PlaylistMemory(List<MissingTrack> missing)
    {
        _missing = missing;
    }

    /// <summary>Gets a memory with no missing file (a first run).</summary>
    public static PlaylistMemory Empty => new([]);

    /// <summary>Gets the files of the saved list that could not be read, in order.</summary>
    public IReadOnlyList<string> MissingTracks => [.. _missing.Select(track => track.Path)];

    /// <summary>
    /// Matches the saved list with what could be read at start.
    /// </summary>
    /// <param name="saved">The saved list (<see cref="WebampState.Tracks"/>).</param>
    /// <param name="savedCurrent">Index of the current track in <paramref name="saved"/>.</param>
    /// <param name="loaded">The files given to Webamp, in order: <paramref name="saved"/> without the missing ones.</param>
    /// <param name="loadedKeys">The keys <see cref="Tracks.TrackLibrary"/> gave to <paramref name="loaded"/>, in the same order.</param>
    /// <returns>
    /// The memory, and where to start in Webamp's list: the saved current track, or the next one
    /// that could be read when it is missing (<c>Resumed</c> is then <see langword="false"/>: the
    /// saved position belongs to another file).
    /// </returns>
    public static RestoredPlaylist Restore(
        IReadOnlyList<string> saved,
        int? savedCurrent,
        IReadOnlyList<string> loaded,
        IReadOnlyList<string> loadedKeys)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(loadedKeys);
        if (loadedKeys.Count != loaded.Count)
        {
            throw new ArgumentException("Every loaded file has one key.", nameof(loadedKeys));
        }

        List<MissingTrack> missing = [];
        int next = 0;
        string? anchor = null;
        int? current = null;
        bool resumed = false;
        for (int index = 0; index < saved.Count; index++)
        {
            bool isLoaded = next < loaded.Count && string.Equals(saved[index], loaded[next], StringComparison.Ordinal);
            if (isLoaded)
            {
                if (savedCurrent is { } wanted && current is null && index >= wanted)
                {
                    current = next;
                    resumed = index == wanted;
                }

                anchor = loadedKeys[next];
                next++;
            }
            else
            {
                missing.Add(new MissingTrack(anchor, saved[index]));
            }
        }

        return new RestoredPlaylist(new PlaylistMemory(missing), current, resumed);
    }

    /// <summary>
    /// The list to save: Webamp's files with the missing ones put back at their place.
    /// </summary>
    /// <param name="keys">The keys of Webamp's list, in its order (<see cref="Tracks.TrackLibrary.Keys"/>).</param>
    /// <param name="paths">Their files, in the same order (<see cref="Tracks.TrackLibrary.Paths"/>).</param>
    /// <param name="currentKey">The key of the current track, or <see langword="null"/>.</param>
    /// <returns>The files to save and the index of the current one.</returns>
    public (IReadOnlyList<string> Tracks, int? Current) Compose(
        IReadOnlyList<string> keys,
        IReadOnlyList<string> paths,
        string? currentKey)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(paths);
        if (keys.Count == 0)
        {
            // The user emptied the list: the missing files go with it.
            _missing.Clear();
            return ([], null);
        }

        HashSet<string> present = [.. keys];
        List<string> tracks = [.. MissingAfter(null)];
        int? current = null;
        for (int index = 0; index < keys.Count; index++)
        {
            if (string.Equals(keys[index], currentKey, StringComparison.Ordinal))
            {
                current = tracks.Count;
            }

            tracks.Add(paths[index]);
            tracks.AddRange(MissingAfter(keys[index]));
        }

        tracks.AddRange(_missing.Where(track => track.After is not null && !present.Contains(track.After)).Select(track => track.Path));
        return (tracks, current);
    }

    private IEnumerable<string> MissingAfter(string? key) =>
        _missing.Where(track => string.Equals(track.After, key, StringComparison.Ordinal)).Select(track => track.Path);

    private readonly record struct MissingTrack(string? After, string Path);
}

/// <summary>What <see cref="PlaylistMemory.Restore"/> found.</summary>
/// <param name="Memory">The missing files, to put back in the saved list.</param>
/// <param name="Current">Index in Webamp's list of the track to start from, or <see langword="null"/>.</param>
/// <param name="Resumed">Whether that track is the saved current track (its saved position applies).</param>
public sealed record RestoredPlaylist(PlaylistMemory Memory, int? Current, bool Resumed);
