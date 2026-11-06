using Kazo.Webamp.State;

namespace Kazo.Webamp.Tests.State;

/// <summary>state.json: written and read back, and a broken file never breaks Webamp.</summary>
public sealed class WebampStateJsonTests
{
    private static readonly WindowLayout Layout = new(
        Absolute: true,
        Main: new(10, 20, true, false, 0, 0),
        Equalizer: new(10, 136, true, true, 0, 0),
        Playlist: new(10, 252, true, false, 2, 3),
        Milkdrop: new(285, 20, false, false, 7, 12));

    private static WebampState Full() => new(
        ["/home/neo/Music/one.mp3", "/mnt/nas/two.flac"],
        1,
        new PlayerSettings(42.5, 63, -20, true, false, new EqualizerSettings(false, true, 60, [10, 20, 30, 40, 50, 60, 70, 80, 90, 100]), true, Layout));

    [Fact]
    public void A_state_written_reads_back_the_same()
    {
        WebampState state = Full();

        WebampState read = WebampStateJson.Parse(WebampStateJson.Serialize(state));

        Assert.Equal(state.Tracks, read.Tracks);
        Assert.Equal(state.CurrentTrack, read.CurrentTrack);
        Assert.Equal(state.Player, read.Player);
    }

    [Fact]
    public void The_file_is_versioned_camel_case_json()
    {
        string json = WebampStateJson.Serialize(Full());

        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"currentTrack\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"extraHeight\": 3", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\": 1, \"tracks\": 5}")]
    [InlineData("[1, 2]")]
    [InlineData("{\"version\": 2, \"tracks\": [\"/home/neo/a.mp3\"]}")]
    [InlineData("{\"tracks\": [\"/home/neo/a.mp3\"]}")]
    public void A_corrupt_or_unknown_file_is_an_empty_state(string? json)
    {
        WebampState state = WebampStateJson.Parse(json);

        Assert.Same(WebampState.Empty, state);
    }

    [Fact]
    public void Missing_members_take_Webamp_defaults()
    {
        WebampState state = WebampStateJson.Parse("{\"version\": 1}");

        Assert.Empty(state.Tracks);
        Assert.Null(state.CurrentTrack);
        Assert.Equal(PlayerSettings.Default, state.Player);
    }

    [Fact]
    public void Values_out_of_range_are_brought_back_into_range()
    {
        const string json = """
            {
              "version": 1,
              "player": {
                "position": -4, "volume": 400, "balance": -900,
                "equalizer": { "on": true, "preamp": 101, "bands": [-1, 0, 50, 100, 200, 50, 50, 50, 50, 50] },
                "windows": {
                  "absolute": true,
                  "main": { "x": 99999999, "y": 0 }, "equalizer": {}, "playlist": { "extraWidth": -3 }, "milkdrop": {}
                }
              }
            }
            """;

        PlayerSettings player = WebampStateJson.Parse(json).Player;

        Assert.Equal(0, player.Position);
        Assert.Equal(100, player.Volume);
        Assert.Equal(-100, player.Balance);
        Assert.Equal(100, player.Equalizer.Preamp);
        Assert.Equal([0, 0, 50, 100, 100, 50, 50, 50, 50, 50], player.Equalizer.Bands);
        Assert.Equal(100_000, player.Windows!.Main.X);
        Assert.Equal(0, player.Windows.Playlist.ExtraWidth);
        Assert.True(player.Windows.Equalizer.Open);
    }

    [Fact]
    public void An_equalizer_without_ten_bands_is_flat_and_an_incomplete_layout_is_dropped()
    {
        const string json = """
            { "version": 1, "player": { "equalizer": { "bands": [1, 2] }, "windows": { "main": { "x": 1, "y": 2 } } } }
            """;

        PlayerSettings player = WebampStateJson.Parse(json).Player;

        Assert.Equal(EqualizerSettings.Default.Bands, player.Equalizer.Bands);
        Assert.Null(player.Windows);
    }

    [Fact]
    public void Paths_the_OS_refuses_are_dropped_and_the_current_track_follows_its_file()
    {
        const string json = """
            { "version": 1, "tracks": ["relative.mp3", null, "/home/neo/../neo/a.mp3", "C:\\b.mp3", "/home/neo/c.mp3"], "currentTrack": 4 }
            """;

        WebampState state = WebampStateJson.Parse(json);

        Assert.Equal(["/home/neo/a.mp3", "/home/neo/c.mp3"], state.Tracks);
        Assert.Equal(1, state.CurrentTrack);
    }

    [Fact]
    public void A_current_track_whose_path_was_dropped_is_no_current_track()
    {
        WebampState state = WebampStateJson.Parse("{ \"version\": 1, \"tracks\": [\"bad\", \"/a.mp3\"], \"currentTrack\": 0 }");

        Assert.Null(state.CurrentTrack);
    }
}
