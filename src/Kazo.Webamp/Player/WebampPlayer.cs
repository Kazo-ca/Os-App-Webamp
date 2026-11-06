using Kazo.Webamp.State;
using Kazo.Webamp.Tracks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Kazo.Webamp.Player;

/// <summary>
/// The C# side of one Webamp instance: it starts the host module (<c>wwwroot/js/webamp-host.js</c>)
/// in an element of the window, sends it the tracks of <see cref="TrackLibrary"/>, and raises
/// Webamp's events on the .NET side.
/// </summary>
/// <remarks>
/// The contract with the module is described in doc/en/architecture.md ("C# and JavaScript"). Webamp's
/// events arrive through the <see cref="JSInvokableAttribute"/> methods below, called by the module
/// with <c>invokeMethodAsync</c>.
/// </remarks>
public sealed class WebampPlayer : ITrackPlayer, IStatePlayer, IAsyncDisposable
{
    /// <summary>Path of the host module, served by the app's Razor library.</summary>
    public const string ModulePath = "./_content/Kazo.Webamp/js/webamp-host.js";

    private readonly IJSRuntime _javaScript;
    private DotNetObjectReference<WebampPlayer>? _self;
    private IJSObjectReference? _module;
    private IJSObjectReference? _host;

    /// <summary>Creates a player; <see cref="StartAsync"/> puts Webamp on screen.</summary>
    /// <param name="javaScript">The window's JavaScript runtime.</param>
    public WebampPlayer(IJSRuntime javaScript)
    {
        ArgumentNullException.ThrowIfNull(javaScript);
        _javaScript = javaScript;
    }

    /// <summary>Raised when the user closes Webamp (its close button or its menu).</summary>
    public event Func<Task>? Closed;

    /// <summary>Raised when the user presses Webamp's minimize button.</summary>
    public event Func<Task>? Minimized;

    /// <summary>Raised when the current track or its tags changed, or with <see langword="null"/> when there is none.</summary>
    public event Func<WebampTrackInfo?, Task>? TrackChanged;

    /// <summary>Raised with the keys of <see cref="TrackLibrary"/> still in Webamp's list, in its order.</summary>
    public event Action<IReadOnlyList<string>>? PlaylistChanged;

    /// <summary>
    /// Raised when something saved in <c>state.json</c> changed in Webamp (volume, equalizer, a window
    /// moved, the current track; the position in steps of a few seconds while playing).
    /// </summary>
    public event Action? StateChanged;

    /// <summary>Raised when the user chose "Load list" in Webamp (decision 10: the OS dialog opens).</summary>
    public event Func<Task>? LoadListRequested;

    /// <summary>
    /// Raised when the user chose "Save list" in Webamp, with the tracks C# gave it, in its order,
    /// and how many other tracks (dropped from the computer, without a file in Kazo OS) are left out.
    /// </summary>
    public event Func<IReadOnlyList<WebampListEntry>, int, Task>? SaveListRequested;

    /// <summary>Gets whether Webamp is on screen.</summary>
    public bool IsStarted => _host is not null;

    /// <summary>Loads Webamp and renders it into <paramref name="element"/>.</summary>
    /// <param name="element">An empty element of the window, sized by the window.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    public Task StartAsync(ElementReference element, CancellationToken cancellationToken = default) =>
        StartAsync(element, settings: null, focused: false, cancellationToken);

    /// <summary>
    /// Loads Webamp with saved settings and renders it into <paramref name="element"/>: the volume,
    /// equalizer, window layout and double size are there from the first frame.
    /// </summary>
    /// <param name="element">An empty element of the window, sized by the window.</param>
    /// <param name="settings">The saved settings, or <see langword="null"/> for Webamp's defaults.</param>
    /// <param name="focused">Whether the app's window has the focus (Webamp's hotkeys act).</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    public async Task StartAsync(ElementReference element, PlayerSettings? settings, bool focused, CancellationToken cancellationToken = default)
    {
        if (_host is not null)
        {
            throw new InvalidOperationException("Webamp is already started.");
        }

        _self ??= DotNetObjectReference.Create(this);
        _module ??= await _javaScript.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        _host = await _module.InvokeAsync<IJSObjectReference>("create", cancellationToken, element, _self, new { zIndex = 0, settings, focused });
    }

    /// <inheritdoc />
    public ValueTask<PlayerSnapshot> ReadStateAsync(CancellationToken cancellationToken = default) =>
        Host.InvokeAsync<PlayerSnapshot>("readState", cancellationToken);

    /// <inheritdoc />
    public async ValueTask ResumeAsync(int index, double position, CancellationToken cancellationToken = default) =>
        await Host.InvokeAsync<bool>("resume", cancellationToken, index, position);

    /// <inheritdoc />
    public ValueTask ApplySkinAsync(string name, byte[] bytes, CancellationToken cancellationToken = default) =>
        Host.InvokeVoidAsync("setSkin", cancellationToken, bytes);

    /// <summary>
    /// Lets Webamp's hotkeys act while the app's window has the focus, and only then (decision 17).
    /// </summary>
    /// <param name="focused">Whether the window has the focus.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task completed once the module was told.</returns>
    public ValueTask SetFocusedAsync(bool focused, CancellationToken cancellationToken = default) =>
        _host is null ? ValueTask.CompletedTask : _host.InvokeVoidAsync("setFocused", cancellationToken, focused);

    /// <inheritdoc />
    public ValueTask AddAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default) =>
        Host.InvokeVoidAsync("addTracks", cancellationToken, ToJs(tracks), play);

    /// <inheritdoc />
    public ValueTask ReplaceAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default) =>
        Host.InvokeVoidAsync("replaceTracks", cancellationToken, ToJs(tracks), play);

    /// <summary>Called by the host module when Webamp closed.</summary>
    [JSInvokable]
    public Task OnWebampClosed() => Closed?.Invoke() ?? Task.CompletedTask;

    /// <summary>Called by the host module when Webamp's minimize button was pressed.</summary>
    [JSInvokable]
    public Task OnWebampMinimized() => Minimized?.Invoke() ?? Task.CompletedTask;

    /// <summary>Called by the host module when the current track or its tags changed.</summary>
    /// <param name="track">Its tags and file name, or <see langword="null"/> when there is no current track.</param>
    [JSInvokable]
    public Task OnTrackChanged(WebampTrackInfo? track) => TrackChanged?.Invoke(track) ?? Task.CompletedTask;

    /// <summary>Called by the host module when something saved in <c>state.json</c> changed.</summary>
    [JSInvokable]
    public void OnStateChanged() => StateChanged?.Invoke();

    /// <summary>Called by the host module when the tracks of Webamp's list changed.</summary>
    /// <param name="keys">The keys still listed, in order.</param>
    [JSInvokable]
    public void OnPlaylistChanged(string[] keys) => PlaylistChanged?.Invoke(keys);

    /// <summary>Called by the host module when "Load list" was chosen.</summary>
    [JSInvokable]
    public Task OnLoadListRequested() => LoadListRequested?.Invoke() ?? Task.CompletedTask;

    /// <summary>Called by the host module when "Save list" was chosen.</summary>
    /// <param name="entries">The tracks of the list that have a key, in order.</param>
    /// <param name="unsaved">How many tracks of the list have no key.</param>
    [JSInvokable]
    public Task OnSaveListRequested(WebampListEntry[] entries, int unsaved) =>
        SaveListRequested?.Invoke(entries, unsaved) ?? Task.CompletedTask;

    /// <summary>Stops the music, removes Webamp and gives its memory back (decision 8).</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_host is not null)
            {
                await _host.InvokeVoidAsync("dispose");
                await _host.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The page is going away: the browser frees everything with it.
        }
        finally
        {
            _host = null;
            _module = null;
            _self?.Dispose();
            _self = null;
        }
    }

    private IJSObjectReference Host => _host ?? throw new InvalidOperationException("Webamp is not started.");

    // byte[] crosses to JavaScript as a Uint8Array.
    private static object[] ToJs(IReadOnlyList<TrackData> tracks) =>
        [.. tracks.Select(track => new { key = track.Key, name = track.Name, mimeType = track.MimeType, bytes = track.Bytes, durationSeconds = track.DurationSeconds })];
}
