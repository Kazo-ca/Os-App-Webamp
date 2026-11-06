using Kazo.Os.App.Abstractions;
using Kazo.Webamp.Opening;
using Kazo.Webamp.Player;

namespace Kazo.Webamp.Tests.Opening;

/// <summary>What the OS hands Webamp: files opened while it runs, its two file menu entries.</summary>
public sealed class OpenIntentsTests
{
    private static string[] Paths(OpenRequest? request) => [.. Assert.IsType<OpenRequest>(request).Paths.Select(path => path.Value)];

    [Fact]
    public void A_file_opened_while_Webamp_runs_arrives_as_a_launch_with_its_path_and_plays()
    {
        AppIntentDelivery intent = new(AppIntentIds.LaunchApp) { Arguments = ["/home/neo/Music/a.mp3"] };

        OpenRequest? request = OpenIntents.Read(intent);

        Assert.Equal(OpenMode.Play, request?.Mode);
        Assert.Equal(["/home/neo/Music/a.mp3"], Paths(request));
    }

    [Fact]
    public void The_play_entry_plays_and_the_enqueue_entry_adds_the_whole_selection_in_order()
    {
        string payload(string entry) => $$"""{"paths":["/home/neo/b.mp3","/home/neo/Music"],"scope":"File","entry":"{{entry}}"}""";

        OpenRequest? play = OpenIntents.Read(new AppIntentDelivery(WebampApp.FilesIntent, payload(WebampApp.PlayEntry), "org.kazoos.shell"));
        OpenRequest? enqueue = OpenIntents.Read(new AppIntentDelivery(WebampApp.FilesIntent, payload(WebampApp.EnqueueEntry), "org.kazoos.shell"));

        Assert.Equal(OpenMode.Play, play?.Mode);
        Assert.Equal(OpenMode.Enqueue, enqueue?.Mode);
        Assert.Equal(["/home/neo/b.mp3", "/home/neo/Music"], Paths(enqueue));
    }

    [Fact]
    public void A_plain_launch_an_option_or_a_broken_payload_opens_nothing()
    {
        Assert.Null(OpenIntents.Read(null));
        Assert.Null(OpenIntents.Read(new AppIntentDelivery(AppIntentIds.LaunchApp)));
        Assert.Null(OpenIntents.Read(new AppIntentDelivery(AppIntentIds.LaunchApp) { Arguments = ["--verbose"] }));
        Assert.Null(OpenIntents.Read(new AppIntentDelivery(WebampApp.FilesIntent, "not json")));
        Assert.Null(OpenIntents.FromArguments([]));
    }

    [Fact]
    public void Several_launch_arguments_play_in_order()
    {
        Assert.Equal(["/home/neo/a.mp3", "/home/neo/b.mp3"], Paths(OpenIntents.FromArguments(["/home/neo/a.mp3", "-x", "/home/neo/b.mp3"])));
    }

    [Theory]
    [InlineData("open", WebampOpenKind.Open)]
    [InlineData("add-files", WebampOpenKind.AddFiles)]
    [InlineData("add-folder", WebampOpenKind.AddFolder)]
    public void The_host_module_names_Webamp_s_open_commands(string name, WebampOpenKind kind) =>
        Assert.Equal(kind, WebampPlayer.ParseOpenKind(name));

    [Fact]
    public void An_unknown_command_name_is_ignored() => Assert.Null(WebampPlayer.ParseOpenKind("add-url"));
}
