using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.Tests.Tracks;

/// <summary>Stands for Webamp: records what the library sent and plays its list changes back.</summary>
internal sealed class FakeTrackPlayer : ITrackPlayer
{
    public List<(string Call, IReadOnlyList<TrackData> Tracks, bool Play)> Calls { get; } = [];

    /// <summary>The keys of Webamp's list, in order, as the host module would report them.</summary>
    public List<string> Listed { get; } = [];

    /// <summary>Called during each call, like the module reporting the list while Webamp adds tracks.</summary>
    public Action<IReadOnlyList<string>>? ReportDuringCall { get; set; }

    public bool Throw { get; set; }

    public ValueTask AddAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default)
    {
        Calls.Add(("add", tracks, play));
        if (Throw)
        {
            throw new InvalidOperationException("Webamp is gone.");
        }

        Listed.AddRange(tracks.Select(track => track.Key));
        ReportDuringCall?.Invoke([.. Listed]);
        return ValueTask.CompletedTask;
    }

    public ValueTask ReplaceAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default)
    {
        Calls.Add(("replace", tracks, play));
        if (Throw)
        {
            throw new InvalidOperationException("Webamp is gone.");
        }

        Listed.Clear();
        Listed.AddRange(tracks.Select(track => track.Key));
        ReportDuringCall?.Invoke([.. Listed]);
        return ValueTask.CompletedTask;
    }
}
