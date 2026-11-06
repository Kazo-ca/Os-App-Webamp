using Kazo.Webamp.Opening;

namespace Kazo.Webamp.Tests.Opening;

/// <summary>Decision 14: past 1 GB of tracks in memory, a warning; the user may go on.</summary>
public sealed class LoadedMemoryWarningTests
{
    [Fact]
    public void The_limit_is_one_gigabyte()
    {
        Assert.Equal(1_073_741_824, LoadedMemoryWarning.Limit);
        LoadedMemoryWarning warning = new();
        Assert.False(warning.ShouldWarn(LoadedMemoryWarning.Limit));
        Assert.True(warning.ShouldWarn(LoadedMemoryWarning.Limit + 1));
    }

    [Fact]
    public void It_warns_once_while_over_the_limit_and_again_after_going_back_under_it()
    {
        LoadedMemoryWarning warning = new(limit: 100);

        Assert.False(warning.ShouldWarn(60));
        Assert.True(warning.ShouldWarn(120));
        Assert.False(warning.ShouldWarn(180));
        Assert.False(warning.ShouldWarn(90));
        Assert.True(warning.ShouldWarn(101));
    }
}
