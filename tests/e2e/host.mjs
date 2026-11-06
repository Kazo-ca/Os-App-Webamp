// Test harness of the end-to-end runs: starts the Webamp host (src/Kazo.Webamp.Os, built beforehand
// with `dotnet build -c Release`, or the configuration named by WEBAMP_CONFIGURATION) on a free port,
// or uses WEBAMP_BASE_URL when it points to one already running, and boots a fresh Kazo OS profile.
import { spawn } from 'node:child_process';
import { createServer } from 'node:net';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const hostProject = path.join(root, 'src', 'Kazo.Webamp.Os');

/** The test track: four seconds of a 440 Hz tone, tagged "Kazo Test - Tone 440" (fixtures/make-tone.sh). */
export const TONE = path.join(root, 'tests', 'e2e', 'fixtures', 'tone.mp3');

/** A second track, told apart by its title: "Kazo Test - Tone 220" (fixtures/make-tone.sh). */
export const TONE_220 = path.join(root, 'tests', 'e2e', 'fixtures', 'tone-220.mp3');

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
 * Starts the host, or reuses WEBAMP_BASE_URL.
 * @returns {Promise<{ baseUrl: string, stop: () => void }>}
 */
export async function startHost() {
    if (process.env.WEBAMP_BASE_URL) {
        return { baseUrl: process.env.WEBAMP_BASE_URL, stop: () => {} };
    }
    const port = await freePort();
    const baseUrl = `http://127.0.0.1:${port}/`;
    const configuration = process.env.WEBAMP_CONFIGURATION ?? 'Release';
    const server = spawn('dotnet', ['run', '--no-build', '-c', configuration, '--urls', baseUrl], { cwd: hostProject, stdio: 'ignore' });
    await waitForHttp(baseUrl, 60_000);
    return { baseUrl, stop: () => server.kill() };
}

/** Chromium for the runs: audio may start without a click (the terminal's keys start it). */
export const launchOptions = { args: ['--autoplay-policy=no-user-gesture-required'] };

/**
 * Boots Kazo OS in a fresh browser context (first boot: the administrator "neo" is created).
 * @param {import('playwright').Page} page A page of a new context.
 * @param {string} baseUrl The host's address.
 */
export async function boot(page, baseUrl) {
    await page.goto(baseUrl);
    const inputs = page.locator('input');
    await inputs.first().waitFor({ timeout: BOOT_TIMEOUT_MS });
    await inputs.nth(0).fill('neo');
    await inputs.nth(1).fill('Neo');
    await inputs.nth(2).fill('matrix123');
    await inputs.nth(3).fill('matrix123');
    await page.getByRole('button', { name: 'Create local profile' }).click();
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
    await input.setInputFiles(list);
    for (const file of list) {
        await page.getByText(path.basename(file), { exact: true }).first().waitFor({ timeout: 30_000 });
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
 * Selects rows of the File Explorer (Ctrl for the next ones) and opens the file menu on the last one.
 * @param {import('playwright').Page} page The page.
 * @param {string[]} paths The items, in the order they are selected.
 */
export async function openFileMenu(page, paths) {
    for (const [index, itemPath] of paths.entries()) {
        await explorerRow(page, itemPath).click({ position: ROW_RIGHT, modifiers: index === 0 ? [] : ['Control'] });
    }
    await explorerRow(page, paths.at(-1)).click({ position: ROW_RIGHT, button: 'right' });
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
