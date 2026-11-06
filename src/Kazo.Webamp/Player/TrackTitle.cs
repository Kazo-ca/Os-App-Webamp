namespace Kazo.Webamp.Player;

/// <summary>
/// The title of the OS window (decision 8 of doc/en/plan.md): <c>Artist - Title - Webamp</c> while a
/// track is current, as Winamp names its taskbar button, and <c>Webamp</c> without one.
/// </summary>
public static class TrackTitle
{
    /// <summary>Between the parts of the title, as in Winamp.</summary>
    public const string Separator = " - ";

    /// <summary>Builds the window's title.</summary>
    /// <param name="track">The current track, or <see langword="null"/>.</param>
    /// <param name="appName">The app's name (<c>Webamp</c>).</param>
    /// <returns>
    /// <c>Artist - Title - Webamp</c>; without an artist tag <c>Title - Webamp</c>; without a title
    /// tag, the file's name in its place; without any of them, <c>Webamp</c>.
    /// </returns>
    public static string Format(WebampTrackInfo? track, string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        if (track is null)
        {
            return appName;
        }

        string? title = Clean(track.Title) ?? Clean(track.Name);
        string? artist = Clean(track.Artist);
        if (title is null)
        {
            return artist is null ? appName : string.Join(Separator, artist, appName);
        }

        return artist is null
            ? string.Join(Separator, title, appName)
            : string.Join(Separator, artist, title, appName);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
