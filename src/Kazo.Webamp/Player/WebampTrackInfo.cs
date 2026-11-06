namespace Kazo.Webamp.Player;

/// <summary>The track Webamp started, as the host module reports it.</summary>
/// <param name="Artist">The artist tag, or <see langword="null"/> when the file has none.</param>
/// <param name="Title">The title tag, or <see langword="null"/> when the file has none.</param>
/// <param name="Name">The file's name, for a file without tags; <see langword="null"/> when unknown.</param>
public sealed record WebampTrackInfo(string? Artist, string? Title, string? Name = null);
