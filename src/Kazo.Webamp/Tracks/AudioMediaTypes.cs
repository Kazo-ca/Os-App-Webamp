namespace Kazo.Webamp.Tracks;

/// <summary>
/// The audio formats browsers play (decision 4 of doc/en/plan.md): extension → media type, given to
/// the <c>Blob</c> so the browser picks the right decoder.
/// </summary>
public static class AudioMediaTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg",
        [".wav"] = "audio/wav",
        [".ogg"] = "audio/ogg",
        [".oga"] = "audio/ogg",
        [".opus"] = "audio/opus",
        [".flac"] = "audio/flac",
        [".m4a"] = "audio/mp4",
        [".aac"] = "audio/aac",
        [".weba"] = "audio/webm",
    };

    /// <summary>Gets the extensions Webamp plays, dot included, lowercase.</summary>
    public static IReadOnlyCollection<string> Extensions => ByExtension.Keys;

    /// <summary>Returns the media type of a file name, or <see langword="null"/> for another extension.</summary>
    /// <param name="fileName">A file name or path.</param>
    public static string? ForFile(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        return ByExtension.TryGetValue(extension, out string? mediaType) ? mediaType : null;
    }
}
