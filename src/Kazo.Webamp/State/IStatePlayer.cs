namespace Kazo.Webamp.State;

/// <summary>
/// What <see cref="WebampStateKeeper"/> needs from Webamp: read what it holds, put the current track
/// back, apply a skin. <see cref="Player.WebampPlayer"/> implements it; the tests use a fake.
/// </summary>
public interface IStatePlayer
{
    /// <summary>Reads what Webamp holds now.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The current track's key and Webamp's settings.</returns>
    ValueTask<PlayerSnapshot> ReadStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a track of the list current without playing it; with a position, Webamp waits paused at
    /// that position (Play continues from there, as after a pause).
    /// </summary>
    /// <param name="index">Index in Webamp's list.</param>
    /// <param name="position">Seconds into the track; 0 leaves it stopped at its start.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task completed once Webamp was told.</returns>
    ValueTask ResumeAsync(int index, double position, CancellationToken cancellationToken = default);

    /// <summary>Applies a skin file's bytes (a <c>.wsz</c>).</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="bytes">Its content.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task completed once Webamp was told.</returns>
    ValueTask ApplySkinAsync(string name, byte[] bytes, CancellationToken cancellationToken = default);
}

/// <summary>What Webamp holds at one moment.</summary>
/// <param name="CurrentKey">
/// The key (<see cref="Tracks.TrackLibrary"/>) of the current track, or <see langword="null"/> when
/// there is none or it is a file dropped from the computer (Webamp's own, without a path).
/// </param>
/// <param name="Settings">Position, volume, balance, equalizer, windows.</param>
public sealed record PlayerSnapshot(string? CurrentKey, PlayerSettings Settings);
