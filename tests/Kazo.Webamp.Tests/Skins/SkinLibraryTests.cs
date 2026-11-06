using Kazo.Webamp.Skins;

namespace Kazo.Webamp.Tests.Skins;

/// <summary>The user's skins folder: copies, listing, refused files.</summary>
public sealed class SkinLibraryTests
{
    private const string Folder = "/home/neo/.local/share/webamp/skins";
    private readonly MemorySkinFileSystem _files = new();

    private SkinLibrary CreateLibrary() => new(_files, "/home/neo");

    [Fact]
    public void The_folder_is_under_the_users_local_share()
    {
        Assert.Equal(Folder, CreateLibrary().Directory);
        Assert.Equal(Folder, new SkinLibrary(_files, "/home/neo/").Directory);
    }

    [Fact]
    public async Task Loading_lists_the_wsz_skins_of_the_folder_sorted_by_name_and_skips_the_rest()
    {
        _files.Files[$"{Folder}/zebra.wsz"] = SkinFixtures.Skin();
        _files.Files[$"{Folder}/Bento.WSZ"] = SkinFixtures.Skin();
        _files.Files[$"{Folder}/apple.wsz"] = SkinFixtures.Skin();
        _files.Files[$"{Folder}/broken.wsz"] = "nope"u8.ToArray();
        _files.Files[$"{Folder}/locked.wsz"] = SkinFixtures.Skin();
        _files.Files[$"{Folder}/notes.txt"] = SkinFixtures.Skin();
        _files.Files[$"{Folder}/old/inner.wsz"] = SkinFixtures.Skin();
        _files.Unreadable.Add($"{Folder}/locked.wsz");
        SkinLibrary library = CreateLibrary();

        await library.LoadAsync();

        Assert.Equal(["apple.wsz", "Bento.WSZ", "zebra.wsz"], library.Skins.Select(skin => skin.FileName));
        Assert.Equal(["apple", "Bento", "zebra"], library.Skins.Select(skin => skin.Name));
    }

    [Fact]
    public async Task Loading_a_missing_folder_lists_nothing()
    {
        SkinLibrary library = CreateLibrary();

        await library.LoadAsync();

        Assert.Empty(library.Skins);
    }

    [Fact]
    public async Task Opening_a_skin_of_the_user_copies_it_to_the_folder_and_lists_it()
    {
        byte[] skin = SkinFixtures.Skin();
        _files.Files["/home/neo/Downloads/Bento.wsz"] = skin;
        SkinLibrary library = CreateLibrary();

        SkinImportResult result = await library.ImportFileAsync("/home/neo/Downloads/Bento.wsz");

        Assert.Null(result.Problem);
        Assert.Equal("Bento.wsz", result.Skin!.FileName);
        Assert.Equal(skin, _files.Files[$"{Folder}/Bento.wsz"]);
        Assert.Equal(["Bento.wsz"], library.Skins.Select(listed => listed.FileName));
    }

    [Fact]
    public async Task Opening_a_skin_already_in_the_folder_does_not_copy_it_again()
    {
        _files.Files[$"{Folder}/Bento.wsz"] = SkinFixtures.Skin();
        SkinLibrary library = CreateLibrary();
        await library.LoadAsync();

        SkinImportResult result = await library.ImportFileAsync($"{Folder}/Bento.wsz");

        Assert.Equal("Bento.wsz", result.Skin!.FileName);
        Assert.Empty(_files.Writes);
        Assert.Single(library.Skins);
    }

    [Fact]
    public async Task A_skin_from_the_computer_is_kept_as_a_wsz_under_its_own_name_and_replaces_one_of_the_same_name()
    {
        SkinLibrary library = CreateLibrary();
        await library.ImportAsync("Bento.wsz", SkinFixtures.Skin());
        byte[] newer = SkinFixtures.Skin("MAIN.BMP");

        SkinImportResult result = await library.ImportAsync("C:\\skins\\Bento.zip", newer);

        Assert.Equal("Bento.wsz", result.Skin!.FileName);
        Assert.Equal(newer, _files.Files[$"{Folder}/Bento.wsz"]);
        Assert.Equal(newer, Assert.Single(library.Skins).Bytes);
    }

    [Fact]
    public async Task A_file_that_is_not_a_skin_is_refused_and_nothing_is_copied()
    {
        _files.Files["/home/neo/fake.wsz"] = SkinFixtures.Zip("readme.txt");
        SkinLibrary library = CreateLibrary();

        SkinImportResult result = await library.ImportFileAsync("/home/neo/fake.wsz");

        Assert.Null(result.Skin);
        Assert.Equal(SkinProblem.NoMainBitmap, result.Problem);
        Assert.Empty(_files.Writes);
        Assert.Empty(library.Skins);
    }

    [Fact]
    public async Task A_missing_file_throws_an_IOException()
    {
        SkinLibrary library = CreateLibrary();

        await Assert.ThrowsAnyAsync<IOException>(() => library.ImportFileAsync("/home/neo/gone.wsz"));
    }

    [Theory]
    [InlineData("Bento.wsz", "Bento.wsz")]
    [InlineData("Bento.ZIP", "Bento.wsz")]
    [InlineData("C:\\Users\\me\\Skins\\Bento.wsz", "Bento.wsz")]
    [InlineData("../../etc/passwd", "passwd.wsz")]
    [InlineData(".wsz", "skin.wsz")]
    [InlineData("..hidden.wsz", "hidden.wsz")]
    [InlineData("tab\there.wsz", "tabhere.wsz")]
    public void The_folder_name_is_the_files_own_name_made_safe(string fileName, string expected) =>
        Assert.Equal(expected, SkinLibrary.FolderFileName(fileName));
}
