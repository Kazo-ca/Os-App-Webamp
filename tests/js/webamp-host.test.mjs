// Tests of the Webamp host module (src/Kazo.Webamp/wwwroot/js/webamp-host.js) under node, with a
// fake Webamp: the options, the Blob addresses (created, kept while listed, given back), the events.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { FilePromptInterceptor, classifyFilePrompt, createHost, describeTrack, ownsTarget, webampOptions } from '../../src/Kazo.Webamp/wwwroot/js/webamp-host.js';

/** Just enough of Webamp 2.3.1's public API, with a store that notifies on every change. */
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
        this.listeners = { close: [], minimize: [], track: [], store: [] };
        this.store = {
            subscribe: listener => this._on('store', listener),
        };
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
    onTrackDidChange(listener) { return this._on('track', listener); }
    getPlaylistTracks() { return this.tracks.map((track, id) => ({ id, url: track.url, defaultName: track.defaultName })); }

    // Like Webamp, a batch arrives one track at a time, the store notifying after each one.
    appendTracks(tracks) {
        for (const track of tracks) {
            this.tracks.push(track);
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

    setCurrentTrack(index) { this.current = index; }
    play() { this.playing = true; }
    stop() { this.playing = false; }
    dispose() { this.disposed = true; }

    removeTrack(index) {
        this.tracks.splice(index, 1);
        this._emit('store');
    }
}

const start = async () => {
    const events = { closed: 0, minimized: 0, tracks: [], lists: [] };
    const urls = { created: [], revoked: [] };
    let next = 0;
    const host = await createHost({ id: 'surface' }, {
        onClose: () => events.closed++,
        onMinimize: () => events.minimized++,
        onTrackChanged: track => events.tracks.push(track),
        onPlaylistChanged: keys => events.lists.push(keys),
    }, {}, {
        Webamp: FakeWebamp,
        createObjectURL: blob => {
            const url = `blob:test/${++next}`;
            urls.created.push({ url, blob });
            return url;
        },
        revokeObjectURL: url => urls.revoked.push(url),
    });
    return { host, webamp: FakeWebamp.last, events, urls };
};

const track = (key, bytes = [1, 2, 3]) => ({ key, name: `${key}.mp3`, mimeType: 'audio/mpeg', bytes: new Uint8Array(bytes) });

test('Webamp is created without its page-wide hotkeys and without Add URL, then rendered into the element', async () => {
    const { webamp } = await start();
    assert.equal(webamp.options.enableHotkeys, false);
    assert.equal(webamp.options.handleAddUrlEvent(), null);
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

test('close, minimize and track changes reach C#', async () => {
    const { webamp, events } = await start();
    webamp._emit('close');
    webamp._emit('minimize');
    webamp._emit('track', { url: 'blob:test/1', metaData: { artist: 'Artist', title: 'Title', album: null } });
    webamp._emit('track', null);
    assert.equal(events.closed, 1);
    assert.equal(events.minimized, 1);
    assert.deepEqual(events.tracks, [{ artist: 'Artist', title: 'Title' }, null]);
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

test('describeTrack keeps the artist and the title only', () => {
    assert.equal(describeTrack(null), null);
    assert.deepEqual(describeTrack({ metaData: {} }), { artist: null, title: null });
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
