using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Globalization;
using Kazo.Os.AppSdk.IO;
using Kazo.Os.Simulation.Abstractions.Processes;
using Kazo.Webamp.Opening;
using Kazo.Webamp.Player;
using Kazo.Webamp.Playlists;
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
/// <para>
/// One window per user (decision 5): a file opened from the OS (a double-click, "Open with",
/// <c>webamp song.mp3</c>), at launch or while the window is open, and the two file menu entries go
/// through <see cref="OpenQueue"/>, which adds them at the end of the list in the order they came,
/// the first of a burst playing. A folder brings its audio files, sorted by path; a playlist its
/// tracks (decision 10). Webamp's "Open", "Add file" and "Add dir" (decision 16), "Load list" and
/// "Save list" go through the OS dialogs.
/// </para>
/// <para>The <see cref="Tracks"/> library is what work items 4 and 5 build on (skins, state).</para>
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
    private readonly AudioFileCollector _collector = new();
    private readonly LoadedMemoryWarning _memoryWarning = new();
    private OpenQueue? _opens;
    private bool _dialogOpen;
    private VirtualPath? _lastFolder;

    [Inject]
    private IJSRuntime JavaScript { get; set; } = default!;

    /// <summary>Gets the files in Webamp's list; <see langword="null"/> until Webamp is on screen.</summary>
    internal TrackLibrary? Tracks { get; private set; }

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    private OpenQueue Opens => _opens ??= CreateQueue();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _opens?.Stop();
        if (_player is not null)
        {
            _player.PlaylistChanged -= OnPlaylistChanged;
            _player.OpenRequested -= OnOpenRequestedAsync;
            _player.LoadListRequested -= OnLoadListRequestedAsync;
            _player.SaveListRequested -= OnSaveListRequestedAsync;
            await _player.DisposeAsync();
            _player = null;
        }
    }

    /// <inheritdoc />
    protected override void OnAppInitialized()
    {
        _status = L["Loading"];

        // Opened on files (a double-click, "Open with", a file menu entry, `webamp a.mp3 b.mp3`):
        // they wait in the queue until Webamp is on screen.
        if ((OpenIntents.Read(LaunchIntent) ?? OpenIntents.FromArguments(LaunchArguments)) is { } request)
        {
            Opens.Enqueue(request);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// A file opened while Webamp runs, a second launch with files, or a file menu entry: added to the
    /// list (decision 5). Intents that arrive before Webamp is on screen wait in the queue.
    /// </remarks>
    protected override Task OnIntentReceivedAsync(AppIntentDelivery intent)
    {
        if (OpenIntents.Read(intent) is { } request)
        {
            Opens.Enqueue(request);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task OnAppAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        WebampPlayer player = new(JavaScript);
        try
        {
            await player.StartAsync(_surface, AppContext.CancellationToken);
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
        _player.OpenRequested += OnOpenRequestedAsync;
        Tracks = new TrackLibrary(player);
        _status = null;
        Opens.Start();
        StateHasChanged();
    }

    private OpenQueue CreateQueue()
    {
        OpenQueue queue = new(request => InvokeAsync(() => OpenAsync(request)));
        queue.Failed += exception => _ = InvokeAsync(() => Fail(L["ActionFailed", exception.Message]));
        return queue;
    }

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
    /// its tracks, a folder its audio files, in order. With <paramref name="play"/>, the first one
    /// added starts playing.
    /// </summary>
    /// <param name="files">The files opened.</param>
    /// <param name="play">Whether the first track starts playing.</param>
    internal Task OpenFilesAsync(IReadOnlyList<VirtualPath> files, bool play) =>
        OpenAsync(new OpenRequest(files, play ? OpenMode.Play : OpenMode.Enqueue));

    // One request of the queue: folders become their audio files, playlists their tracks, and the
    // tracks go into Webamp's list.
    private async Task OpenAsync(OpenRequest request)
    {
        if (Tracks is null)
        {
            return;
        }

        // The queue may run after a call from JavaScript (a dialog), which carries no app context:
        // KazoFile and KazoDirectory need it (ADR 0043 of Kazo OS).
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        try
        {
            AudioFiles found = await _collector.CollectAsync(request.Paths, AppContext.CancellationToken);
            PlaylistExpansion expansion = await _playlists.ExpandAsync(found.Files, AppContext.CancellationToken);
            expansion = expansion with { Failures = [.. found.Failures, .. expansion.Failures] };
            if (expansion.Entries.Count == 0)
            {
                // Nothing to play (an empty folder, a list of web addresses): the list stays as it was.
                Report(expansion, null, expansion.Failures.Count == 0 ? L["NoAudioFiles", NameOf(request.Paths[0])].Value : null);
                return;
            }

            TrackLoadResult result = request.Mode == OpenMode.Replace
                ? await Tracks.ReplaceAsync(expansion.Entries, play: true, AppContext.CancellationToken)
                : await Tracks.AddAsync(expansion.Entries, play: request.Mode == OpenMode.Play, AppContext.CancellationToken);
            Report(expansion, result);
            WarnAboutMemory();
        }
        catch (JSException exception)
        {
            Fail(L["StartFailed", exception.Message]);
        }
    }

    // Decision 14: every track is a whole file in memory; past 1 GB, say so once. The user may go on,
    // so the queue does not wait for the answer.
    private void WarnAboutMemory()
    {
        if (Tracks is null || !_memoryWarning.ShouldWarn(Tracks.LoadedBytes))
        {
            return;
        }

        string size = (Tracks.LoadedBytes / (double)LoadedMemoryWarning.Limit).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);
        _ = ShowMemoryWarningAsync(L["MemoryWarning", size]);
    }

    private async Task ShowMemoryWarningAsync(string message)
    {
        try
        {
            await MessageBox(L["MemoryWarningTitle"], message, MessageBoxType.Ok, MessageBoxIcon.Warning, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            // The window closed while the message was shown.
        }
    }

    // Webamp's "Open", "Add file" and "Add dir" (decision 16): the OS dialogs instead of the
    // browser's file picker, which the host module stopped. One dialog at a time.
    private Task OnOpenRequestedAsync(WebampOpenKind kind) => FromWebampAsync(async () =>
    {
        if (_dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        try
        {
            if (await ChooseAsync(kind) is { } request)
            {
                Opens.Enqueue(request);
            }
        }
        finally
        {
            _dialogOpen = false;
        }
    });

    private async Task<OpenRequest?> ChooseAsync(WebampOpenKind kind)
    {
        VirtualPath start = await StartFolderAsync();
        if (kind == WebampOpenKind.AddFolder)
        {
            SelectFolderDialogResult folder = await SelectFolderAsync(
                new SelectFolderDialogRequest { Title = L["AddFolderTitle"], InitialDirectory = start }, AppContext.CancellationToken);
            if (folder is not { Status: FileDialogStatus.Selected, Resource: { } chosen })
            {
                return null;
            }

            _lastFolder = ParentOf(chosen.Path);
            return new OpenRequest([chosen.Path], OpenMode.Enqueue);
        }

        OpenFileDialogResult files = await OpenFileAsync(
            new OpenFileDialogRequest
            {
                Title = kind == WebampOpenKind.Open ? L["OpenTitle"] : L["AddFilesTitle"],
                InitialDirectory = start,
                AllowMultiple = true,
                Filters = [MusicFilter(), new FileDialogFilter(L["AllFiles"], [], [])],
            },
            AppContext.CancellationToken);
        if (files.Status != FileDialogStatus.Selected || files.Resources.Count == 0)
        {
            return null;
        }

        _lastFolder = ParentOf(files.Resources[0].Path);
        return new OpenRequest(
            [.. files.Resources.Select(resource => resource.Path)],
            kind == WebampOpenKind.Open ? OpenMode.Replace : OpenMode.Enqueue);
    }

    // Where the dialogs open: the last folder used, else ~/Music when it exists, else ~. Always given:
    // without it the dialogs of Kazo OS 3.0.0 open /home/<user id>, which does not exist.
    private async Task<VirtualPath> StartFolderAsync()
    {
        string music = KazoPath.Combine(AppContext.HomeDirectory, "Music");
        foreach (string? candidate in new[] { _lastFolder?.Value, music })
        {
            if (candidate is not null && await KazoDirectory.ExistsAsync(candidate, AppContext.CancellationToken))
            {
                return VirtualPath.Parse(candidate);
            }
        }

        return VirtualPath.Parse(AppContext.HomeDirectory);
    }

    // Music Webamp opens: the audio formats and the playlists.
    private FileDialogFilter MusicFilter() => new(
        L["MusicFilter"],
        [.. AudioMediaTypes.Extensions.Order(StringComparer.Ordinal), .. PlaylistFormats.Extensions],
        [.. AudioMediaTypes.MediaTypes, PlaylistFormats.M3uMediaType, PlaylistFormats.M3u8MediaType, PlaylistFormats.PlsMediaType]);

    private static VirtualPath? ParentOf(VirtualPath path) =>
        KazoPath.GetDirectoryName(path.Value) is { Length: > 0 } parent ? VirtualPath.Parse(parent) : null;

    private static string NameOf(VirtualPath path) => KazoPath.GetFileName(path.Value) is { Length: > 0 } name ? name : path.Value;

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
