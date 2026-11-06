using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Files;

namespace Kazo.Webamp.Opening;

/// <summary>
/// Reads what the OS hands Webamp into an <see cref="OpenRequest"/> (decision 5 of doc/en/plan.md):
/// a file opened while Webamp runs, or one of its two file menu entries.
/// </summary>
public static class OpenIntents
{
    /// <summary>
    /// Gets the request of an intent, or <see langword="null"/> when it opens nothing (a plain
    /// launch from the menu, an intent Webamp does not know).
    /// </summary>
    /// <param name="intent">The intent, at launch (<c>LaunchIntent</c>) or while Webamp runs.</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item><see cref="WebampApp.FilesIntent"/>: the paths of the payload; the entry
    /// <see cref="WebampApp.EnqueueEntry"/> adds without playing, any other plays.</item>
    /// <item>Any other intent with arguments (a second launch arrives as
    /// <see cref="AppIntentIds.LaunchApp"/>, a double-click on a file as its path): the absolute
    /// paths among the arguments play.</item>
    /// </list>
    /// </remarks>
    public static OpenRequest? Read(AppIntentDelivery? intent)
    {
        if (intent is null)
        {
            return null;
        }

        if (intent.IntentId == WebampApp.FilesIntent)
        {
            OpenMode mode = KazoFileMenuIntent.ReadEntry(intent.PayloadJson) == WebampApp.EnqueueEntry
                ? OpenMode.Enqueue
                : OpenMode.Play;
            return Request(KazoFileMenuIntent.ReadPaths(intent.PayloadJson), mode);
        }

        return Request(intent.Arguments, OpenMode.Play);
    }

    /// <summary>
    /// Gets the request of the launch arguments (<c>webamp a.mp3 b.mp3</c>, a double-click before
    /// Webamp runs), or <see langword="null"/> when none is a path.
    /// </summary>
    /// <param name="arguments">The arguments, without the app id.</param>
    public static OpenRequest? FromArguments(IReadOnlyList<string> arguments) => Request(arguments, OpenMode.Play);

    private static OpenRequest? Request(IReadOnlyList<string> candidates, OpenMode mode)
    {
        List<VirtualPath> paths = [];
        foreach (string candidate in candidates)
        {
            if (!candidate.StartsWith('/'))
            {
                continue;
            }

            try
            {
                paths.Add(VirtualPath.Parse(candidate));
            }
            catch (FormatException)
            {
                // Not a path: an option or a broken argument.
            }
        }

        return paths.Count > 0 ? new OpenRequest(paths, mode) : null;
    }
}
