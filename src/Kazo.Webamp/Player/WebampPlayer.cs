using Kazo.Webamp.Skins;
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
public sealed class WebampPlayer : ITrackPlayer, ISkinPlayer, IAsyncDisposable
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

    /// <summary>Raised when Webamp starts another track, or <see langword="null"/> when none plays.</summary>
    public event Func<WebampTrackInfo?, Task>? TrackChanged;

    /// <summary>Raised with the keys of <see cref="TrackLibrary"/> still in Webamp's list, in its order.</summary>
    public event Action<IReadOnlyList<string>>? PlaylistChanged;

    /// <summary>
    /// Raised with the file name and bytes of a skin the user chose on the computer (Webamp's
    /// "Skins ▸ Load Skin…", or a skin dropped on Webamp); Webamp has not put it on.
    /// </summary>
    public event Func<string, byte[], Task>? SkinFileChosen;

    /// <summary>
    /// Raised when the user picked a skin in Webamp's Skins menu, which Webamp puts on itself: its
    /// <see cref="SkinFile.FileName"/>, or <see langword="null"/> for "&lt;Base Skin&gt;".
    /// </summary>
    public event Func<string?, Task>? SkinChosen;

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
        StartAsync(element, [], null, cancellationToken);

    /// <summary>Loads Webamp with the user's skins and renders it into <paramref name="element"/>.</summary>
    /// <param name="element">An empty element of the window, sized by the window.</param>
    /// <param name="skins">The skins of Webamp's Skins menu, in order.</param>
    /// <param name="initialSkin">The skin to start with, or <see langword="null"/> for Webamp's base skin.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    public async Task StartAsync(
        ElementReference element, IReadOnlyList<SkinFile> skins, SkinFile? initialSkin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skins);
        if (_host is not null)
        {
            throw new InvalidOperationException("Webamp is already started.");
        }

        _self ??= DotNetObjectReference.Create(this);
        _module ??= await _javaScript.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        _host = await _module.InvokeAsync<IJSObjectReference>("create", cancellationToken, element, _self, new
        {
            zIndex = 0,
            skins = skins.Select(ToJs).ToArray(),
            initialSkin = initialSkin?.FileName,
        });
    }

    /// <inheritdoc />
    public ValueTask AddAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default) =>
        Host.InvokeVoidAsync("addTracks", cancellationToken, ToJs(tracks), play);

    /// <inheritdoc />
    public ValueTask ReplaceAsync(IReadOnlyList<TrackData> tracks, bool play, CancellationToken cancellationToken = default) =>
        Host.InvokeVoidAsync("replaceTracks", cancellationToken, ToJs(tracks), play);

    /// <inheritdoc />
    public async ValueTask AddSkinAsync(SkinFile skin, bool apply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skin);
        await Host.InvokeVoidAsync("addSkin", cancellationToken, ToJs(skin), apply);
    }

    /// <summary>Called by the host module when Webamp closed.</summary>
    [JSInvokable]
    public Task OnWebampClosed() => Closed?.Invoke() ?? Task.CompletedTask;

    /// <summary>Called by the host module when Webamp's minimize button was pressed.</summary>
    [JSInvokable]
    public Task OnWebampMinimized() => Minimized?.Invoke() ?? Task.CompletedTask;

    /// <summary>Called by the host module when Webamp started another track.</summary>
    /// <param name="track">Its tags, or <see langword="null"/> when none plays.</param>
    [JSInvokable]
    public Task OnTrackChanged(WebampTrackInfo? track) => TrackChanged?.Invoke(track) ?? Task.CompletedTask;

    /// <summary>Called by the host module when the tracks of Webamp's list changed.</summary>
    /// <param name="keys">The keys still listed, in order.</param>
    [JSInvokable]
    public void OnPlaylistChanged(string[] keys) => PlaylistChanged?.Invoke(keys);

    /// <summary>Called by the host module with a skin the user chose on the computer.</summary>
    /// <param name="fileName">Its file name.</param>
    /// <param name="bytes">Its content (a <c>Uint8Array</c> on the JavaScript side).</param>
    [JSInvokable]
    public Task OnSkinFileChosen(string fileName, byte[] bytes) => SkinFileChosen?.Invoke(fileName, bytes) ?? Task.CompletedTask;

    /// <summary>Called by the host module when a skin was picked in Webamp's Skins menu.</summary>
    /// <param name="fileName">The skin's key (its file name), or <see langword="null"/> for the base skin.</param>
    [JSInvokable]
    public Task OnSkinChosen(string? fileName) => SkinChosen?.Invoke(fileName) ?? Task.CompletedTask;

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

    // A skin's key is its file name in the skins folder.
    private static object ToJs(SkinFile skin) => new { key = skin.FileName, name = skin.Name, bytes = skin.Bytes };

    // byte[] crosses to JavaScript as a Uint8Array.
    private static object[] ToJs(IReadOnlyList<TrackData> tracks) =>
        [.. tracks.Select(track => new { key = track.Key, name = track.Name, mimeType = track.MimeType, bytes = track.Bytes, durationSeconds = track.DurationSeconds })];
}
