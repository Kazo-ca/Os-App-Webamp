using Kazo.Os.AppSdk.IO;

namespace Kazo.Webamp.Skins;

/// <summary><see cref="ISkinFileSystem"/> on the Kazo OS file system (<see cref="KazoFile"/>, <see cref="KazoDirectory"/>).</summary>
public sealed class KazoSkinFileSystem : ISkinFileSystem
{
    /// <summary>The one instance.</summary>
    public static KazoSkinFileSystem Instance { get; } = new();

    private KazoSkinFileSystem()
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetFilesAsync(string directory, CancellationToken cancellationToken)
    {
        if (!await KazoDirectory.ExistsAsync(directory, cancellationToken))
        {
            return [];
        }

        return await KazoDirectory.GetFilesAsync(directory, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        KazoFile.ReadAllBytesAsync(path, cancellationToken);

    /// <inheritdoc />
    public async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        if (KazoPath.GetDirectoryName(path) is { Length: > 0 } directory)
        {
            await KazoDirectory.CreateDirectoryAsync(directory, cancellationToken);
        }

        await KazoFile.WriteAllBytesAsync(path, bytes, cancellationToken);
    }
}
