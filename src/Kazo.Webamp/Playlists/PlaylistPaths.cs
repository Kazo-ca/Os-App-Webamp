using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.Playlists;

/// <summary>Paths inside playlists: the folder of a list, and a track's path relative to it.</summary>
public static class PlaylistPaths
{
    /// <summary>Returns the folder that holds <paramref name="path"/> (<c>/</c> for a file at the root).</summary>
    /// <param name="path">A file.</param>
    public static string FolderOf(VirtualPath path)
    {
        int slash = path.Value.LastIndexOf('/');
        return slash <= 0 ? "/" : path.Value[..slash];
    }

    /// <summary>
    /// Returns the path of <paramref name="target"/> relative to <paramref name="folder"/>, with
    /// <c>/</c> separators and <c>..</c> to go up: the list keeps working when the folder that holds
    /// the list and its music moves (decision 10 of doc/en/plan.md).
    /// </summary>
    /// <param name="folder">The folder of the list.</param>
    /// <param name="target">A track.</param>
    public static string GetRelativePath(string folder, VirtualPath target)
    {
        ArgumentNullException.ThrowIfNull(folder);
        string[] from = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string[] to = target.Value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int common = 0;
        // The last segment of the target is the file: never part of the common folder.
        while (common < from.Length && common < to.Length - 1 && string.Equals(from[common], to[common], StringComparison.Ordinal))
        {
            common++;
        }

        IEnumerable<string> up = Enumerable.Repeat("..", from.Length - common);
        return string.Join('/', up.Concat(to.Skip(common)));
    }
}
