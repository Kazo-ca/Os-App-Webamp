namespace Kazo.Webamp.Skins;

/// <summary>A skin of the user's skins folder, as Webamp's Skins menu lists it.</summary>
/// <param name="FileName">Its file name in the folder, for example <c>Bento.wsz</c>; also its key.</param>
/// <param name="Bytes">Its content, checked by <see cref="SkinArchive"/>.</param>
public sealed record SkinFile(string FileName, byte[] Bytes)
{
    /// <summary>Gets the name shown in the Skins menu: the file name without <c>.wsz</c>.</summary>
    public string Name => Path.GetFileNameWithoutExtension(FileName);
}
