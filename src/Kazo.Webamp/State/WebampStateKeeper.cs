using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.State;

/// <summary>
/// Puts a saved <see cref="WebampState"/> back into Webamp at start, and builds the state to save
/// from what Webamp holds (decision 13 of doc/en/plan.md).
/// </summary>
/// <remarks>
/// Webamp's settings and window layout go to Webamp when it is created
/// (<see cref="Player.WebampPlayer.StartAsync"/>): they are drawn from the first frame. This class
/// does the rest, once Webamp is on screen: the list, the current track and its position, the skin.
/// </remarks>
public sealed class WebampStateKeeper
{
    private readonly IStatePlayer _player;
    private readonly TrackLibrary _tracks;
    private readonly Func<string, CancellationToken, Task<byte[]>> _readAllBytes;
    private PlaylistMemory _memory = PlaylistMemory.Empty;

    /// <summary>Creates a keeper that reads skins through the Kazo OS file system.</summary>
    /// <param name="player">Webamp.</param>
    /// <param name="tracks">The files of Webamp's list.</param>
    public WebampStateKeeper(IStatePlayer player, TrackLibrary tracks)
        : this(player, tracks, KazoFile.ReadAllBytesAsync)
    {
    }

    /// <summary>Creates a keeper with its own file reader (the tests).</summary>
    /// <param name="player">Webamp.</param>
    /// <param name="tracks">The files of Webamp's list.</param>
    /// <param name="readAllBytes">Reads a whole file (the skin).</param>
    public WebampStateKeeper(IStatePlayer player, TrackLibrary tracks, Func<string, CancellationToken, Task<byte[]>> readAllBytes)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(readAllBytes);
        _player = player;
        _tracks = tracks;
        _readAllBytes = readAllBytes;
    }

    /// <summary>
    /// Gets or sets the skin file applied, saved with the state; <see langword="null"/> is Webamp's
    /// default skin. The skins work item sets it when the user picks a skin.
    /// </summary>
    public string? SkinPath { get; set; }

    /// <summary>Gets the files of the saved list that could not be read at start; they stay in the saved list.</summary>
    public IReadOnlyList<string> MissingTracks => _memory.MissingTracks;

    /// <summary>
    /// Puts the saved list, current track, position and skin back. A file that cannot be read is
    /// skipped, and stays in the saved list.
    /// </summary>
    /// <param name="state">The saved state.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The files read and the ones that were not.</returns>
    public async Task<TrackLoadResult> RestoreAsync(WebampState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await RestoreSkinAsync(state.Skin, cancellationToken);

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
        return new WebampState(tracks, current, settings, SkinPath);
    }

    private async Task RestoreSkinAsync(string? skin, CancellationToken cancellationToken)
    {
        SkinPath = null;
        if (skin is null)
        {
            return;
        }

        try
        {
            byte[] bytes = await _readAllBytes(skin, cancellationToken);
            await _player.ApplySkinAsync(KazoPath.GetFileName(skin) ?? skin, bytes, cancellationToken);
            SkinPath = skin;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The skin is gone: Webamp keeps its default skin, and the next save forgets it.
        }
    }
}
