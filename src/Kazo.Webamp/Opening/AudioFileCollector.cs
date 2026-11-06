using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.IO;
using Kazo.Webamp.Playlists;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp.Opening;

/// <summary>
/// Turns the paths of an <see cref="OpenRequest"/> into the files to add (decision 16 of
/// doc/en/plan.md): an audio file or a playlist stays where it is (<c>PlaylistFiles</c> then reads
/// the playlist), a folder becomes its audio files and those of its subfolders, sorted by path (the
/// playlists of a folder are not read: they would add its tracks twice).
/// </summary>
/// <remarks>
/// A path that is none of these (a <c>.txt</c> in a mixed selection) is left out: Webamp would only
/// show it as a broken track. A path that does not exist is kept, so it is reported as unreadable.
/// </remarks>
public sealed class AudioFileCollector
{
    // Sort by path the way a file explorer lists a tree: folder by folder (so "Album/" comes before
    // "Album 2/"), names compared ignoring case, the case kept only to break ties.
    private static readonly IComparer<string> PathOrder = Comparer<string>.Create(static (left, right) =>
    {
        string[] leftParts = left.Split('/');
        string[] rightParts = right.Split('/');
        for (int index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            int byName = StringComparer.OrdinalIgnoreCase.Compare(leftParts[index], rightParts[index]);
            if (byName != 0)
            {
                return byName;
            }
        }

        int byDepth = leftParts.Length.CompareTo(rightParts.Length);
        return byDepth != 0 ? byDepth : StringComparer.Ordinal.Compare(left, right);
    });

    private readonly Func<string, CancellationToken, Task<bool>> _isDirectory;
    private readonly Func<string, CancellationToken, Task<string[]>> _filesBelow;

    /// <summary>Creates a collector that reads the Kazo OS file system.</summary>
    public AudioFileCollector()
        : this(
            KazoDirectory.ExistsAsync,
            static (path, cancellationToken) => KazoDirectory.GetFilesAsync(path, "*", SearchOption.AllDirectories, cancellationToken))
    {
    }

    /// <summary>Creates a collector with its own file system (the tests).</summary>
    /// <param name="isDirectory">Whether a path is a folder.</param>
    /// <param name="filesBelow">Every file of a folder and of its subfolders, as absolute paths.</param>
    public AudioFileCollector(
        Func<string, CancellationToken, Task<bool>> isDirectory,
        Func<string, CancellationToken, Task<string[]>> filesBelow)
    {
        ArgumentNullException.ThrowIfNull(isDirectory);
        ArgumentNullException.ThrowIfNull(filesBelow);
        _isDirectory = isDirectory;
        _filesBelow = filesBelow;
    }

    /// <summary>Gets the audio files and playlists of the paths, in order.</summary>
    /// <param name="paths">Files and folders.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The files; a folder that cannot be listed is reported in <see cref="AudioFiles.Failures"/>.</returns>
    public async Task<AudioFiles> CollectAsync(IReadOnlyList<VirtualPath> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        List<VirtualPath> files = [];
        List<TrackLoadFailure> failures = [];
        foreach (VirtualPath path in paths)
        {
            if (!await _isDirectory(path.Value, cancellationToken))
            {
                if (AudioMediaTypes.ForFile(path.Value) is not null || PlaylistFormats.IsPlaylist(path.Value))
                {
                    files.Add(path);
                }

                continue;
            }

            try
            {
                string[] below = await _filesBelow(path.Value, cancellationToken);
                files.AddRange(below
                    .Where(static file => AudioMediaTypes.ForFile(file) is not null)
                    .Order(PathOrder)
                    .Select(VirtualPath.Parse));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(new TrackLoadFailure(path, exception.Message));
            }
        }

        return new AudioFiles(files, failures);
    }
}

/// <summary>What <see cref="AudioFileCollector.CollectAsync"/> found.</summary>
/// <param name="Files">The audio files, in order.</param>
/// <param name="Failures">The folders that could not be listed.</param>
public sealed record AudioFiles(IReadOnlyList<VirtualPath> Files, IReadOnlyList<TrackLoadFailure> Failures);
