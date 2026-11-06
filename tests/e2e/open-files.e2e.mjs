// End-to-end runs of work item 2 (opening music from the OS) in the Kazo OS host:
// `npm run test:e2e` after `dotnet build Kazo.Webamp.slnx -c Release`.
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { TONE, TONE_220, boot, explorerRow, launchOptions, openFileMenu, runInTerminal, startHost, uploadToHome } from './host.mjs';

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

/** The titles of Webamp's list, in order, and the index (from 0) of the current track. */
async function playlist(page) {
    const cells = page.locator('#playlist-window .playlist-track-titles .track-cell');
    const titles = (await cells.allInnerTexts()).map(text => text.replace(/^\d+\.\s*/, '').trim());
    const classes = await cells.evaluateAll(nodes => nodes.map(node => node.className));
    return { titles, current: classes.findIndex(name => /\bcurrent\b/.test(name)) };
}

/** Waits until Webamp's list has `count` tracks with their tags read. */
async function waitForTracks(page, count) {
    await page.waitForFunction(expected => {
        const cells = [...document.querySelectorAll('#playlist-window .playlist-track-titles .track-cell')];
        return cells.length === expected && cells.every(cell => /Kazo Test - Tone/.test(cell.textContent));
    }, count, { timeout: 30_000 });
}

const newPage = async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    const pickers = [];
    page.on('filechooser', chooser => pickers.push(chooser));
    await boot(page, host.baseUrl);
    return { page, pickers };
};

test('a double-click on an .mp3 plays it in Webamp; two more opened at once are added in order and the first plays', { timeout: 300_000 }, async () => {
    const { page } = await newPage();
    await uploadToHome(page, [TONE, TONE_220]);

    // Decision 4: Webamp is the app of .mp3 files.
    await explorerRow(page, '/home/neo/tone.mp3').dblclick({ position: { x: 40, y: 10 } });
    await page.locator('#main-window.play').waitFor({ timeout: 60_000 });
    await waitForTracks(page, 1);
    assert.deepEqual(await playlist(page), { titles: ['Kazo Test - Tone 440'], current: 0 });

    // Decision 5: one window; the explorer opens a selection one file at a time, Webamp adds it whole.
    await openFileMenu(page, ['/home/neo/tone-220.mp3', '/home/neo/tone.mp3']);
    await page.getByRole('menuitem', { name: 'Open', exact: true }).click();
    await waitForTracks(page, 3);
    assert.deepEqual(await playlist(page), { titles: ['Kazo Test - Tone 440', 'Kazo Test - Tone 220', 'Kazo Test - Tone 440'], current: 1 });
    assert.equal(await page.locator('.webamp-surface').count(), 1);
    await page.close();
});

test('the file menu adds a folder to Webamp\'s list without playing, or plays it, its files sorted by path', { timeout: 300_000 }, async () => {
    const { page } = await newPage();
    await uploadToHome(page, TONE);
    // Two tracks in ~/Documents, sent in the opposite order of their names.
    await explorerRow(page, '/home/neo/Documents').dblclick({ position: { x: 40, y: 10 } });
    await page.getByText('This folder is empty.').waitFor({ timeout: 30_000 });
    await uploadToHome(page, [TONE, TONE_220], { openExplorer: false });
    await page.getByRole('button', { name: /up|parent/i }).first().click();
    await explorerRow(page, '/home/neo/Documents').waitFor();

    await openFileMenu(page, ['/home/neo/Documents']);
    await page.getByRole('menuitem', { name: 'Add to Webamp\'s list' }).click();
    await waitForTracks(page, 2);
    assert.deepEqual(await playlist(page), { titles: ['Kazo Test - Tone 220', 'Kazo Test - Tone 440'], current: -1 });
    assert.equal(await page.locator('#main-window.play').count(), 0);

    await openFileMenu(page, ['/home/neo/tone.mp3']);
    await page.getByRole('menuitem', { name: 'Play in Webamp' }).click();
    await waitForTracks(page, 3);
    await page.locator('#main-window.play').waitFor({ timeout: 15_000 });
    assert.equal((await playlist(page)).current, 2);
    await page.close();
});

test('Webamp\'s eject and Add file show the OS dialog, never the browser\'s picker', { timeout: 300_000 }, async () => {
    const { page, pickers } = await newPage();
    await uploadToHome(page, [TONE, TONE_220]);
    await runInTerminal(page, 'launch ca.kazo.webamp /home/neo/tone.mp3');
    await waitForTracks(page, 1);

    // Eject (Webamp's "Open"): the list is replaced by the chosen file, which plays.
    await page.locator('#main-window').click({ position: { x: 140, y: 6 } });
    await page.locator('#main-window #eject').click();
    const open = page.getByRole('dialog', { name: 'Open in Webamp' }).last();
    await open.waitFor({ timeout: 15_000 });
    await explorerRow(open, '/home/neo/tone-220.mp3').click();
    await open.getByRole('button', { name: 'Open', exact: true }).click();
    await page.waitForFunction(() => /Tone 220/.test(document.querySelector('#playlist-window .playlist-track-titles')?.textContent ?? ''), null, { timeout: 30_000 });
    await waitForTracks(page, 1);
    assert.deepEqual(await playlist(page), { titles: ['Kazo Test - Tone 220'], current: 0 });

    // ADD ▸ Add file: added at the end.
    await page.locator('#playlist-window').click({ position: { x: 140, y: 6 } });
    await page.locator('#playlist-add-menu').click();
    await page.locator('#playlist-add-menu .add-file').click();
    const add = page.getByRole('dialog', { name: 'Add files to Webamp' }).last();
    await add.waitFor({ timeout: 15_000 });
    await explorerRow(add, '/home/neo/tone.mp3').click();
    await add.getByRole('button', { name: 'Open', exact: true }).click();
    await waitForTracks(page, 2);
    assert.deepEqual((await playlist(page)).titles, ['Kazo Test - Tone 220', 'Kazo Test - Tone 440']);

    assert.equal(pickers.length, 0, 'the browser\'s file picker never opened');
    await page.close();
});
