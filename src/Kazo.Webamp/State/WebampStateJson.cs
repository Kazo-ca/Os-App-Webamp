using System.Text.Json;
using System.Text.Json.Serialization;
using Kazo.Os.App.Abstractions;

namespace Kazo.Webamp.State;

/// <summary>
/// Reads and writes <c>state.json</c> (doc/en/architecture.md, "State"). Reading never throws: a
/// file that is not JSON, of another version, or with values out of range gives what can be kept
/// of it, down to <see cref="WebampState.Empty"/>.
/// </summary>
/// <remarks>The JSON is source-generated (<see cref="StateJsonContext"/>): the app is trimmed when the OS is published.</remarks>
public static class WebampStateJson
{
    /// <summary>Reads a state file's text.</summary>
    /// <param name="json">The text of the file.</param>
    /// <returns>The state; <see cref="WebampState.Empty"/> when the file is unusable.</returns>
    public static WebampState Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return WebampState.Empty;
        }

        StateDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, StateJsonContext.Default.StateDocument);
        }
        catch (JsonException)
        {
            return WebampState.Empty;
        }

        if (document is null || document.Version != WebampState.FormatVersion)
        {
            return WebampState.Empty;
        }

        return FromDocument(document);
    }

    /// <summary>Writes a state as the text of <c>state.json</c> (indented, so a person can read it).</summary>
    /// <param name="state">The state.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(WebampState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(ToDocument(state), StateJsonContext.Default.StateDocument);
    }

    private static WebampState FromDocument(StateDocument document)
    {
        List<string> tracks = [];
        int? current = null;
        List<string?> listed = document.Tracks ?? [];
        for (int index = 0; index < listed.Count; index++)
        {
            if (!TryNormalize(listed[index], out string? path))
            {
                continue;
            }

            if (index == document.CurrentTrack)
            {
                current = tracks.Count;
            }

            tracks.Add(path);
        }

        return new WebampState(tracks, current, FromDocument(document.Player));
    }

    private static PlayerSettings FromDocument(PlayerDocument? player)
    {
        if (player is null)
        {
            return PlayerSettings.Default;
        }

        double position = player.Position is { } seconds && double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
        return new PlayerSettings(
            position,
            Math.Clamp(player.Volume ?? PlayerSettings.DefaultVolume, 0, 100),
            Math.Clamp(player.Balance ?? 0, -100, 100),
            player.Shuffle ?? false,
            player.Repeat ?? false,
            FromDocument(player.Equalizer),
            player.DoubleSize ?? false,
            FromDocument(player.Windows));
    }

    private static EqualizerSettings FromDocument(EqualizerDocument? equalizer)
    {
        if (equalizer is null)
        {
            return EqualizerSettings.Default;
        }

        List<int> bands = equalizer.Bands is { Count: EqualizerSettings.BandCount } values
            ? [.. values.Select(Slider)]
            : [.. EqualizerSettings.Default.Bands];
        return new EqualizerSettings(
            equalizer.On ?? true,
            equalizer.Auto ?? false,
            Slider(equalizer.Preamp ?? EqualizerSettings.Flat),
            bands);
    }

    private static WindowLayout? FromDocument(WindowsDocument? windows)
    {
        if (windows?.Main is null || windows.Equalizer is null || windows.Playlist is null || windows.Milkdrop is null)
        {
            return null;
        }

        return new WindowLayout(
            windows.Absolute ?? false,
            FromDocument(windows.Main),
            FromDocument(windows.Equalizer),
            FromDocument(windows.Playlist),
            FromDocument(windows.Milkdrop));
    }

    private static WebampWindowPlacement FromDocument(WindowDocument window) =>
        new(
            Math.Clamp(window.X ?? 0, -MaxCoordinate, MaxCoordinate),
            Math.Clamp(window.Y ?? 0, -MaxCoordinate, MaxCoordinate),
            window.Open ?? true,
            window.Shade ?? false,
            Math.Clamp(window.ExtraWidth ?? 0, 0, MaxExtraSteps),
            Math.Clamp(window.ExtraHeight ?? 0, 0, MaxExtraSteps));

    private static StateDocument ToDocument(WebampState state)
    {
        PlayerSettings player = state.Player;
        WindowLayout? windows = player.Windows;
        return new StateDocument
        {
            Version = WebampState.FormatVersion,
            Tracks = [.. state.Tracks],
            CurrentTrack = state.CurrentTrack,
            Player = new PlayerDocument
            {
                Position = Math.Round(player.Position, 3),
                Volume = player.Volume,
                Balance = player.Balance,
                Shuffle = player.Shuffle,
                Repeat = player.Repeat,
                DoubleSize = player.DoubleSize,
                Equalizer = new EqualizerDocument
                {
                    On = player.Equalizer.On,
                    Auto = player.Equalizer.Auto,
                    Preamp = player.Equalizer.Preamp,
                    Bands = [.. player.Equalizer.Bands],
                },
                Windows = windows is null
                    ? null
                    : new WindowsDocument
                    {
                        Absolute = windows.Absolute,
                        Main = ToDocument(windows.Main),
                        Equalizer = ToDocument(windows.Equalizer),
                        Playlist = ToDocument(windows.Playlist),
                        Milkdrop = ToDocument(windows.Milkdrop),
                    },
            },
        };
    }

    private static WindowDocument ToDocument(WebampWindowPlacement window) => new()
    {
        X = window.X,
        Y = window.Y,
        Open = window.Open,
        Shade = window.Shade,
        ExtraWidth = window.ExtraWidth,
        ExtraHeight = window.ExtraHeight,
    };

    // A path the OS accepts, in its canonical form; anything else is dropped.
    private static bool TryNormalize(string? value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            path = VirtualPath.Parse(value).Value;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static int Slider(int value) => Math.Clamp(value, 0, 100);

    // Far beyond any screen: only absurd values are cut, Webamp brings windows back on screen itself.
    private const int MaxCoordinate = 100_000;

    // Webamp's own limit is the screen; this only keeps a hand-edited file reasonable.
    private const int MaxExtraSteps = 200;
}

// The file as written. Every member is optional on reading: a missing one takes its default.
internal sealed class StateDocument
{
    public int Version { get; set; }

    public List<string?>? Tracks { get; set; }

    public int? CurrentTrack { get; set; }

    public PlayerDocument? Player { get; set; }
}

internal sealed class PlayerDocument
{
    public double? Position { get; set; }

    public int? Volume { get; set; }

    public int? Balance { get; set; }

    public bool? Shuffle { get; set; }

    public bool? Repeat { get; set; }

    public EqualizerDocument? Equalizer { get; set; }

    public bool? DoubleSize { get; set; }

    public WindowsDocument? Windows { get; set; }
}

internal sealed class EqualizerDocument
{
    public bool? On { get; set; }

    public bool? Auto { get; set; }

    public int? Preamp { get; set; }

    public List<int>? Bands { get; set; }
}

internal sealed class WindowsDocument
{
    public bool? Absolute { get; set; }

    public WindowDocument? Main { get; set; }

    public WindowDocument? Equalizer { get; set; }

    public WindowDocument? Playlist { get; set; }

    public WindowDocument? Milkdrop { get; set; }
}

internal sealed class WindowDocument
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public bool? Open { get; set; }

    public bool? Shade { get; set; }

    public int? ExtraWidth { get; set; }

    public int? ExtraHeight { get; set; }
}

/// <summary>Source-generated JSON of <c>state.json</c>: camelCase, indented, nulls left out.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    NumberHandling = JsonNumberHandling.Strict,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(StateDocument))]
internal sealed partial class StateJsonContext : JsonSerializerContext;
