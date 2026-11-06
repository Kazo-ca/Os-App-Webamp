namespace Kazo.Webamp.Tracks;

/// <summary>
/// One file handed to Webamp: the bytes read by <see cref="TrackLibrary"/>, which the host module
/// (<c>wwwroot/js/webamp-host.js</c>) turns into a <c>Blob</c> address.
/// </summary>
/// <param name="Key">The library's key for the track, reported back by Webamp's list changes.</param>
/// <param name="Name">The name Webamp shows until it reads the file's tags (the file name).</param>
/// <param name="MimeType">The audio media type, or <see langword="null"/> when the extension is unknown.</param>
/// <param name="Bytes">The whole file.</param>
/// <param name="DurationSeconds">The duration Webamp shows before it reads the file (from a
/// playlist), or <see langword="null"/>.</param>
public sealed record TrackData(string Key, string Name, string? MimeType, byte[] Bytes, double? DurationSeconds = null);
