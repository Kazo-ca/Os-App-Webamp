// End-to-end runs of work item 5 (state, title, hotkeys, Milkdrop) inside the Kazo OS host:
// `npm run test:e2e` after `dotnet build Kazo.Webamp.slnx -c Release`.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { TONE, boot, launchOptions, openApp, runInTerminal, startHost, uploadToHome } from './host.mjs';

let host;
let browser;

before(async () => {
    host = await startHost();
    browser = await chromium.launch(launchOptions);
});

after(async () => {
    await browser?.close();
    host?.stop();
});

/** The window title Webamp takes while the test tone is current: "Artist - Title - Webamp". */
const TONE_TITLE = 'Kazo Test - Tone 440 - Webamp';

/** Plays the test tone in Webamp, opened on it from the Terminal; Webamp's window is then active. */
async function playTone(page) {
    await boot(page, host.baseUrl);
    await uploadToHome(page, TONE);
    await runInTerminal(page, 'launch ca.kazo.webamp /home/neo/tone.mp3');
    await page.locator('#main-window.play').waitFor({ timeout: 30_000 });
}

/** Gives Webamp's OS window the focus: a click on its title bar ("… - Webamp" or "Webamp"). */
async function focusWebamp(page) {
    await page.locator('h2', { hasText: /(^|- )Webamp$/ }).first().click();
}

const repeatIsOn = page => page.locator('#main-window #repeat.selected');

test('the window title follows the track, and Webamp comes back as it was after a reload', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await playTone(page);

    // Decision 8: "Artist - Title - Webamp" on the taskbar.
    await page.locator('button', { hasText: TONE_TITLE }).waitFor({ timeout: 15_000 });

    // Repeat on (the tone is four seconds long), a lower volume, then paused in the track.
    await focusWebamp(page);
    await page.keyboard.press('r');
    await repeatIsOn(page).waitFor({ timeout: 5_000 });
    for (let i = 0; i < 10; i++) {
        await page.keyboard.press('ArrowDown');
    }
    await page.waitForTimeout(1_000);
    await page.keyboard.press('c');
    await page.locator('#main-window.pause').waitFor({ timeout: 5_000 });
    const volume = await page.locator('#main-window #volume input').inputValue();
    assert.notEqual(volume, '78');
    // The changes are written once they stop for a second.
    await page.waitForTimeout(2_500);

    await page.reload();
    await page.locator('input[type=password]').fill('matrix123');
    await page.getByRole('button', { name: 'Start session' }).click();
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: 60_000 });
    await openApp(page, 'Webamp');

    // Same list, same track, paused where it was, same volume and repeat, same title.
    await page.locator('#playlist-window').getByText('Kazo Test - Tone 440').waitFor({ timeout: 30_000 });
    await page.locator('#main-window.pause').waitFor({ timeout: 15_000 });
    await repeatIsOn(page).waitFor({ timeout: 5_000 });
    assert.equal(await page.locator('#main-window #volume input').inputValue(), volume);
    await page.locator('button', { hasText: TONE_TITLE }).waitFor({ timeout: 15_000 });

    // Play continues from the saved position, not from the start.
    await page.locator('#main-window #play').click();
    await page.locator('#main-window #play').click();
    await page.locator('#main-window.play').waitFor({ timeout: 10_000 });
    await page.close();
});

test('Webamp\'s hotkeys are ignored while another window has the focus', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await playTone(page);
    await focusWebamp(page);
    await page.keyboard.press('r');
    await repeatIsOn(page).waitFor({ timeout: 5_000 });

    // Another app takes the focus: V (Webamp's Stop) does nothing to the music.
    await openApp(page, 'File Explorer');
    await page.locator('h2', { hasText: 'File Explorer' }).first().click();
    await page.keyboard.press('v');
    await page.waitForTimeout(1_000);
    assert.equal(await page.locator('#main-window.play').count(), 1);

    // Back in Webamp (its taskbar button), V stops it.
    await page.locator('button', { hasText: TONE_TITLE }).click();
    await page.keyboard.press('v');
    await page.locator('#main-window.stop').waitFor({ timeout: 5_000 });
    await page.close();
});

test('Milkdrop\'s Butterchurn is downloaded only when Milkdrop first opens', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const requested = [];
    page.on('request', request => requested.push(request.url()));
    await boot(page, host.baseUrl);
    await openApp(page, 'Webamp');
    await page.locator('#main-window').waitFor({ timeout: 30_000 });
    await page.waitForTimeout(2_000);
    assert.equal(requested.some(url => url.includes('butterchurn')), false);

    // Webamp's main menu (its top-left icon) ▸ Milkdrop. The first press on a Webamp window that is
    // not selected only selects it.
    const milkdropItem = page.getByText('Milkdrop', { exact: true }).first();
    // Static files are fingerprinted (butterchurn.min.<hash>.js).
    const butterchurn = page.waitForRequest(request => /butterchurn\/butterchurn\.min\./.test(request.url()), { timeout: 30_000 });
    await page.locator('#main-window #option').click();
    if (!await milkdropItem.isVisible()) {
        await page.locator('#main-window #option').click();
    }
    await milkdropItem.click();
    await butterchurn;
    // Both start together: the presets may already have been asked for.
    const deadline = Date.now() + 30_000;
    while (!requested.some(url => /butterchurn-presets\/base\.min\./.test(url)) && Date.now() < deadline) {
        await page.waitForTimeout(200);
    }
    assert.equal(requested.some(url => /butterchurn-presets\/base\.min\./.test(url)), true);
    await page.close();
});
