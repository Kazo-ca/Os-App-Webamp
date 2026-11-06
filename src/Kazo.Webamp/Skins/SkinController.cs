namespace Kazo.Webamp.Skins;

/// <summary>
/// The skins of one Webamp window (decisions 11 and 20 of doc/en/plan.md): the Skins menu built from
/// the user's skins folder, a skin opened or loaded copied there and put on, the skin in use
/// remembered, and the import tip shown once.
/// </summary>
/// <remarks>
/// <see cref="StartAsync"/> runs before Webamp is created, so that Webamp starts with the user's
/// skins and skin (no flash of the base skin). Not thread-safe: the window calls it from its render
/// thread.
/// </remarks>
public sealed class SkinController
{
    private readonly ISkinFileSystem _files;
    private readonly string _homeDirectory;
    private SkinSettings _settings = new();
    private ISkinPlayer? _player;

    /// <summary>Creates the skins of one user's window.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home, for example <c>/home/neo</c>.</param>
    public SkinController(ISkinFileSystem files, string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _homeDirectory = homeDirectory;
        Library = new SkinLibrary(files, homeDirectory);
    }

    /// <summary>Gets the user's skins folder.</summary>
    public SkinLibrary Library { get; }

    /// <summary>Gets the skin in use, or <see langword="null"/> for Webamp's base skin.</summary>
    public SkinFile? Current => Library.Find(_settings.Current);

    /// <summary>Reads the skins folder and the settings, before Webamp is created.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Library.LoadAsync(cancellationToken);
        _settings = await SkinSettings.LoadAsync(_files, _homeDirectory, cancellationToken);
    }

    /// <summary>Connects Webamp, once it is on screen with <see cref="SkinLibrary.Skins"/> and <see cref="Current"/>.</summary>
    /// <param name="player">Webamp.</param>
    public void Attach(ISkinPlayer player) => _player = player ?? throw new ArgumentNullException(nameof(player));

    /// <summary>A <c>.wsz</c> of the user was opened (a double-click, "Open with", <c>webamp skin.wsz</c>): copy it and put it on.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the read and the copy.</param>
    /// <returns>The skin, or why it was refused; the current skin stays when it was.</returns>
    /// <exception cref="IOException">The file does not read, or the copy cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or the folder is not allowed.</exception>
    public async Task<SkinImportResult> OpenAsync(string path, CancellationToken cancellationToken = default) =>
        await ApplyAsync(await Library.ImportFileAsync(path, cancellationToken), cancellationToken);

    /// <summary>A skin was chosen on the computer (Webamp's "Skins ▸ Load Skin…", or dropped on Webamp): copy it and put it on.</summary>
    /// <param name="fileName">Its file name on the computer.</param>
    /// <param name="bytes">Its content.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The skin, or why it was refused; the current skin stays when it was.</returns>
    /// <exception cref="IOException">The copy cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not allowed.</exception>
    public async Task<SkinImportResult> LoadFromComputerAsync(string fileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        await ApplyAsync(await Library.ImportAsync(fileName, bytes, cancellationToken), cancellationToken);

    /// <summary>The user picked a skin in Webamp's Skins menu (Webamp already put it on): remember it.</summary>
    /// <param name="fileName">The skin's file name, or <see langword="null"/> for "&lt;Base Skin&gt;".</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task ChosenAsync(string? fileName, CancellationToken cancellationToken = default) =>
        RememberAsync(Library.Find(fileName)?.FileName, cancellationToken);

    /// <summary>
    /// Posts the tip that explains how to import a skin, the first time only (decision 20).
    /// </summary>
    /// <param name="post">Posts the notification; when it throws, the tip is tried again at the next start.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>Whether the tip was posted now.</returns>
    public async Task<bool> ShowImportTipOnceAsync(Action post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (_settings.ImportTipShown)
        {
            return false;
        }

        post();
        _settings = _settings with { ImportTipShown = true };
        await _settings.SaveAsync(_files, _homeDirectory, cancellationToken);
        return true;
    }

    private async Task<SkinImportResult> ApplyAsync(SkinImportResult result, CancellationToken cancellationToken)
    {
        if (result.Skin is { } skin)
        {
            if (_player is not null)
            {
                await _player.AddSkinAsync(skin, apply: true, cancellationToken);
            }

            await RememberAsync(skin.FileName, cancellationToken);
        }

        return result;
    }

    private async Task RememberAsync(string? fileName, CancellationToken cancellationToken)
    {
        if (string.Equals(_settings.Current, fileName, StringComparison.Ordinal))
        {
            return;
        }

        _settings = _settings with { Current = fileName };
        await _settings.SaveAsync(_files, _homeDirectory, cancellationToken);
    }
}
