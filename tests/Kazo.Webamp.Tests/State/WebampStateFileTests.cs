using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.State;

namespace Kazo.Webamp.Tests.State;

/// <summary>~/.config/ca.kazo.webamp/state.json: read at start, written with its folder.</summary>
public sealed class WebampStateFileTests
{
    private readonly Dictionary<string, string> _files = [];
    private readonly List<string> _folders = [];

    private WebampStateFile CreateFile() => new(
        "/home/neo/",
        (path, _) => _files.TryGetValue(path, out string? text) ? Task.FromResult(text) : throw new KazoFileNotFoundException(path),
        (path, text, _) =>
        {
            _files[path] = text;
            return Task.CompletedTask;
        },
        (path, _) =>
        {
            _folders.Add(path);
            return Task.CompletedTask;
        });

    [Fact]
    public void The_file_is_in_the_app_s_own_config_folder()
    {
        WebampStateFile file = CreateFile();

        Assert.Equal("/home/neo/.config/ca.kazo.webamp", file.Directory);
        Assert.Equal("/home/neo/.config/ca.kazo.webamp/state.json", file.Path);
    }

    [Fact]
    public async Task No_file_yet_is_a_first_run()
    {
        WebampState state = await CreateFile().LoadAsync();

        Assert.Same(WebampState.Empty, state);
    }

    [Fact]
    public async Task A_corrupt_file_is_an_empty_state()
    {
        _files["/home/neo/.config/ca.kazo.webamp/state.json"] = "{ broken";

        WebampState state = await CreateFile().LoadAsync();

        Assert.Same(WebampState.Empty, state);
    }

    [Fact]
    public async Task Saving_creates_the_folder_and_the_next_run_reads_it_back()
    {
        WebampStateFile file = CreateFile();
        WebampState state = WebampState.Empty with { Tracks = ["/home/neo/a.mp3"], CurrentTrack = 0 };

        await file.SaveAsync(state);
        WebampState read = await CreateFile().LoadAsync();

        Assert.Equal(["/home/neo/.config/ca.kazo.webamp"], _folders);
        Assert.Equal(["/home/neo/a.mp3"], read.Tracks);
        Assert.Equal(0, read.CurrentTrack);
    }

    [Fact]
    public async Task A_file_the_app_may_not_read_is_an_empty_state()
    {
        WebampStateFile file = new(
            "/home/neo",
            (_, _) => throw new UnauthorizedAccessException("denied"),
            (_, _, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask);

        Assert.Same(WebampState.Empty, await file.LoadAsync());
    }
}
