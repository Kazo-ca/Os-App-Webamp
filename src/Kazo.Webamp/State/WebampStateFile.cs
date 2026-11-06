using Kazo.Os.AppSdk.IO;

namespace Kazo.Webamp.State;

/// <summary>
/// The file <c>~/.config/ca.kazo.webamp/state.json</c> (decision 13 of doc/en/plan.md): the app's
/// own settings folder, reached with <c>filesystem.private.read</c> and <c>.write</c> (ADR 0044 of
/// Kazo OS), as a Linux program keeps its state under <c>~/.config/&lt;program&gt;/</c>.
/// </summary>
public sealed class WebampStateFile
{
    /// <summary>The app's folder under <c>~/.config</c>: its id, as the OS expects (ADR 0044).</summary>
    public const string FolderName = "ca.kazo.webamp";

    /// <summary>The file's name.</summary>
    public const string FileName = "state.json";

    private readonly Func<string, CancellationToken, Task<string>> _readAllText;
    private readonly Func<string, string, CancellationToken, Task> _writeAllText;
    private readonly Func<string, CancellationToken, Task> _createDirectory;

    /// <summary>Creates the state file of a user, read and written through the Kazo OS file system.</summary>
    /// <param name="homeDirectory">The user's home (<c>AppContext.HomeDirectory</c>).</param>
    public WebampStateFile(string homeDirectory)
        : this(
            homeDirectory,
            (path, token) => KazoFile.ReadAllTextAsync(path, token),
            (path, text, token) => KazoFile.WriteAllTextAsync(path, text, token),
            (path, token) => KazoDirectory.CreateDirectoryAsync(path, token))
    {
    }

    /// <summary>Creates the state file with its own file access (the tests).</summary>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="readAllText">Reads a file; throws an <see cref="IOException"/> when it cannot.</param>
    /// <param name="writeAllText">Writes a file, replacing it.</param>
    /// <param name="createDirectory">Creates a folder and its parents; does nothing when it exists.</param>
    public WebampStateFile(
        string homeDirectory,
        Func<string, CancellationToken, Task<string>> readAllText,
        Func<string, string, CancellationToken, Task> writeAllText,
        Func<string, CancellationToken, Task> createDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        ArgumentNullException.ThrowIfNull(readAllText);
        ArgumentNullException.ThrowIfNull(writeAllText);
        ArgumentNullException.ThrowIfNull(createDirectory);
        Directory = $"{homeDirectory.TrimEnd('/')}/.config/{FolderName}";
        Path = $"{Directory}/{FileName}";
        _readAllText = readAllText;
        _writeAllText = writeAllText;
        _createDirectory = createDirectory;
    }

    /// <summary>Gets the folder of the file.</summary>
    public string Directory { get; }

    /// <summary>Gets the file's path.</summary>
    public string Path { get; }

    /// <summary>
    /// Reads the state. A missing, unreadable or corrupt file gives <see cref="WebampState.Empty"/>:
    /// Webamp starts as on a first run rather than not at all.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The state.</returns>
    public async Task<WebampState> LoadAsync(CancellationToken cancellationToken = default)
    {
        string text;
        try
        {
            text = await _readAllText(Path, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WebampState.Empty;
        }

        return WebampStateJson.Parse(text);
    }

    /// <summary>Writes the state, creating the folder the first time.</summary>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task completed once the file is written.</returns>
    public async Task SaveAsync(WebampState state, CancellationToken cancellationToken = default)
    {
        string text = WebampStateJson.Serialize(state);
        await _createDirectory(Directory, cancellationToken);
        await _writeAllText(Path, text, cancellationToken);
    }
}
