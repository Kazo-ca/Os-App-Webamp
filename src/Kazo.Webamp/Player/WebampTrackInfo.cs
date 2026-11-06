namespace Kazo.Webamp.Player;

/// <summary>The tags of the track Webamp started, as the host module reports them.</summary>
/// <param name="Artist">The artist, or <see langword="null"/> when the file has none.</param>
/// <param name="Title">The title, or <see langword="null"/> when the file has none.</param>
public sealed record WebampTrackInfo(string? Artist, string? Title);
