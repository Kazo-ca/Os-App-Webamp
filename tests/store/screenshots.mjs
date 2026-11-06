// Screenshots of the Kazo OS Store listing (store/screenshots/N.png), taken in the Webamp host:
// `npm run store:screenshots` after `dotnet build Kazo.Webamp.slnx -c Release`.
//
// Each scene boots a fresh Kazo OS profile, puts the test tones in ~ and opens them the way a user
// would (double-click, file menu). The tones are this repository's own (tests/e2e/fixtures); Webamp
// shows its default skin (the Winamp 2.91 base skin, see THIRD-PARTY-NOTICES.md). Not part of CI:
// the screenshots are checked in, and taken again when the app's look changes.
import { mkdir } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { chromium } from 'playwright';
import { TONE, TONE_220, boot, explorerRow, launchOptions, openFileMenu, startHost, uploadToHome } from '../e2e/host.mjs';

const output = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', 'store', 'screenshots');
const VIEWPORT = { width: 1280, height: 800 };

/** Waits until Webamp's list shows `count` tracks with their tags read. */
async function waitForTracks(page, count) {
    await page.waitForFunction(expected => {
        const cells = [...document.querySelectorAll('#playlist-window .playlist-track-titles .track-cell')];
        return cells.length === expected && cells.every(cell => /Kazo Test - Tone/.test(cell.textContent));
    }, count, { timeout: 30_000 });
}

/** A booted desktop with the two tones in ~, the File Explorer open on it. */
async function desktop(browser, baseUrl) {
    const page = await browser.newPage({ viewport: VIEWPORT });
    await boot(page, baseUrl);
    await uploadToHome(page, [TONE, TONE_220]);
    return page;
}

/** Drags an OS window by its title bar to (x, y). */
async function moveWindow(page, title, x, y) {
    const bar = page.locator('h2', { hasText: title }).first();
    const box = await bar.boundingBox();
    await page.mouse.move(box.x + 20, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(x, y, { steps: 10 });
    await page.mouse.up();
}

/** Opens Milkdrop from Webamp's main menu (the first press may only select the window). */
async function openMilkdrop(page) {
    const item = page.getByText('Milkdrop', { exact: true }).first();
    await page.locator('#main-window #option').click();
    if (!await item.isVisible()) {
        await page.locator('#main-window #option').click();
    }
    await item.click();
    await page.locator('#webamp .gen-window').first().waitFor({ timeout: 30_000 });
}

const scenes = [
    // 1. Music opened from the File Explorer: the file menu's Webamp entries, Webamp playing.
    async page => {
        await explorerRow(page, '/home/neo/tone.mp3').dblclick({ position: { x: 40, y: 10 } });
        await page.locator('#main-window.play').waitFor({ timeout: 60_000 });
        await waitForTracks(page, 1);
        await moveWindow(page, /- Webamp$/, 900, 120);
        await openFileMenu(page, ['/home/neo/tone-220.mp3']);
        await page.getByRole('menuitem', { name: 'Play in Webamp' }).hover();
    },
    // 2. A list of two tracks played from the file menu, the equalizer set.
    async page => {
        await openFileMenu(page, ['/home/neo/tone-220.mp3', '/home/neo/tone.mp3']);
        await page.getByRole('menuitem', { name: 'Play in Webamp' }).click();
        await page.locator('#main-window.play').waitFor({ timeout: 60_000 });
        await waitForTracks(page, 2);
        await moveWindow(page, /- Webamp$/, 700, 160);
        const bands = page.locator('#equalizer-window .band');
        const count = await bands.count();
        for (let i = 0; i < count; i++) {
            const box = await bands.nth(i).boundingBox();
            if (box == null) {
                continue;
            }
            const lift = Math.sin((i / Math.max(1, count - 1)) * Math.PI) * 0.35;
            await page.mouse.click(box.x + box.width / 2, box.y + box.height * (0.5 - lift));
        }
    },
    // 3. Milkdrop, downloaded when its window first opens.
    async page => {
        await explorerRow(page, '/home/neo/tone.mp3').dblclick({ position: { x: 40, y: 10 } });
        await page.locator('#main-window.play').waitFor({ timeout: 60_000 });
        await page.locator('#main-window #repeat').click();
        await page.locator('#main-window #repeat').click();
        await openMilkdrop(page);
        await moveWindow(page, /- Webamp$/, 700, 100);
        await page.waitForTimeout(5_000);
    },
];

const host = await startHost();
const browser = await chromium.launch(launchOptions);
try {
    await mkdir(output, { recursive: true });
    for (const [index, scene] of scenes.entries()) {
        const page = await desktop(browser, host.baseUrl);
        await scene(page);
        const file = path.join(output, `${index + 1}.png`);
        await page.screenshot({ path: file });
        console.log(`[screenshots] ${file}`);
        await page.close();
    }
} finally {
    await browser.close();
    host.stop();
}
