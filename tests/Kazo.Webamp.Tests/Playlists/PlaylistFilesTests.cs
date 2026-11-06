using System.Text;
using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.Player;
using Kazo.Webamp.Playlists;
using Kazo.Webamp.Tests.Tracks;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.Tests.Playlists;

/// <summary>Opening lists with other files, the tracks of a list in Webamp, saving Webamp's list.</summary>
public sealed class PlaylistFilesTests
{
    private readonly Dictionary<string, byte[]> _files = new()
    {
        ["/home/neo/Music/one.mp3"] = [1, 2, 3],
        ["/home/neo/Music/two.ogg"] = [4],
        ["/home/neo/Music/list.m3u8"] = Encoding.UTF8.GetBytes("#EXTM3U\n#EXTINF:61,A - One\none.mp3\ngone.mp3\nhttp://radio.example/\nC:\\x.mp3\n"),
    };

    private readonly FakeTrackPlayer _player = new();

    private static VirtualPath P(string path) => VirtualPath.Parse(path);

    private Task<byte[]> ReadAsync(string path, CancellationToken _) =>
        _files.TryGetValue(path, out byte[]? bytes) ? Task.FromResult(bytes) : throw new KazoFileNotFoundException(path);

    private PlaylistFiles CreateFiles() => new(ReadAsync, (path, bytes, _) =>
    {
        _files[path] = bytes;
        return Task.CompletedTask;
    });

    [Fact]
    public async Task Opening_files_and_a_list_together_gives_the_tracks_in_order()
    {
        PlaylistExpansion expansion = await CreateFiles().ExpandAsync([P("/home/neo/Music/two.ogg"), P("/home/neo/Music/list.m3u8"), P("/home/neo/Music/one.mp3")]);

        Assert.Equal(
            [P("/home/neo/Music/two.ogg"), P("/home/neo/Music/one.mp3"), P("/home/neo/Music/gone.mp3"), P("/home/neo/Music/one.mp3")],
            expansion.Entries.Select(entry => entry.Path));
        Assert.Equal("A - One", expansion.Entries[1].Title);
        Assert.Equal(["http://radio.example/"], expansion.IgnoredUrls);
        Assert.Equal(["C:\\x.mp3"], expansion.Unresolved);
        Assert.Empty(expansion.Failures);
    }

    [Fact]
    public async Task A_list_that_does_not_read_is_a_failure_and_the_other_files_stay()
    {
        PlaylistExpansion expansion = await CreateFiles().ExpandAsync([P("/home/neo/Music/missing.pls"), P("/home/neo/Music/one.mp3")]);

        Assert.Equal(P("/home/neo/Music/missing.pls"), Assert.Single(expansion.Failures).Path);
        Assert.Equal(P("/home/neo/Music/one.mp3"), Assert.Single(expansion.Entries).Path);
    }

    [Fact]
    public async Task The_tracks_of_a_list_reach_Webamp_with_their_title_and_duration_and_missing_files_fail()
    {
        TrackLibrary library = new(_player, ReadAsync);
        PlaylistExpansion expansion = await CreateFiles().ExpandAsync([P("/home/neo/Music/list.m3u8")]);

        TrackLoadResult result = await library.AddAsync(expansion.Entries, play: true);

        (_, IReadOnlyList<TrackData> tracks, bool play) = Assert.Single(_player.Calls);
        Assert.True(play);
        TrackData track = Assert.Single(tracks);
        Assert.Equal(("A - One", 61d, "audio/mpeg"), (track.Name, track.DurationSeconds, track.MimeType));
        Assert.Equal(P("/home/neo/Music/gone.mp3"), Assert.Single(result.Failures).Path);
    }

    [Fact]
    public async Task Load_list_replaces_Webamp_s_list()
    {
        TrackLibrary library = new(_player, ReadAsync);
        await library.AddAsync([P("/home/neo/Music/two.ogg")], play: true);
        PlaylistReadResult list = await CreateFiles().ReadAsync(P("/home/neo/Music/list.m3u8"));

        await library.ReplaceAsync(list.Entries, play: false);

        Assert.Equal(("replace", false), (_player.Calls[^1].Call, _player.Calls[^1].Play));
        Assert.Equal([P("/home/neo/Music/one.mp3")], library.Paths);
    }

    [Fact]
    public async Task Saving_writes_the_tracks_that_have_a_file_and_reopening_gives_them_back()
    {
        TrackLibrary library = new(_player, ReadAsync);
        await library.AddAsync([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/two.ogg")], play: false);
        string[] keys = [.. _player.Listed];
        WebampListEntry[] listed =
        [
            new(keys[1], "Two", 4.6),
            new("dropped-from-the-computer", "Not a file", 3),
            new(keys[0], null, null),
        ];
        PlaylistFiles files = CreateFiles();
        VirtualPath list = P("/home/neo/Lists/saved.m3u8");

        await files.SaveAsync(list, PlaylistFiles.ToEntries(library, listed));

        Assert.Equal(
            "#EXTM3U\n#EXTINF:5,Two\n../Music/two.ogg\n#EXTINF:-1,one\n../Music/one.mp3\n",
            Encoding.UTF8.GetString(_files[list.Value]));
        PlaylistExpansion reopened = await files.ExpandAsync([list]);
        Assert.Equal([P("/home/neo/Music/two.ogg"), P("/home/neo/Music/one.mp3")], reopened.Entries.Select(entry => entry.Path));
    }
}
