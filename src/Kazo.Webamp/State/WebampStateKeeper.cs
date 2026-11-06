using Kazo.Os.App.Abstractions;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.State;

/// <summary>
/// Puts a saved <see cref="WebampState"/> back into Webamp at start, and builds the state to save
/// from what Webamp holds (decision 13 of doc/en/plan.md).
/// </summary>
/// <remarks>
/// Webamp's settings and window layout go to Webamp when it is created
/// (<see cref="Player.WebampPlayer.StartAsync"/>): they are drawn from the first frame. This class
/// does the rest, once Webamp is on screen: the list, the current track and its position. The skin
/// is the skins work item's (<c>skins.json</c>).
/// </remarks>
public sealed class WebampStateKeeper
{
    private readonly IStatePlayer _player;
    private readonly TrackLibrary _tracks;
    private PlaylistMemory _memory = PlaylistMemory.Empty;

    /// <summary>Creates a keeper.</summary>
    /// <param name="player">Webamp.</param>
    /// <param name="tracks">The files of Webamp's list.</param>
    public WebampStateKeeper(IStatePlayer player, TrackLibrary tracks)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(tracks);
        _player = player;
        _tracks = tracks;
    }

    /// <summary>Gets the files of the saved list that could not be read at start; they stay in the saved list.</summary>
    public IReadOnlyList<string> MissingTracks => _memory.MissingTracks;

    /// <summary>
    /// Puts the saved list, current track and position back. A file that cannot be read is
    /// skipped, and stays in the saved list.
    /// </summary>
    /// <param name="state">The saved state.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The files read and the ones that were not.</returns>
    public async Task<TrackLoadResult> RestoreAsync(WebampState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        List<VirtualPath> paths = [.. state.Tracks.Select(VirtualPath.Parse)];
        TrackLoadResult result = paths.Count == 0
            ? new TrackLoadResult([], [])
            : await _tracks.AddAsync(paths, play: false, cancellationToken);

        RestoredPlaylist restored = PlaylistMemory.Restore(
            state.Tracks,
            state.CurrentTrack,
            [.. result.Added.Select(path => path.Value)],
            [.. _tracks.Keys.TakeLast(result.Added.Count)]);
        _memory = restored.Memory;

        if (restored.Current is { } current)
        {
            await _player.ResumeAsync(current, restored.Resumed ? state.Player.Position : 0, cancellationToken);
        }

        return result;
    }

    /// <summary>Builds the state to save from what Webamp holds now.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The state.</returns>
    public async Task<WebampState> CaptureAsync(CancellationToken cancellationToken = default)
    {
        PlayerSnapshot snapshot = await _player.ReadStateAsync(cancellationToken);
        (IReadOnlyList<string> tracks, int? current) = _memory.Compose(
            _tracks.Keys,
            [.. _tracks.Paths.Select(path => path.Value)],
            snapshot.CurrentKey);
        PlayerSettings settings = current is null ? snapshot.Settings with { Position = 0 } : snapshot.Settings;
        return new WebampState(tracks, current, settings);
    }
}
