using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Blazor.Files;
using Kazo.Os.AppSdk.Files;
using Kazo.Os.AppSdk.Icons;
using Kazo.Webamp.Tracks;

namespace Kazo.Webamp;

/// <summary>
/// The app's manifest (decision 18 of doc/en/plan.md). The build generates <c>manifest.json</c> from
/// this class (Kazo.Os.App.Build); it is never instantiated at run time.
/// </summary>
/// <remarks>
/// Work item 1 declares the base: the window, desktop only, one window per user, reading the
/// user's music and the app's own folder. Work item 2 adds the audio types (Webamp becomes the app
/// that opens them), the OS dialogs and the two file menu entries. Work items 3 to 5 add theirs.
/// </remarks>
[AppManifest]
public sealed class WebampApp : AppDefinition
{
    /// <summary>Width of Webamp's three windows stacked at normal size (main, equalizer, playlist).</summary>
    public const int ContentWidth = 275;

    /// <summary>Height of Webamp's three windows stacked at normal size.</summary>
    public const int ContentHeight = 348;

    /// <summary>
    /// The intent of the two file menu entries (decision 5): the payload names the entry,
    /// <see cref="PlayEntry"/> or <see cref="EnqueueEntry"/>, and the files or folders.
    /// </summary>
    public const string FilesIntent = "ca.kazo.webamp.files.v1";

    /// <summary>Name of the "Play in Webamp" entry: add at the end of the list and play.</summary>
    public const string PlayEntry = "play";

    /// <summary>Name of the "Add to Webamp's list" entry: add at the end of the list, without playing.</summary>
    public const string EnqueueEntry = "enqueue";

    /// <inheritdoc />
    public override string Id => "ca.kazo.webamp";

    /// <inheritdoc />
    public override string Name => "Webamp";

    /// <inheritdoc />
    public override AppKind Kind => AppKind.Window;

    /// <inheritdoc />
    public override Type EntryPoint => typeof(WebampWindow);

    // No MobileEntryPoint: desktop only for the first version (decision 7).

    /// <inheritdoc />
    public override string PublisherId => "pub.kazo";

    /// <inheritdoc />
    public override string Description => "A Winamp 2 style music player, built on Webamp";

    /// <inheritdoc />
    public override string Category => "multimedia";

    /// <inheritdoc />
    /// <remarks>The tag that publishes to the Kazo OS Store must be v + this version.</remarks>
    public override string Version => "1.0.0";

    /// <inheritdoc />
    /// <remarks>One Webamp per user: opening a file while it runs reaches the open window (decision 5).</remarks>
    public override bool SingleInstancePerUser => true;

    /// <inheritdoc />
    public override IReadOnlyList<string> Capabilities =>
    [
        // The user's music (their own network drives too once Kazo OS has ADR 0231).
        AppCapabilities.FileSystemUserHomeRead,
        // ~/.config/ca.kazo.webamp/ (the state of work item 5).
        AppCapabilities.FileSystemPrivateRead,
        AppCapabilities.FileSystemPrivateWrite,
        // Webamp's "Open" and "Add file" (decision 16).
        AppCapabilities.DialogFileOpen,
        // Webamp's "Add dir" (decision 16).
        AppCapabilities.DialogFolderSelect,
        // "Play in Webamp" and "Add to Webamp's list" in the file menus (decision 5).
        AppCapabilities.ShellContextMenu,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// Decision 4: the formats browsers play. No other OS app opens audio, so Webamp is their
    /// default app; each type comes from <see cref="AudioMediaTypes"/>, which also types the Blob.
    /// </remarks>
    public override IReadOnlyList<FileHandler> FileHandlers =>
    [
        new("audio/mpeg", [".mp3"], ["open"]),
        new("audio/wav", [".wav"], ["open"]),
        new("audio/ogg", [".ogg", ".oga"], ["open"]),
        new("audio/opus", [".opus"], ["open"]),
        new("audio/flac", [".flac"], ["open"]),
        new("audio/mp4", [".m4a"], ["open"]),
        new("audio/aac", [".aac"], ["open"]),
        new("audio/webm", [".weba"], ["open"]),
    ];

    /// <inheritdoc />
    public override IReadOnlyList<AppIntentDeclarationManifest> Intents => [new(FilesIntent)];

    /// <summary>
    /// The file menu entries (decision 5, ADR 0151 and 0159 of Kazo OS): "Play in Webamp" and "Add to
    /// Webamp's list", on audio files and on folders (a folder brings its audio files and those of
    /// its subfolders, sorted by path). One launch carries the whole selection, in its order.
    /// </summary>
    /// <param name="context">Records the entries; the build writes them to manifest.json.</param>
    [RegisterFileContextMenu]
    public static void RegisterFileContextMenu(RegisterFileContextMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyList<string> types = [.. AudioMediaTypes.Extensions.Order(StringComparer.Ordinal), "inode/directory"];
        context.Register(
            title: "Menu.Play",
            name: PlayEntry,
            intent: FilesIntent,
            icon: KazoIcons.Vector(IconLibrary.Lucide, "play"),
            scope: FileMenuScope.File | FileMenuScope.Directory,
            types: types,
            after: KazoFileMenuItems.Open);
        context.Register(
            title: "Menu.Enqueue",
            name: EnqueueEntry,
            intent: FilesIntent,
            icon: KazoIcons.Vector(IconLibrary.Lucide, "list-plus"),
            scope: FileMenuScope.File | FileMenuScope.Directory,
            types: types,
            after: PlayEntry);
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Icons => ["icons/app.svg"];

    /// <inheritdoc />
    public override IReadOnlyList<AssetManifest> Assets =>
        [new("icons/app.svg", AssetKind.Image, new string('0', 64), "app-icon")];

    /// <inheritdoc />
    /// <remarks>
    /// Sizes are the outer size of the OS window (title bar included): the content is Webamp's three
    /// windows stacked (275×348), the smallest size shows its main window.
    /// </remarks>
    public override WindowOptions? Window => new()
    {
        DefaultWidth = ContentWidth + WindowChromeWidth,
        DefaultHeight = ContentHeight + WindowChromeHeight,
        MinWidth = ContentWidth + WindowChromeWidth,
        MinHeight = MainWindowHeight + WindowChromeHeight,
    };

    // Borders and title bar of an OS window (the tallest title bar of the Kazo OS themes, 2.35rem).
    private const int WindowChromeWidth = 2;
    private const int WindowChromeHeight = 40;

    // Height of Webamp's main window alone.
    private const int MainWindowHeight = 116;
}
