namespace Kazo.Webamp.Player;

/// <summary>One track of Webamp's list, as the host module reports it for "Save list".</summary>
/// <param name="Key">The key <see cref="Tracks.TrackLibrary"/> gave the track.</param>
/// <param name="Title">"Artist - Title" from the tags, the name Webamp shows, or <see langword="null"/>.</param>
/// <param name="Duration">The duration in seconds, or <see langword="null"/> before Webamp knows it.</param>
public sealed record WebampListEntry(string Key, string? Title, double? Duration);
