using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Blazor.Files;
using Kazo.Os.AppSdk.Files;
using Kazo.Os.AppSdk.Icons;
using Kazo.Webamp.Playlists;
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
    public override string Version => "1.1.1";

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
        // Work items 3 and 4: "Save list" writes a .m3u8 where the user chose, and skins opened or
        // loaded are copied to ~/.local/share/webamp/skins/ (decisions 10 and 11).
        AppCapabilities.FileSystemUserHomeWrite,
        // Webamp's "Open", "Add file" (work item 2) and "Load list" (work item 3).
        AppCapabilities.DialogFileOpen,
        // Webamp's "Save list" (work item 3).
        AppCapabilities.DialogFileSave,
        // Webamp's "Add dir" (work item 2, decision 16).
        AppCapabilities.DialogFolderSelect,
        // "Play in Webamp" and "Add to Webamp's list" in the file menus (work item 2, decision 5).
        AppCapabilities.ShellContextMenu,
        // The first launch explains how to import a skin (work item 4, decision 20).
        AppCapabilities.NotificationsPost,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// Decision 4 (work item 2): the formats browsers play. No other OS app opens audio, so Webamp is
    /// their default app; each type comes from <see cref="AudioMediaTypes"/>, which also types the
    /// Blob. Work item 3: playlists. Opening one adds its tracks to Webamp's list and plays the first one
    /// (decisions 5 and 10). Work item 4: Winamp 2 skins; opening one puts it on and keeps it in the
    /// Skins menu (decision 11).
    /// </remarks>
    public override IReadOnlyList<FileHandler> FileHandlers =>
    [
        new(PlaylistFormats.M3uMediaType, [".m3u"], ["open"]),
        new(PlaylistFormats.M3u8MediaType, [".m3u8"], ["open"]),
        new(PlaylistFormats.PlsMediaType, [".pls"], ["open"]),
        new("audio/mpeg", [".mp3"], ["open"]),
        new("audio/wav", [".wav"], ["open"]),
        new("audio/ogg", [".ogg", ".oga"], ["open"]),
        new("audio/opus", [".opus"], ["open"]),
        new("audio/flac", [".flac"], ["open"]),
        new("audio/mp4", [".m4a"], ["open"]),
        new("audio/aac", [".aac"], ["open"]),
        new("audio/webm", [".weba"], ["open"]),
        new(SkinMediaType, [".wsz"], ["open"]),
    ];

    /// <summary>Media type of a Winamp 2 skin (<c>.wsz</c>).</summary>
    public const string SkinMediaType = "application/x-winamp-skin";

    /// <inheritdoc />
    public override IReadOnlyList<AppIntentDeclarationManifest> Intents => [new(FilesIntent)];

    /// <summary>
    /// The file menu entries (decision 5, ADR 0151 and 0159 of Kazo OS): "Play in Webamp" and "Add to
    /// Webamp's list", on audio files, playlists (their tracks) and folders (a folder brings its audio
    /// files and those of its subfolders, sorted by path). One launch carries the whole selection, in
    /// its order.
    /// </summary>
    /// <param name="context">Records the entries; the build writes them to manifest.json.</param>
    [RegisterFileContextMenu]
    public static void RegisterFileContextMenu(RegisterFileContextMenuContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        IReadOnlyList<string> types =
        [
            .. AudioMediaTypes.Extensions.Order(StringComparer.Ordinal),
            .. PlaylistFormats.Extensions,
            "inode/directory",
        ];
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
