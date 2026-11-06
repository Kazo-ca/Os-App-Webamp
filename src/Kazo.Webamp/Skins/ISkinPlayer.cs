namespace Kazo.Webamp.Skins;

/// <summary>What <see cref="SkinController"/> asks of Webamp; <c>WebampPlayer</c> in the app, a fake in the tests.</summary>
public interface ISkinPlayer
{
    /// <summary>Adds a skin to Webamp's Skins menu (or replaces the one of the same file name) and, with <paramref name="apply"/>, puts it on.</summary>
    /// <param name="skin">The skin.</param>
    /// <param name="apply">Whether Webamp changes to it now.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    ValueTask AddSkinAsync(SkinFile skin, bool apply, CancellationToken cancellationToken = default);
}
