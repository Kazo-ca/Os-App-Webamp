// Webamp host module of the Webamp app (doc/en/architecture.md, "C# and JavaScript").
//
// The C# side (WebampPlayer, TrackLibrary) keeps the logic: which files are in the list, where they
// come from, what to play. This module only does what C# cannot reach: it creates the Webamp
// instance inside the app's window, turns the bytes of a file into a Blob address Webamp can play,
// gives that address back to the browser when the track leaves Webamp's list, reports Webamp's
// events (close, minimize, track change, list change, "Load list", "Save list", state change) to
// C#, and stops the browser's file picker that Webamp's "Open", "Add file" and "Add dir" would
// show, so C# shows the OS dialogs instead (decision 16, FilePromptInterceptor below).
//
// Work item 5 adds: reading and restoring what Webamp holds (state.json, decision 13), the Media
// Session (decision 8), Webamp's hotkeys only while the app's window has the focus (decision 17), and
// Milkdrop's Butterchurn loaded only when its window first opens (decision 12).
//
// It keeps no state outside one host object per window, so a test drives it with a fake Webamp
// class (tests/js/webamp-host.test.mjs). The skins (work item 4) go through the same host, with the
// Skins menu bridge of webamp-skins.js.

import { installSkinBridge } from './webamp-skins.js';

/** Address of the Webamp 2.3.1 bundle (JSZip and music-metadata included), next to this module. */
const WEBAMP_BUNDLE = new URL('../lib/webamp/webamp.bundle.min.mjs', import.meta.url).href;

/** Butterchurn 3.0.0-beta.5 (ES module) and its base presets (UMD, declares `self.base`). */
const BUTTERCHURN = new URL('../lib/butterchurn/butterchurn.min.js', import.meta.url).href;
const BUTTERCHURN_PRESETS = new URL('../lib/butterchurn-presets/base.min.js', import.meta.url).href;

/** Webamp's four windows, by their id in its store. */
export const WINDOW_IDS = ['main', 'equalizer', 'playlist', 'milkdrop'];

/** The bands of Winamp's equalizer, by their id in Webamp's store (Hz). */
export const EQ_BANDS = [60, 170, 310, 600, 1000, 3000, 6000, 12000, 14000, 16000];

/** While playing, the position is reported to C# once per this many seconds (a save at most that often). */
export const POSITION_STEP_SECONDS = 5;

/** How long a restored track may take to load before its position is given up (ms). */
export const RESUME_TIMEOUT_MS = 10000;

/**
 * Loads the Webamp class from the vendored bundle.
 * @returns {Promise<Function>} The Webamp constructor.
 */
export async function loadWebamp() {
    const module = await import(WEBAMP_BUNDLE);
    return module.default;
}

/** Loads Butterchurn, the Milkdrop renderer (2 MB with its presets): only when Milkdrop first opens. */
async function importButterchurn() {
    return import(BUTTERCHURN);
}

/**
 * Loads the base presets: the UMD file declares `self.base` (its `default` maps names to presets).
 * @returns {Promise<{ name: string, butterchurnPresetObject: object }[]>} Presets as Webamp takes them.
 */
async function importPresets() {
    await import(BUTTERCHURN_PRESETS);
    const base = globalThis.base;
    return Object.entries(base?.default ?? base ?? {})
        .map(([name, preset]) => ({ name, butterchurnPresetObject: preset }));
}

/**
 * Milkdrop without its cost (decision 12): Webamp asks for Butterchurn and the presets as soon as
 * it is created; the promises given here wait until `open()` is called, when Milkdrop's window
 * first opens.
 * @param {boolean} butterchurnOpen Whether Milkdrop's window is open at start (a restored layout).
 * @param {{ importButterchurn?: () => Promise<any>, importPresets?: () => Promise<object[]> }} [loaders] Replaced by the tests.
 * @returns {{ options: object, open: () => void, readonly requested: boolean }} Webamp's `__butterchurnOptions` and the gate.
 */
export function lazyButterchurn(butterchurnOpen, loaders = {}) {
    let release;
    const opened = new Promise(resolve => {
        release = resolve;
    });
    const gate = {
        requested: false,
        open() {
            gate.requested = true;
            release();
        },
        options: {
            importButterchurn: () => opened.then(loaders.importButterchurn ?? importButterchurn),
            getPresets: () => opened.then(loaders.importPresets ?? importPresets),
            butterchurnOpen,
        },
    };
    return gate;
}

/**
 * The layout Webamp takes at creation (its `windowLayout` option) from the saved one.
 * @param {object | null | undefined} windows The saved windows (C# `WindowLayout`).
 * @returns {object | undefined} Webamp's layout; `undefined` stacks the windows as Webamp does.
 */
export function toWindowLayout(windows) {
    if (windows == null) {
        return undefined;
    }
    const layout = {};
    for (const id of WINDOW_IDS) {
        const w = windows[id];
        if (w == null) {
            continue;
        }
        layout[id] = {
            position: { left: w.x, top: w.y },
            closed: !w.open,
            // Milkdrop cannot be rolled up (Webamp throws).
            ...(id !== 'milkdrop' && w.shade ? { shadeMode: true } : {}),
            ...(id === 'playlist' || id === 'milkdrop'
                ? { size: { extraWidth: w.extraWidth ?? 0, extraHeight: w.extraHeight ?? 0 } }
                : {}),
        };
    }
    return layout;
}

/**
 * The options every Webamp of the app is created with (decisions 8, 9, 12, 13 and 17 of doc/en/plan.md).
 * @param {{ zIndex?: number, settings?: object | null }} [options] Options from C#.
 * @param {object} [butterchurn] Webamp's `__butterchurnOptions` (`lazyButterchurn(...).options`).
 * @returns {object} Webamp options.
 */
export function webampOptions(options = {}, callbacks = {}, butterchurn = undefined) {
    const settings = options.settings ?? null;
    return {
        // Webamp's hotkeys listen to the whole page: WebampHost captures that listener at creation
        // and calls it only while the app's window has the focus (decision 17).
        enableHotkeys: true,
        // Media keys and the browser's media controls (decision 8).
        enableMediaSession: true,
        // No music from the internet in the first version: "Add URL" does nothing (its button is
        // hidden by WebampWindow.razor.css).
        handleAddUrlEvent: () => null,
        // "Load list" and "Save list" go through the OS dialogs, run by C# (decision 10). Webamp
        // gets null back: C# replaces the list itself, through replaceTracks.
        handleLoadListEvent: async () => {
            callbacks.onLoadList?.();
            return null;
        },
        handleSaveListEvent: () => callbacks.onSaveList?.(),
        zIndex: options.zIndex ?? 0,
        enableDoubleSizeMode: settings?.doubleSize === true,
        windowLayout: toWindowLayout(settings?.windows),
        ...(butterchurn ? { __butterchurnOptions: butterchurn } : {}),
    };
}

/**
 * Creates Webamp while capturing the page-wide `keydown` listener its hotkeys install on `document`
 * (Webamp 2.3.1 `bindHotkeys`), instead of letting it listen: WebampHost calls it only while the
 * app's window has the focus. Webamp's own removal of that listener, at dispose, does nothing.
 * @param {Function} Webamp The Webamp class.
 * @param {object} options Webamp options.
 * @param {Document | undefined} doc The page (none under node).
 * @returns {{ webamp: object, hotkeys: Function[] }} The instance and the captured listeners.
 */
export function constructCapturingHotkeys(Webamp, options, doc) {
    const hotkeys = [];
    if (doc == null || typeof doc.addEventListener !== 'function') {
        return { webamp: new Webamp(options), hotkeys };
    }
    const original = doc.addEventListener;
    doc.addEventListener = function (type, listener, ...rest) {
        if (type === 'keydown') {
            hotkeys.push(listener);
            return undefined;
        }
        return original.call(this, type, listener, ...rest);
    };
    try {
        return { webamp: new Webamp(options), hotkeys };
    } finally {
        doc.addEventListener = original;
    }
}

/**
 * Creates the host of one Webamp window.
 * @param {HTMLElement} element The element Webamp renders into.
 * @param {{ onClose?: () => void, onMinimize?: () => void, onTrackChanged?: (track: object | null) => void,
 *           onPlaylistChanged?: (keys: string[]) => void, onLoadList?: () => void,
 *           onSaveList?: (entries: { key: string, title: string | null, duration: number | null }[], unsaved: number) => void,
 *           onStateChanged?: () => void, onSkinFile?: (name: string, bytes: Uint8Array) => void,
 *           onSkinChosen?: (key: string | null) => void, onOpenRequested?: (kind: string) => void }} callbacks What C# hears.
 * @param {{ zIndex?: number, skins?: SkinData[], initialSkin?: string | null, settings?: object | null, focused?: boolean }} [options]
 *        Options from C#: the skins of the user's folder, in menu order, and the key of the one to
 *        start with; `settings` is the saved C# `PlayerSettings` (or null on a first run).
 * @param {{ Webamp?: Function, createObjectURL?: (blob: Blob) => string, revokeObjectURL?: (url: string) => void,
 *           pickSkinFile?: () => Promise<File | null>, document?: Document, loaders?: object,
 *           element?: Element, interceptor?: FilePromptInterceptor }} [deps] Replaced by the tests.
 * @returns {Promise<WebampHost>} The host, once Webamp is on screen.
 */
export async function createHost(element, callbacks, options = {}, deps = {}) {
    const Webamp = deps.Webamp ?? await loadWebamp();
    let host = null;
    const listCallbacks = {
        onLoadList: () => callbacks.onLoadList?.(),
        onSaveList: () => {
            if (host !== null) {
                const entries = host.playlistEntries();
                callbacks.onSaveList?.(entries, host.webamp.getPlaylistTracks().length - entries.length);
            }
        },
    };
    const urls = {
        createObjectURL: deps.createObjectURL ?? (blob => URL.createObjectURL(blob)),
        revokeObjectURL: deps.revokeObjectURL ?? (url => URL.revokeObjectURL(url)),
    };

    // The user's skins are in Webamp's menu from the start, and Webamp starts with the skin in use
    // (no flash of the base skin).
    const skins = (options.skins ?? []).map(skin => toSkinEntry(skin, urls));
    const initial = skins.find(skin => skin.key === options.initialSkin);
    const settings = options.settings ?? null;
    const butterchurn = lazyButterchurn(settings?.windows?.milkdrop?.open === true, deps.loaders);
    const doc = 'document' in deps ? deps.document : globalThis.document;
    const { webamp, hotkeys } = constructCapturingHotkeys(Webamp, {
        ...webampOptions(options, listCallbacks, butterchurn.options),
        availableSkins: skins.map(skinMenuItem),
        ...(initial ? { initialSkin: { url: initial.url } } : {}),
    }, doc);
    host = new WebampHost(webamp, callbacks, urls, skins, { hotkeys, document: doc, butterchurn });
    // Before the first frame: the saved volume, equalizer... and, when the user had moved Webamp's
    // windows, their exact places (otherwise Webamp centers them in the element).
    host.applySettings(settings);
    host.setFocused(options.focused === true);
    const interceptor = deps.interceptor ?? (deps.element && globalThis.document && globalThis.HTMLInputElement
        ? FilePromptInterceptor.shared(globalThis.document, globalThis.HTMLInputElement.prototype)
        : null);
    if (interceptor) {
        host.attachInterceptor(interceptor, deps.element);
    }
    await host.webamp.renderInto(element);
    if (typeof element.addEventListener === 'function') {
        host.watchSkinMenu(element, deps.pickSkinFile);
    }
    return host;
}

/**
 * A skin sent by C#.
 * @typedef {{ key: string, name: string, bytes: Uint8Array }} SkinData
 */

function toSkinEntry(skin, urls) {
    const url = urls.createObjectURL(new Blob([skin.bytes], { type: 'application/x-winamp-skin' }));
    return { key: skin.key, name: skin.name, url };
}

function skinMenuItem(skin) {
    return { url: skin.url, name: skin.name };
}

/**
 * Entry point for Blazor: the same host, its events sent to a .NET object reference.
 * @param {HTMLElement} element The element Webamp renders into.
 * @param {{ invokeMethodAsync: (name: string, ...args: any[]) => Promise<any> }} dotNet The WebampPlayer.
 * @param {{ zIndex?: number }} [options] Options from C#.
 * @returns {Promise<WebampHost>} The host (an IJSObjectReference for C#).
 */
export function create(element, dotNet, options = {}) {
    const send = (name, ...args) => {
        // The window may be gone already (the .NET object disposed): nothing to tell anymore.
        dotNet.invokeMethodAsync(name, ...args).catch(() => {});
    };
    return createHost(element, {
        onClose: () => send('OnWebampClosed'),
        onMinimize: () => send('OnWebampMinimized'),
        onTrackChanged: track => send('OnTrackChanged', track),
        onPlaylistChanged: keys => send('OnPlaylistChanged', keys),
        onSkinFile: (name, bytes) => send('OnSkinFileChosen', name, bytes),
        onSkinChosen: key => send('OnSkinChosen', key),
        onLoadList: () => send('OnLoadListRequested'),
        onSaveList: (entries, unsaved) => send('OnSaveListRequested', entries, unsaved),
        onStateChanged: () => send('OnStateChanged'),
        onOpenRequested: kind => send('OnOpenRequested', kind),
    }, options, { element });
}

/** One Webamp instance and the Blob addresses of the tracks it was given. */
export class WebampHost {
    /**
     * @param {object} webamp The Webamp instance.
     * @param {object} callbacks What C# hears.
     * @param {{ createObjectURL: (blob: Blob) => string, revokeObjectURL: (url: string) => void }} urls Blob addresses.
     * @param {{ key: string, name: string, url: string }[]} [skins] The skins of Webamp's menu, in its order.
     * @param {{ hotkeys?: Function[], document?: Document, butterchurn?: { open: () => void, requested: boolean } }} [extras]
     *        Webamp's captured hotkey listeners, the page, the Milkdrop gate.
     */
    constructor(webamp, callbacks, urls, skins = [], extras = {}) {
        this.webamp = webamp;
        this._callbacks = callbacks;
        this._urls = urls;
        this._hotkeys = extras.hotkeys ?? [];
        this._document = extras.document;
        this._butterchurn = extras.butterchurn;
        this._focused = false;
        this._lastTrack = undefined;
        this._lastState = '';
        this._onKeyDown = event => {
            if (this._focused && !this._disposed) {
                for (const listener of this._hotkeys) {
                    listener(event);
                }
            }
        };
        if (this._hotkeys.length > 0) {
            this._document?.addEventListener('keydown', this._onKeyDown);
        }
        /** Blob address → C# key, for every track this host gave Webamp and that is still listed. */
        this._keysByUrl = new Map();
        /** Addresses already seen in Webamp's list: only those can have left it. */
        this._listed = new Set();
        this._lastSignature = '';
        this._disposed = false;
        /** The skins of Webamp's Skins menu, in its order, each with its Blob address. */
        this._skins = [...skins];
        this._unwatchSkinMenu = null;
        this._unsubscribe = [
            webamp.onClose(() => this._callbacks.onClose?.()),
            webamp.onMinimize(() => this._callbacks.onMinimize?.()),
            webamp.store.subscribe(() => this._storeChanged()),
        ];
    }

    /** @returns {boolean} Whether Webamp's hotkeys act (the app's window has the focus). */
    get focused() {
        return this._focused;
    }

    /**
     * Webamp's hotkeys act only while the app's window has the focus (decision 17): C# calls this
     * when the OS activates or deactivates the window.
     * @param {boolean} focused Whether the window has the focus.
     */
    setFocused(focused) {
        this._focused = focused === true;
    }

    /**
     * Puts saved settings back (C# `PlayerSettings`), through Webamp's own actions so its audio
     * follows: volume, balance, shuffle, repeat, equalizer, and the exact window places when the
     * user had moved them. The layout itself and the double size are Webamp options (`webampOptions`).
     * @param {object | null | undefined} settings The saved settings; nothing on a first run.
     */
    applySettings(settings) {
        if (settings == null || this._disposed) {
            return;
        }
        const { store } = this.webamp;
        this.webamp.setVolume(settings.volume);
        store.dispatch({ type: 'SET_BALANCE', balance: settings.balance });
        if (this.webamp.isShuffleEnabled() !== settings.shuffle) {
            this.webamp.toggleShuffle();
        }
        if (this.webamp.isRepeatEnabled() !== settings.repeat) {
            this.webamp.toggleRepeat();
        }
        const eq = settings.equalizer;
        if (eq != null) {
            store.dispatch({ type: 'SET_BAND_VALUE', band: 'preamp', value: eq.preamp });
            EQ_BANDS.forEach((band, i) => {
                if (eq.bands?.[i] != null) {
                    store.dispatch({ type: 'SET_BAND_VALUE', band, value: eq.bands[i] });
                }
            });
            store.dispatch({ type: eq.on ? 'SET_EQ_ON' : 'SET_EQ_OFF' });
            store.dispatch({ type: 'SET_EQ_AUTO', value: eq.auto === true });
        }
        if (settings.windows?.absolute) {
            const positions = {};
            for (const id of WINDOW_IDS) {
                const w = settings.windows[id];
                if (w != null) {
                    positions[id] = { x: w.x, y: w.y };
                }
            }
            store.dispatch({ type: 'UPDATE_WINDOW_POSITIONS', positions, absolute: true });
        }
        this._lastState = this._stateSignature();
    }

    /**
     * Reads what Webamp holds (C# `PlayerSnapshot`): the current track's key and the settings.
     * @returns {{ currentKey: string | null, settings: object }}
     */
    readState() {
        const state = this.webamp.store.getState();
        const currentId = state.playlist.currentTrack;
        const currentUrl = currentId == null ? null : state.tracks[currentId]?.url;
        const status = state.media.status;
        const playing = status === 'PLAYING' || status === 'PAUSED';
        const elapsed = playing ? Number(this.webamp.media?.timeElapsed?.() ?? state.media.timeElapsed) : 0;
        const windows = { absolute: state.windows.positionsAreRelative === false };
        for (const id of WINDOW_IDS) {
            const w = state.windows.genWindows[id];
            windows[id] = {
                x: Math.round(w.position.x),
                y: Math.round(w.position.y),
                open: w.open === true,
                shade: w.shade === true,
                extraWidth: w.size?.[0] ?? 0,
                extraHeight: w.size?.[1] ?? 0,
            };
        }
        return {
            currentKey: (currentUrl != null ? this._keysByUrl.get(currentUrl) : undefined) ?? null,
            settings: {
                position: Number.isFinite(elapsed) && elapsed > 0 ? elapsed : 0,
                volume: state.media.volume,
                balance: state.media.balance,
                shuffle: state.media.shuffle === true,
                repeat: state.media.repeat === true,
                equalizer: {
                    on: state.equalizer.on === true,
                    auto: state.equalizer.auto === true,
                    preamp: state.equalizer.sliders.preamp,
                    bands: EQ_BANDS.map(band => state.equalizer.sliders[band]),
                },
                doubleSize: state.display.doubled === true,
                windows,
            },
        };
    }

    /**
     * Makes a track of the list current without playing it. With a position, waits until the track
     * is loaded, goes there and leaves Webamp paused: Play continues from there (Webamp starts a
     * stopped track from its beginning).
     * @param {number} index Index in Webamp's list.
     * @param {number} position Seconds into the track.
     * @returns {Promise<boolean>} Whether the position was reached.
     */
    async resume(index, position) {
        if (this._disposed || index < 0 || index >= this.webamp.getPlaylistTracks().length) {
            return false;
        }
        // Webamp is stopped: setCurrentTrack loads the track without playing it.
        this.webamp.setCurrentTrack(index);
        if (!(position > 0)) {
            return false;
        }
        const loaded = await this._waitFor(() => Number.isFinite(this.webamp.media?.duration?.()) && this.webamp.media.duration() > 0, RESUME_TIMEOUT_MS);
        if (!loaded || this._disposed) {
            return false;
        }
        this.webamp.seekToTime(Math.min(position, this.webamp.media.duration()));
        this.webamp.store.dispatch({ type: 'PAUSE' });
        return true;
    }

    /** @returns {boolean} Whether Butterchurn was asked for (Milkdrop opened once). */
    get milkdropLoaded() {
        return this._butterchurn?.requested === true;
    }

    /**
     * Adds tracks at the end of Webamp's list.
     * @param {{ key: string, name: string, mimeType?: string | null, bytes: Uint8Array }[]} tracks The files, read by C#.
     * @param {boolean} play Whether the first added track starts playing.
     */
    addTracks(tracks, play) {
        if (this._disposed || tracks.length === 0) {
            return;
        }
        const first = this.webamp.getPlaylistTracks().length;
        this.webamp.appendTracks(tracks.map(track => this._toWebampTrack(track)));
        if (play) {
            this.webamp.setCurrentTrack(first);
            this.webamp.play();
        }
    }

    /**
     * Replaces Webamp's list; the addresses of the tracks that leave it are given back.
     * @param {{ key: string, name: string, mimeType?: string | null, bytes: Uint8Array }[]} tracks The files, read by C#.
     * @param {boolean} play Whether the first track starts playing.
     */
    replaceTracks(tracks, play) {
        if (this._disposed) {
            return;
        }
        // setTracksToPlay always starts the first track: stop it when the caller did not ask for it.
        this.webamp.setTracksToPlay(tracks.map(track => this._toWebampTrack(track)));
        if (!play) {
            this.webamp.stop();
        }
    }

    /**
     * Sends Webamp's file prompts of this window to C# (decision 16).
     * @param {FilePromptInterceptor} interceptor The page's interceptor.
     * @param {Element} element The element Webamp renders into.
     */
    attachInterceptor(interceptor, element) {
        const detach = interceptor.attach({
            owns: target => ownsTarget(element, target),
            onPrompt: kind => this._callbacks.onOpenRequested?.(kind),
        });
        this._unsubscribe.push(detach);
    }

    /** @returns {string[]} The C# keys of the tracks in Webamp's list, in its order. */
    playlistKeys() {
        return this.webamp.getPlaylistTracks()
            .map(track => this._keysByUrl.get(track.url))
            .filter(key => key !== undefined);
    }

    /**
     * The tracks of Webamp's list that C# gave it, with what Webamp knows of them, for "Save list".
     * Tracks dropped from the computer have no file in Kazo OS: they are left out.
     * @returns {{ key: string, title: string | null, duration: number | null }[]}
     */
    playlistEntries() {
        return this.webamp.getPlaylistTracks()
            .filter(track => this._keysByUrl.has(track.url))
            .map(track => ({
                key: this._keysByUrl.get(track.url),
                title: trackTitle(track),
                duration: typeof track.duration === 'number' && Number.isFinite(track.duration) ? track.duration : null,
            }));
    }

    /** @returns {number} How many Blob addresses of tracks this host still holds. */
    get heldAddresses() {
        return this._keysByUrl.size;
    }

    /** @returns {string[]} The keys of the skins in Webamp's Skins menu, in its order. */
    skinKeys() {
        return this._skins.map(skin => skin.key);
    }

    /**
     * Adds a skin to Webamp's Skins menu, or replaces the one with the same key, and puts it on.
     * @param {SkinData} skin The skin, checked by C#.
     * @param {boolean} apply Whether Webamp changes to it now.
     * @returns {Promise<void>} Once Webamp has drawn it (with apply).
     */
    async addSkin(skin, apply) {
        if (this._disposed) {
            return;
        }
        const entry = toSkinEntry(skin, this._urls);
        const index = this._skins.findIndex(listed => listed.key === skin.key);
        if (index >= 0) {
            this._urls.revokeObjectURL(this._skins[index].url);
            this._skins[index] = entry;
        } else {
            this._skins.push(entry);
            this._skins.sort(compareSkins);
        }
        // Webamp 2.3.1 has no method to change the list after construction: this is the action its
        // constructor dispatches with the availableSkins option.
        this.webamp.store.dispatch({ type: 'SET_AVAILABLE_SKINS', skins: this._skins.map(skinMenuItem) });
        if (apply) {
            this.webamp.setSkinFromUrl(entry.url);
            await this.webamp.skinIsLoaded();
        }
    }

    /**
     * Catches "Skins ▸ Load Skin…", a skin chosen in the menu and a skin dropped on Webamp.
     * @param {HTMLElement} element The element Webamp renders into.
     * @param {() => Promise<File | null>} [pickFile] Replaced by the tests.
     */
    watchSkinMenu(element, pickFile) {
        this._unwatchSkinMenu?.();
        this._unwatchSkinMenu = installSkinBridge(element, {
            pickFile,
            onSkinFile: file => this._skinFileChosen(file),
            onMenuChoice: action => this._callbacks.onSkinChosen?.(
                action.kind === 'skin' ? this._skins[action.index]?.key ?? null : null),
        });
    }

    async _skinFileChosen(file) {
        const bytes = new Uint8Array(await file.arrayBuffer());
        if (!this._disposed) {
            this._callbacks.onSkinFile?.(file.name, bytes);
        }
    }

    /** Stops the music and gives every address back (decision 8: the sound stops on close). */
    dispose() {
        if (this._disposed) {
            return;
        }
        this._disposed = true;
        for (const unsubscribe of this._unsubscribe) {
            unsubscribe?.();
        }
        this._document?.removeEventListener?.('keydown', this._onKeyDown);
        this._unwatchSkinMenu?.();
        try {
            this.webamp.stop();
        } finally {
            this.webamp.dispose();
            for (const url of this._keysByUrl.keys()) {
                this._urls.revokeObjectURL(url);
            }
            this._keysByUrl.clear();
            this._listed.clear();
            for (const skin of this._skins) {
                this._urls.revokeObjectURL(skin.url);
            }
            this._skins = [];
        }
    }

    _toWebampTrack(track) {
        const blob = new Blob([track.bytes], track.mimeType ? { type: track.mimeType } : {});
        const url = this._urls.createObjectURL(blob);
        this._keysByUrl.set(url, track.key);
        // A URL track rather than a BlobTrack: given a Blob, Webamp creates the address itself and
        // never gives it back; this way the host gives it back when the track leaves the list.
        const webampTrack = { url, defaultName: track.name };
        if (typeof track.durationSeconds === 'number') {
            // From a playlist: shown until Webamp reads the file.
            webampTrack.duration = track.durationSeconds;
        }
        return webampTrack;
    }

    _storeChanged() {
        if (this._disposed) {
            return;
        }
        this._playlistMayHaveChanged();
        this._trackMayHaveChanged();
        if (this._butterchurn && !this._butterchurn.requested
            && this.webamp.store.getState().windows.genWindows.milkdrop?.open) {
            this._butterchurn.open();
        }
        const signature = this._stateSignature();
        if (signature !== this._lastState) {
            this._lastState = signature;
            this._callbacks.onStateChanged?.();
        }
    }

    // What is saved, without the clock: while playing, the position counts in steps of
    // POSITION_STEP_SECONDS, so the state is written at most that often.
    _stateSignature() {
        const { currentKey, settings } = this.readState();
        const step = Math.floor(settings.position / POSITION_STEP_SECONDS);
        return JSON.stringify([currentKey, step, { ...settings, position: 0 }]);
    }

    // The window's title follows the current track, playing or paused (Webamp's onTrackDidChange
    // only names a playing track), with its tags once Webamp has read them.
    _trackMayHaveChanged() {
        const state = this.webamp.store.getState();
        const id = state.playlist.currentTrack;
        const track = id == null ? null : state.tracks[id];
        const info = track == null ? null : {
            artist: track.artist || null,
            title: track.title || null,
            name: track.defaultName || null,
        };
        const signature = JSON.stringify(info);
        if (signature !== this._lastTrack) {
            this._lastTrack = signature;
            this._callbacks.onTrackChanged?.(info);
        }
    }

    _waitFor(predicate, timeoutMs) {
        if (predicate()) {
            return Promise.resolve(true);
        }
        return new Promise(resolve => {
            let unsubscribe = () => {};
            const timer = setTimeout(() => {
                unsubscribe();
                resolve(false);
            }, timeoutMs);
            unsubscribe = this.webamp.store.subscribe(() => {
                if (this._disposed || predicate()) {
                    clearTimeout(timer);
                    unsubscribe();
                    resolve(!this._disposed);
                }
            });
        });
    }

    // Webamp's store changes for every tick of the clock: only a different list of tracks matters.
    _playlistMayHaveChanged() {
        const present = new Set(this.webamp.getPlaylistTracks().map(track => track.url));
        for (const url of [...this._keysByUrl.keys()]) {
            if (present.has(url)) {
                this._listed.add(url);
            } else if (this._listed.has(url)) {
                // Webamp adds a batch one track at a time: an address not listed yet is still
                // arriving; one that was listed and is gone has left the list.
                this._listed.delete(url);
                this._keysByUrl.delete(url);
                this._urls.revokeObjectURL(url);
            }
        }
        const keys = this.playlistKeys();
        const signature = keys.join('\n');
        if (signature !== this._lastSignature) {
            this._lastSignature = signature;
            this._callbacks.onPlaylistChanged?.(keys);
        }
    }
}

// The order of the Skins menu, the same as SkinLibrary's in C#: by name, case ignored.
function compareSkins(left, right) {
    const byName = left.name.localeCompare(right.name, undefined, { sensitivity: 'accent' });
    return byName !== 0 ? byName : (left.key < right.key ? -1 : left.key > right.key ? 1 : 0);
}

/**
 * The title a list writes for a track: "Artist - Title" once Webamp read the tags (as Winamp
 * writes it), the title alone, the name Webamp shows, or null (C# then writes the file name).
 * @param {{ artist?: string | null, title?: string | null, defaultName?: string | null }} track A track of Webamp's state.
 * @returns {string | null}
 */
export function trackTitle(track) {
    const artist = track.artist?.trim();
    const title = track.title?.trim();
    if (artist && title) {
        return `${artist} - ${title}`;
    }
    // No tags: the name Webamp shows (the playlist's title, or the file name).
    return title || track.defaultName?.trim() || null;
}

/** Where Webamp draws its context menus: a node of its own, at the end of the page (outside the window). */
const CONTEXT_MENU_SELECTOR = '#webamp-context-menu';

/**
 * Whether an event target belongs to the Webamp of one window: inside the element it renders into,
 * or in Webamp's context menu (the app runs one Webamp per user, decision 5).
 * @param {{ contains?: (node: any) => boolean }} element The element Webamp renders into.
 * @param {{ closest?: (selector: string) => any } | null | undefined} target The event target.
 * @returns {boolean}
 */
export function ownsTarget(element, target) {
    if (target == null || typeof target.closest !== 'function') {
        return false;
    }
    return element.contains(target) || target.closest(CONTEXT_MENU_SELECTOR) != null;
}

/**
 * Names the file prompt Webamp is opening, from the input it built and the element the user chose
 * (decision 16 of doc/en/plan.md).
 *
 * Webamp opens every file prompt the same way (fileUtils.ts, promptForFileReferences): a new
 * `<input type="file" multiple>`, never put in the page, and `input.click()`. Its "open" commands
 * give no option to replace that picker, so the host recognises them by the input:
 * - `webkitdirectory` set: "ADD ▸ Add dir", → `add-folder`;
 * - an `accept` filter (`.zip, .wsz` for skins, `.eqf` for equalizer presets): not music, left to
 *   the browser (skins are work item 4);
 * - otherwise music: "ADD ▸ Add file" (`.add-file`) → `add-files`, the eject buttons and
 *   "Play ▸ File…" → `open`.
 * @param {{ accept?: string | null, webkitdirectory?: boolean, directory?: boolean, getAttribute?: (name: string) => string | null }} input
 * @param {{ closest?: (selector: string) => any } | null | undefined} target The element clicked.
 * @returns {'open' | 'add-files' | 'add-folder' | null} The command, or null to let the browser open its picker.
 */
export function classifyFilePrompt(input, target) {
    if (input.webkitdirectory || input.directory) {
        return 'add-folder';
    }
    const accept = input.accept ?? input.getAttribute?.('accept') ?? '';
    if (accept !== '') {
        return null;
    }
    if (target != null && typeof target.closest === 'function' && target.closest('.add-file') != null) {
        return 'add-files';
    }
    return 'open';
}

/**
 * Stops the browser's file picker that Webamp opens for music, and tells C# instead.
 *
 * It listens, in the capture phase, to the clicks and keys of the page: when the target belongs to
 * a Webamp window, it is "armed" until the event is over (Webamp builds and clicks its input while
 * it handles the event, synchronously). `HTMLInputElement.prototype.click` is wrapped once for the
 * page: called on a file input while armed, it asks `classifyFilePrompt` and, for music, calls the
 * window instead of opening the picker. Webamp's own promise then never resolves, which only leaves
 * one empty input to the garbage collector. Every other click on an input (the File Explorer's
 * upload, a skin) goes to the browser as before, and the wrapper is removed with the last window.
 */
export class FilePromptInterceptor {
    /**
     * The interceptor of a page (one for every Webamp window of the page).
     * @param {Document} document The page.
     * @param {object} inputPrototype `HTMLInputElement.prototype`.
     * @returns {FilePromptInterceptor}
     */
    static shared(document, inputPrototype) {
        const key = Symbol.for('ca.kazo.webamp.file-prompt-interceptor');
        document[key] ??= new FilePromptInterceptor(document, inputPrototype);
        return document[key];
    }

    /**
     * @param {{ addEventListener: Function, removeEventListener: Function }} document The page.
     * @param {{ click: Function }} inputPrototype `HTMLInputElement.prototype`, or a fake.
     * @param {{ defer?: (callback: () => void) => void }} [deps] Replaced by the tests.
     */
    constructor(document, inputPrototype, deps = {}) {
        this._document = document;
        this._prototype = inputPrototype;
        this._defer = deps.defer ?? (callback => setTimeout(callback, 0));
        /** @type {Set<{ owns: (target: any) => boolean, onPrompt: (kind: string) => void }>} */
        this._windows = new Set();
        this._armed = null;
        this._originalClick = null;
        this._onEvent = event => this._arm(event);
    }

    /** @returns {boolean} Whether `click` is wrapped now. */
    get installed() {
        return this._originalClick !== null;
    }

    /**
     * Adds a Webamp window.
     * @param {{ owns: (target: any) => boolean, onPrompt: (kind: string) => void }} window Its targets and what it hears.
     * @returns {() => void} Removes it again.
     */
    attach(window) {
        this._windows.add(window);
        this._install();
        return () => {
            this._windows.delete(window);
            if (this._windows.size === 0) {
                this._uninstall();
            }
        };
    }

    _install() {
        if (this.installed) {
            return;
        }
        const original = this._prototype.click;
        this._originalClick = original;
        const interceptor = this;
        this._prototype.click = function click(...args) {
            if (interceptor._intercept(this)) {
                return undefined;
            }
            return original.apply(this, args);
        };
        for (const type of ['click', 'keydown']) {
            this._document.addEventListener(type, this._onEvent, true);
        }
    }

    _uninstall() {
        if (!this.installed) {
            return;
        }
        this._prototype.click = this._originalClick;
        this._originalClick = null;
        this._armed = null;
        for (const type of ['click', 'keydown']) {
            this._document.removeEventListener(type, this._onEvent, true);
        }
    }

    // Capture phase, before Webamp: remember which window the event belongs to, until it is over.
    _arm(event) {
        const target = event.target;
        for (const window of this._windows) {
            if (window.owns(target)) {
                const armed = { window, target };
                this._armed = armed;
                this._defer(() => {
                    if (this._armed === armed) {
                        this._armed = null;
                    }
                });
                return;
            }
        }
    }

    _intercept(input) {
        const armed = this._armed;
        if (armed === null || input == null || input.type !== 'file') {
            return false;
        }
        const kind = classifyFilePrompt(input, armed.target);
        if (kind === null) {
            return false;
        }
        // One prompt per event: a second click() in the same event opens nothing either.
        this._armed = null;
        armed.window.onPrompt(kind);
        return true;
    }
}
