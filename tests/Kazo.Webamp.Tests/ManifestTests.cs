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
    public void The_app_asks_only_for_the_capabilities_it_uses_none_dangerous()
    {
        Assert.Equal(
            new[]
            {
                AppCapabilities.FileSystemUserHomeRead, AppCapabilities.FileSystemPrivateRead, AppCapabilities.FileSystemPrivateWrite,
                AppCapabilities.FileSystemUserHomeWrite, AppCapabilities.DialogFileOpen, AppCapabilities.DialogFileSave,
                AppCapabilities.DialogFolderSelect, AppCapabilities.ShellContextMenu,
            },
            _app.Capabilities);
        Assert.DoesNotContain(_app.Capabilities, AppCapabilities.IsDangerous);
        Assert.DoesNotContain(AppCapabilities.FileSystemSystemRead, _app.Capabilities);
        Assert.DoesNotContain(AppCapabilities.FileSystemSystemWrite, _app.Capabilities);
    }

    [Fact]
    public void Webamp_opens_the_audio_formats_browsers_play()
    {
        Dictionary<string, string?> byExtension = _app.FileHandlers
            .Where(handler => handler.MediaType?.StartsWith("audio/", StringComparison.Ordinal) == true && handler.Extensions.All(extension => Kazo.Webamp.Tracks.AudioMediaTypes.ForFile("x" + extension) is not null))
            .SelectMany(handler => handler.Extensions.Select(extension => (extension, handler.MediaType)))
            .ToDictionary(pair => pair.extension, pair => pair.MediaType);

        Assert.Equal(
            [".aac", ".flac", ".m4a", ".mp3", ".oga", ".ogg", ".opus", ".wav", ".weba"],
            byExtension.Keys.Order(StringComparer.Ordinal));
        foreach ((string extension, string? mediaType) in byExtension)
        {
            Assert.Equal(Kazo.Webamp.Tracks.AudioMediaTypes.ForFile("x" + extension), mediaType);
        }

        Assert.All(_app.FileHandlers, handler => Assert.Equal(["open"], handler.Actions));
    }

    [Fact]
    public void The_two_file_menu_entries_are_declared_with_their_intent_on_audio_files_and_folders()
    {
        Assert.Equal([WebampApp.FilesIntent], _app.Intents.Select(intent => intent.IntentId));

        string manifest = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Kazo.Webamp", "manifest.json"));
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(manifest);
        System.Text.Json.JsonElement[] entries = [.. document.RootElement.GetProperty("contextMenus").EnumerateArray()];
        Assert.Equal([WebampApp.PlayEntry, WebampApp.EnqueueEntry], entries.Select(entry => entry.GetProperty("name").GetString()));
        foreach (System.Text.Json.JsonElement entry in entries)
        {
            Assert.Equal(WebampApp.FilesIntent, entry.GetProperty("intent").GetString());
            Assert.Equal(["file", "directory"], entry.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
            string?[] types = [.. entry.GetProperty("types").EnumerateArray().Select(type => type.GetString())];
            Assert.Contains("inode/directory", types);
            Assert.Contains(".mp3", types);
            Assert.Contains(".m3u8", types);
            Assert.Equal("all", entry.GetProperty("selection").GetString());
        }

        Assert.Equal(
            ["Lire dans Webamp", "Ajouter à la liste de Webamp"],
            entries.Select(entry => entry.GetProperty("titles").EnumerateArray()
                .Single(title => title.GetProperty("culture").GetString() == "fr").GetProperty("text").GetString()));
    }

    [Theory]
    [InlineData("audio/x-mpegurl", ".m3u")]
    [InlineData("application/vnd.apple.mpegurl", ".m3u8")]
    [InlineData("audio/x-scpls", ".pls")]
    public void The_app_opens_the_three_playlist_formats(string mediaType, string extension)
    {
        FileHandler handler = Assert.Single(_app.FileHandlers, handler => handler.MediaType == mediaType);
        Assert.Equal(new[] { extension }, handler.Extensions);
        Assert.Equal(new[] { "open" }, handler.Actions);
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
