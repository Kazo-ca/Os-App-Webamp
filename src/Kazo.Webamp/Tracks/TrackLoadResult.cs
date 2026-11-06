using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.Tracks;

/// <summary>What <see cref="TrackLibrary.AddAsync"/> or <see cref="TrackLibrary.ReplaceAsync"/> did.</summary>
/// <param name="Added">The files given to Webamp, in order.</param>
/// <param name="Failures">The files that could not be read, and why; they are not in the list.</param>
public sealed record TrackLoadResult(IReadOnlyList<VirtualPath> Added, IReadOnlyList<TrackLoadFailure> Failures)
{
    /// <summary>Gets whether every file was read.</summary>
    public bool Succeeded => Failures.Count == 0;
}

/// <summary>A file that could not be read.</summary>
/// <param name="Path">The file.</param>
/// <param name="Message">Why, as the file system said it.</param>
public sealed record TrackLoadFailure(VirtualPath Path, string Message);
