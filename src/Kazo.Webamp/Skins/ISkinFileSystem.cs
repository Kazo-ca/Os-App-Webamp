namespace Kazo.Webamp.Skins;

/// <summary>
/// The few file operations of the skins (the folder of copied skins and the skin settings), so
/// that the tests run them on memory; the app uses <see cref="KazoSkinFileSystem"/>.
/// </summary>
public interface ISkinFileSystem
{
    /// <summary>Lists the files of a folder, by full path; an empty list when the folder does not exist.</summary>
    Task<IReadOnlyList<string>> GetFilesAsync(string directory, CancellationToken cancellationToken);

    /// <summary>Reads a whole file; throws an <see cref="IOException"/> or an <see cref="UnauthorizedAccessException"/>.</summary>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes a whole file, creating its folder and every missing parent.</summary>
    Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken);
}
