using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.Tests;

/// <summary>The app's manifest class, the source of the generated manifest.json (decision 18).</summary>
public sealed class ManifestTests
{
    private readonly WebampApp _app = new();

    [Fact]
    public void The_app_is_the_Webamp_window_app_of_pub_kazo()
    {
        Assert.Equal("ca.kazo.webamp", _app.Id);
        Assert.Equal("Webamp", _app.Name);
        Assert.Equal("pub.kazo", _app.PublisherId);
        Assert.Equal("multimedia", _app.Category);
        Assert.Equal("1.0.0", _app.Version);
        Assert.Equal(AppKind.Window, _app.Kind);
        Assert.Equal(typeof(WebampWindow), _app.EntryPoint);
    }

    [Fact]
    public void The_app_runs_on_the_desktop_only_in_one_window_per_user()
    {
        Assert.Null(_app.MobileEntryPoint);
        Assert.True(_app.SingleInstancePerUser);
    }

    [Fact]
    public void The_app_asks_only_for_the_base_capabilities_none_dangerous()
    {
        Assert.Equal(
            new[] { AppCapabilities.FileSystemUserHomeRead, AppCapabilities.FileSystemPrivateRead, AppCapabilities.FileSystemPrivateWrite },
            _app.Capabilities);
        Assert.DoesNotContain(AppCapabilities.FileSystemSystemRead, _app.Capabilities);
    }

    [Fact]
    public void The_window_opens_at_the_size_of_the_three_Webamp_windows_and_can_be_resized()
    {
        WindowOptions window = Assert.IsType<WindowOptions>(_app.Window);
        Assert.True(window.Resizable);
        Assert.InRange(window.DefaultWidth - WebampApp.ContentWidth, 0, 8);
        Assert.InRange(window.DefaultHeight - WebampApp.ContentHeight, 30, 48);
        Assert.True(window.MinWidth <= window.DefaultWidth);
        Assert.True(window.MinHeight < window.DefaultHeight);
    }

    [Fact]
    public void The_app_icon_is_the_svg_of_wwwroot()
    {
        Assert.Equal(new[] { "icons/app.svg" }, _app.Icons);
        Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "src", "Kazo.Webamp", "wwwroot", "icons", "app.svg")));
    }

    [Fact]
    public void Webamp_its_license_and_the_host_module_ship_in_wwwroot()
    {
        string wwwroot = Path.Combine(RepositoryRoot(), "src", "Kazo.Webamp", "wwwroot");
        foreach (string file in new[]
                 {
                     "lib/webamp/webamp.bundle.min.mjs", "lib/webamp/LICENSE.txt",
                     "lib/butterchurn/butterchurn.min.js", "lib/butterchurn/LICENSE",
                     "lib/butterchurn-presets/base.min.js", "lib/butterchurn-presets/LICENSE",
                     "js/webamp-host.js",
                 })
        {
            Assert.True(File.Exists(Path.Combine(wwwroot, file)), file);
        }
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kazo.Webamp.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Kazo.Webamp.slnx not found above the test output.");
    }
}
