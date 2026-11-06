// End-to-end runs of the Webamp app inside the Kazo OS host: `npm run test:e2e` after
// `dotnet build Kazo.Webamp.slnx -c Release`. Each test boots a fresh browser profile.
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

// Webamp's taskbar button: the OS window is there while it shows.
const taskbarButton = page => page.locator('button', { hasText: /^\s*Webamp\s*$/ });

test('Webamp opens from the Applications menu inside its OS window, without Add URL', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await boot(page, host.baseUrl);
    await openApp(page, 'Webamp');

    // Its three windows, stacked, inside the OS window's content.
    for (const id of ['#main-window', '#equalizer-window', '#playlist-window']) {
        await page.locator(id).waitFor({ timeout: 30_000 });
    }
    const surface = await page.locator('.webamp-surface').boundingBox();
    const playlist = await page.locator('#playlist-window').boundingBox();
    const main = await page.locator('#main-window').boundingBox();
    assert.ok(main.y >= surface.y && playlist.y + playlist.height <= surface.y + surface.height + 1, 'the three windows fit in the OS window');
    assert.equal(main.width, 275);

    // Decision 9: no music from the internet in the first version. The first press on a Webamp
    // window only selects it (Webamp brings it to the front), the second opens the ADD menu.
    await page.locator('#playlist-window').click({ position: { x: 140, y: 6 } });
    await page.locator('#playlist-add-menu').click();
    await page.locator('#playlist-add-menu .add-file').waitFor({ timeout: 10_000 });
    assert.equal(await page.locator('#playlist-add-menu .add-dir').isVisible(), true);
    assert.equal(await page.locator('#playlist-add-menu .add-url').isVisible(), false);
    assert.deepEqual(errors, []);
    await page.close();
});

test('an .mp3 of the user opened with Webamp plays, and Webamp\'s close button closes the OS window', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await uploadToHome(page, TONE);

    // LaunchFile: the file given when the app starts (a double-click once work item 2 adds the type).
    await runInTerminal(page, 'launch ca.kazo.webamp /home/neo/tone.mp3');

    // Read through KazoFile, handed over as a Blob, its tags read by Webamp, and playing.
    await page.locator('#main-window.play').waitFor({ timeout: 30_000 });
    await page.locator('#playlist-window').getByText('Kazo Test - Tone 440').waitFor({ timeout: 15_000 });
    assert.match(await page.locator('#playlist-window').innerText(), /0:04/);
    assert.equal(await taskbarButton(page).count(), 1);

    await page.locator('#main-window #close').click();
    await page.locator('#webamp').waitFor({ state: 'detached', timeout: 15_000 });
    await taskbarButton(page).waitFor({ state: 'detached', timeout: 15_000 });
    await page.close();
});

test('a file that cannot be read says why and Webamp stays open', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await runInTerminal(page, 'launch ca.kazo.webamp /home/neo/missing.mp3');

    const status = page.locator('.webamp-window .app-status', { hasText: 'missing.mp3' });
    await status.waitFor({ timeout: 30_000 });
    assert.equal(await status.getAttribute('role'), 'alert');
    await page.locator('#main-window').waitFor();
    assert.equal(await page.locator('#playlist-window .track-cell').count(), 0);
    await page.close();
});
