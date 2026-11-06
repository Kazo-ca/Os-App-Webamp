using System.IO.Compression;

namespace Kazo.Webamp.Skins;

/// <summary>
/// Checks that some bytes are a Winamp 2 skin Webamp can apply (decision 11 of doc/en/plan.md),
/// before anything is copied or sent to Webamp.
/// </summary>
/// <remarks>
/// A <c>.wsz</c> is a ZIP archive of bitmaps; <c>main.bmp</c> (the main window) is the one file every
/// skin has. Webamp itself only reports a broken skin with a browser <c>alert</c>, so the app refuses
/// it here first, with its own message, and the current skin stays.
/// </remarks>
public static class SkinArchive
{
    /// <summary>The file every Winamp 2 skin holds, at its root or in one folder.</summary>
    public const string MainBitmap = "main.bmp";

    /// <summary>The largest skin accepted, in bytes (the biggest classic skins are a few MB).</summary>
    public const int MaxBytes = 16 * 1024 * 1024;

    /// <summary>Checks <paramref name="bytes"/>.</summary>
    /// <param name="bytes">The content of a <c>.wsz</c> (or <c>.zip</c>) file.</param>
    /// <returns>Why it is not a skin, or <see langword="null"/> when it is one.</returns>
    public static SkinProblem? Check(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxBytes)
        {
            return SkinProblem.TooLarge;
        }

        try
        {
            using ZipArchive archive = new(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            return archive.Entries.Any(IsMainBitmap) ? null : SkinProblem.NoMainBitmap;
        }
        catch (InvalidDataException)
        {
            return SkinProblem.NotAnArchive;
        }
    }

    // Skins are made on Windows: names are case-insensitive, and some skins keep their files in a
    // folder inside the archive, which Webamp accepts too.
    private static bool IsMainBitmap(ZipArchiveEntry entry)
    {
        string name = entry.FullName.Replace('\\', '/');
        int slash = name.LastIndexOf('/');
        return string.Equals(slash < 0 ? name : name[(slash + 1)..], MainBitmap, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Why some bytes are not a skin.</summary>
public enum SkinProblem
{
    /// <summary>Not a ZIP archive.</summary>
    NotAnArchive,

    /// <summary>A ZIP archive without <c>main.bmp</c>.</summary>
    NoMainBitmap,

    /// <summary>Larger than <see cref="SkinArchive.MaxBytes"/>.</summary>
    TooLarge,
}
