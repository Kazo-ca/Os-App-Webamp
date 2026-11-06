using Kazo.Webamp.Skins;

namespace Kazo.Webamp.Tests.Skins;

/// <summary>A skin is refused before it reaches Webamp when it is not one.</summary>
public sealed class SkinArchiveTests
{
    [Theory]
    [InlineData("main.bmp")]
    [InlineData("MAIN.BMP")]
    [InlineData("Bento/Main.bmp")]
    [InlineData("Bento\\main.bmp")]
    public void An_archive_with_main_bmp_is_a_skin(string mainName) =>
        Assert.Null(SkinArchive.Check(SkinFixtures.Skin(mainName)));

    [Fact]
    public void Bytes_that_are_not_an_archive_are_not_a_skin() =>
        Assert.Equal(SkinProblem.NotAnArchive, SkinArchive.Check("not a zip at all"u8.ToArray()));

    [Fact]
    public void An_empty_file_is_not_a_skin() =>
        Assert.Equal(SkinProblem.NotAnArchive, SkinArchive.Check([]));

    [Fact]
    public void An_archive_without_main_bmp_is_not_a_skin() =>
        Assert.Equal(SkinProblem.NoMainBitmap, SkinArchive.Check(SkinFixtures.Zip("readme.txt", "notmain.bmp.txt")));

    [Fact]
    public void A_file_larger_than_the_limit_is_refused_without_being_opened() =>
        Assert.Equal(SkinProblem.TooLarge, SkinArchive.Check(new byte[SkinArchive.MaxBytes + 1]));
}
