// Webamp as the Kazo OS Store installs it: `npm run test:installed -- <Kazo OS wwwroot> <packages>`.
//
// <Kazo OS wwwroot>: a published Kazo OS (for example the output of
//   external/KazoOs/wasm2/KazoOs/build/publish-trimmed.sh, artifacts/publish-trimmed/publish/wwwroot),
//   the trimmed OS users run.
// <packages>: the folder `dotnet build src/Kazo.Webamp -c Release -t:KazoOsPack` writes
//   (default src/Kazo.Webamp/bin/kazoos-packages), the content of the kazoos-packages branch.
//
// The run serves the OS, answers the GitHub addresses of this repository's packages from <packages>,
// installs Webamp with `sudo kazopkg install` (its API check against the trimmed runtime included),
// double-clicks an .mp3 in the File Explorer (the installed app's file types), checks it plays with
// the app's scoped CSS linked, and opens Milkdrop (Butterchurn and its presets from the package).
// Not part of CI: it needs a Kazo OS publish (about ten minutes to build).
import { createServer } from 'node:http';
import { readFile, stat } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';
import { TONE, explorerRow, launchOptions, uploadToHome } from '../e2e/host.mjs';

const [wwwroot, packages] = process.argv.slice(2).map(argument => argument && path.resolve(argument));
if (!wwwroot || !packages) {
    console.error('usage: npm run test:installed -- <Kazo OS wwwroot> <packages folder>');
    process.exit(2);
}

const REPOSITORY = 'Kazo-ca/Os-App-Webamp';
const LOGIN = 'neo';
const PASSWORD = 'matrix123';
const STEP_MS = 120_000;
const TYPES = {
    '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript', '.css': 'text/css',
    '.json': 'application/json', '.wasm': 'application/wasm', '.svg': 'image/svg+xml', '.png': 'image/png',
    '.webmanifest': 'application/manifest+json', '.dat': 'application/octet-stream', '.blat': 'application/octet-stream',
};

const log = message => console.log(`[installed] ${message}`);

// A plain static server, as a release host is: no COOP/COEP, no cache.
const server = createServer(async (request, response) => {
    const relative = decodeURIComponent(new URL(request.url, 'http://host').pathname);
    let file = path.join(wwwroot, relative);
    if (!file.startsWith(wwwroot)) {
        response.writeHead(403).end();
        return;
    }
    try {
        if ((await stat(file)).isDirectory()) file = path.join(file, 'index.html');
        const body = await readFile(file);
        response.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] ?? 'application/octet-stream', 'Cache-Control': 'no-store' });
        response.end(body);
    } catch {
        response.writeHead(404).end();
    }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const baseUrl = `http://127.0.0.1:${server.address().port}/`;

const waitForText = async (locator, predicate, what) => {
    const deadline = Date.now() + STEP_MS;
    while (Date.now() < deadline) {
        const text = (await locator.innerText()).replace(/ /g, ' ');
        if (predicate(text)) return text;
        await new Promise(resolve => setTimeout(resolve, 250));
    }
    throw new Error(`timed out waiting for ${what}`);
};

const browser = await chromium.launch(launchOptions);
const errors = [];
let page;
try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
    // raw.githubusercontent.com, as kazopkg reads it (ADR 0182 of Kazo OS), answered locally.
    await context.route(new RegExp(`^https://raw\\.githubusercontent\\.com/${REPOSITORY}/kazoos-packages/`), async route => {
        const relative = decodeURIComponent(new URL(route.request().url()).pathname).split('/kazoos-packages/')[1];
        const file = path.join(packages, relative);
        const headers = { 'Access-Control-Allow-Origin': '*' };
        try {
            if (!file.startsWith(packages)) throw new Error('outside the packages folder');
            await route.fulfill({ status: 200, body: await readFile(file), headers: { ...headers, 'Content-Type': 'application/octet-stream' } });
            log(`served ${relative}`);
        } catch {
            await route.fulfill({ status: 404, headers });
        }
    });
    page = await context.newPage();
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    page.on('pageerror', error => errors.push(String(error)));

    // First boot: the administrator is created.
    await page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    await page.getByRole('heading', { name: 'Create administrator' }).waitFor({ timeout: 180_000 });
    await page.getByLabel('Login name').fill(LOGIN);
    await page.getByLabel('Display name').fill('Neo');
    await page.getByLabel('Password', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirm password').fill(PASSWORD);
    await page.getByRole('button', { name: 'Create local profile' }).click();
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: STEP_MS });
    const openApp = async name => {
        await page.getByRole('button', { name: 'App launcher' }).click({ timeout: STEP_MS });
        await page.getByRole('combobox', { name: 'Search applications' }).fill(name);
        await page.getByRole('button', { name: `Launch ${name}`, exact: true }).click();
    };

    // Install, as an administrator.
    await openApp('Terminal');
    const rows = page.locator('[data-terminal-panel] .xterm-rows').first();
    await waitForText(rows, text => text.includes('$'), 'the shell prompt');
    await page.locator('[data-terminal-panel] .xterm-screen').first().click();
    await page.keyboard.type(`sudo kazopkg install ${REPOSITORY}`);
    await page.keyboard.press('Enter');
    const prompt = page.locator('.credential-prompt-dialog, .aaa-window').first();
    await prompt.waitFor({ state: 'visible', timeout: STEP_MS });
    const login = prompt.locator('input[type=text]');
    if (await login.count() > 0 && !(await login.inputValue())) await login.fill(LOGIN);
    await prompt.locator('input[type=password]').fill(PASSWORD);
    await prompt.locator('input[type=password]').press('Enter');
    const output = await waitForText(rows, text => text.includes('available now') || /does not have|refused|error/i.test(text), 'the end of the installation');
    if (!output.includes('available now')) throw new Error(`kazopkg did not install Webamp:\n${output}`);
    log('kazopkg installed Webamp.');

    // Kazo OS 3.0.0 grants an app's declared capabilities at sign-in (CleanProfileCapabilityGrantSeeder
    // in LocalSessionService): an app installed during the session has none until the next one, and
    // reading the user's music is refused. Sign in again, as a user would after an install.
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.getByRole('button', { name: 'Start session' }).waitFor({ timeout: 180_000 });
    await page.locator('input[type=password]').fill(PASSWORD);
    await page.getByRole('button', { name: 'Start session' }).click();
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: STEP_MS });

    // A double-click on an .mp3: the installed app's file types make Webamp open and play it.
    await uploadToHome(page, TONE);
    await explorerRow(page, '/home/neo/tone.mp3').dblclick({ position: { x: 40, y: 10 } });
    await page.locator('#main-window.play').waitFor({ timeout: STEP_MS });
    await page.waitForFunction(() => /Kazo Test - Tone 440/.test(document.querySelector('#playlist-window .playlist-track-titles')?.textContent ?? ''), null, { timeout: STEP_MS });
    const linked = await page.evaluate(() => [...document.querySelectorAll('link[data-kazoos-installed-app]')].map(link => link.getAttribute('href')));
    if (!linked.some(href => href.endsWith('_content/Kazo.Webamp/Kazo.Webamp.bundle.scp.css'))) {
        throw new Error(`the scoped CSS of Webamp is not linked: ${JSON.stringify(linked)}`);
    }
    const overflow = await page.evaluate(() => getComputedStyle(document.querySelector('.webamp-surface')).overflow);
    if (overflow !== 'hidden') throw new Error(`the Webamp window is not styled (overflow: ${overflow})`);
    log('Webamp opened the .mp3, styled, and plays it.');

    // Milkdrop: Butterchurn and its presets come from the installed package.
    const butterchurn = page.waitForRequest(request => /_content\/Kazo\.Webamp\/lib\/butterchurn\//.test(request.url()), { timeout: STEP_MS });
    const item = page.getByText('Milkdrop', { exact: true }).first();
    await page.locator('#main-window #option').click();
    if (!await item.isVisible()) await page.locator('#main-window #option').click();
    await item.click();
    await butterchurn;
    await page.locator('#webamp .gen-window canvas').first().waitFor({ timeout: STEP_MS });
    log('Milkdrop opened with Butterchurn from the package.');

    if (errors.length > 0) throw new Error(`console errors: ${errors.join(' | ')}`);
    log('PASS');
} catch (error) {
    console.error(`[installed] FAIL: ${error.message}`);
    await page?.screenshot({ path: 'installed-failure.png' }).catch(() => {});
    process.exitCode = 1;
} finally {
    await browser.close();
    server.close();
}
