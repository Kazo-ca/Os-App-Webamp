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
 * Copies a file of the computer into ~ (/home/neo) with the File Explorer's Upload button.
 * @param {import('playwright').Page} page The page.
 * @param {string} file The file on the computer.
 */
export async function uploadToHome(page, file) {
    await openApp(page, 'File Explorer');
    const input = page.locator('input[type=file]').first();
    await input.waitFor({ state: 'attached', timeout: 30_000 });
    await input.setInputFiles(file);
    await page.getByText(path.basename(file), { exact: true }).first().waitFor({ timeout: 30_000 });
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
