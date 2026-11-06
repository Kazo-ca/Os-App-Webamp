using Kazo.Os.App.Abstractions;
using Kazo.Webamp.Playlists;

namespace Kazo.Webamp;

/// <summary>
/// The app's manifest (decision 18 of doc/en/plan.md). The build generates <c>manifest.json</c> from
/// this class (Kazo.Os.App.Build); it is never instantiated at run time.
/// </summary>
/// <remarks>
/// Work item 1 declares the base: the window, desktop only, one window per user, reading the
/// user's music and the app's own folder. Work items 2 to 5 add their file types and capabilities.
/// </remarks>
[AppManifest]
public sealed class WebampApp : AppDefinition
{
    /// <summary>Width of Webamp's three windows stacked at normal size (main, equalizer, playlist).</summary>
    public const int ContentWidth = 275;

    /// <summary>Height of Webamp's three windows stacked at normal size.</summary>
    public const int ContentHeight = 348;

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
        // "Save list" writes a .m3u8 where the user chose (work item 3).
        AppCapabilities.FileSystemUserHomeWrite,
        // "Load list" and "Save list" of Webamp open the OS dialogs (work item 3).
        AppCapabilities.DialogFileOpen,
        AppCapabilities.DialogFileSave,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// Work item 3: playlists. Opening one adds its tracks to Webamp's list and plays the first one
    /// (decisions 5 and 10).
    /// </remarks>
    public override IReadOnlyList<FileHandler> FileHandlers =>
    [
        new(PlaylistFormats.M3uMediaType, [".m3u"], ["open"]),
        new(PlaylistFormats.M3u8MediaType, [".m3u8"], ["open"]),
        new(PlaylistFormats.PlsMediaType, [".pls"], ["open"]),
    ];

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
