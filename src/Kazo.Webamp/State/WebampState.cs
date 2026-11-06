namespace Kazo.Webamp.State;

/// <summary>
/// Everything Webamp remembers between two runs (decision 13 of doc/en/plan.md), as it is written
/// in <c>~/.config/ca.kazo.webamp/state.json</c>.
/// </summary>
/// <remarks>
/// <see cref="WebampStateJson"/> reads and writes it; a value out of range is brought back into its
/// range when the file is read, so a hand-edited file never breaks Webamp.
/// </remarks>
/// <param name="Tracks">The files of the list, in order, absolute paths. A file that has disappeared stays in it.</param>
/// <param name="CurrentTrack">Index in <paramref name="Tracks"/> of the current track, or <see langword="null"/>.</param>
/// <param name="Player">Volume, balance, equalizer, windows: what Webamp itself holds.</param>
/// <param name="Skin">The skin file applied, or <see langword="null"/> for Webamp's default skin.</param>
public sealed record WebampState(
    IReadOnlyList<string> Tracks,
    int? CurrentTrack,
    PlayerSettings Player,
    string? Skin)
{
    /// <summary>The version written in the file; another version is read as an empty state.</summary>
    public const int FormatVersion = 1;

    /// <summary>Gets the state of a first run: empty list, Webamp's own defaults.</summary>
    public static WebampState Empty { get; } = new([], null, PlayerSettings.Default, null);
}

/// <summary>What Webamp itself holds and the host module reads and restores (doc/en/architecture.md, "State").</summary>
/// <param name="Position">Seconds played in the current track; 0 when stopped.</param>
/// <param name="Volume">Volume, 0 to 100.</param>
/// <param name="Balance">Balance, -100 (left) to 100 (right).</param>
/// <param name="Shuffle">Whether shuffle is on.</param>
/// <param name="Repeat">Whether repeat is on.</param>
/// <param name="Equalizer">The equalizer.</param>
/// <param name="DoubleSize">Whether Webamp's windows are drawn at twice their size.</param>
/// <param name="Windows">Where Webamp's windows are, or <see langword="null"/> to stack them as Webamp does.</param>
public sealed record PlayerSettings(
    double Position,
    int Volume,
    int Balance,
    bool Shuffle,
    bool Repeat,
    EqualizerSettings Equalizer,
    bool DoubleSize,
    WindowLayout? Windows)
{
    /// <summary>Webamp's own volume at first start (200 of 255 in Winamp's ini file).</summary>
    public const int DefaultVolume = 78;

    /// <summary>Gets Webamp's defaults.</summary>
    public static PlayerSettings Default { get; } =
        new(0, DefaultVolume, 0, false, false, EqualizerSettings.Default, false, null);
}

/// <summary>Webamp's equalizer.</summary>
/// <param name="On">Whether it is on.</param>
/// <param name="Auto">Whether "Auto" is on.</param>
/// <param name="Preamp">The preamplifier, 0 to 100 (50 is 0 dB).</param>
/// <param name="Bands">The ten bands, 60 Hz to 16 kHz, each 0 to 100 (50 is 0 dB).</param>
public sealed record EqualizerSettings(bool On, bool Auto, int Preamp, IReadOnlyList<int> Bands)
{
    /// <summary>Number of bands of Winamp's equalizer.</summary>
    public const int BandCount = 10;

    /// <summary>The flat position of a slider.</summary>
    public const int Flat = 50;

    /// <summary>Gets Webamp's defaults: on, flat.</summary>
    public static EqualizerSettings Default { get; } =
        new(true, false, Flat, [.. Enumerable.Repeat(Flat, BandCount)]);

    /// <inheritdoc />
    public bool Equals(EqualizerSettings? other) =>
        other is not null && On == other.On && Auto == other.Auto && Preamp == other.Preamp && Bands.SequenceEqual(other.Bands);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(On, Auto, Preamp, Bands.Count);
}

/// <summary>Where Webamp's four windows are, inside the app's window.</summary>
/// <param name="Absolute">
/// Whether the positions are kept as they are (the user moved a window); otherwise Webamp centers
/// the group of windows in the app's window, as at first start.
/// </param>
/// <param name="Main">The main window.</param>
/// <param name="Equalizer">The equalizer.</param>
/// <param name="Playlist">The playlist editor.</param>
/// <param name="Milkdrop">The Milkdrop visualizer.</param>
public sealed record WindowLayout(
    bool Absolute,
    WebampWindowPlacement Main,
    WebampWindowPlacement Equalizer,
    WebampWindowPlacement Playlist,
    WebampWindowPlacement Milkdrop);

/// <summary>One of Webamp's windows.</summary>
/// <param name="X">Left edge, in pixels, inside the app's window.</param>
/// <param name="Y">Top edge, in pixels.</param>
/// <param name="Open">Whether it is shown.</param>
/// <param name="Shade">Whether it is rolled up to its title bar (main, equalizer, playlist).</param>
/// <param name="ExtraWidth">Steps of 25 pixels added to its width (playlist, Milkdrop).</param>
/// <param name="ExtraHeight">Steps of 29 pixels added to its height (playlist, Milkdrop).</param>
public sealed record WebampWindowPlacement(int X, int Y, bool Open, bool Shade, int ExtraWidth, int ExtraHeight);
