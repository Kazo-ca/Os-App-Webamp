namespace Kazo.Webamp.Playlists;

/// <summary>What <see cref="PlaylistReader"/> found in a playlist file.</summary>
/// <param name="Entries">The tracks, in the list's order.</param>
/// <param name="IgnoredUrls">Web addresses of the list: the first version plays no music from the
/// internet (decision 9 of doc/en/plan.md), so they are left out.</param>
/// <param name="Unresolved">Locations that are not a path of the Kazo OS file system (a Windows
/// drive such as <c>C:\</c>, a network share, a malformed path).</param>
public sealed record PlaylistReadResult(
    IReadOnlyList<PlaylistEntry> Entries,
    IReadOnlyList<string> IgnoredUrls,
    IReadOnlyList<string> Unresolved);
