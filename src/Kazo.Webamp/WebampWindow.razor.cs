using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Globalization;
using Kazo.Os.AppSdk.IO;
using Kazo.Os.Simulation.Abstractions.Processes;
using Kazo.Webamp.Player;
using Kazo.Webamp.Playlists;
using Kazo.Webamp.Skins;
using Kazo.Webamp.Tracks;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Kazo.Webamp;

/// <summary>
/// The Webamp window (decision 6 of doc/en/plan.md): an OS window that contains Webamp. Webamp's
/// close button closes the OS window, and the music stops with it (decision 8).
/// </summary>
/// <remarks>
/// Opened on a file (a double-click, "Open with", <c>webamp song.mp3</c>), it plays that file; on a
/// playlist, its tracks are added and the first one plays (decision 10); on a <c>.wsz</c>, it puts
/// that skin on (<c>WebampWindow.Skins.cs</c>, work item 4). Webamp's "Load list" and "Save list" go
/// through the OS dialogs. The <see cref="Tracks"/> library is what work items 2 to 5 build on (open,
/// playlists, state).
/// </remarks>
public sealed partial class WebampWindow : IAsyncDisposable
{
    // How long the OS waits for the app's own process to stop after Webamp's close button.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private ElementReference _surface;
    private WebampPlayer? _player;
    private string? _status;
    private bool _failed;
    private bool _closing;
    private IStringLocalizer<Strings>? _localizer;
    private readonly PlaylistFiles _playlists = new();

    [Inject]
    private IJSRuntime JavaScript { get; set; } = default!;

    /// <summary>Gets the files in Webamp's list; <see langword="null"/> until Webamp is on screen.</summary>
    internal TrackLibrary? Tracks { get; private set; }

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_player is not null)
        {
            _player.PlaylistChanged -= OnPlaylistChanged;
            _player.LoadListRequested -= OnLoadListRequestedAsync;
            _player.SaveListRequested -= OnSaveListRequestedAsync;
            DetachSkins(_player);
            await _player.DisposeAsync();
            _player = null;
        }
    }

    /// <inheritdoc />
    protected override void OnAppInitialized() => _status = L["Loading"];

    /// <inheritdoc />
    protected override async Task OnAppAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // The user's skins first: Webamp starts with them in its menu and the skin in use on.
        SkinController skins = await StartSkinsAsync();
        WebampPlayer player = new(JavaScript);
        try
        {
            await player.StartAsync(_surface, skins.Library.Skins, skins.Current, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            await player.DisposeAsync();
            Fail(L["StartFailed", exception.Message]);
            return;
        }

        _player = player;
        _player.Closed += OnWebampClosedAsync;
        _player.PlaylistChanged += OnPlaylistChanged;
        _player.LoadListRequested += OnLoadListRequestedAsync;
        _player.SaveListRequested += OnSaveListRequestedAsync;
        Tracks = new TrackLibrary(player);
        AttachSkins(player, skins);
        _status = null;

        // Opened on a file: a skin is put on; music plays (a playlist: its tracks are added and the
        // first one plays).
        if (LaunchFile is { } file)
        {
            await OpenAsync(file);
        }

        StateHasChanged();
        await ShowSkinTipOnceAsync();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Webamp is one window per user: a file opened while it runs (a double-click, <c>webamp x</c>)
    /// arrives here with its path as the first argument.
    /// </remarks>
    protected override async Task OnIntentReceivedAsync(AppIntentDelivery intent)
    {
        if (intent.Arguments is not [{ } first, ..] || !first.StartsWith('/'))
        {
            return;
        }

        VirtualPath file;
        try
        {
            file = VirtualPath.Parse(first);
        }
        catch (FormatException)
        {
            return;
        }

        await OpenAsync(file);
    }

    // A file given to Webamp: a skin is put on, anything else goes to the list and plays.
    private Task OpenAsync(VirtualPath file) =>
        IsSkinFile(file) ? OpenSkinAsync(file) : OpenFilesAsync([file], play: true);

    // Webamp's close button (or "Exit" in its menu) closes the OS window: the app's own process
    // stops, which an app may always do (ADR 0140 of Kazo OS).
    private async Task OnWebampClosedAsync()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        try
        {
            await AppContext.Processes.StopAsync(AppContext.ProcessId, CloseTimeout, ProcessExitReason.CloseRequested);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
        {
            // Already stopping.
        }
    }

    private void OnPlaylistChanged(IReadOnlyList<string> keys) => Tracks?.OnPlaylistChanged(keys);

    /// <summary>
    /// Adds files at the end of Webamp's list (decision 5): an audio file is one track, a playlist
    /// its tracks, in order. With <paramref name="play"/>, the first one added starts playing.
    /// </summary>
    /// <param name="files">The files opened.</param>
    /// <param name="play">Whether the first track starts playing.</param>
    internal async Task OpenFilesAsync(IReadOnlyList<VirtualPath> files, bool play)
    {
        if (Tracks is null)
        {
            return;
        }

        try
        {
            PlaylistExpansion expansion = await _playlists.ExpandAsync(files, AppContext.CancellationToken);
            TrackLoadResult result = await Tracks.AddAsync(expansion.Entries, play, AppContext.CancellationToken);
            Report(expansion, result);
        }
        catch (JSException exception)
        {
            Fail(L["StartFailed", exception.Message]);
        }
    }

    private Task OnLoadListRequestedAsync() => FromWebampAsync(LoadListAsync);

    private Task OnSaveListRequestedAsync(IReadOnlyList<WebampListEntry> listed, int unsaved) =>
        FromWebampAsync(() => SaveListAsync(listed, unsaved));

    // Webamp's menus call C# from JavaScript, outside the window's own handlers: KazoFile needs the
    // app's ambient context entered here (ADR 0043 of Kazo OS), and an error must be shown, since
    // the host module has nobody to give it to.
    private async Task FromWebampAsync(Func<Task> action)
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(L["ActionFailed", exception.Message]);
        }
    }

    // "Load list" of Webamp: the OS's Open dialog, then the list replaces Webamp's, stopped, as
    // Winamp does.
    private async Task LoadListAsync()
    {
        if (Tracks is null)
        {
            return;
        }

        OpenFileDialogResult choice = await OpenFileAsync(new OpenFileDialogRequest
        {
            Title = L["LoadListTitle"],
            InitialDirectory = VirtualPath.Parse(AppContext.HomeDirectory),
            Filters = [PlaylistFilter(), new FileDialogFilter(L["AllFiles"], [], [])],
        });
        if (choice.Status != FileDialogStatus.Selected || choice.Resources.Count == 0)
        {
            return;
        }

        VirtualPath path = choice.Resources[0].Path;
        try
        {
            PlaylistExpansion expansion = await _playlists.ExpandAsync([path], AppContext.CancellationToken);
            if (expansion.Failures.Count == 0 && expansion.Entries.Count == 0)
            {
                // Nothing playable (only web addresses, say): Webamp's list stays as it was.
                Report(expansion, null, L["ListEmpty", KazoPath.GetFileName(path.Value) ?? path.Value]);
                return;
            }

            TrackLoadResult result = await Tracks.ReplaceAsync(expansion.Entries, play: false, AppContext.CancellationToken);
            Report(expansion, result);
        }
        catch (JSException exception)
        {
            Fail(L["StartFailed", exception.Message]);
        }
    }

    // "Save list" of Webamp: the OS's Save dialog, then a .m3u8 with paths relative to it.
    private async Task SaveListAsync(IReadOnlyList<WebampListEntry> listed, int unsaved)
    {
        if (Tracks is null)
        {
            return;
        }

        SaveFileDialogResult choice = await SaveFileAsync(new SaveFileDialogRequest
        {
            Title = L["SaveListTitle"],
            InitialDirectory = VirtualPath.Parse(AppContext.HomeDirectory),
            SuggestedFileName = L["DefaultListName"] + PlaylistFormats.SaveExtension,
            DefaultExtension = PlaylistFormats.SaveExtension,
            Filters = [new FileDialogFilter(L["M3u8Filter"], [PlaylistFormats.SaveExtension], [PlaylistFormats.M3u8MediaType])],
            ConfirmOverwrite = true,
        });
        if (choice.Status != FileDialogStatus.Selected || choice.Resource is null)
        {
            return;
        }

        VirtualPath path = choice.Resource.Path;
        string name = KazoPath.GetFileName(path.Value) ?? path.Value;
        try
        {
            await _playlists.SaveAsync(path, PlaylistFiles.ToEntries(Tracks, listed), AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Fail(L["SaveFailed", name, exception.Message]);
            return;
        }

        Inform(unsaved > 0 ? $"{L["ListSaved", name]} {L["TracksNotSaved", unsaved]}" : L["ListSaved", name]);
    }

    private FileDialogFilter PlaylistFilter() => new(
        L["PlaylistFilter"],
        [.. PlaylistFormats.Extensions],
        [PlaylistFormats.M3uMediaType, PlaylistFormats.M3u8MediaType, PlaylistFormats.PlsMediaType]);

    // One line for what could not be played: the files that failed, then what the lists left out.
    private void Report(PlaylistExpansion expansion, TrackLoadResult? result, string? problem = null)
    {
        List<TrackLoadFailure> failures = [.. expansion.Failures, .. result?.Failures ?? []];
        List<string> notes = problem is null ? [] : [problem];
        if (failures.Count > 0)
        {
            TrackLoadFailure first = failures[0];
            notes.Add(L["FileFailed", KazoPath.GetFileName(first.Path.Value) ?? first.Path.Value, first.Message]);
            if (failures.Count > 1)
            {
                notes.Add(L["MoreFailed", failures.Count - 1]);
            }
        }

        if (expansion.IgnoredUrls.Count > 0)
        {
            notes.Add(L["UrlsIgnored", expansion.IgnoredUrls.Count]);
        }

        if (expansion.Unresolved.Count > 0)
        {
            notes.Add(L["PathsUnresolved", expansion.Unresolved.Count, expansion.Unresolved[0]]);
        }

        if (notes.Count == 0)
        {
            Dismiss();
        }
        else if (failures.Count > 0 || problem is not null)
        {
            Fail(string.Join(' ', notes));
        }
        else
        {
            Inform(string.Join(' ', notes));
        }
    }

    private void Inform(string message)
    {
        _failed = false;
        _status = message;
        StateHasChanged();
    }

    private void Dismiss()
    {
        _failed = false;
        _status = null;
        StateHasChanged();
    }

    private void Fail(string message)
    {
        _failed = true;
        _status = message;
        StateHasChanged();
    }
}
