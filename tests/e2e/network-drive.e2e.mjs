// End-to-end runs of work item 7 (music on a network drive, ADR 0231 of Kazo OS) and of Webamp's
// minimize button (issue Kazo-ca/Os#384) in the Kazo OS host: `npm run test:e2e` after
// `dotnet build Kazo.Webamp.slnx -c Release`.
//
// A network drive is a folder of the computer mounted at /mnt/<name> (ADR 0181 of Kazo OS). The
// browser's folder dialog cannot be driven by a test, so the run uses the OS's test hook:
// globalThis.__kazoOsPickHostFolder hands `mount -t hostfs` an origin private folder
// (navigator.storage.getDirectory()), which has the same interface as a folder of the computer.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { chromium } from 'playwright';
import { TONE, TONE_220, boot, launchOptions, runInTerminal, startHost } from './host.mjs';

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

// A list that names its track relative to itself, as a list saved on another computer does.
const MIX = '#EXTM3U\n#EXTINF:4,Kazo Test - Tone 220\nalbum/tone-220.mp3\n';

/**
 * Fills an origin private folder with music and makes it the folder the next `mount -t hostfs`
 * "chooses": tone.mp3, mix.m3u and album/{tone.mp3, tone-220.mp3}.
 * @param {import('playwright').Page} page The page, booted.
 */
async function prepareDrive(page) {
    const files = {
        'tone.mp3': (await readFile(TONE)).toString('base64'),
        'mix.m3u': Buffer.from(MIX).toString('base64'),
        'album/tone.mp3': (await readFile(TONE)).toString('base64'),
        'album/tone-220.mp3': (await readFile(TONE_220)).toString('base64'),
    };
    await page.evaluate(async entries => {
        const root = await navigator.storage.getDirectory();
        const drive = await root.getDirectoryHandle('webamp-network-drive', { create: true });
        for (const [name, base64] of Object.entries(entries)) {
            const parts = name.split('/');
            let folder = drive;
            for (const part of parts.slice(0, -1)) {
                folder = await folder.getDirectoryHandle(part, { create: true });
            }
            const file = await folder.getFileHandle(parts.at(-1), { create: true });
            const writable = await file.createWritable();
            await writable.write(Uint8Array.from(atob(base64), character => character.charCodeAt(0)));
            await writable.close();
        }
        globalThis.__kazoOsPickHostFolder = async () => drive;
    }, files);
}

/**
 * Mounts the prepared folder at /mnt/music from a new Terminal window and waits until `ls` lists it.
 * @param {import('playwright').Page} page The page, booted, with prepareDrive done.
 */
async function mountDrive(page) {
    await runInTerminal(page, 'mount -t hostfs /mnt/music');
    await typeInTerminal(page, 'ls /mnt/music');
    await page.locator('.xterm-rows').getByText('mix.m3u', { exact: false }).first().waitFor({ timeout: 30_000 });
}

/** Types one more command in the Terminal window already open. */
async function typeInTerminal(page, command) {
    const input = page.locator('.xterm-helper-textarea').first();
    await input.focus();
    await page.keyboard.type(`${command}\n`, { delay: 5 });
}

/** The titles of Webamp's list, in order. */
async function titles(page) {
    const cells = await page.locator('#playlist-window .playlist-track-titles .track-cell').allInnerTexts();
    return cells.map(text => text.replace(/^\d+\.\s*/, '').trim());
}

/** Waits until Webamp's list has `count` tracks with their tags read. */
async function waitForTracks(page, count) {
    await page.waitForFunction(expected => {
        const cells = [...document.querySelectorAll('#playlist-window .playlist-track-titles .track-cell')];
        return cells.length === expected && cells.every(cell => /Kazo Test - Tone/.test(cell.textContent));
    }, count, { timeout: 30_000 });
}

test('a track, a list and a folder on a network drive play in Webamp', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await prepareDrive(page);
    await mountDrive(page);

    // A track: before ADR 0231 an app with only filesystem.user-home.read got "Permission denied".
    await typeInTerminal(page, 'launch ca.kazo.webamp /mnt/music/tone.mp3');
    await page.locator('#main-window.play').waitFor({ timeout: 30_000 });
    await waitForTracks(page, 1);
    assert.deepEqual(await titles(page), ['Kazo Test - Tone 440']);

    // A list on the drive: its relative path is read from the list's own folder.
    await typeInTerminal(page, 'launch ca.kazo.webamp /mnt/music/mix.m3u');
    await waitForTracks(page, 2);
    assert.deepEqual(await titles(page), ['Kazo Test - Tone 440', 'Kazo Test - Tone 220']);

    // A folder on the drive: its music, sorted by path.
    await typeInTerminal(page, 'launch ca.kazo.webamp /mnt/music/album');
    await waitForTracks(page, 4);
    assert.deepEqual(await titles(page), ['Kazo Test - Tone 440', 'Kazo Test - Tone 220', 'Kazo Test - Tone 220', 'Kazo Test - Tone 440']);
    assert.equal(await page.locator('.webamp-window .app-status').count(), 0, 'no error message');
    await page.close();
});

test('Webamp\'s minimize button minimizes the OS window and the music keeps playing', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await prepareDrive(page);
    await mountDrive(page);
    await typeInTerminal(page, 'launch ca.kazo.webamp /mnt/music/tone.mp3');
    await page.locator('#main-window.play').waitFor({ timeout: 30_000 });

    // The first press on a Webamp window only selects it; the second presses the button.
    await page.locator('#main-window #title-bar').click({ position: { x: 100, y: 6 } });
    await page.locator('#main-window #minimize').click();
    const item = page.locator('.taskbar-item.is-minimized', { hasText: /Webamp/ });
    await item.waitFor({ timeout: 15_000 });
    await page.locator('#main-window').waitFor({ state: 'hidden', timeout: 15_000 });
    assert.equal(await page.locator('#main-window.play').count(), 1, 'still playing');

    // The taskbar button brings it back.
    await item.click();
    await page.locator('#main-window').waitFor({ state: 'visible', timeout: 15_000 });
    assert.equal(await page.locator('.taskbar-item.is-minimized', { hasText: /Webamp/ }).count(), 0);
    await page.close();
});
