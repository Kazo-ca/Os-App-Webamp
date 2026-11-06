using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.Opening;

/// <summary>What happens to Webamp's list when files arrive (decisions 5 and 16 of doc/en/plan.md).</summary>
public enum OpenMode
{
    /// <summary>Added at the end of the list, the first of them plays: a file opened from the OS, "Play in Webamp".</summary>
    Play,

    /// <summary>Added at the end of the list without playing: "Add to Webamp's list", Webamp's "Add file" and "Add dir".</summary>
    Enqueue,

    /// <summary>The list is replaced and its first file plays: Webamp's own "Open" (its eject button, "Play ▸ File…").</summary>
    Replace,
}

/// <summary>
/// Files or folders to put in Webamp's list. A folder stands for its audio files and those of its
/// subfolders, sorted by path (<see cref="AudioFileCollector"/>).
/// </summary>
/// <param name="Paths">The files and folders, in the order they were chosen.</param>
/// <param name="Mode">What happens to the list.</param>
public sealed record OpenRequest(IReadOnlyList<VirtualPath> Paths, OpenMode Mode);
