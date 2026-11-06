using Kazo.Webamp.State;

namespace Kazo.Webamp.Tests.State;

/// <summary>Stands for Webamp's state side: records what the keeper asked and answers a set snapshot.</summary>
internal sealed class FakeStatePlayer : IStatePlayer
{
    public PlayerSnapshot Snapshot { get; set; } = new(null, PlayerSettings.Default);

    public List<(int Index, double Position)> Resumed { get; } = [];

    public List<(string Name, byte[] Bytes)> Skins { get; } = [];

    public ValueTask<PlayerSnapshot> ReadStateAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Snapshot);

    public ValueTask ResumeAsync(int index, double position, CancellationToken cancellationToken = default)
    {
        Resumed.Add((index, position));
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplySkinAsync(string name, byte[] bytes, CancellationToken cancellationToken = default)
    {
        Skins.Add((name, bytes));
        return ValueTask.CompletedTask;
    }
}
