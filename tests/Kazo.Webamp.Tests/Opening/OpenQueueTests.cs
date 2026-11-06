using Kazo.Os.App.Abstractions;
using Kazo.Webamp.Opening;

namespace Kazo.Webamp.Tests.Opening;

/// <summary>Decision 5: opened files go into the one window in order, a burst added at once.</summary>
public sealed class OpenQueueTests
{
    private readonly List<OpenRequest> _handled = [];
    private TaskCompletionSource _window = new();

    private OpenQueue CreateQueue(Func<OpenRequest, Task>? handle = null) =>
        new(handle ?? (request => { _handled.Add(request); return Task.CompletedTask; }), _ => _window.Task);

    private static OpenRequest R(OpenMode mode, params string[] paths) => new([.. paths.Select(VirtualPath.Parse)], mode);

    private static string[] Paths(OpenRequest request) => [.. request.Paths.Select(path => path.Value)];

    [Fact]
    public async Task Requests_wait_until_Webamp_is_on_screen()
    {
        OpenQueue queue = CreateQueue();
        queue.Enqueue(R(OpenMode.Play, "/home/neo/a.mp3"));
        _window.SetResult();

        await queue.WhenIdleAsync();
        Assert.Empty(_handled);
        Assert.Equal(1, queue.PendingCount);

        queue.Start();
        await queue.WhenIdleAsync();
        Assert.Equal(["/home/neo/a.mp3"], Paths(Assert.Single(_handled)));
    }

    [Fact]
    public async Task One_file_after_another_from_the_explorer_is_added_at_once_in_order_and_the_first_plays()
    {
        OpenQueue queue = CreateQueue();
        queue.Start();

        // The explorer opens a selection one file at a time: the first launch, then intents.
        queue.Enqueue(R(OpenMode.Play, "/home/neo/1.mp3"));
        queue.Enqueue(R(OpenMode.Play, "/home/neo/2.mp3"));
        queue.Enqueue(R(OpenMode.Play, "/home/neo/3.mp3"));
        _window.SetResult();
        await queue.WhenIdleAsync();

        OpenRequest request = Assert.Single(_handled);
        Assert.Equal(OpenMode.Play, request.Mode);
        Assert.Equal(["/home/neo/1.mp3", "/home/neo/2.mp3", "/home/neo/3.mp3"], Paths(request));
    }

    [Fact]
    public async Task A_request_that_comes_later_is_handled_after_the_first_one()
    {
        TaskCompletionSource firstDone = new();
        OpenQueue queue = CreateQueue(async request =>
        {
            _handled.Add(request);
            if (_handled.Count == 1)
            {
                await firstDone.Task;
            }
        });
        queue.Start();
        _window.SetResult();
        queue.Enqueue(R(OpenMode.Play, "/home/neo/slow.flac"));

        queue.Enqueue(R(OpenMode.Enqueue, "/home/neo/next.mp3"));
        Assert.Single(_handled);
        firstDone.SetResult();
        await queue.WhenIdleAsync();

        Assert.Equal([OpenMode.Play, OpenMode.Enqueue], _handled.Select(request => request.Mode));
        Assert.Equal(["/home/neo/next.mp3"], Paths(_handled[1]));
    }

    [Fact]
    public void Merge_joins_neighbours_of_the_same_mode_only_and_never_two_replacements()
    {
        IReadOnlyList<OpenRequest> merged = OpenQueue.Merge(
        [
            R(OpenMode.Play, "/a.mp3"), R(OpenMode.Play, "/b.mp3"),
            R(OpenMode.Enqueue, "/c.mp3"), R(OpenMode.Enqueue, "/d.mp3"),
            R(OpenMode.Play, "/e.mp3"),
            R(OpenMode.Replace, "/f.mp3"), R(OpenMode.Replace, "/g.mp3"),
        ]);

        Assert.Equal([OpenMode.Play, OpenMode.Enqueue, OpenMode.Play, OpenMode.Replace, OpenMode.Replace], merged.Select(request => request.Mode));
        Assert.Equal(["/a.mp3", "/b.mp3"], Paths(merged[0]));
        Assert.Equal(["/c.mp3", "/d.mp3"], Paths(merged[1]));
    }

    [Fact]
    public async Task A_request_that_throws_is_reported_and_the_next_one_still_runs()
    {
        List<Exception> failures = [];
        OpenQueue queue = CreateQueue(request => request.Mode == OpenMode.Replace
            ? throw new InvalidOperationException("Webamp is gone.")
            : Task.Run(() => _handled.Add(request)));
        queue.Failed += failures.Add;
        queue.Start();
        _window.SetResult();

        queue.Enqueue(R(OpenMode.Replace, "/home/neo/a.mp3"));
        queue.Enqueue(R(OpenMode.Enqueue, "/home/neo/b.mp3"));
        await queue.WhenIdleAsync();

        Assert.Single(failures);
        Assert.Equal(OpenMode.Enqueue, Assert.Single(_handled).Mode);
    }

    [Fact]
    public async Task A_stopped_queue_forgets_its_requests_and_takes_no_more()
    {
        OpenQueue queue = CreateQueue();
        queue.Enqueue(R(OpenMode.Play, "/home/neo/a.mp3"));
        queue.Stop();
        queue.Enqueue(R(OpenMode.Play, "/home/neo/b.mp3"));
        queue.Start();
        _window.SetResult();
        await queue.WhenIdleAsync();

        Assert.Empty(_handled);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void A_request_without_files_is_ignored()
    {
        OpenQueue queue = CreateQueue();
        queue.Enqueue(new OpenRequest([], OpenMode.Play));
        Assert.Equal(0, queue.PendingCount);
    }
}
