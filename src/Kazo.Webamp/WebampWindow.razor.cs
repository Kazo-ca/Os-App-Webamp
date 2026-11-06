using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Globalization;
using Kazo.Os.AppSdk.IO;
using Kazo.Os.Simulation.Abstractions.Processes;
using Kazo.Webamp.Player;
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
/// <c>.wsz</c>, it puts that skin on (<c>WebampWindow.Skins.cs</c>, work item 4). The
/// <see cref="Tracks"/> library is what work items 2 to 5 build on (open, playlists, state).
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
        Tracks = new TrackLibrary(player);
        AttachSkins(player, skins);
        _status = null;

        // Opened on a file: a skin is put on, music plays.
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

    // A file given to Webamp: a skin is put on, anything else goes to the list.
    private Task OpenAsync(VirtualPath file) =>
        IsSkinFile(file) ? OpenSkinAsync(file) : PlayAsync([file]);

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
