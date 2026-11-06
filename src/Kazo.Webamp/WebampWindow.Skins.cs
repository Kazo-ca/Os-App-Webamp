using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.IO;
using Kazo.Os.Simulation.Abstractions.Gateways;
using Kazo.Os.Simulation.Abstractions.Notifications;
using Kazo.Webamp.Player;
using Kazo.Webamp.Skins;
using Microsoft.JSInterop;

namespace Kazo.Webamp;

/// <summary>
/// The skins of the Webamp window (work item 4, decisions 11 and 20 of doc/en/plan.md): a
/// <c>.wsz</c> opened in the OS or chosen on the computer is copied to <c>~/.local/share/webamp/skins/</c>
/// and put on, Webamp's Skins menu lists that folder, the skin in use is remembered, a broken skin
/// is refused with a message, and a notification explains once how to import a skin.
/// </summary>
public sealed partial class WebampWindow
{
    private SkinController? _skins;

    /// <summary>Whether a file opened in Webamp is a skin rather than music.</summary>
    /// <param name="file">The file.</param>
    internal static bool IsSkinFile(VirtualPath file) =>
        file.Value.EndsWith(SkinLibrary.Extension, StringComparison.OrdinalIgnoreCase);

    // Reads the skins folder and the settings; when they cannot be read, Webamp starts with its base
    // skin and an empty menu, and the next skin opened tries the folder again.
    private async Task<SkinController> StartSkinsAsync()
    {
        SkinController skins = new(KazoSkinFileSystem.Instance, AppContext.HomeDirectory);
        try
        {
            await skins.StartAsync(AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing to list.
        }

        return skins;
    }

    private void AttachSkins(WebampPlayer player, SkinController skins)
    {
        _skins = skins;
        skins.Attach(player);
        player.SkinFileChosen += OnSkinFileChosenAsync;
        player.SkinChosen += OnSkinChosenAsync;
    }

    private void DetachSkins(WebampPlayer player)
    {
        player.SkinFileChosen -= OnSkinFileChosenAsync;
        player.SkinChosen -= OnSkinChosenAsync;
    }

    // A .wsz of the user (a double-click, "Open with", webamp skin.wsz).
    private async Task OpenSkinAsync(VirtualPath file)
    {
        string name = KazoPath.GetFileName(file.Value) ?? file.Value;
        if (_skins is null)
        {
            return;
        }

        await ImportSkinAsync(name, token => _skins.OpenAsync(file.Value, token));
    }

    // Webamp's "Skins ▸ Load Skin…", or a skin dropped on Webamp: a file of the computer. Called
    // from JavaScript, outside the OS's scope for the app: the file system needs it back.
    private Task OnSkinFileChosenAsync(string fileName, byte[] bytes) =>
        _skins is null ? Task.CompletedTask : InvokeAsync(async () =>
        {
            using IDisposable scope = KazoExecutionContext.Enter(AppContext);
            await ImportSkinAsync(fileName, token => _skins.LoadFromComputerAsync(fileName, bytes, token));
        });

    // A skin picked in Webamp's Skins menu: Webamp put it on, the app remembers it.
    private Task OnSkinChosenAsync(string? fileName) =>
        _skins is null ? Task.CompletedTask : InvokeAsync(async () =>
        {
            using IDisposable scope = KazoExecutionContext.Enter(AppContext);
            try
            {
                await _skins.ChosenAsync(fileName, AppContext.CancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Not remembered: Webamp starts with the previous skin next time.
            }
        });

    private async Task ImportSkinAsync(string name, Func<CancellationToken, Task<SkinImportResult>> import)
    {
        string? problem;
        try
        {
            SkinImportResult result = await import(AppContext.CancellationToken);
            problem = result.Problem switch
            {
                null => null,
                SkinProblem.TooLarge => L["SkinTooLarge", SkinArchive.MaxBytes / (1024 * 1024)].Value,
                SkinProblem.NoMainBitmap => L["SkinNoMainBitmap", SkinArchive.MainBitmap].Value,
                _ => L["SkinNotAnArchive"].Value,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JSException)
        {
            problem = exception.Message;
        }

        if (problem is not null)
        {
            // The skin in use stays: nothing was sent to Webamp.
            await MessageBox(L["SkinFailedTitle"], L["SkinFailed", name, problem], MessageBoxType.Ok, MessageBoxIcon.Error);
        }
    }

    // Decision 20: the first time Webamp opens, a notification tells how to change its skin.
    private async Task ShowSkinTipOnceAsync()
    {
        if (_skins is null)
        {
            return;
        }

        try
        {
            await _skins.ShowImportTipOnceAsync(
                () => AppContext.Notifications.Post(NotificationSeverity.Information, L["SkinTipTitle"], L["SkinTip"]),
                AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is AppGatewayAccessDeniedException or IOException or UnauthorizedAccessException)
        {
            // Not shown, or not remembered: tried again at the next start.
        }
    }
}
