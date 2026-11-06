// Webamp host module of the Webamp app (doc/en/architecture.md, "C# and JavaScript").
//
// The C# side (WebampPlayer, TrackLibrary) keeps the logic: which files are in the list, where they
// come from, what to play. This module only does what C# cannot reach: it creates the Webamp
// instance inside the app's window, turns the bytes of a file into a Blob address Webamp can play,
// gives that address back to the browser when the track leaves Webamp's list, reports Webamp's
// events (close, minimize, track change, list change) to C#, and stops the browser's file picker
// that Webamp's "Open", "Add file" and "Add dir" would show, so C# shows the OS dialogs instead
// (decision 16, FilePromptInterceptor below).
//
// It keeps no state outside one host object per window, so a test drives it with a fake Webamp
// class (tests/js/webamp-host.test.mjs).

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
export function webampOptions(options = {}) {
    return {
        // Webamp's own hotkeys listen to the whole page and would take the keys of the other apps.
        enableHotkeys: false,
        // No music from the internet in the first version: "Add URL" does nothing (its button is
        // hidden by WebampWindow.razor.css).
        handleAddUrlEvent: () => null,
        zIndex: options.zIndex ?? 0,
    };
}

/**
 * Creates the host of one Webamp window.
 * @param {HTMLElement} element The element Webamp renders into.
 * @param {{ onClose?: () => void, onMinimize?: () => void, onTrackChanged?: (track: object | null) => void,
 *           onPlaylistChanged?: (keys: string[]) => void }} callbacks What C# hears.
 * @param {{ zIndex?: number }} [options] Options from C#.
 * @param {{ Webamp?: Function, createObjectURL?: (blob: Blob) => string, revokeObjectURL?: (url: string) => void }} [deps]
 *        Replaced by the tests.
 * @returns {Promise<WebampHost>} The host, once Webamp is on screen.
 */
export async function createHost(element, callbacks, options = {}, deps = {}) {
    const Webamp = deps.Webamp ?? await loadWebamp();
    const host = new WebampHost(new Webamp(webampOptions(options)), callbacks, {
        createObjectURL: deps.createObjectURL ?? (blob => URL.createObjectURL(blob)),
        revokeObjectURL: deps.revokeObjectURL ?? (url => URL.revokeObjectURL(url)),
    });
    const interceptor = deps.interceptor ?? (deps.element && globalThis.document && globalThis.HTMLInputElement
        ? FilePromptInterceptor.shared(globalThis.document, globalThis.HTMLInputElement.prototype)
        : null);
    if (interceptor) {
        host.attachInterceptor(interceptor, deps.element);
    }
    await host.webamp.renderInto(element);
    return host;
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
        onOpenRequested: kind => send('OnOpenRequested', kind),
    }, options, { element });
}

/** One Webamp instance and the Blob addresses of the tracks it was given. */
export class WebampHost {
    /**
     * @param {object} webamp The Webamp instance.
     * @param {object} callbacks What C# hears.
     * @param {{ createObjectURL: (blob: Blob) => string, revokeObjectURL: (url: string) => void }} urls Blob addresses.
     */
    constructor(webamp, callbacks, urls) {
        this.webamp = webamp;
        this._callbacks = callbacks;
        this._urls = urls;
        /** Blob address → C# key, for every track this host gave Webamp and that is still listed. */
        this._keysByUrl = new Map();
        /** Addresses already seen in Webamp's list: only those can have left it. */
        this._listed = new Set();
        this._lastSignature = '';
        this._disposed = false;
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

    /** @returns {number} How many Blob addresses this host still holds. */
    get heldAddresses() {
        return this._keysByUrl.size;
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
        try {
            this.webamp.stop();
        } finally {
            this.webamp.dispose();
            for (const url of this._keysByUrl.keys()) {
                this._urls.revokeObjectURL(url);
            }
            this._keysByUrl.clear();
            this._listed.clear();
        }
    }

    _toWebampTrack(track) {
        const blob = new Blob([track.bytes], track.mimeType ? { type: track.mimeType } : {});
        const url = this._urls.createObjectURL(blob);
        this._keysByUrl.set(url, track.key);
        // A URL track rather than a BlobTrack: given a Blob, Webamp creates the address itself and
        // never gives it back; this way the host gives it back when the track leaves the list.
        return { url, defaultName: track.name };
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
