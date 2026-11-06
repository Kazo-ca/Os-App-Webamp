using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.Opening;

namespace Kazo.Webamp.Tests.Opening;

/// <summary>Decision 16: a folder brings its audio files and those of its subfolders, sorted by path.</summary>
public sealed class AudioFileCollectorTests
{
    private readonly Dictionary<string, string[]> _folders = new(StringComparer.Ordinal)
    {
        ["/home/neo/Music"] =
        [
            "/home/neo/Music/b.mp3",
            "/home/neo/Music/cover.jpg",
            "/home/neo/Music/Album/02 - Two.flac",
            "/home/neo/Music/A.ogg",
            "/home/neo/Music/Album/01 - One.flac",
            "/home/neo/Music/notes.txt",
            "/home/neo/Music/album 2/x.WAV",
        ],
        ["/home/neo/Empty"] = ["/home/neo/Empty/readme.txt"],
    };

    private AudioFileCollector CreateCollector() => new(
        (path, _) => Task.FromResult(_folders.ContainsKey(path) || path == "/home/neo/Locked"),
        (path, _) => path == "/home/neo/Locked"
            ? throw new KazoUnauthorizedAccessException(path)
            : Task.FromResult(_folders[path]));

    private static VirtualPath P(string path) => VirtualPath.Parse(path);

    [Fact]
    public async Task A_folder_becomes_its_audio_files_and_those_below_sorted_by_path()
    {
        AudioFiles found = await CreateCollector().CollectAsync([P("/home/neo/Music")]);

        Assert.Empty(found.Failures);
        Assert.Equal(
            [
                "/home/neo/Music/A.ogg",
                "/home/neo/Music/Album/01 - One.flac",
                "/home/neo/Music/Album/02 - Two.flac",
                "/home/neo/Music/album 2/x.WAV",
                "/home/neo/Music/b.mp3",
            ],
            found.Files.Select(file => file.Value));
    }

    [Fact]
    public async Task Files_keep_their_place_between_folders_and_non_audio_files_are_left_out()
    {
        AudioFiles found = await CreateCollector().CollectAsync(
            [P("/home/neo/z.mp3"), P("/home/neo/Empty"), P("/home/neo/list.txt"), P("/home/neo/a.opus")]);

        Assert.Equal(["/home/neo/z.mp3", "/home/neo/a.opus"], found.Files.Select(file => file.Value));
    }

    [Fact]
    public async Task A_playlist_stays_for_PlaylistFiles_to_read_but_the_lists_of_a_folder_are_not_read()
    {
        _folders["/home/neo/Lists"] = ["/home/neo/Lists/all.m3u8", "/home/neo/Lists/one.mp3"];

        AudioFiles found = await CreateCollector().CollectAsync([P("/home/neo/mix.pls"), P("/home/neo/Lists")]);

        Assert.Equal(["/home/neo/mix.pls", "/home/neo/Lists/one.mp3"], found.Files.Select(file => file.Value));
    }

    [Fact]
    public async Task A_folder_that_cannot_be_listed_is_reported_and_the_rest_is_kept()
    {
        AudioFiles found = await CreateCollector().CollectAsync([P("/home/neo/Locked"), P("/home/neo/one.m4a")]);

        TrackLoadFailureAssert.Single(found.Failures, "/home/neo/Locked");
        Assert.Equal(["/home/neo/one.m4a"], found.Files.Select(file => file.Value));
    }
}

internal static class TrackLoadFailureAssert
{
    public static void Single(IReadOnlyList<Kazo.Webamp.Tracks.TrackLoadFailure> failures, string path) =>
        Assert.Equal(path, Assert.Single(failures).Path.Value);
}
