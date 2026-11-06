namespace Kazo.Webamp.Player;

/// <summary>
/// Which of Webamp's own "open" commands the user chose (decision 16 of doc/en/plan.md). Webamp
/// would show the browser's file picker; the host module stops it and the window shows the OS
/// dialog instead.
/// </summary>
public enum WebampOpenKind
{
    /// <summary>The eject button, "Play ▸ File…" or the playlist's eject: replaces the list and plays.</summary>
    Open,

    /// <summary>The playlist's "ADD ▸ Add file": adds files at the end of the list.</summary>
    AddFiles,

    /// <summary>The playlist's "ADD ▸ Add dir": adds a folder's audio files at the end of the list.</summary>
    AddFolder,
}
