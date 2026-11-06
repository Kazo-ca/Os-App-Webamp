using System.Text;
using Kazo.Os.App.Abstractions;
using Kazo.Webamp.Playlists;

namespace Kazo.Webamp.Tests.Playlists;

/// <summary>Writing .m3u8 (decision 10): UTF-8, relative paths, #EXTINF, and reading it back.</summary>
public sealed class PlaylistWriterTests
{
    private static VirtualPath P(string path) => VirtualPath.Parse(path);

    [Fact]
    public void The_list_is_utf8_without_byte_order_mark_with_extinf_and_relative_paths()
    {
        byte[] bytes = PlaylistWriter.WriteM3u8(P("/home/neo/Music/list.m3u8"),
        [
            new PlaylistEntry(P("/home/neo/Music/Album/01 Été.mp3"), "Artist - Été", TimeSpan.FromSeconds(215.4)),
            new PlaylistEntry(P("/home/neo/Music/two.ogg")),
            new PlaylistEntry(P("/home/neo/Other/three.flac"), "Three", null),
            new PlaylistEntry(P("/mnt/nas/four.mp3"), "Line\r\nbreak"),
        ]);

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(
            "#EXTM3U\n#EXTINF:215,Artist - Été\nAlbum/01 Été.mp3\n#EXTINF:-1,two\ntwo.ogg\n#EXTINF:-1,Three\n../Other/three.flac\n#EXTINF:-1,Line break\n../../../mnt/nas/four.mp3\n",
            Encoding.UTF8.GetString(bytes));
    }

    [Theory]
    [InlineData("/home/neo/Music", "/home/neo/Music/a.mp3", "a.mp3")]
    [InlineData("/home/neo/Music", "/home/neo/Music/x/y/a.mp3", "x/y/a.mp3")]
    [InlineData("/home/neo/Music/Mix", "/home/neo/a.mp3", "../../a.mp3")]
    [InlineData("/", "/home/a.mp3", "home/a.mp3")]
    [InlineData("/home/neo/Music", "/home/neo/MusicOld/a.mp3", "../MusicOld/a.mp3")]
    [InlineData("/home/neo/Music", "/home/neo/Music", "../Music")]
    public void Relative_paths_go_up_with_dot_dot(string folder, string target, string expected)
    {
        Assert.Equal(expected, PlaylistPaths.GetRelativePath(folder, P(target)));
    }

    [Fact]
    public void A_saved_list_reads_back_to_the_same_tracks_from_another_folder_too()
    {
        PlaylistEntry[] entries =
        [
            new(P("/home/neo/Music/Album/01 Intro.mp3"), "A - Intro", TimeSpan.FromSeconds(61)),
            new(P("/home/neo/Podcasts/épisode #1.mp3"), "Épisode", null),
            new(P("/home/neo/Music/a,b.mp3"), "Comma, in title", TimeSpan.FromSeconds(3)),
        ];
        VirtualPath list = P("/home/neo/Music/Lists/mine.m3u8");

        PlaylistReadResult read = PlaylistReader.Read(list, PlaylistWriter.WriteM3u8(list, entries));

        Assert.Equal(entries, read.Entries);

        // Moving the whole folder (list and music) keeps the list working: the paths are relative.
        VirtualPath moved = P("/home/neo/Backup/Music/Lists/mine.m3u8");
        PlaylistReadResult again = PlaylistReader.Read(moved, PlaylistWriter.WriteM3u8(list, entries));
        Assert.Equal(P("/home/neo/Backup/Music/Album/01 Intro.mp3"), again.Entries[0].Path);
    }
}
