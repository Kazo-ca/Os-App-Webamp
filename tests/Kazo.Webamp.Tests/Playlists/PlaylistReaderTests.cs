using System.Text;
using Kazo.Os.App.Abstractions;
using Kazo.Webamp.Playlists;

namespace Kazo.Webamp.Tests.Playlists;

/// <summary>Reading .m3u, .m3u8 and .pls files (decision 10): paths, titles, encodings, what is left out.</summary>
public sealed class PlaylistReaderTests
{
    private static readonly VirtualPath List = VirtualPath.Parse("/home/neo/Music/Mix/list.m3u8");

    private static VirtualPath P(string path) => VirtualPath.Parse(path);

    private static PlaylistReadResult Read(string path, string text) => PlaylistReader.Read(P(path), Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Relative_paths_start_from_the_folder_of_the_list_and_absolute_ones_stay()
    {
        PlaylistReadResult result = Read(List.Value, "one.mp3\nsub/two.ogg\n../three.flac\n/home/neo/four.wav\n./five.mp3\n");

        Assert.Equal(
            [P("/home/neo/Music/Mix/one.mp3"), P("/home/neo/Music/Mix/sub/two.ogg"), P("/home/neo/Music/three.flac"), P("/home/neo/four.wav"), P("/home/neo/Music/Mix/five.mp3")],
            result.Entries.Select(entry => entry.Path));
        Assert.Empty(result.IgnoredUrls);
        Assert.Empty(result.Unresolved);
    }

    [Fact]
    public void Windows_separators_are_accepted()
    {
        PlaylistReadResult result = Read(List.Value, "Album\\01 Intro.mp3\r\n..\\Other\\b.mp3\r\n");

        Assert.Equal([P("/home/neo/Music/Mix/Album/01 Intro.mp3"), P("/home/neo/Music/Other/b.mp3")], result.Entries.Select(entry => entry.Path));
    }

    [Fact]
    public void Extinf_gives_the_title_and_duration_of_the_next_track_only()
    {
        PlaylistReadResult result = Read(List.Value, "#EXTM3U\n#EXTINF:215,Artist - Song\none.mp3\ntwo.mp3\n#EXTINF:-1,Unknown length\nthree.mp3\n#EXTINF:12 tvg-id=\"x\",With attributes\nfour.mp3\n");

        Assert.Equal(("Artist - Song", TimeSpan.FromSeconds(215)), (result.Entries[0].Title, result.Entries[0].Duration));
        Assert.Equal((null, null), (result.Entries[1].Title, result.Entries[1].Duration));
        Assert.Equal(("Unknown length", (TimeSpan?)null), (result.Entries[2].Title, result.Entries[2].Duration));
        Assert.Equal(("With attributes", TimeSpan.FromSeconds(12)), (result.Entries[3].Title, result.Entries[3].Duration));
    }

    [Fact]
    public void Web_addresses_are_left_out_and_reported_file_addresses_are_paths()
    {
        PlaylistReadResult result = Read(List.Value, "http://radio.example/stream\nHTTPS://x.example/a.mp3\nfile:///home/neo/My%20Song.mp3\nlocal.mp3\n");

        Assert.Equal([P("/home/neo/My Song.mp3"), P("/home/neo/Music/Mix/local.mp3")], result.Entries.Select(entry => entry.Path));
        Assert.Equal(["http://radio.example/stream", "HTTPS://x.example/a.mp3"], result.IgnoredUrls);
    }

    [Fact]
    public void Windows_drives_shares_and_paths_above_the_root_are_unresolved()
    {
        PlaylistReadResult result = Read("/home/list.m3u", "C:\\Music\\a.mp3\nd:/b.mp3\n\\\\server\\share\\c.mp3\n../../../d.mp3\nfile:///C:/e.mp3\nok.mp3\n");

        Assert.Equal([P("/home/ok.mp3")], result.Entries.Select(entry => entry.Path));
        Assert.Equal(["C:\\Music\\a.mp3", "d:/b.mp3", "\\\\server\\share\\c.mp3", "../../../d.mp3", "file:///C:/e.mp3"], result.Unresolved);
    }

    [Fact]
    public void An_m3u_that_is_not_utf8_is_read_as_latin1()
    {
        byte[] latin1 = Encoding.Latin1.GetBytes("#EXTINF:10,Café\nété.mp3\n");

        PlaylistReadResult result = PlaylistReader.Read(P("/home/neo/old.m3u"), latin1);

        Assert.Equal("Café", result.Entries[0].Title);
        Assert.Equal(P("/home/neo/été.mp3"), result.Entries[0].Path);
    }

    [Fact]
    public void An_m3u_in_utf8_and_an_m3u8_with_a_byte_order_mark_are_read_as_utf8()
    {
        Assert.Equal(P("/home/neo/été.mp3"), PlaylistReader.Read(P("/home/neo/new.m3u"), Encoding.UTF8.GetBytes("été.mp3")).Entries[0].Path);

        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("#EXTM3U\nnaïve.mp3\n")];
        Assert.Equal(P("/home/neo/naïve.mp3"), PlaylistReader.Read(P("/home/neo/list.m3u8"), withBom).Entries[0].Path);
    }

    [Fact]
    public void An_m3u8_is_always_utf8_even_with_invalid_bytes()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("a"), 0xE9, .. Encoding.UTF8.GetBytes(".mp3")];

        PlaylistReadResult result = PlaylistReader.Read(P("/home/neo/list.m3u8"), bytes);

        Assert.Equal("/home/neo/a\uFFFD.mp3", Assert.Single(result.Entries).Path.Value);
    }

    [Fact]
    public void A_pls_gives_its_entries_in_number_order_with_their_titles_and_lengths()
    {
        const string text = "[playlist]\nNumberOfEntries=3\nFile2=sub\\two.mp3\nTitle2=Second\nLength2=-1\nFile1=one.mp3\nTitle1=First\nLength1=61\nfile3=http://radio.example/\nVersion=2\n";

        PlaylistReadResult result = Read("/home/neo/Music/list.pls", text);

        Assert.Equal([P("/home/neo/Music/one.mp3"), P("/home/neo/Music/sub/two.mp3")], result.Entries.Select(entry => entry.Path));
        Assert.Equal(("First", TimeSpan.FromSeconds(61)), (result.Entries[0].Title, result.Entries[0].Duration));
        Assert.Equal(("Second", (TimeSpan?)null), (result.Entries[1].Title, result.Entries[1].Duration));
        Assert.Equal(["http://radio.example/"], result.IgnoredUrls);
    }

    [Fact]
    public void A_pls_in_latin1_is_read()
    {
        byte[] bytes = Encoding.Latin1.GetBytes("[playlist]\nFile1=Été.mp3\nTitle1=Été\n");

        PlaylistEntry entry = Assert.Single(PlaylistReader.Read(P("/home/neo/list.pls"), bytes).Entries);

        Assert.Equal(("/home/neo/Été.mp3", "Été"), (entry.Path.Value, entry.Title));
    }

    [Fact]
    public void Empty_lines_comments_and_spaces_around_paths_are_skipped()
    {
        PlaylistReadResult result = Read(List.Value, "\n\n# a comment\n   one.mp3   \n\n");

        Assert.Equal(P("/home/neo/Music/Mix/one.mp3"), Assert.Single(result.Entries).Path);
    }

    [Fact]
    public void A_file_that_is_not_a_playlist_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PlaylistReader.Read(P("/home/neo/song.mp3"), []));
    }
}
