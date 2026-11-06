namespace Kazo.Webamp.Tracks;

/// <summary>
/// Where <see cref="TrackLibrary"/> sends the files it read: Webamp, through
/// <see cref="Player.WebampPlayer"/>, or a fake in the tests.
/// </summary>
public interface ITrackPlayer
{
    /// <summary>Adds tracks at the end of Webamp's list.</summary>
    /// <param name="tracks">The tracks, in order.</param>
    /// <param name="play">Whether the first of them starts playing.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    ValueTask AddAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default);

    /// <summary>Replaces Webamp's list.</summary>
    /// <param name="tracks">The new list, in order.</param>
    /// <param name="play">Whether its first track starts playing.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    ValueTask ReplaceAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default);
}
