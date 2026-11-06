using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kazo.Webamp.Skins;

/// <summary>
/// What the app remembers about skins, in <c>~/.config/ca.kazo.webamp/skins.json</c>: the skin in
/// use (decision 13) and whether the import tip was shown (decision 20).
/// </summary>
/// <param name="Current">File name of the skin in use in the skins folder, or <see langword="null"/> for Webamp's base skin.</param>
/// <param name="ImportTipShown">Whether the notification that explains how to import a skin was posted.</param>
/// <remarks>Work item 5 may fold the skin into its <c>state.json</c>; this file stays the skins' own.</remarks>
public sealed record SkinSettings(string? Current = null, bool ImportTipShown = false)
{
    /// <summary>Gets the settings file of one user.</summary>
    /// <param name="homeDirectory">The user's home, for example <c>/home/neo</c>.</param>
    public static string PathFor(string homeDirectory) => homeDirectory.TrimEnd('/') + "/.config/ca.kazo.webamp/skins.json";

    /// <summary>Reads the settings; a missing or broken file gives the defaults.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<SkinSettings> LoadAsync(ISkinFileSystem files, string homeDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        try
        {
            byte[] bytes = await files.ReadAllBytesAsync(PathFor(homeDirectory), cancellationToken);
            return JsonSerializer.Deserialize(bytes, SkinSettingsJsonContext.Default.SkinSettings) ?? new SkinSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SkinSettings();
        }
    }

    /// <summary>Writes the settings.</summary>
    /// <param name="files">The file system.</param>
    /// <param name="homeDirectory">The user's home.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task SaveAsync(ISkinFileSystem files, string homeDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        string json = JsonSerializer.Serialize(this, SkinSettingsJsonContext.Default.SkinSettings);
        return files.WriteAllBytesAsync(PathFor(homeDirectory), Encoding.UTF8.GetBytes(json), cancellationToken);
    }
}

/// <summary>Trim-safe JSON of <see cref="SkinSettings"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SkinSettings))]
internal sealed partial class SkinSettingsJsonContext : JsonSerializerContext;
