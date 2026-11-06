using System.IO.Compression;
using Kazo.Webamp.Skins;

namespace Kazo.Webamp.Tests.Skins;

/// <summary>Small skins made by the tests (no community skin in the repository) and a file system in memory.</summary>
internal static class SkinFixtures
{
    /// <summary>A ZIP archive with the given entries, each holding a few bytes.</summary>
    public static byte[] Zip(params string[] entries)
    {
        using MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string entry in entries)
            {
                using Stream content = archive.CreateEntry(entry).Open();
                content.Write([0x42, 0x4D, 1, 2, 3]);
            }
        }

        return stream.ToArray();
    }

    /// <summary>A valid skin: <c>main.bmp</c> and a few other bitmaps.</summary>
    public static byte[] Skin(string mainName = "main.bmp") => Zip(mainName, "cbuttons.bmp", "pledit.txt");
}

/// <summary>An <see cref="ISkinFileSystem"/> on a dictionary.</summary>
internal sealed class MemorySkinFileSystem : ISkinFileSystem
{
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

    public List<string> Writes { get; } = [];

    public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

    public bool FailWrites { get; set; }

    public Task<IReadOnlyList<string>> GetFilesAsync(string directory, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(
            [.. Files.Keys.Where(path => path.StartsWith(directory + "/", StringComparison.Ordinal) && !path[(directory.Length + 1)..].Contains('/'))]);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        !Unreadable.Contains(path) && Files.TryGetValue(path, out byte[]? bytes)
            ? Task.FromResult(bytes)
            : throw new FileNotFoundException(path);

    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        if (FailWrites)
        {
            throw new UnauthorizedAccessException(path);
        }

        Files[path] = bytes;
        Writes.Add(path);
        return Task.CompletedTask;
    }
}

/// <summary>Webamp's side of the skins, recorded.</summary>
internal sealed class FakeSkinPlayer : ISkinPlayer
{
    public List<(string FileName, bool Apply)> Calls { get; } = [];

    public ValueTask AddSkinAsync(SkinFile skin, bool apply, CancellationToken cancellationToken = default)
    {
        Calls.Add((skin.FileName, apply));
        return ValueTask.CompletedTask;
    }
}
