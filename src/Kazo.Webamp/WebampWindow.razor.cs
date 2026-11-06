using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Globalization;
using Kazo.Os.AppSdk.IO;
using Kazo.Os.Simulation.Abstractions.Processes;
using Kazo.Webamp.Opening;
using Kazo.Webamp.Player;
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
/// One window per user (decision 5): a file opened from the OS, at launch or while the window is
/// open, and the two file menu entries go through <see cref="OpenQueue"/>, which adds them at the end
/// of the list in the order they came, the first of a burst playing. Webamp's own "Open", "Add file"
/// and "Add dir" show the OS dialogs (decision 16).
/// </para>
/// <para>The <see cref="Tracks"/> library is what work items 3 to 5 build on (playlists, state).</para>
/// </remarks>
public sealed partial class WebampWindow : IAsyncDisposable
{
    // How long the OS waits for the app's own process to stop after Webamp's close button.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private readonly LoadedMemoryWarning _memoryWarning = new();
    private readonly AudioFileCollector _collector = new();
    private OpenQueue? _opens;
    private ElementReference _surface;
    private WebampPlayer? _player;
    private string? _status;
    private bool _failed;
    private bool _closing;
    private bool _dialogOpen;
    private VirtualPath? _lastFolder;
    private IStringLocalizer<Strings>? _localizer;

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
        _player.OpenRequested += OnOpenRequestedAsync;
        Tracks = new TrackLibrary(player);
        _status = null;
        Opens.Start();
        StateHasChanged();
    }

    private OpenQueue CreateQueue()
    {
        OpenQueue queue = new(request => InvokeAsync(() => OpenAsync(request)));
        queue.Failed += exception => _ = InvokeAsync(() => Fail(L["StartFailed", exception.Message]));
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

    // Webamp's "Open", "Add file" and "Add dir" (decision 16): the OS dialogs instead of the
    // browser's file picker, which the host module stopped.
    private Task OnOpenRequestedAsync(WebampOpenKind kind) => InvokeAsync(async () =>
    {
        // A call from JavaScript carries no app context: KazoUser and KazoDirectory need it.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
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
        catch (Exception exception) when (exception is InvalidOperationException or OperationCanceledException)
        {
            // The window is closing, or the dialog service is not there.
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

            _lastFolder = KazoPath.GetDirectoryName(chosen.Path.Value) is { Length: > 0 } parent ? VirtualPath.Parse(parent) : null;
            return new OpenRequest([chosen.Path], OpenMode.Enqueue);
        }

        OpenFileDialogResult files = await OpenFileAsync(
            new OpenFileDialogRequest
            {
                Title = kind == WebampOpenKind.Open ? L["OpenTitle"] : L["AddFilesTitle"],
                InitialDirectory = start,
                AllowMultiple = true,
                Filters = [AudioFilter()],
            },
            AppContext.CancellationToken);
        if (files.Status != FileDialogStatus.Selected || files.Resources.Count == 0)
        {
            return null;
        }

        _lastFolder = KazoPath.GetDirectoryName(files.Resources[0].Path.Value) is { Length: > 0 } folderOfFiles ? VirtualPath.Parse(folderOfFiles) : null;
        return new OpenRequest([.. files.Resources.Select(resource => resource.Path)], kind == WebampOpenKind.Open ? OpenMode.Replace : OpenMode.Enqueue);
    }

    // Where the dialogs open: the last folder used, else ~/Music when it exists, else ~. Always given:
    // without it the dialogs of Kazo OS 3.0.0 open /home/<user id>, which does not exist.
    private async Task<VirtualPath> StartFolderAsync()
    {
        string home = KazoUser.Describe(AppContext).HomeDirectory;
        string music = KazoPath.Combine(home, "Music");
        foreach (string? candidate in new[] { _lastFolder?.Value, music })
        {
            if (candidate is not null && await KazoDirectory.ExistsAsync(candidate, AppContext.CancellationToken))
            {
                return VirtualPath.Parse(candidate);
            }
        }

        return VirtualPath.Parse(home);
    }

    private FileDialogFilter AudioFilter() => new(
        L["AudioFilter"],
        [.. AudioMediaTypes.Extensions.Order(StringComparer.Ordinal)],
        [.. AudioMediaTypes.MediaTypes]);

    // One request of the queue: folders become their audio files, the files go into Webamp's list.
    private async Task OpenAsync(OpenRequest request)
    {
        if (Tracks is null)
        {
            return;
        }

        // The queue may run after a call from JavaScript (a dialog), which carries no app context:
        // KazoFile and KazoDirectory need it.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);

        try
        {
            AudioFiles found = await _collector.CollectAsync(request.Paths, AppContext.CancellationToken);
            TrackLoadResult result = found.Files.Count == 0
                ? new TrackLoadResult([], [])
                : request.Mode == OpenMode.Replace
                    ? await Tracks.ReplaceAsync(found.Files, play: true, AppContext.CancellationToken)
                    : await Tracks.AddAsync(found.Files, play: request.Mode == OpenMode.Play, AppContext.CancellationToken);
            Report(request, found, result);
            WarnAboutMemory();
        }
        catch (JSException exception)
        {
            Fail(L["StartFailed", exception.Message]);
        }
    }

    private void Report(OpenRequest request, AudioFiles found, TrackLoadResult result)
    {
        if (found.Failures.Concat(result.Failures).FirstOrDefault() is { } failure)
        {
            Fail(L["FileFailed", NameOf(failure.Path), failure.Message]);
        }
        else if (found.Files.Count == 0)
        {
            Fail(L["NoAudioFiles", NameOf(request.Paths[0])]);
        }
        else if (_failed)
        {
            // Everything was read this time: the last error no longer applies.
            _failed = false;
            _status = null;
            StateHasChanged();
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

    private static string NameOf(VirtualPath path) => KazoPath.GetFileName(path.Value) is { Length: > 0 } name ? name : path.Value;

    private void Fail(string message)
    {
        _failed = true;
        _status = message;
        StateHasChanged();
    }
}
