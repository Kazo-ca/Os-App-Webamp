namespace Kazo.Webamp.Opening;

/// <summary>
/// Decides when to warn that the tracks held in memory are large (decision 14 of doc/en/plan.md):
/// every track is a whole file in a <c>Blob</c>, and past <see cref="Limit"/> the browser may slow
/// down or close the tab. The user may go on: it is a warning, not a refusal.
/// </summary>
/// <remarks>
/// The warning is given once each time the total goes over the limit: it comes back only after the
/// total went under the limit again (tracks removed from the list).
/// </remarks>
public sealed class LoadedMemoryWarning
{
    /// <summary>The limit: 1 GB (1 GiB, 1 073 741 824 bytes).</summary>
    public const long Limit = 1L << 30;

    private readonly long _limit;
    private bool _warned;

    /// <summary>Creates the warning at 1 GB.</summary>
    public LoadedMemoryWarning()
        : this(Limit)
    {
    }

    /// <summary>Creates the warning at another limit (the tests).</summary>
    /// <param name="limit">The limit, in bytes.</param>
    public LoadedMemoryWarning(long limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        _limit = limit;
    }

    /// <summary>Gets whether the window must warn now, given the total held in memory.</summary>
    /// <param name="loadedBytes">The total of the tracks in Webamp's list (<c>TrackLibrary.LoadedBytes</c>).</param>
    public bool ShouldWarn(long loadedBytes)
    {
        if (loadedBytes <= _limit)
        {
            _warned = false;
            return false;
        }

        if (_warned)
        {
            return false;
        }

        _warned = true;
        return true;
    }
}
