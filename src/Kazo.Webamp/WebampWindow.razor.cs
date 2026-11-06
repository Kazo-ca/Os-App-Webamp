using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Globalization;
using Kazo.Os.AppSdk.IO;
using Kazo.Os.Simulation.Abstractions.Processes;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Diagnostics;
using Kazo.Webamp.Player;
using Kazo.Webamp.State;
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
/// Opened on a file (a double-click, "Open with", <c>webamp song.mp3</c>), it plays that file. The
/// <see cref="Tracks"/> library is what work items 2 to 5 build on (open, playlists, state).
/// </para>
/// <para>
/// At start it reads <c>~/.config/ca.kazo.webamp/state.json</c> and puts Webamp back as it was
/// (decision 13); every change is written there, grouped, and a last time when the window closes.
/// The window's title follows the current track (decision 8), and Webamp's hotkeys act only while
/// the window has the focus (decision 17).
/// </para>
/// </remarks>
public sealed partial class WebampWindow : IAsyncDisposable, IWindowCloseGuard
{
    // How long the OS waits for the app's own process to stop after Webamp's close button.
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

    private ElementReference _surface;
    private WebampPlayer? _player;
    private string? _status;
    private bool _failed;
    private bool _closing;
    private bool _saveFailureShown;
    private IStringLocalizer<Strings>? _localizer;
    private WebampStateFile? _stateFile;
    private StateSaveScheduler? _saver;

    [Inject]
    private IJSRuntime JavaScript { get; set; } = default!;

    /// <summary>Gets the files in Webamp's list; <see langword="null"/> until Webamp is on screen.</summary>
    internal TrackLibrary? Tracks { get; private set; }

    /// <summary>
    /// Gets what is saved between runs (decision 13); the skins work item sets
    /// <see cref="WebampStateKeeper.SkinPath"/> on it. <see langword="null"/> until Webamp is on screen.
    /// </summary>
    internal WebampStateKeeper? State { get; private set; }

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Closed without the close guard (the process was killed): one last try to save.
        await SaveLastTimeAsync();
        if (_player is not null)
        {
            _player.PlaylistChanged -= OnPlaylistChanged;
            _player.StateChanged -= OnStateChanged;
            _player.TrackChanged -= OnTrackChangedAsync;
            await _player.DisposeAsync();
            _player = null;
        }
    }

    /// <summary>
    /// The window closes (its close button, Alt+F4): the state is written a last time, then the
    /// window may close. Nothing is ever lost, so the user is never asked.
    /// </summary>
    /// <param name="cancellationToken">Cancels the close.</param>
    /// <returns>Always <see langword="true"/>.</returns>
    public async ValueTask<bool> ConfirmCloseAsync(CancellationToken cancellationToken = default)
    {
        await SaveLastTimeAsync();
        return true;
    }

    /// <inheritdoc />
    protected override Task OnAppStopRequestedAsync(KazoStopRequestedEventArgs e) => SaveLastTimeAsync();

    /// <inheritdoc />
    protected override async Task OnWindowActivatedAsync()
    {
        if (_player is not null)
        {
            await _player.SetFocusedAsync(true);
        }
    }

    /// <inheritdoc />
    protected override async Task OnWindowDeactivatedAsync()
    {
        if (_player is not null)
        {
            await _player.SetFocusedAsync(false);
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

        _stateFile = new WebampStateFile(AppContext.HomeDirectory);
        WebampState saved = await _stateFile.LoadAsync(AppContext.CancellationToken);

        WebampPlayer player = new(JavaScript);
        player.TrackChanged += OnTrackChangedAsync;
        try
        {
            await player.StartAsync(_surface, saved.Player, IsWindowActive, AppContext.CancellationToken);
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
        Tracks = new TrackLibrary(player);
        State = new WebampStateKeeper(player, Tracks);
        _status = null;

        // The saved list, current track and position, skin; a file that has disappeared is skipped
        // and stays in the saved list. Changes are saved only from here on.
        try
        {
            await State.RestoreAsync(saved, AppContext.CancellationToken);
        }
        catch (JSException exception)
        {
            Fail(L["StartFailed", exception.Message]);
        }

        _saver = new StateSaveScheduler(SaveStateAsync, onError: OnSaveFailed);
        _player.StateChanged += OnStateChanged;
        Tracks.Changed += OnStateChanged;

        // Opened on a file: play it, so the app can be tried from the file explorer already.
        if (LaunchFile is { } file)
        {
            await PlayAsync([file]);
        }

        StateHasChanged();
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
        await SaveLastTimeAsync();
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

    private void OnStateChanged() => _saver?.Schedule();

    // "Artist - Title - Webamp", or "Webamp" without a current track (decision 8).
    private async Task OnTrackChangedAsync(WebampTrackInfo? track)
    {
        try
        {
            await InvokeAsync(() => SetWindowTitleAsync(TrackTitle.Format(track, AppContext.Manifest.Name)).AsTask());
        }
        catch (InvalidOperationException)
        {
            // The window is closing: no title to show anymore.
        }
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        if (State is null || _stateFile is null || _player is null)
        {
            return;
        }

        // Saves start from Webamp's events (a JavaScript callback) and a timer, outside the scope the
        // OS opens for app code: enter it, so the file system knows which app and user write.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        WebampState state = await State.CaptureAsync(cancellationToken);
        await _stateFile.SaveAsync(state, cancellationToken);
    }

    // Once: on close, on a stop request, at dispose, whichever comes first.
    private Task SaveLastTimeAsync() => _saver?.StopAsync() ?? Task.CompletedTask;

    private void OnSaveFailed(Exception exception)
    {
        // The page is going away, or Webamp with it: nothing to save from anymore.
        if (exception is JSDisconnectedException or ObjectDisposedException || _closing || _saveFailureShown)
        {
            return;
        }

        // Shown once: a full disk would otherwise repeat it at every change.
        _saveFailureShown = true;
        _ = InvokeAsync(() => Fail(L["StateSaveFailed", exception.Message]));
    }

    private async Task PlayAsync(IReadOnlyList<VirtualPath> files)
    {
        if (Tracks is null)
        {
            return;
        }

        try
        {
            TrackLoadResult result = await Tracks.AddAsync(files, play: true, AppContext.CancellationToken);
            if (result.Failures.Count > 0)
            {
                TrackLoadFailure failure = result.Failures[0];
                Fail(L["FileFailed", KazoPath.GetFileName(failure.Path.Value) ?? failure.Path.Value, failure.Message]);
            }
        }
        catch (JSException exception)
        {
            Fail(L["StartFailed", exception.Message]);
        }
    }

    private void Fail(string message)
    {
        _failed = true;
        _status = message;
        StateHasChanged();
    }
}
