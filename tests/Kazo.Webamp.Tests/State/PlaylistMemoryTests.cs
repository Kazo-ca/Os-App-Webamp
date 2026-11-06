using Kazo.Webamp.State;

namespace Kazo.Webamp.Tests.State;

/// <summary>A file that has disappeared stays in the saved list, at its place, and is skipped when playing.</summary>
public sealed class PlaylistMemoryTests
{
    [Fact]
    public void Missing_files_are_kept_after_the_track_that_preceded_them()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(
            ["/gone0", "/a", "/gone1", "/gone2", "/b"],
            savedCurrent: null,
            loaded: ["/a", "/b"],
            loadedKeys: ["k1", "k2"]);

        (IReadOnlyList<string> tracks, int? current) = restored.Memory.Compose(["k1", "k2"], ["/a", "/b"], null);

        Assert.Equal(["/gone0", "/a", "/gone1", "/gone2", "/b"], tracks);
        Assert.Null(current);
        Assert.Equal(["/gone0", "/gone1", "/gone2"], restored.Memory.MissingTracks);
    }

    [Fact]
    public void The_saved_current_track_is_resumed_when_it_was_read()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(["/gone", "/a", "/b"], 2, ["/a", "/b"], ["k1", "k2"]);

        Assert.Equal(1, restored.Current);
        Assert.True(restored.Resumed);
    }

    [Fact]
    public void A_missing_current_track_is_skipped_to_the_next_file_that_was_read()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(["/a", "/gone", "/b"], 1, ["/a", "/b"], ["k1", "k2"]);

        Assert.Equal(1, restored.Current);
        Assert.False(restored.Resumed);
    }

    [Fact]
    public void A_missing_current_track_at_the_end_leaves_no_current_track()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(["/a", "/gone"], 1, ["/a"], ["k1"]);

        Assert.Null(restored.Current);
    }

    [Fact]
    public void The_saved_current_track_follows_the_user_s_list_and_new_tracks()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(["/a", "/gone", "/b"], 0, ["/a", "/b"], ["k1", "k2"]);

        // The user moved /b first and added /c, which is now current.
        (IReadOnlyList<string> tracks, int? current) = restored.Memory.Compose(["k2", "k1", "k3"], ["/b", "/a", "/c"], "k3");

        Assert.Equal(["/b", "/a", "/gone", "/c"], tracks);
        Assert.Equal(3, current);
    }

    [Fact]
    public void A_missing_file_whose_preceding_track_left_goes_to_the_end()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(["/a", "/gone", "/b"], null, ["/a", "/b"], ["k1", "k2"]);

        (IReadOnlyList<string> tracks, _) = restored.Memory.Compose(["k2"], ["/b"], "k2");

        Assert.Equal(["/b", "/gone"], tracks);
    }

    [Fact]
    public void Emptying_the_list_forgets_the_missing_files()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(["/a", "/gone"], null, ["/a"], ["k1"]);

        (IReadOnlyList<string> emptied, _) = restored.Memory.Compose([], [], null);
        (IReadOnlyList<string> later, _) = restored.Memory.Compose(["k9"], ["/new"], null);

        Assert.Empty(emptied);
        Assert.Equal(["/new"], later);
        Assert.Empty(restored.Memory.MissingTracks);
    }

    [Fact]
    public void The_same_file_twice_is_matched_in_order()
    {
        RestoredPlaylist restored = PlaylistMemory.Restore(["/a", "/a", "/gone"], 1, ["/a", "/a"], ["k1", "k2"]);

        Assert.Equal(1, restored.Current);
        Assert.Equal(["/a", "/a", "/gone"], restored.Memory.Compose(["k1", "k2"], ["/a", "/a"], null).Tracks);
    }

    [Fact]
    public void Every_loaded_file_needs_its_key()
    {
        Assert.Throws<ArgumentException>(() => PlaylistMemory.Restore(["/a"], null, ["/a"], []));
    }
}
