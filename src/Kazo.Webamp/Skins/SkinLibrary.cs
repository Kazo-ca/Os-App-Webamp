using Kazo.Os.AppSdk.IO;

namespace Kazo.Webamp.Skins;

/// <summary>
/// The user's skins folder, <c>~/.local/share/webamp/skins/</c> (decision 11 of doc/en/plan.md): every
/// skin the user opened or loaded is copied there, and Webamp's Skins menu lists them, rebuilt from
/// the folder at each start.
/// </summary>
/// <remarks>
/// A file of the folder that is not a skin (<see cref="SkinArchive"/>) is left alone and not listed.
/// Not thread-safe: the window calls it from its render thread.
/// </remarks>
public sealed class SkinLibrary
{
    /// <summary>The extension of the copied skins.</summary>
    public const string Extension = ".wsz";

    private readonly ISkinFileSystem _files;
    private readonly List<SkinFile> _skins = [];

    /// <summary>Creates the library of one user.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home, for example <c>/home/neo</c>.</param>
    public SkinLibrary(ISkinFileSystem files, string homeDirectory)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentException.ThrowIfNullOrWhiteSpace(homeDirectory);
        _files = files;
        Directory = homeDirectory.TrimEnd('/') + "/.local/share/webamp/skins";
    }

    /// <summary>Gets the folder of the copied skins.</summary>
    public string Directory { get; }

    /// <summary>Gets the skins of the folder, sorted by name.</summary>
    public IReadOnlyList<SkinFile> Skins => _skins;

    /// <summary>Lists the folder again; a file that does not read or is not a skin is skipped.</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _skins.Clear();
        foreach (string path in await _files.GetFilesAsync(Directory, cancellationToken))
        {
            if (!path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                byte[] bytes = await _files.ReadAllBytesAsync(path, cancellationToken);
                if (SkinArchive.Check(bytes) is null)
                {
                    _skins.Add(new SkinFile(KazoPath.GetFileName(path) ?? path, bytes));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A skin that cannot be read is simply not offered.
            }
        }

        Sort();
    }

    /// <summary>Finds a skin of the folder by its file name.</summary>
    /// <param name="fileName">For example <c>Bento.wsz</c>.</param>
    /// <returns>The skin, or <see langword="null"/>.</returns>
    public SkinFile? Find(string? fileName) =>
        fileName is null ? null : _skins.FirstOrDefault(skin => string.Equals(skin.FileName, fileName, StringComparison.Ordinal));

    /// <summary>Reads a skin file of the user (a double-click on a <c>.wsz</c>) and copies it to the folder.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the read and the copy.</param>
    /// <returns>The skin, or why it was refused.</returns>
    /// <exception cref="IOException">The file does not read, or the copy cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The file or the folder is not allowed.</exception>
    public async Task<SkinImportResult> ImportFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] bytes = await _files.ReadAllBytesAsync(path, cancellationToken);

        // Opening a skin that is already in the folder only applies it.
        bool inFolder = string.Equals(KazoPath.GetDirectoryName(path), Directory, StringComparison.Ordinal);
        return await ImportAsync(KazoPath.GetFileName(path) ?? path, bytes, write: !inFolder, cancellationToken);
    }

    /// <summary>Copies a skin given as bytes (Webamp's "Load Skin…", from the computer) to the folder.</summary>
    /// <param name="fileName">Its original file name; a <c>.zip</c> is kept as a <c>.wsz</c>.</param>
    /// <param name="bytes">Its content.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The skin, or why it was refused.</returns>
    /// <exception cref="IOException">The copy cannot be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not allowed.</exception>
    public Task<SkinImportResult> ImportAsync(string fileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        ImportAsync(fileName, bytes, write: true, cancellationToken);

    /// <summary>The name a skin gets in the folder: its own file name, made safe, ending in <c>.wsz</c>.</summary>
    /// <param name="fileName">The original name, perhaps with a path from the computer.</param>
    /// <returns>For example <c>Bento.wsz</c> for <c>C:\skins\Bento.zip</c>.</returns>
    public static string FolderFileName(string fileName)
    {
        string name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        string stem = name.EndsWith(".wsz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? name[..^4]
            : name;
        stem = new string([.. stem.Where(character => !char.IsControl(character))]).Trim().TrimStart('.');
        return (stem.Length == 0 ? "skin" : stem) + Extension;
    }

    private async Task<SkinImportResult> ImportAsync(string fileName, byte[] bytes, bool write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (SkinArchive.Check(bytes) is { } problem)
        {
            return SkinImportResult.Refused(problem);
        }

        SkinFile skin = new(write ? FolderFileName(fileName) : fileName, bytes);
        if (write)
        {
            await _files.WriteAllBytesAsync($"{Directory}/{skin.FileName}", bytes, cancellationToken);
        }

        // The same name again replaces the listed skin (the user loaded a new version of it).
        _skins.RemoveAll(listed => string.Equals(listed.FileName, skin.FileName, StringComparison.Ordinal));
        _skins.Add(skin);
        Sort();
        return SkinImportResult.Imported(skin);
    }

    private void Sort() =>
        _skins.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase) switch
        {
            0 => string.CompareOrdinal(left.FileName, right.FileName),
            int order => order,
        });
}

/// <summary>What became of a skin the user opened or loaded.</summary>
/// <param name="Skin">The skin, now in the folder, or <see langword="null"/> when it was refused.</param>
/// <param name="Problem">Why it was refused, or <see langword="null"/>.</param>
public sealed record SkinImportResult(SkinFile? Skin, SkinProblem? Problem)
{
    /// <summary>A skin copied to the folder.</summary>
    public static SkinImportResult Imported(SkinFile skin) => new(skin, null);

    /// <summary>Some bytes that are not a skin.</summary>
    public static SkinImportResult Refused(SkinProblem problem) => new(null, problem);
}
