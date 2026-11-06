using Kazo.Webamp.Skins;

namespace Kazo.Webamp.Tests.Skins;

/// <summary>A window's skins: put on, remembered, and the import tip posted once.</summary>
public sealed class SkinControllerTests
{
    private const string Folder = "/home/neo/.local/share/webamp/skins";
    private const string Settings = "/home/neo/.config/ca.kazo.webamp/skins.json";
    private readonly MemorySkinFileSystem _files = new();
    private readonly FakeSkinPlayer _player = new();

    private async Task<SkinController> StartAsync()
    {
        SkinController controller = new(_files, "/home/neo");
        await controller.StartAsync();
        controller.Attach(_player);
        return controller;
    }

    [Fact]
    public async Task A_skin_opened_is_put_on_and_remembered_for_the_next_start()
    {
        _files.Files["/home/neo/Bento.wsz"] = SkinFixtures.Skin();
        SkinController controller = await StartAsync();

        await controller.OpenAsync("/home/neo/Bento.wsz");

        Assert.Equal([("Bento.wsz", true)], _player.Calls);
        SkinController next = new(_files, "/home/neo");
        await next.StartAsync();
        Assert.Equal("Bento.wsz", next.Current?.FileName);
        Assert.Equal(["Bento.wsz"], next.Library.Skins.Select(skin => skin.FileName));
    }

    [Fact]
    public async Task A_skin_from_the_computer_is_put_on_and_kept()
    {
        SkinController controller = await StartAsync();

        SkinImportResult result = await controller.LoadFromComputerAsync("Green.zip", SkinFixtures.Skin());

        Assert.Equal("Green.wsz", result.Skin!.FileName);
        Assert.Equal([("Green.wsz", true)], _player.Calls);
        Assert.True(_files.Files.ContainsKey($"{Folder}/Green.wsz"));
        Assert.Equal("Green.wsz", controller.Current?.FileName);
    }

    [Fact]
    public async Task A_broken_skin_is_refused_and_the_current_skin_stays()
    {
        _files.Files[$"{Folder}/Bento.wsz"] = SkinFixtures.Skin();
        SkinController controller = await StartAsync();
        await controller.ChosenAsync("Bento.wsz");

        SkinImportResult result = await controller.LoadFromComputerAsync("broken.wsz", "garbage"u8.ToArray());

        Assert.Equal(SkinProblem.NotAnArchive, result.Problem);
        Assert.Empty(_player.Calls);
        Assert.Equal("Bento.wsz", controller.Current?.FileName);
        Assert.False(_files.Files.ContainsKey($"{Folder}/broken.wsz"));
    }

    [Fact]
    public async Task A_skin_picked_in_the_menu_is_remembered_and_the_base_skin_too()
    {
        _files.Files[$"{Folder}/Bento.wsz"] = SkinFixtures.Skin();
        SkinController controller = await StartAsync();

        await controller.ChosenAsync("Bento.wsz");
        Assert.Equal("Bento.wsz", (await SkinSettings.LoadAsync(_files, "/home/neo")).Current);

        await controller.ChosenAsync(null);
        Assert.Null((await SkinSettings.LoadAsync(_files, "/home/neo")).Current);
        Assert.Null(controller.Current);
        Assert.Empty(_player.Calls);
    }

    [Fact]
    public async Task A_remembered_skin_that_left_the_folder_starts_Webamp_on_its_base_skin()
    {
        await new SkinSettings("Gone.wsz").SaveAsync(_files, "/home/neo");

        SkinController controller = await StartAsync();

        Assert.Null(controller.Current);
    }

    [Fact]
    public async Task Broken_settings_are_the_defaults()
    {
        _files.Files[Settings] = "{ not json"u8.ToArray();

        SkinSettings settings = await SkinSettings.LoadAsync(_files, "/home/neo");

        Assert.Equal(new SkinSettings(), settings);
    }

    [Fact]
    public async Task The_import_tip_is_posted_on_the_first_launch_only()
    {
        int posted = 0;
        SkinController first = await StartAsync();

        Assert.True(await first.ShowImportTipOnceAsync(() => posted++));
        Assert.False(await first.ShowImportTipOnceAsync(() => posted++));

        SkinController second = await StartAsync();
        Assert.False(await second.ShowImportTipOnceAsync(() => posted++));
        Assert.Equal(1, posted);
    }

    [Fact]
    public async Task A_tip_that_could_not_be_posted_is_tried_again_at_the_next_start()
    {
        SkinController first = await StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => first.ShowImportTipOnceAsync(() => throw new InvalidOperationException("denied")));

        int posted = 0;
        SkinController second = await StartAsync();
        Assert.True(await second.ShowImportTipOnceAsync(() => posted++));
        Assert.Equal(1, posted);
    }

    [Fact]
    public async Task Choosing_the_skin_already_in_use_writes_nothing()
    {
        _files.Files[$"{Folder}/Bento.wsz"] = SkinFixtures.Skin();
        SkinController controller = await StartAsync();
        await controller.ChosenAsync("Bento.wsz");
        int writes = _files.Writes.Count;

        await controller.ChosenAsync("Bento.wsz");

        Assert.Equal(writes, _files.Writes.Count);
    }
}
