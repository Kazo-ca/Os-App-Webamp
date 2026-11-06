// End-to-end runs of the skins (work item 4) inside the Kazo OS host: `npm run test:e2e` after
// `dotnet build Kazo.Webamp.slnx -c Release`. The skins are made by the test (fixtures/make-skin.mjs).
import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { boot, launchOptions, runInTerminal, startHost, uploadToHome } from './host.mjs';
import { makeSkins } from './fixtures/make-skin.mjs';

let host;
let browser;
let skins;

before(async () => {
    host = await startHost();
    browser = await chromium.launch(launchOptions);
    skins = makeSkins('Green.wsz', [0, 200, 0]);
});

after(async () => {
    await browser?.close();
    host?.stop();
});

const TIP = 'To change the skin: double-click a .wsz file';

// The image Webamp draws its main window with: it changes with the skin.
const mainWindowImage = page => page.locator('#main-window').evaluate(element => getComputedStyle(element).backgroundImage);

const waitForOtherImage = (page, before) => page.waitForFunction(
    previous => {
        const element = document.querySelector('#main-window');
        return element && getComputedStyle(element).backgroundImage !== previous;
    },
    before,
    { timeout: 30_000 });

// Webamp's context menu (a right-click on its windows), opened on its Skins submenu.
async function openSkinsMenu(page) {
    await page.locator('#main-window').click({ button: 'right', position: { x: 150, y: 60 } });
    // The submenu whose first item is "Load Skin..." (submenus nest, so match on the item itself).
    const skinsItem = page.locator(
        'xpath=//div[@id="webamp-context-menu"]//li[contains(@class, "parent")][ul/li[1][normalize-space(.)="Load Skin..."]]').locator('visible=true').first();
    await skinsItem.waitFor({ timeout: 10_000 });
    await skinsItem.hover();
    return skinsItem.locator('xpath=ul/li');
}

// Types a command in the Terminal already open (a second one from the menu would find the first
// one's title instead of the menu entry).
async function typeInTerminal(page, command) {
    await page.locator('.xterm-helper-textarea').first().focus();
    await page.keyboard.type(`${command}\n`, { delay: 5 });
}

async function startWebamp(page, { terminalOpen = false } = {}) {
    if (terminalOpen) {
        await typeInTerminal(page, 'launch ca.kazo.webamp');
    } else {
        await runInTerminal(page, 'launch ca.kazo.webamp');
    }
    await page.locator('#main-window').waitFor({ timeout: 30_000 });
    // Webamp has drawn its skin once its main window has an image.
    await page.waitForFunction(() => {
        const element = document.querySelector('#main-window');
        return element && getComputedStyle(element).backgroundImage.startsWith('url(');
    }, null, { timeout: 30_000 });
}

test('a .wsz opened in the OS is put on and listed in the Skins menu, and the import tip shows on the first launch', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await uploadToHome(page, skins.skin);

    await startWebamp(page);
    await page.getByText(TIP, { exact: false }).first().waitFor({ timeout: 15_000 });
    const baseImage = await mainWindowImage(page);

    // Webamp runs: the file reaches its window (one window per user).
    await typeInTerminal(page, 'launch ca.kazo.webamp /home/neo/Green.wsz');
    await waitForOtherImage(page, baseImage);
    assert.equal(await page.locator('#webamp').count(), 1);

    const items = await openSkinsMenu(page);
    assert.deepEqual((await items.allInnerTexts()).map(text => text.trim()).filter(Boolean), ['Load Skin...', '<Base Skin>', 'Green']);
    await page.close();
});

test('"Load Skin…" keeps the skin, refuses a broken one, and Webamp restarts with the skin in use', { timeout: 300_000 }, async () => {
    const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
    await boot(page, host.baseUrl);
    await startWebamp(page);
    const baseImage = await mainWindowImage(page);
    // A fresh profile: the tip shows on this first launch.
    await page.getByText(TIP, { exact: false }).first().waitFor({ timeout: 15_000 });
    const tips = await page.getByText(TIP, { exact: false }).count();

    // A skin of the computer, chosen with Webamp's own menu item.
    let items = await openSkinsMenu(page);
    let chooser = page.waitForEvent('filechooser', { timeout: 10_000 });
    await items.filter({ hasText: 'Load Skin...' }).click();
    await (await chooser).setFiles(skins.skin);
    await waitForOtherImage(page, baseImage);
    const greenImage = await mainWindowImage(page);
    await page.locator('#webamp-context-menu').getByText('Load Skin...').waitFor({ state: 'hidden', timeout: 10_000 });

    // A broken one: the app says why, and the skin in use stays.
    items = await openSkinsMenu(page);
    chooser = page.waitForEvent('filechooser', { timeout: 10_000 });
    await items.filter({ hasText: 'Load Skin...' }).click();
    await (await chooser).setFiles(skins.broken);
    await page.getByText('broken.wsz was not loaded', { exact: false }).waitFor({ timeout: 15_000 });
    assert.equal(await mainWindowImage(page), greenImage);
    await page.getByRole('button', { name: 'OK' }).last().click();

    // Closed and opened again: the skin is in the menu and on, and the tip is not shown again.
    await page.locator('#main-window').click({ position: { x: 150, y: 60 } });
    await page.locator('#main-window #close').click();
    await page.locator('#webamp').waitFor({ state: 'detached', timeout: 15_000 });
    await startWebamp(page, { terminalOpen: true });
    await page.waitForFunction(
        expected => getComputedStyle(document.querySelector('#main-window')).backgroundImage === expected,
        greenImage,
        { timeout: 30_000 });
    items = await openSkinsMenu(page);
    assert.ok((await items.allInnerTexts()).some(text => text.trim() === 'Green'));
    await page.waitForTimeout(3_000);
    assert.ok(await page.getByText(TIP, { exact: false }).count() <= tips, 'the tip is not posted a second time');
    await page.close();
});
