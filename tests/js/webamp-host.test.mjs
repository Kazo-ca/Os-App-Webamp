// Tests of the Webamp host module (src/Kazo.Webamp/wwwroot/js/webamp-host.js) under node, with a
// fake Webamp: the options, the Blob addresses (created, kept while listed, given back), the events.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { FilePromptInterceptor, classifyFilePrompt, constructCapturingHotkeys, createHost, lazyButterchurn, ownsTarget, toWindowLayout, trackTitle, webampOptions } from '../../src/Kazo.Webamp/wwwroot/js/webamp-host.js';

/** Just enough of Webamp 2.3.1's public API and store, notifying on every change. */
class FakeWebamp {
    static last;

    constructor(options) {
        FakeWebamp.last = this;
        this.options = options;
        this.tracks = [];
        this.current = null;
        this.playing = false;
        this.disposed = false;
        this.renderedInto = null;
        this.dispatched = [];
        this.skinUrl = null;
        this.duration = NaN;
        this.elapsed = 0;
        this.listeners = { close: [], minimize: [], store: [] };
        this.media = { duration: () => this.duration, timeElapsed: () => this.elapsed };
        this.s = {
            media: { status: 'STOPPED', volume: 78, balance: 0, shuffle: false, repeat: false, timeElapsed: 0 },
            equalizer: { on: true, auto: false, sliders: { preamp: 50, 60: 50, 170: 50, 310: 50, 600: 50, 1000: 50, 3000: 50, 6000: 50, 12000: 50, 14000: 50, 16000: 50 } },
            display: { doubled: options.enableDoubleSizeMode === true },
            windows: {
                positionsAreRelative: true,
                genWindows: {
                    main: { position: { x: 0, y: 0 }, open: true, shade: false, size: [0, 0] },
                    equalizer: { position: { x: 0, y: 116 }, open: true, shade: false, size: [0, 0] },
                    playlist: { position: { x: 0, y: 232 }, open: true, shade: false, size: [0, 0] },
                    milkdrop: { position: { x: 0, y: 0 }, open: options.__butterchurnOptions?.butterchurnOpen === true, shade: false, size: [0, 0] },
                },
            },
        };
        this.store = {
            subscribe: listener => this._on('store', listener),
            getState: () => ({
                ...this.s,
                playlist: { currentTrack: this.current },
                tracks: Object.fromEntries(this.tracks.map((track, id) => [id, { ...track, id }])),
            }),
            dispatch: action => this._dispatch(action),
        };
        // Like Webamp: hotkeys bound on the page, Butterchurn asked for at construction.
        if (options.enableHotkeys) {
            globalThis.document?.addEventListener('keydown', event => this.dispatched.push({ type: 'HOTKEY', key: event.key }));
        }
        const butterchurn = options.__butterchurnOptions;
        if (butterchurn) {
            this.butterchurn = butterchurn.importButterchurn();
            this.presets = butterchurn.getPresets();
        }
    }

    _dispatch(action) {
        this.dispatched.push(action);
        const s = this.s;
        switch (action.type) {
            case 'SET_BALANCE': s.media.balance = action.balance; break;
            case 'SET_BAND_VALUE': s.equalizer.sliders[action.band] = action.value; break;
            case 'SET_EQ_ON': s.equalizer.on = true; break;
            case 'SET_EQ_OFF': s.equalizer.on = false; break;
            case 'SET_EQ_AUTO': s.equalizer.auto = action.value; break;
            case 'PAUSE': s.media.status = 'PAUSED'; this.playing = false; break;
            case 'UPDATE_WINDOW_POSITIONS':
                for (const [id, position] of Object.entries(action.positions)) {
                    s.windows.genWindows[id].position = position;
                }
                if (action.absolute) {
                    s.windows.positionsAreRelative = false;
                }
                break;
        }
        this._emit('store');
    }

    _on(name, listener) {
        this.listeners[name].push(listener);
        return () => {
            this.listeners[name] = this.listeners[name].filter(l => l !== listener);
        };
    }

    _emit(name, ...args) {
        for (const listener of this.listeners[name]) {
            listener(...args);
        }
    }

    async renderInto(element) { this.renderedInto = element; }
    onClose(listener) { return this._on('close', listener); }
    onMinimize(listener) { return this._on('minimize', listener); }
    getPlaylistTracks() {
        return this.tracks.map((track, id) => ({
            id, url: track.url, defaultName: track.defaultName, duration: track.duration ?? null,
            artist: track.artist ?? null, title: track.title ?? null,
        }));
    }

    // Like Webamp, a batch arrives one track at a time, the store notifying after each one.
    appendTracks(tracks) {
        for (const track of tracks) {
            this.tracks.push({ ...track });
            this._emit('store');
        }
    }

    setTracksToPlay(tracks) {
        this.tracks = [];
        this._emit('store');
        this.appendTracks(tracks);
        this.current = 0;
        this.playing = true;
    }

    setCurrentTrack(index) { this.current = index; this._emit('store'); }
    play() { this.playing = true; this.s.media.status = 'PLAYING'; this._emit('store'); }
    stop() { this.playing = false; this.s.media.status = 'STOPPED'; this._emit('store'); }
    setVolume(volume) { this.s.media.volume = volume; this._emit('store'); }
    isShuffleEnabled() { return this.s.media.shuffle; }
    toggleShuffle() { this.s.media.shuffle = !this.s.media.shuffle; this._emit('store'); }
    isRepeatEnabled() { return this.s.media.repeat; }
    toggleRepeat() { this.s.media.repeat = !this.s.media.repeat; this._emit('store'); }
    seekToTime(seconds) { this.elapsed = seconds; this._emit('store'); }
    dispose() { this.disposed = true; }
    setSkinFromUrl(url) { this.skinUrl = url; }
    async skinIsLoaded() { this.skinLoadedAwaited = true; }

    removeTrack(index) {
        this.tracks.splice(index, 1);
        this._emit('store');
    }

    setTags(index, tags) {
        Object.assign(this.tracks[index], tags);
        this._emit('store');
    }

    openWindow(id) {
        this.s.windows.genWindows[id].open = true;
        this._emit('store');
    }
}

/** A page with only what the host uses: keydown listeners. */
class FakeDocument {
    constructor() { this.listeners = []; }
    addEventListener(type, listener) { if (type === 'keydown') this.listeners.push(listener); }
    removeEventListener(type, listener) { this.listeners = this.listeners.filter(l => l !== listener); }
    press(key) { for (const listener of [...this.listeners]) listener({ key }); }
}

const start = async (options = {}, extra = {}) => {
    const events = { closed: 0, minimized: 0, tracks: [], lists: [], states: 0, loads: 0, saves: [], skinFiles: [], skinChoices: [] };
    const urls = { created: [], revoked: [] };
    const loads = { butterchurn: 0, presets: 0 };
    let next = 0;
    const host = await createHost({ id: 'surface' }, {
        onClose: () => events.closed++,
        onMinimize: () => events.minimized++,
        onTrackChanged: track => events.tracks.push(track),
        onPlaylistChanged: keys => events.lists.push(keys),
        onStateChanged: () => events.states++,
        onLoadList: () => events.loads++,
        onSaveList: (entries, unsaved) => events.saves.push({ entries, unsaved }),
        onSkinFile: (name, bytes) => events.skinFiles.push({ name, bytes }),
        onSkinChosen: key => events.skinChoices.push(key),
    }, options, {
        Webamp: FakeWebamp,
        document: extra.document ?? null,
        createObjectURL: blob => {
            const url = `blob:test/${++next}`;
            urls.created.push({ url, blob });
            return url;
        },
        revokeObjectURL: url => urls.revoked.push(url),
        loaders: {
            importButterchurn: async () => { loads.butterchurn++; return { default: 'butterchurn' }; },
            importPresets: async () => { loads.presets++; return [{ name: 'p', butterchurnPresetObject: {} }]; },
        },
    });
    return { host, webamp: FakeWebamp.last, events, urls, loads };
};

const track = (key, bytes = [1, 2, 3]) => ({ key, name: `${key}.mp3`, mimeType: 'audio/mpeg', bytes: new Uint8Array(bytes) });

test('Webamp is created with the Media Session, without Add URL, then rendered into the element', async () => {
    const { webamp } = await start();
    assert.equal(webamp.options.enableMediaSession, true);
    assert.equal(webamp.options.handleAddUrlEvent(), null);
    assert.equal(webamp.options.windowLayout, undefined);
    assert.equal(webamp.options.enableDoubleSizeMode, false);
    assert.deepEqual(webamp.renderedInto, { id: 'surface' });
    assert.equal(webampOptions({ zIndex: 5 }).zIndex, 5);
    assert.equal(webampOptions().zIndex, 0);
});

test('added tracks become Blob addresses with their type and name, and the first one plays', async () => {
    const { host, webamp, urls, events } = await start();
    host.addTracks([track('t1'), track('t2', [9])], true);

    assert.equal(urls.created.length, 2);
    assert.equal(urls.created[0].blob.type, 'audio/mpeg');
    assert.equal(urls.created[1].blob.size, 1);
    assert.deepEqual(webamp.tracks, [{ url: 'blob:test/1', defaultName: 't1.mp3' }, { url: 'blob:test/2', defaultName: 't2.mp3' }]);
    assert.equal(webamp.current, 0);
    assert.equal(webamp.playing, true);
    // A batch arrives one track at a time: no address is given back while it arrives.
    assert.deepEqual(urls.revoked, []);
    assert.deepEqual(events.lists.at(-1), ['t1', 't2']);
    assert.deepEqual(host.playlistKeys(), ['t1', 't2']);
});

test('adding to a list plays the first new track, or nothing when not asked', async () => {
    const { host, webamp } = await start();
    host.addTracks([track('t1')], false);
    assert.equal(webamp.playing, false);
    host.addTracks([track('t2'), track('t3')], true);
    assert.equal(webamp.current, 1);
    assert.equal(webamp.playing, true);
});

test('a track removed from Webamp\'s list gives its address back and the new list is reported once', async () => {
    const { host, webamp, urls, events } = await start();
    host.addTracks([track('t1'), track('t2')], false);
    const reports = events.lists.length;

    webamp.removeTrack(0);

    assert.deepEqual(urls.revoked, ['blob:test/1']);
    assert.deepEqual(events.lists.at(-1), ['t2']);
    assert.equal(events.lists.length, reports + 1);
    assert.equal(host.heldAddresses, 1);

    // Another store change with the same list (the clock ticking) reports nothing.
    webamp._emit('store');
    assert.equal(events.lists.length, reports + 1);
});

test('replacing the list gives the old addresses back, and can leave the new list stopped', async () => {
    const { host, webamp, urls, events } = await start();
    host.addTracks([track('t1'), track('t2')], true);
    host.replaceTracks([track('t3')], false);

    assert.deepEqual(urls.revoked.sort(), ['blob:test/1', 'blob:test/2']);
    assert.deepEqual(events.lists.at(-1), ['t3']);
    assert.equal(webamp.playing, false);
});

test('tracks Webamp got elsewhere (dropped from the computer) have no key', async () => {
    const { host, webamp } = await start();
    host.addTracks([track('t1')], false);
    webamp.appendTracks([{ url: 'blob:dropped/1' }]);
    assert.deepEqual(host.playlistKeys(), ['t1']);
});

test('close and minimize reach C#', async () => {
    const { webamp, events } = await start();
    webamp._emit('close');
    webamp._emit('minimize');
    assert.equal(events.closed, 1);
    assert.equal(events.minimized, 1);
});

test('the current track is reported with its tags, playing or not, and none when the list empties', async () => {
    const { host, webamp, events } = await start();
    assert.deepEqual(events.tracks, []);

    host.addTracks([track('t1')], true);
    assert.deepEqual(events.tracks.at(-1), { artist: null, title: null, name: 't1.mp3' });

    webamp.setTags(0, { artist: 'Artist', title: 'Title' });
    webamp.stop();
    assert.deepEqual(events.tracks.at(-1), { artist: 'Artist', title: 'Title', name: 't1.mp3' });
    const reports = events.tracks.length;
    webamp._emit('store');
    assert.equal(events.tracks.length, reports);

    webamp.current = null;
    webamp._emit('store');
    assert.equal(events.tracks.at(-1), null);
});

test('dispose stops the music, removes Webamp, gives every address back and ignores later calls', async () => {
    const { host, webamp, urls, events } = await start();
    host.addTracks([track('t1'), track('t2')], true);
    host.dispose();

    assert.equal(webamp.playing, false);
    assert.equal(webamp.disposed, true);
    assert.deepEqual(urls.revoked.sort(), ['blob:test/1', 'blob:test/2']);

    const reports = events.lists.length;
    host.addTracks([track('t3')], true);
    webamp._emit('close');
    host.dispose();
    assert.equal(urls.created.length, 2);
    assert.equal(events.lists.length, reports);
    assert.equal(events.closed, 0);
});

const saved = {
    position: 0, volume: 33, balance: -40, shuffle: true, repeat: true,
    equalizer: { on: false, auto: true, preamp: 70, bands: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10] },
    doubleSize: true,
    windows: {
        absolute: true,
        main: { x: 5, y: 6, open: true, shade: true, extraWidth: 0, extraHeight: 0 },
        equalizer: { x: 5, y: 122, open: false, shade: false, extraWidth: 0, extraHeight: 0 },
        playlist: { x: 5, y: 238, open: true, shade: false, extraWidth: 2, extraHeight: 3 },
        milkdrop: { x: 280, y: 6, open: false, shade: false, extraWidth: 7, extraHeight: 12 },
    },
};

test('the saved layout becomes Webamp options: places, closed windows, shade, sizes, double size', async () => {
    const { webamp } = await start({ settings: saved });
    assert.equal(webamp.options.enableDoubleSizeMode, true);
    assert.deepEqual(webamp.options.windowLayout.main, { position: { left: 5, top: 6 }, closed: false, shadeMode: true });
    assert.deepEqual(webamp.options.windowLayout.equalizer, { position: { left: 5, top: 122 }, closed: true });
    assert.deepEqual(webamp.options.windowLayout.playlist.size, { extraWidth: 2, extraHeight: 3 });
    assert.equal(webamp.options.windowLayout.milkdrop.shadeMode, undefined);
    assert.equal(toWindowLayout(null), undefined);
});

test('saved settings are put back before the first frame and read back the same', async () => {
    const { host, webamp } = await start({ settings: saved });
    const read = host.readState();

    assert.equal(webamp.renderedInto.id, 'surface');
    assert.equal(read.currentKey, null);
    assert.deepEqual(read.settings.equalizer, saved.equalizer);
    assert.equal(read.settings.volume, 33);
    assert.equal(read.settings.balance, -40);
    assert.equal(read.settings.shuffle, true);
    assert.equal(read.settings.repeat, true);
    assert.equal(read.settings.windows.absolute, true);
    assert.deepEqual(read.settings.windows.main, { x: 5, y: 6, open: true, shade: false, extraWidth: 0, extraHeight: 0 });
});

test('without moved windows, Webamp keeps centering them', async () => {
    const { webamp } = await start({ settings: { ...saved, windows: { ...saved.windows, absolute: false } } });
    assert.equal(webamp.dispatched.some(action => action.type === 'UPDATE_WINDOW_POSITIONS'), false);
});

test('the state names the current track by its key, and its position only while playing or paused', async () => {
    const { host, webamp } = await start();
    host.addTracks([track('t1'), track('t2')], true);
    webamp.setCurrentTrack(1);
    webamp.elapsed = 12.5;
    assert.equal(host.readState().currentKey, 't2');
    assert.equal(host.readState().settings.position, 12.5);

    webamp.stop();
    assert.equal(host.readState().settings.position, 0);
});

test('state changes reach C# once per change, the clock only every few seconds', async () => {
    const { host, webamp, events } = await start();
    const before = events.states;
    webamp.setVolume(10);
    assert.equal(events.states, before + 1);
    webamp._emit('store');
    assert.equal(events.states, before + 1);

    host.addTracks([track('t1')], true);
    const playing = events.states;
    webamp.elapsed = 1;
    webamp._emit('store');
    webamp.elapsed = 4.9;
    webamp._emit('store');
    assert.equal(events.states, playing);
    webamp.elapsed = 5.1;
    webamp._emit('store');
    assert.equal(events.states, playing + 1);
});

test('resume makes a track current, then waits for it to load, seeks and stays paused', async () => {
    const { host, webamp } = await start();
    host.addTracks([track('t1'), track('t2')], false);

    const resumed = host.resume(1, 30);
    assert.equal(webamp.current, 1);
    assert.equal(webamp.elapsed, 0);
    webamp.duration = 200;
    webamp._emit('store');

    assert.equal(await resumed, true);
    assert.equal(webamp.elapsed, 30);
    assert.equal(webamp.s.media.status, 'PAUSED');
    assert.equal(webamp.playing, false);
});

test('resume without a position, or outside the list, does not seek', async () => {
    const { host, webamp } = await start();
    host.addTracks([track('t1')], false);
    assert.equal(await host.resume(0, 0), false);
    assert.equal(webamp.current, 0);
    assert.equal(await host.resume(4, 10), false);
    assert.equal(webamp.dispatched.some(action => action.type === 'PAUSE'), false);
});

test('Webamp\'s hotkeys act only while the window has the focus, never on the page itself', async () => {
    const document = new FakeDocument();
    const previous = globalThis.document;
    globalThis.document = document;
    try {
        const { host, webamp } = await start({ focused: false }, { document });
        // Webamp's own listener was captured: the page only has the host's.
        assert.equal(document.listeners.length, 1);

        document.press('x');
        assert.equal(webamp.dispatched.filter(action => action.type === 'HOTKEY').length, 0);

        host.setFocused(true);
        document.press('x');
        assert.deepEqual(webamp.dispatched.filter(action => action.type === 'HOTKEY'), [{ type: 'HOTKEY', key: 'x' }]);

        host.setFocused(false);
        document.press('c');
        assert.equal(webamp.dispatched.filter(action => action.type === 'HOTKEY').length, 1);

        host.dispose();
        assert.equal(document.listeners.length, 0);
    } finally {
        globalThis.document = previous;
    }
});

test('other listeners added while Webamp is created still reach the page', () => {
    const document = new FakeDocument();
    let other = 0;
    document.addEventListener = function (type, listener) { if (type === 'click') other++; else FakeDocument.prototype.addEventListener.call(this, type, listener); };
    const original = document.addEventListener;
    class Probe {
        constructor() {
            document.addEventListener('click', () => {});
            document.addEventListener('keydown', () => {});
        }
    }
    const { hotkeys } = constructCapturingHotkeys(Probe, {}, document);
    assert.equal(other, 1);
    assert.equal(hotkeys.length, 1);
    assert.equal(document.listeners.length, 0);
    assert.equal(document.addEventListener, original);
});

test('Milkdrop: Butterchurn and its presets load only when its window first opens', async () => {
    const { host, webamp, loads } = await start();
    await new Promise(resolve => setTimeout(resolve, 10));
    assert.equal(loads.butterchurn, 0);
    assert.equal(loads.presets, 0);
    assert.equal(host.milkdropLoaded, false);
    assert.equal(webamp.options.__butterchurnOptions.butterchurnOpen, false);

    webamp.openWindow('milkdrop');
    assert.deepEqual(await webamp.butterchurn, { default: 'butterchurn' });
    assert.equal((await webamp.presets)[0].name, 'p');
    assert.equal(loads.butterchurn, 1);
    assert.equal(loads.presets, 1);
    assert.equal(host.milkdropLoaded, true);
});

test('Milkdrop open in the saved layout loads at start', async () => {
    const { webamp, loads } = await start({ settings: { ...saved, windows: { ...saved.windows, milkdrop: { ...saved.windows.milkdrop, open: true } } } });
    assert.equal(webamp.options.__butterchurnOptions.butterchurnOpen, true);
    await webamp.butterchurn;
    assert.equal(loads.butterchurn, 1);
});

test('lazyButterchurn holds its promises until opened', async () => {
    let loaded = false;
    const gate = lazyButterchurn(false, { importButterchurn: async () => { loaded = true; return 1; } });
    const pending = gate.options.importButterchurn();
    await new Promise(resolve => setTimeout(resolve, 5));
    assert.equal(loaded, false);
    gate.open();
    assert.equal(await pending, 1);
});

test('Load list asks C# and gives Webamp nothing, so Webamp keeps its list until C# replaces it', async () => {
    const { host, webamp, events } = await start();
    host.addTracks([track('t1')], false);

    const given = await webamp.options.handleLoadListEvent();

    assert.equal(given, null);
    assert.equal(events.loads, 1);
    assert.deepEqual(host.playlistKeys(), ['t1']);
});

test('Save list sends C# the keyed tracks with their title and duration, and counts the others', async () => {
    const { host, webamp, events } = await start();
    host.addTracks([track('t1'), { ...track('t2'), name: 'From the list', durationSeconds: 61 }], false);
    webamp.tracks[0].artist = 'Artist';
    webamp.tracks[0].title = 'Song';
    webamp.tracks[0].duration = 4.2;
    webamp.appendTracks([{ url: 'blob:dropped/1', defaultName: 'dropped.mp3' }]);

    webamp.options.handleSaveListEvent([]);

    assert.deepEqual(events.saves, [{
        entries: [
            { key: 't1', title: 'Artist - Song', duration: 4.2 },
            { key: 't2', title: 'From the list', duration: 61 },
        ],
        unsaved: 1,
    }]);
});

test('a track from a playlist carries its duration to Webamp; others carry none', async () => {
    const { host, webamp } = await start();
    host.addTracks([{ ...track('t1'), durationSeconds: 12 }, track('t2')], false);
    assert.equal(webamp.tracks[0].duration, 12);
    assert.equal('duration' in webamp.tracks[1], false);
});

test('the title of a saved track is Artist - Title, the title, or the name Webamp shows', () => {
    assert.equal(trackTitle({ artist: 'A', title: 'T' }), 'A - T');
    assert.equal(trackTitle({ artist: '', title: 'T' }), 'T');
    assert.equal(trackTitle({ artist: 'A', title: null, defaultName: 'file.mp3' }), 'file.mp3');
    assert.equal(trackTitle({}), null);
});

// ---- Decision 16: Webamp's file prompts go to the OS dialogs (FilePromptInterceptor). ----

/** An element of the page: `closest` answers for the selectors it is listed under. */
const element = (...selectors) => ({ closest: selector => (selectors.includes(selector) ? {} : null) });

/** The input Webamp builds in promptForFileReferences. */
const fileInput = ({ accept = '', directory = false } = {}) => ({ type: 'file', accept, webkitdirectory: directory });

/** A page: capture listeners, an input prototype whose real click() is recorded, deferred callbacks. */
const page = () => {
    const listeners = new Map();
    const opened = [];
    const deferred = [];
    const prototype = { click() { opened.push(this); } };
    const document = {
        addEventListener: (type, listener, capture) => listeners.set(`${type}:${capture}`, listener),
        removeEventListener: (type, listener, capture) => {
            if (listeners.get(`${type}:${capture}`) === listener) {
                listeners.delete(`${type}:${capture}`);
            }
        },
    };
    const interceptor = new FilePromptInterceptor(document, prototype, { defer: callback => deferred.push(callback) });
    return {
        interceptor,
        prototype,
        opened,
        listeners,
        // The user clicks `target`; during the event, Webamp clicks `input`.
        click(target, input) {
            listeners.get('click:true')?.({ target });
            if (input) {
                prototype.click.call(input);
            }
        },
        endOfEvent() {
            deferred.splice(0).forEach(callback => callback());
        },
    };
};

test('classifyFilePrompt names the music prompts and leaves skins and presets to the browser', () => {
    assert.equal(classifyFilePrompt(fileInput(), element('#eject')), 'open');
    assert.equal(classifyFilePrompt(fileInput(), element('#webamp-context-menu')), 'open');
    assert.equal(classifyFilePrompt(fileInput(), element('.add-file', '#playlist-add-menu')), 'add-files');
    assert.equal(classifyFilePrompt(fileInput({ directory: true }), element('.add-dir')), 'add-folder');
    assert.equal(classifyFilePrompt(fileInput({ accept: '.zip, .wsz' }), element('#webamp-context-menu')), null);
    assert.equal(classifyFilePrompt(fileInput({ accept: '.eqf' }), element('#equalizer-window')), null);
});

test('ownsTarget: the window\'s element and Webamp\'s context menu, nothing else', () => {
    const inside = element();
    const surface = { contains: node => node === inside };
    assert.equal(ownsTarget(surface, inside), true);
    assert.equal(ownsTarget(surface, element('#webamp-context-menu')), true);
    assert.equal(ownsTarget(surface, element()), false);
    assert.equal(ownsTarget(surface, null), false);
    assert.equal(ownsTarget(surface, {}), false);
});

test('a music prompt of a Webamp window opens no browser picker and reaches C#', () => {
    const p = page();
    const prompts = [];
    const eject = element('#eject');
    p.interceptor.attach({ owns: target => target === eject, onPrompt: kind => prompts.push(kind) });

    p.click(eject, fileInput());

    assert.deepEqual(prompts, ['open']);
    assert.deepEqual(p.opened, []);
});

test('outside Webamp, after the event, and for skins, the browser picker opens as before', () => {
    const p = page();
    const prompts = [];
    const eject = element('#eject');
    p.interceptor.attach({ owns: target => target === eject, onPrompt: kind => prompts.push(kind) });

    // The File Explorer's upload button.
    const upload = fileInput();
    p.click(element('.explorer-upload'), upload);
    // A skin from Webamp's menu (work item 4).
    const skin = fileInput({ accept: '.zip, .wsz' });
    p.click(eject, skin);
    p.endOfEvent();
    // A file input clicked later, by something else.
    const later = fileInput();
    p.prototype.click.call(later);
    // Not a file input.
    const button = { type: 'button' };
    p.click(eject, button);

    assert.deepEqual(prompts, []);
    assert.deepEqual(p.opened, [upload, skin, later, button]);
});

test('one event asks C# once, and the wrapper goes away with the last window', () => {
    const p = page();
    const prompts = [];
    const original = p.prototype.click;
    const addFile = element('.add-file');
    const detach = p.interceptor.attach({ owns: () => true, onPrompt: kind => prompts.push(kind) });
    assert.equal(p.interceptor.installed, true);
    assert.notEqual(p.prototype.click, original);

    p.click(addFile, fileInput());
    p.prototype.click.call(fileInput());

    assert.deepEqual(prompts, ['add-files']);
    assert.equal(p.opened.length, 1);

    detach();
    assert.equal(p.interceptor.installed, false);
    assert.equal(p.prototype.click, original);
    assert.equal(p.listeners.size, 0);
});

test('a host given an interceptor reports the prompts of its element and detaches on dispose', async () => {
    const p = page();
    const requests = [];
    const surface = { contains: node => node?.inSurface === true };
    const host = await createHost(surface, { onOpenRequested: kind => requests.push(kind) }, {}, {
        Webamp: FakeWebamp,
        interceptor: p.interceptor,
        element: surface,
        createObjectURL: () => 'blob:x',
        revokeObjectURL: () => {},
    });
    const addDir = { inSurface: true, closest: () => null };

    p.click(addDir, fileInput({ directory: true }));
    assert.deepEqual(requests, ['add-folder']);

    host.dispose();
    assert.equal(p.interceptor.installed, false);
});

const skin = (key, name = key.replace(/\.wsz$/, ''), bytes = [0x50, 0x4b]) => ({ key, name, bytes: new Uint8Array(bytes) });

test('Webamp starts with the user\'s skins in its menu and the skin in use on', async () => {
    const { webamp, urls } = await start({ skins: [skin('Bento.wsz'), skin('Green.wsz')], initialSkin: 'Green.wsz' });

    assert.equal(urls.created.length, 2);
    assert.equal(urls.created[0].blob.type, 'application/x-winamp-skin');
    assert.deepEqual(webamp.options.availableSkins, [{ url: 'blob:test/1', name: 'Bento' }, { url: 'blob:test/2', name: 'Green' }]);
    assert.deepEqual(webamp.options.initialSkin, { url: 'blob:test/2' });
});

test('without a skin in use, Webamp starts on its base skin', async () => {
    const { webamp } = await start({ skins: [skin('Bento.wsz')], initialSkin: null });
    assert.equal(webamp.options.initialSkin, undefined);
    const none = await start();
    assert.deepEqual(none.webamp.options.availableSkins, []);
});

test('an added skin joins the menu in name order and is put on once Webamp drew it', async () => {
    const { host, webamp } = await start({ skins: [skin('Bento.wsz'), skin('zebra.wsz')] });

    await host.addSkin(skin('apple.wsz'), true);

    assert.deepEqual(host.skinKeys(), ['apple.wsz', 'Bento.wsz', 'zebra.wsz']);
    assert.deepEqual(webamp.dispatched.at(-1), {
        type: 'SET_AVAILABLE_SKINS',
        skins: [{ url: 'blob:test/3', name: 'apple' }, { url: 'blob:test/1', name: 'Bento' }, { url: 'blob:test/2', name: 'zebra' }],
    });
    assert.equal(webamp.skinUrl, 'blob:test/3');
    assert.equal(webamp.skinLoadedAwaited, true);
});

test('a skin added again replaces the listed one and gives its old address back; without apply nothing changes on screen', async () => {
    const { host, webamp, urls } = await start({ skins: [skin('Bento.wsz')] });

    await host.addSkin(skin('Bento.wsz', 'Bento', [1]), false);

    assert.deepEqual(host.skinKeys(), ['Bento.wsz']);
    assert.deepEqual(urls.revoked, ['blob:test/1']);
    assert.equal(webamp.skinUrl, null);
});

test('dispose gives the skins\' addresses back too', async () => {
    const { host, urls } = await start({ skins: [skin('Bento.wsz')] });
    await host.addSkin(skin('Green.wsz'), true);

    host.dispose();

    assert.deepEqual(urls.revoked.sort(), ['blob:test/1', 'blob:test/2']);
    await host.addSkin(skin('Late.wsz'), true);
    assert.deepEqual(host.skinKeys(), []);
});
