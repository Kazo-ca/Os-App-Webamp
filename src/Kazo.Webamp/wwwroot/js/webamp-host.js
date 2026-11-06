// Webamp host module of the Webamp app (doc/en/architecture.md, "C# and JavaScript").
//
// The C# side (WebampPlayer, TrackLibrary) keeps the logic: which files are in the list, where they
// come from, what to play. This module only does what C# cannot reach: it creates the Webamp
// instance inside the app's window, turns the bytes of a file into a Blob address Webamp can play,
// gives that address back to the browser when the track leaves Webamp's list, and reports Webamp's
// events (close, minimize, track change, list change, "Load list", "Save list") to C#.
//
// It keeps no state outside one host object per window, so a test drives it with a fake Webamp
// class (tests/js/webamp-host.test.mjs). The skins (work item 4) go through the same host, with the
// Skins menu bridge of webamp-skins.js.

import { installSkinBridge } from './webamp-skins.js';

/** Address of the Webamp 2.3.1 bundle (JSZip and music-metadata included), next to this module. */
const WEBAMP_BUNDLE = new URL('../lib/webamp/webamp.bundle.min.mjs', import.meta.url).href;

/**
 * Loads the Webamp class from the vendored bundle.
 * @returns {Promise<Function>} The Webamp constructor.
 */
export async function loadWebamp() {
    const module = await import(WEBAMP_BUNDLE);
    return module.default;
}

/**
 * The options every Webamp of the app is created with (decisions 9 and 17 of doc/en/plan.md).
 * @param {{ zIndex?: number }} [options] Options from C#.
 * @returns {object} Webamp options.
 */
export function webampOptions(options = {}, callbacks = {}) {
    return {
        // Webamp's own hotkeys listen to the whole page and would take the keys of the other apps.
        enableHotkeys: false,
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
    };
}

/**
 * Creates the host of one Webamp window.
 * @param {HTMLElement} element The element Webamp renders into.
 * @param {{ onClose?: () => void, onMinimize?: () => void, onTrackChanged?: (track: object | null) => void,
 *           onPlaylistChanged?: (keys: string[]) => void, onLoadList?: () => void,
 *           onSaveList?: (entries: { key: string, title: string | null, duration: number | null }[], unsaved: number) => void,
 *           onSkinFile?: (name: string, bytes: Uint8Array) => void, onSkinChosen?: (key: string | null) => void }} callbacks
 *        What C# hears.
 * @param {{ zIndex?: number, skins?: SkinData[], initialSkin?: string | null }} [options] Options from C#:
 *        the skins of the user's folder, in menu order, and the key of the one to start with.
 * @param {{ Webamp?: Function, createObjectURL?: (blob: Blob) => string, revokeObjectURL?: (url: string) => void,
 *           pickSkinFile?: () => Promise<File | null> }} [deps] Replaced by the tests.
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
    const webamp = new Webamp({
        ...webampOptions(options, listCallbacks),
        availableSkins: skins.map(skinMenuItem),
        ...(initial ? { initialSkin: { url: initial.url } } : {}),
    });
    host = new WebampHost(webamp, callbacks, urls, skins);
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
    }, options);
}

/** One Webamp instance and the Blob addresses of the tracks it was given. */
export class WebampHost {
    /**
     * @param {object} webamp The Webamp instance.
     * @param {object} callbacks What C# hears.
     * @param {{ createObjectURL: (blob: Blob) => string, revokeObjectURL: (url: string) => void }} urls Blob addresses.
     * @param {{ key: string, name: string, url: string }[]} [skins] The skins of Webamp's menu, in its order.
     */
    constructor(webamp, callbacks, urls, skins = []) {
        this.webamp = webamp;
        this._callbacks = callbacks;
        this._urls = urls;
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
            webamp.onTrackDidChange(track => this._callbacks.onTrackChanged?.(describeTrack(track))),
            webamp.store.subscribe(() => this._playlistMayHaveChanged()),
        ];
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

    // Webamp's store changes for every tick of the clock: only a different list of tracks matters.
    _playlistMayHaveChanged() {
        if (this._disposed) {
            return;
        }
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
 * The part of a Webamp track C# reads (artist and title, for the window title of work item 5).
 * @param {{ metaData?: { artist?: string | null, title?: string | null } } | null | undefined} track
 * @returns {{ artist: string | null, title: string | null } | null}
 */
export function describeTrack(track) {
    if (track == null) {
        return null;
    }
    return {
        artist: track.metaData?.artist ?? null,
        title: track.metaData?.title ?? null,
    };
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
