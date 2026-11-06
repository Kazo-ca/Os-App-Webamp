using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.State;
using Kazo.Webamp.Tests.Tracks;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.Tests.State;

/// <summary>Putting the saved state back at start, and building the state to save.</summary>
public sealed class WebampStateKeeperTests
{
    private readonly FakeTrackPlayer _tracksPlayer = new();
    private readonly FakeStatePlayer _player = new();
    private readonly Dictionary<string, byte[]> _files = new()
    {
        ["/home/neo/Music/one.mp3"] = [1, 2],
        ["/home/neo/Music/two.mp3"] = [3],
        ["/home/neo/skins/green.wsz"] = [80, 75],
    };

    private readonly TrackLibrary _library;
    private readonly WebampStateKeeper _keeper;

    public WebampStateKeeperTests()
    {
        _library = new TrackLibrary(_tracksPlayer, Read);
        _keeper = new WebampStateKeeper(_player, _library, Read);
    }

    private Task<byte[]> Read(string path, CancellationToken token) =>
        _files.TryGetValue(path, out byte[]? bytes) ? Task.FromResult(bytes) : throw new KazoFileNotFoundException(path);

    private static WebampState Saved(int? current, double position, params string[] tracks) =>
        new(tracks, current, PlayerSettings.Default with { Position = position, Volume = 40 }, null);

    [Fact]
    public async Task The_saved_list_comes_back_without_playing_and_the_current_track_at_its_position()
    {
        await _keeper.RestoreAsync(Saved(1, 31.5, "/home/neo/Music/one.mp3", "/home/neo/Music/two.mp3"));

        (string call, IReadOnlyList<TrackData> tracks, bool play) = Assert.Single(_tracksPlayer.Calls);
        Assert.Equal("add", call);
        Assert.False(play);
        Assert.Equal(["one.mp3", "two.mp3"], tracks.Select(track => track.Name));
        Assert.Equal([(1, 31.5)], _player.Resumed);
    }

    [Fact]
    public async Task A_file_that_disappeared_is_skipped_and_stays_in_the_saved_list()
    {
        TrackLoadResult result = await _keeper.RestoreAsync(Saved(1, 31.5, "/home/neo/Music/one.mp3", "/mnt/nas/gone.mp3", "/home/neo/Music/two.mp3"));
        _player.Snapshot = new PlayerSnapshot(_library.Keys[1], PlayerSettings.Default with { Position = 3 });

        WebampState state = await _keeper.CaptureAsync();

        Assert.Equal(["/mnt/nas/gone.mp3"], result.Failures.Select(failure => failure.Path.Value));
        Assert.Equal(["/mnt/nas/gone.mp3"], _keeper.MissingTracks);
        // The current track was missing: the next one is current, from its start.
        Assert.Equal([(1, 0d)], _player.Resumed);
        Assert.Equal(["/home/neo/Music/one.mp3", "/mnt/nas/gone.mp3", "/home/neo/Music/two.mp3"], state.Tracks);
        Assert.Equal(2, state.CurrentTrack);
        Assert.Equal(3, state.Player.Position);
    }

    [Fact]
    public async Task An_empty_list_asks_nothing_of_Webamp()
    {
        await _keeper.RestoreAsync(WebampState.Empty);

        Assert.Empty(_tracksPlayer.Calls);
        Assert.Empty(_player.Resumed);
    }

    [Fact]
    public async Task The_saved_skin_is_applied_and_saved_again()
    {
        await _keeper.RestoreAsync(WebampState.Empty with { Skin = "/home/neo/skins/green.wsz" });

        WebampState state = await _keeper.CaptureAsync();

        (string name, byte[] bytes) = Assert.Single(_player.Skins);
        Assert.Equal("green.wsz", name);
        Assert.Equal(new byte[] { 80, 75 }, bytes);
        Assert.Equal("/home/neo/skins/green.wsz", state.Skin);
    }

    [Fact]
    public async Task A_skin_that_disappeared_leaves_the_default_skin()
    {
        await _keeper.RestoreAsync(WebampState.Empty with { Skin = "/home/neo/skins/gone.wsz" });

        Assert.Empty(_player.Skins);
        Assert.Null(_keeper.SkinPath);
    }

    [Fact]
    public async Task The_state_to_save_has_Webamp_s_settings_and_list()
    {
        await _library.AddAsync([VirtualPathOf("/home/neo/Music/two.mp3")], play: true);
        PlayerSettings settings = PlayerSettings.Default with { Position = 12, Volume = 10, DoubleSize = true };
        _player.Snapshot = new PlayerSnapshot(_library.Keys[0], settings);
        _keeper.SkinPath = "/home/neo/skins/green.wsz";

        WebampState state = await _keeper.CaptureAsync();

        Assert.Equal(["/home/neo/Music/two.mp3"], state.Tracks);
        Assert.Equal(0, state.CurrentTrack);
        Assert.Equal(settings, state.Player);
        Assert.Equal("/home/neo/skins/green.wsz", state.Skin);
    }

    [Fact]
    public async Task Without_a_current_track_of_the_list_the_position_is_not_kept()
    {
        await _library.AddAsync([VirtualPathOf("/home/neo/Music/two.mp3")], play: false);
        // A file dropped from the computer plays: it has no key.
        _player.Snapshot = new PlayerSnapshot(null, PlayerSettings.Default with { Position = 12 });

        WebampState state = await _keeper.CaptureAsync();

        Assert.Null(state.CurrentTrack);
        Assert.Equal(0, state.Player.Position);
    }

    private static Kazo.Os.App.Abstractions.VirtualPath VirtualPathOf(string path) => Kazo.Os.App.Abstractions.VirtualPath.Parse(path);
}
