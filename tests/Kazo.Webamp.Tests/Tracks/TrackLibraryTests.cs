using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.Tests.Tracks;

/// <summary>The files in Webamp's list: read when added, released when they leave the list.</summary>
public sealed class TrackLibraryTests
{
    private readonly FakeTrackPlayer _player = new();
    private readonly Dictionary<string, byte[]> _files = new()
    {
        ["/home/neo/Music/one.mp3"] = [1, 2, 3],
        ["/home/neo/Music/two.flac"] = [4, 5, 6, 7],
        ["/home/neo/Music/three.ogg"] = [8],
    };

    private TrackLibrary CreateLibrary() => new(_player, (path, _) =>
        _files.TryGetValue(path, out byte[]? bytes)
            ? Task.FromResult(bytes)
            : throw new KazoFileNotFoundException(path));

    private static VirtualPath P(string path) => VirtualPath.Parse(path);

    [Fact]
    public async Task Adding_files_reads_them_and_sends_them_to_Webamp_in_order_with_their_type()
    {
        TrackLibrary library = CreateLibrary();

        TrackLoadResult result = await library.AddAsync([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/two.flac")], play: true);

        Assert.True(result.Succeeded);
        (string call, IReadOnlyList<TrackData> tracks, bool play) = Assert.Single(_player.Calls);
        Assert.Equal("add", call);
        Assert.True(play);
        Assert.Equal(["one.mp3", "two.flac"], tracks.Select(track => track.Name));
        Assert.Equal(["audio/mpeg", "audio/flac"], tracks.Select(track => track.MimeType));
        Assert.Equal(new byte[] { 4, 5, 6, 7 }, tracks[1].Bytes);
        Assert.Equal([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/two.flac")], library.Paths);
        Assert.Equal(7, library.LoadedBytes);
    }

    [Fact]
    public async Task Adding_again_appends_at_the_end_with_new_keys()
    {
        TrackLibrary library = CreateLibrary();
        await library.AddAsync([P("/home/neo/Music/one.mp3")], play: false);

        await library.AddAsync([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/three.ogg")], play: false);

        Assert.Equal([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/one.mp3"), P("/home/neo/Music/three.ogg")], library.Paths);
        Assert.Equal(3, _player.Listed.Distinct().Count());
    }

    [Fact]
    public async Task A_file_that_does_not_read_is_reported_and_the_others_are_added()
    {
        TrackLibrary library = CreateLibrary();

        TrackLoadResult result = await library.AddAsync([P("/home/neo/Music/gone.mp3"), P("/home/neo/Music/three.ogg")], play: true);

        TrackLoadFailure failure = Assert.Single(result.Failures);
        Assert.Equal(P("/home/neo/Music/gone.mp3"), failure.Path);
        Assert.Contains("gone.mp3", failure.Message);
        Assert.Equal([P("/home/neo/Music/three.ogg")], result.Added);
        Assert.Equal([P("/home/neo/Music/three.ogg")], library.Paths);
    }

    [Fact]
    public async Task Nothing_is_sent_to_Webamp_when_no_file_reads()
    {
        TrackLibrary library = CreateLibrary();
        int changes = 0;
        library.Changed += () => changes++;

        TrackLoadResult result = await library.AddAsync([P("/home/neo/Music/gone.mp3")], play: true);

        Assert.False(result.Succeeded);
        Assert.Empty(_player.Calls);
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task A_track_that_leaves_Webamps_list_is_released()
    {
        TrackLibrary library = CreateLibrary();
        await library.AddAsync([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/two.flac"), P("/home/neo/Music/three.ogg")], play: false);
        int changes = 0;
        library.Changed += () => changes++;

        // The user removed the second track in Webamp's list.
        library.OnPlaylistChanged([_player.Listed[0], _player.Listed[2]]);

        Assert.Equal([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/three.ogg")], library.Paths);
        Assert.Equal(4, library.LoadedBytes);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Webamps_order_wins_and_unknown_keys_are_ignored()
    {
        TrackLibrary library = CreateLibrary();
        await library.AddAsync([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/two.flac")], play: false);

        // Reordered by the user; "x9" is not the library's (a file dropped from the computer has no key).
        library.OnPlaylistChanged([_player.Listed[1], "x9", _player.Listed[0]]);

        Assert.Equal([P("/home/neo/Music/two.flac"), P("/home/neo/Music/one.mp3")], library.Paths);
    }

    [Fact]
    public async Task The_same_list_reported_twice_changes_nothing()
    {
        TrackLibrary library = CreateLibrary();
        await library.AddAsync([P("/home/neo/Music/one.mp3")], play: false);
        int changes = 0;
        library.Changed += () => changes++;

        library.OnPlaylistChanged([.. _player.Listed]);

        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task Replacing_the_list_releases_the_old_tracks()
    {
        TrackLibrary library = CreateLibrary();
        await library.AddAsync([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/two.flac")], play: true);

        TrackLoadResult result = await library.ReplaceAsync([P("/home/neo/Music/three.ogg")], play: false);

        Assert.True(result.Succeeded);
        Assert.Equal(("replace", false), (_player.Calls[^1].Call, _player.Calls[^1].Play));
        Assert.Equal([P("/home/neo/Music/three.ogg")], library.Paths);
        Assert.Equal(1, library.LoadedBytes);
    }

    [Fact]
    public async Task Replacing_with_files_that_do_not_read_keeps_the_list()
    {
        TrackLibrary library = CreateLibrary();
        await library.AddAsync([P("/home/neo/Music/one.mp3")], play: false);

        TrackLoadResult result = await library.ReplaceAsync([P("/home/neo/Music/gone.mp3")], play: true);

        Assert.Single(result.Failures);
        Assert.Single(_player.Calls);
        Assert.Equal([P("/home/neo/Music/one.mp3")], library.Paths);
    }

    [Fact]
    public async Task List_changes_reported_while_Webamp_adds_the_tracks_keep_them()
    {
        TrackLibrary library = CreateLibrary();
        _player.ReportDuringCall = library.OnPlaylistChanged;

        await library.AddAsync([P("/home/neo/Music/one.mp3")], play: false);
        await library.ReplaceAsync([P("/home/neo/Music/two.flac"), P("/home/neo/Music/three.ogg")], play: true);

        Assert.Equal([P("/home/neo/Music/two.flac"), P("/home/neo/Music/three.ogg")], library.Paths);
        Assert.Equal(5, library.LoadedBytes);
    }

    [Fact]
    public async Task When_Webamp_fails_the_files_are_not_kept()
    {
        TrackLibrary library = CreateLibrary();
        _player.Throw = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => library.AddAsync([P("/home/neo/Music/one.mp3")], play: true));

        Assert.Empty(library.Paths);
        Assert.Equal(0, library.LoadedBytes);
    }

    [Theory]
    [InlineData("a.MP3", "audio/mpeg")]
    [InlineData("a.wav", "audio/wav")]
    [InlineData("a.oga", "audio/ogg")]
    [InlineData("a.opus", "audio/opus")]
    [InlineData("a.m4a", "audio/mp4")]
    [InlineData("a.aac", "audio/aac")]
    [InlineData("a.weba", "audio/webm")]
    [InlineData("a.txt", null)]
    public void Audio_media_types_follow_the_extension(string name, string? expected) =>
        Assert.Equal(expected, AudioMediaTypes.ForFile(name));
}
