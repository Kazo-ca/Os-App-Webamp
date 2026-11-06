namespace Kazo.Webamp.State;

/// <summary>
/// Groups the writes of <c>state.json</c> (decision 13): every change asks for a save, the save
/// happens once the changes have stopped for <see cref="Delay"/>, and <see cref="FlushAsync"/>
/// writes at once (the window closes). Two saves never run at the same time.
/// </summary>
/// <remarks>
/// A volume slider moved across its range is a hundred changes: one write, after the user lets go.
/// A save that fails is reported to <c>onError</c> and does not stop the next ones.
/// </remarks>
public sealed class StateSaveScheduler
{
    /// <summary>How long changes must stop before the state is written.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    private readonly Func<CancellationToken, Task> _save;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<Exception>? _onError;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _pending;
    private bool _stopped;

    /// <summary>Creates a scheduler.</summary>
    /// <param name="save">Writes the state.</param>
    /// <param name="delay">How long changes must stop before a save; <see cref="DefaultDelay"/> when <see langword="null"/>.</param>
    /// <param name="onError">Hears a save that failed.</param>
    /// <param name="wait">Waits (replaced by the tests); <see cref="Task.Delay(TimeSpan, CancellationToken)"/> by default.</param>
    public StateSaveScheduler(
        Func<CancellationToken, Task> save,
        TimeSpan? delay = null,
        Action<Exception>? onError = null,
        Func<TimeSpan, CancellationToken, Task>? wait = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        _save = save;
        Delay = delay ?? DefaultDelay;
        _onError = onError;
        _delay = wait ?? ((time, token) => Task.Delay(time, token));
    }

    /// <summary>Gets how long changes must stop before the state is written.</summary>
    public TimeSpan Delay { get; }

    /// <summary>Gets whether a save is waiting for the changes to stop.</summary>
    public bool IsPending => _pending is not null;

    /// <summary>Asks for a save once the changes stop; each call starts the wait again.</summary>
    public void Schedule()
    {
        if (_stopped)
        {
            return;
        }

        _pending?.Cancel();
        CancellationTokenSource pending = new();
        _pending = pending;
        _ = WaitThenSaveAsync(pending);
    }

    /// <summary>Writes now, whether a save was waiting or not, and drops the waiting one.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task completed once the state is written (or the write failed).</returns>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        CancelPending();
        return SaveAsync(cancellationToken);
    }

    /// <summary>Writes a last time (the window closes) and ignores the requests that come after.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task completed once the state is written.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        CancelPending();
        await SaveAsync(cancellationToken);
    }

    private async Task WaitThenSaveAsync(CancellationTokenSource pending)
    {
        try
        {
            await _delay(Delay, pending.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!ReferenceEquals(_pending, pending))
        {
            return;
        }

        _pending = null;
        pending.Dispose();
        await SaveAsync(CancellationToken.None);
    }

    private void CancelPending()
    {
        CancellationTokenSource? pending = _pending;
        _pending = null;
        pending?.Cancel();
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _save(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A full disk or a denied write must not stop Webamp: the next change tries again.
            _onError?.Invoke(exception);
        }
        finally
        {
            _gate.Release();
        }
    }
}
