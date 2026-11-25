// Test harness of the end-to-end runs. Two ways to run Kazo OS:
// - On the published trimmed OS (Kazo OS ADR 0281 §5): when the CI sets KAZOOS_E2E_WWWROOT,
//   KAZOOS_E2E_PACKAGES and KAZOOS_E2E_REPOSITORY, the OS is the release users load and Webamp is
//   installed in each fresh profile with `sudo kazopkg install`, through the shared harness of the
//   Kazo OS submodule (external/KazoOs/wasm2/KazoOs/build/app-e2e/kazoos-app-e2e.mjs).
// - Otherwise: the Webamp host (src/Kazo.Webamp.Os, built beforehand with `dotnet build -c Release`,
//   or the configuration named by WEBAMP_CONFIGURATION) on a free port, or WEBAMP_BASE_URL when it
//   points to one already running. This is the fast local loop.
// Either way, boot starts a fresh Kazo OS profile with Webamp in it.
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { createServer } from 'node:net';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const hostProject = path.join(root, 'src', 'Kazo.Webamp.Os');
const sharedHarness = path.join(root, 'external', 'KazoOs', 'wasm2', 'KazoOs', 'build', 'app-e2e', 'kazoos-app-e2e.mjs');

/** The test track: four seconds of a 440 Hz tone, tagged "Kazo Test - Tone 440" (fixtures/make-tone.sh). */
export const TONE = path.join(root, 'tests', 'e2e', 'fixtures', 'tone.mp3');

/** A second track, told apart by its title: "Kazo Test - Tone 220" (fixtures/make-tone.sh). */
export const TONE_220 = path.join(root, 'tests', 'e2e', 'fixtures', 'tone-220.mp3');

/** A .m3u next to the tone: the tone, a missing file and a web address (fixtures/tones.m3u). */
export const TONES_LIST = path.join(root, 'tests', 'e2e', 'fixtures', 'tones.m3u');

// The first WebAssembly boot downloads and compiles the whole OS: give it time.
export const BOOT_TIMEOUT_MS = 120_000;

const freePort = () => new Promise((resolve, reject) => {
    const server = createServer();
    server.listen(0, '127.0.0.1', () => {
        const { port } = server.address();
        server.close(() => resolve(port));
    });
    server.on('error', reject);
});

const waitForHttp = async (url, timeoutMs) => {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        try {
            const response = await fetch(url);
            if (response.ok) {
                return;
            }
        } catch {
            // Not listening yet.
        }
        await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error(`${url} did not answer within ${timeoutMs} ms.`);
};

/**
 * The shared harness and its setup when the CI asks for the trimmed OS, null otherwise. The module
 * is imported only then: a Kazo OS submodule older than the harness (before 3.5.1) does not have it.
 * @returns {Promise<{ harness: object, setup: { wwwroot: string, packages: string, repository: string } } | null>}
 */
async function trimmedSetup() {
    const { KAZOOS_E2E_WWWROOT, KAZOOS_E2E_PACKAGES, KAZOOS_E2E_REPOSITORY } = process.env;
    if (!KAZOOS_E2E_WWWROOT || !KAZOOS_E2E_PACKAGES || !KAZOOS_E2E_REPOSITORY) {
        return null;
    }
    if (!existsSync(sharedHarness)) {
        throw new Error(`KAZOOS_E2E_* are set but the Kazo OS submodule has no shared harness (${sharedHarness}).`);
    }
    const harness = await import(pathToFileURL(sharedHarness).href);
    return { harness, setup: harness.fromEnvironment() };
}

// The trimmed setup of each running host, by address: boot finds it from the address alone.
const trimmedHosts = new Map();

/**
 * Starts the host: the published trimmed OS when the CI asks for it, else the Webamp host, or
 * WEBAMP_BASE_URL.
 * @returns {Promise<{ baseUrl: string, trimmed: boolean, stop: () => Promise<void> | void, assertNoFailures: () => void }>}
 *   assertNoFailures throws when a page showed an error that means the trimmed OS lacks something
 *   Webamp needs (a missing member, type or file): call it once the tests are done.
 */
export async function startHost() {
    const trimmed = await trimmedSetup();
    if (trimmed) {
        const os = await trimmed.harness.startTrimmedOs(trimmed.setup);
        const failures = []; // one list per page, filled while the page runs
        trimmedHosts.set(os.baseUrl, { ...trimmed, failures });
        return {
            baseUrl: os.baseUrl,
            trimmed: true,
            stop: async () => {
                trimmedHosts.delete(os.baseUrl);
                await os.stop();
            },
            assertNoFailures: () => {
                const seen = failures.flat();
                if (seen.length > 0) {
                    throw new Error(`Webamp failed on the trimmed Kazo OS:\n${seen.join('\n')}`);
                }
            },
        };
    }
    const none = () => {};
    if (process.env.WEBAMP_BASE_URL) {
        return { baseUrl: process.env.WEBAMP_BASE_URL, trimmed: false, stop: none, assertNoFailures: none };
    }
    const port = await freePort();
    const baseUrl = `http://127.0.0.1:${port}/`;
    const configuration = process.env.WEBAMP_CONFIGURATION ?? 'Release';
    const server = spawn('dotnet', ['run', '--no-build', '-c', configuration, '--urls', baseUrl], { cwd: hostProject, stdio: 'ignore' });
    await waitForHttp(baseUrl, 60_000);
    return { baseUrl, trimmed: false, stop: () => server.kill(), assertNoFailures: none };
}

/** Chromium for the runs: audio may start without a click (the terminal's keys start it). */
export const launchOptions = { args: ['--autoplay-policy=no-user-gesture-required'] };

/**
 * Boots Kazo OS in a fresh browser context (first boot: the administrator "neo" is created). On
 * the trimmed OS, Webamp is then installed with kazopkg, as the Store installs it; the page's
 * errors that mean a missing member or type go to the host's failures.
 * @param {import('playwright').Page} page A page of a new context.
 * @param {string} baseUrl The host's address.
 */
export async function boot(page, baseUrl) {
    const trimmed = trimmedHosts.get(baseUrl);
    if (trimmed) {
        const { harness, setup, failures } = trimmed;
        failures.push(harness.watchFailures(page));
        await harness.routePackages(page.context(), setup);
        await harness.bootFirstAccount(page, baseUrl);
        await harness.installApp(page, setup);
        return;
    }
    await page.goto(baseUrl);
    const inputs = page.locator('input');
    await inputs.first().waitFor({ timeout: BOOT_TIMEOUT_MS });
    await inputs.nth(0).fill('neo');
    await inputs.nth(1).fill('Neo');
    await inputs.nth(2).fill('matrix123');
    await inputs.nth(3).fill('matrix123');
    await page.getByRole('button', { name: 'Create local profile' }).click();
    // The welcome wizard (Kazo OS ADR 0276, from 3.4.0) shows once per account: skip it when
    // this Kazo OS version has it.
    const skip = page.locator('.welcome-skip');
    await skip.or(page.getByText('Applications', { exact: true })).first().waitFor({ timeout: 60_000 });
    if (await skip.isVisible()) {
        await skip.click();
    }
    await page.getByText('Applications', { exact: true }).waitFor({ timeout: 60_000 });
}

/**
 * Opens an app from the Applications menu.
 * @param {import('playwright').Page} page The page.
 * @param {string} name The app's name in the menu.
 */
export async function openApp(page, name) {
    await page.getByText('Applications', { exact: true }).click();
    await page.getByText(name, { exact: true }).first().click();
}

/**
 * Copies files of the computer into ~ (/home/neo) with the File Explorer's Upload button, or into
 * the folder the explorer shows when it is already open.
 * @param {import('playwright').Page} page The page.
 * @param {string | string[]} files The files on the computer.
 * @param {{ openExplorer?: boolean }} [options] `openExplorer: false` uses the explorer already open.
 */
export async function uploadToHome(page, files, { openExplorer = true } = {}) {
    if (openExplorer) {
        await openApp(page, 'File Explorer');
        // The explorer lists ~ first; an upload sent before that may be lost.
        await page.locator('tr[data-item-path="/home/neo/Documents"]').first().waitFor({ timeout: 30_000 });
    }
    const list = Array.isArray(files) ? files : [files];
    const input = page.locator('input[type=file]').first();
    await input.waitFor({ state: 'attached', timeout: 30_000 });
    // The input is in the page before the explorer has listed the folder and wired its change
    // handler: files set that early are lost. Set them again if they still do not show.
    for (let attempt = 0; ; attempt++) {
        await input.setInputFiles(list);
        try {
            for (const file of list) {
                await page.getByText(path.basename(file), { exact: true }).first().waitFor({ timeout: 15_000 });
            }
            return;
        } catch (error) {
            if (attempt === 1) {
                throw error;
            }
        }
    }
}

/**
 * A row of the File Explorer (or of an OS file dialog) by its path.
 * @param {import('playwright').Page | import('playwright').Locator} scope The page or a dialog.
 * @param {string} itemPath The item's absolute path.
 */
export const explorerRow = (scope, itemPath) => scope.locator(`tr[data-item-path="${itemPath}"]`).first();

// Webamp's window covers the left of the explorer's rows: rows are pressed on their right side.
const ROW_RIGHT = { x: 420, y: 10 };

/**
 * Selects rows of the File Explorer (Ctrl for the next ones) and opens the file menu from the
 * keyboard (Shift+F10), which keeps the selection.
 * @param {import('playwright').Page} page The page.
 * @param {string[]} paths The items, in the order they are selected.
 */
export async function openFileMenu(page, paths) {
    // A first press on a window behind Webamp only brings it to the front: press its status bar first.
    await page.getByText(/\d+ item\(s\)/).last().click();
    for (const [index, itemPath] of paths.entries()) {
        await explorerRow(page, itemPath).click({ position: ROW_RIGHT, modifiers: index === 0 ? [] : ['Control'] });
    }
    await page.getByText(new RegExp(`\\| ${paths.length} selected`)).last().waitFor({ timeout: 10_000 });
    await page.keyboard.press('Shift+F10');
    await page.getByRole('menu').first().waitFor({ timeout: 10_000 });
}

/**
 * Types one command in a new Terminal window.
 * @param {import('playwright').Page} page The page.
 * @param {string} command The command line.
 */
export async function runInTerminal(page, command) {
    await openApp(page, 'Terminal');
    const input = page.locator('.xterm-helper-textarea').first();
    await input.waitFor({ state: 'attached', timeout: 30_000 });
    // The shell prints its banner and prompt first.
    await page.locator('.xterm-rows').getByText('neo@', { exact: false }).first().waitFor({ timeout: 30_000 });
    await input.focus();
    await page.keyboard.type(`${command}\n`, { delay: 5 });
}
