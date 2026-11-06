using Kazo.Webamp.State;

namespace Kazo.Webamp.Tests.State;

/// <summary>Grouped writes: one save once the changes stop, at once on close, never two at a time.</summary>
public sealed class StateSaveSchedulerTests
{
    private readonly List<TaskCompletionSource> _waits = [];
    private int _saves;

    private StateSaveScheduler Create(Func<CancellationToken, Task>? save = null, Action<Exception>? onError = null) =>
        new(
            save ?? (_ =>
            {
                _saves++;
                return Task.CompletedTask;
            }),
            TimeSpan.FromSeconds(1),
            onError,
            (_, token) =>
            {
                TaskCompletionSource wait = new(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => wait.TrySetCanceled(token));
                _waits.Add(wait);
                return wait.Task;
            });

    // Lets the delay that is still waiting end, as if a second had passed without any change.
    private async Task ElapseAsync()
    {
        foreach (TaskCompletionSource wait in _waits.ToList())
        {
            wait.TrySetResult();
        }

        await Task.Delay(50);
    }

    [Fact]
    public async Task Many_changes_in_a_row_are_one_save()
    {
        StateSaveScheduler scheduler = Create();

        scheduler.Schedule();
        scheduler.Schedule();
        scheduler.Schedule();
        Assert.True(scheduler.IsPending);
        await ElapseAsync();

        Assert.Equal(1, _saves);
        Assert.False(scheduler.IsPending);
    }

    [Fact]
    public async Task Nothing_is_written_while_the_changes_go_on()
    {
        StateSaveScheduler scheduler = Create();

        scheduler.Schedule();
        await Task.Delay(20);

        Assert.Equal(0, _saves);
    }

    [Fact]
    public async Task Flush_writes_at_once_and_drops_the_waiting_save()
    {
        StateSaveScheduler scheduler = Create();
        scheduler.Schedule();

        await scheduler.FlushAsync();
        await ElapseAsync();

        Assert.Equal(1, _saves);
    }

    [Fact]
    public async Task Stop_writes_a_last_time_and_ignores_what_comes_after()
    {
        StateSaveScheduler scheduler = Create();

        await scheduler.StopAsync();
        await scheduler.StopAsync();
        scheduler.Schedule();
        await ElapseAsync();

        Assert.Equal(1, _saves);
        Assert.False(scheduler.IsPending);
    }

    [Fact]
    public async Task A_failed_save_is_reported_and_the_next_one_still_happens()
    {
        List<Exception> errors = [];
        int calls = 0;
        StateSaveScheduler scheduler = Create(
            _ => ++calls == 1 ? throw new IOException("disk full") : Task.CompletedTask,
            errors.Add);

        await scheduler.FlushAsync();
        await scheduler.FlushAsync();

        Assert.Equal(2, calls);
        IOException error = Assert.IsType<IOException>(Assert.Single(errors));
        Assert.Equal("disk full", error.Message);
    }

    [Fact]
    public async Task Two_saves_never_overlap()
    {
        int running = 0;
        int most = 0;
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StateSaveScheduler scheduler = Create(async _ =>
        {
            most = Math.Max(most, ++running);
            await release.Task;
            running--;
        });

        Task first = scheduler.FlushAsync();
        Task second = scheduler.FlushAsync();
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, most);
    }

    [Fact]
    public void The_default_delay_is_one_second()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), new StateSaveScheduler(_ => Task.CompletedTask).Delay);
    }
}
