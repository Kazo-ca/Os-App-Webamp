namespace Kazo.Webamp.Opening;

/// <summary>
/// Puts the files opened from the OS into Webamp one request at a time, in the order they arrived
/// (decision 5 of doc/en/plan.md).
/// </summary>
/// <remarks>
/// <para>
/// The File Explorer opens a multiple selection one file at a time: the first launches Webamp, the
/// next ones reach the open window as intents, a few milliseconds apart. Treated one by one, each
/// would play in turn and the last one would win. The queue waits <see cref="GatherWindow"/> after
/// a request arrives, then merges the requests that follow each other with the same mode: the whole
/// selection is added at once, in its order, and its first file plays.
/// </para>
/// <para>
/// Requests that arrive before Webamp is on screen wait for <see cref="Start"/>. One request is
/// handled at a time, so a slow read never lets a later request overtake it.
/// </para>
/// <para>Not thread-safe: the window uses it from its render thread.</para>
/// </remarks>
public sealed class OpenQueue
{
    /// <summary>How long the queue waits for the rest of a burst of opens.</summary>
    public static readonly TimeSpan GatherWindow = TimeSpan.FromMilliseconds(300);

    private readonly Func<OpenRequest, Task> _handle;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly List<OpenRequest> _pending = [];
    private Task _draining = Task.CompletedTask;
    private bool _started;
    private bool _stopped;

    /// <summary>Creates a queue that waits with <see cref="Task.Delay(TimeSpan)"/>.</summary>
    /// <param name="handle">Puts one request in Webamp; it reports its own errors.</param>
    public OpenQueue(Func<OpenRequest, Task> handle)
        : this(handle, static window => Task.Delay(window))
    {
    }

    /// <summary>Creates a queue with its own clock (the tests).</summary>
    /// <param name="handle">Puts one request in Webamp; it reports its own errors.</param>
    /// <param name="delay">Waits for the gather window.</param>
    public OpenQueue(Func<OpenRequest, Task> handle, Func<TimeSpan, Task> delay)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(delay);
        _handle = handle;
        _delay = delay;
    }

    /// <summary>Raised when handling a request threw; the queue goes on with the next one.</summary>
    public event Action<Exception>? Failed;

    /// <summary>Gets how many requests wait.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Adds a request; it is handled once the queue is started.</summary>
    /// <param name="request">The files and what to do with them.</param>
    public void Enqueue(OpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_stopped || request.Paths.Count == 0)
        {
            return;
        }

        _pending.Add(request);
        Drain();
    }

    /// <summary>Webamp is on screen: handles the waiting requests and the next ones.</summary>
    public void Start()
    {
        _started = true;
        Drain();
    }

    /// <summary>The window closes: forgets the waiting requests and accepts no more.</summary>
    public void Stop()
    {
        _stopped = true;
        _pending.Clear();
    }

    /// <summary>Completes when every request given so far was handled (the tests).</summary>
    public async Task WhenIdleAsync()
    {
        while (!_draining.IsCompleted)
        {
            await _draining;
        }
    }

    /// <summary>
    /// Merges requests that follow each other with the same mode, keeping their order. Two
    /// <see cref="OpenMode.Replace"/> are not merged: each replaces the list.
    /// </summary>
    /// <param name="requests">The requests, in arrival order.</param>
    public static IReadOnlyList<OpenRequest> Merge(IReadOnlyList<OpenRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);
        List<OpenRequest> merged = [];
        foreach (OpenRequest request in requests)
        {
            if (merged.Count > 0 && merged[^1].Mode == request.Mode && request.Mode != OpenMode.Replace)
            {
                merged[^1] = merged[^1] with { Paths = [.. merged[^1].Paths, .. request.Paths] };
            }
            else
            {
                merged.Add(request);
            }
        }

        return merged;
    }

    private void Drain()
    {
        if (_started && !_stopped && _pending.Count > 0 && _draining.IsCompleted)
        {
            _draining = DrainAsync();
        }
    }

    private async Task DrainAsync()
    {
        while (_pending.Count > 0 && !_stopped)
        {
            await _delay(GatherWindow);
            OpenRequest[] batch = [.. _pending];
            _pending.Clear();
            foreach (OpenRequest request in Merge(batch))
            {
                if (_stopped)
                {
                    return;
                }

                try
                {
                    await _handle(request);
                }
                catch (Exception exception)
                {
                    // One broken request (Webamp gone, a JavaScript error) must not drop the next ones.
                    Failed?.Invoke(exception);
                }
            }
        }
    }
}
