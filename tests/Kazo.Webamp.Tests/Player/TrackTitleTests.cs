using Kazo.Webamp.Player;

namespace Kazo.Webamp.Tests.Player;

/// <summary>The OS window's title follows the current track (decision 8).</summary>
public sealed class TrackTitleTests
{
    [Theory]
    [InlineData("Artist", "Title", "song.mp3", "Artist - Title - Webamp")]
    [InlineData(null, "Title", "song.mp3", "Title - Webamp")]
    [InlineData("Artist", null, "song.mp3", "Artist - song.mp3 - Webamp")]
    [InlineData(null, null, "song.mp3", "song.mp3 - Webamp")]
    [InlineData("  ", " ", null, "Webamp")]
    [InlineData(" Artist ", " Title ", null, "Artist - Title - Webamp")]
    [InlineData("Artist", null, null, "Artist - Webamp")]
    public void A_track_names_the_window(string? artist, string? title, string? name, string expected)
    {
        Assert.Equal(expected, TrackTitle.Format(new WebampTrackInfo(artist, title, name), "Webamp"));
    }

    [Fact]
    public void Without_a_track_the_window_is_Webamp()
    {
        Assert.Equal("Webamp", TrackTitle.Format(null, "Webamp"));
    }
}
