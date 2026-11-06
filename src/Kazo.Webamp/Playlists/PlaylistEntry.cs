using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.Playlists;

/// <summary>One track of a playlist file: where it is, and what the list says about it.</summary>
/// <param name="Path">The track, resolved to an absolute path of the Kazo OS file system.</param>
/// <param name="Title">The title written in the list (<c>#EXTINF</c> or <c>TitleN</c>), or <see langword="null"/>.</param>
/// <param name="Duration">The duration written in the list, or <see langword="null"/> when unknown.</param>
public sealed record PlaylistEntry(VirtualPath Path, string? Title = null, TimeSpan? Duration = null);
