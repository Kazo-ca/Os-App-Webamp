// Tests of the Webamp host module (src/Kazo.Webamp/wwwroot/js/webamp-host.js) under node, with a
// fake Webamp: the options, the Blob addresses (created, kept while listed, given back), the events.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createHost, describeTrack, trackTitle, webampOptions } from '../../src/Kazo.Webamp/wwwroot/js/webamp-host.js';

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
        this.dispatched = [];
        this.skinUrl = null;
        this.store = {
            subscribe: listener => this._on('store', listener),
            dispatch: action => this.dispatched.push(action),
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
    getPlaylistTracks() {
        return this.tracks.map((track, id) => ({
            id, url: track.url, defaultName: track.defaultName, duration: track.duration ?? null,
            artist: track.artist ?? null, title: track.title ?? null,
        }));
    }

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
    setSkinFromUrl(url) { this.skinUrl = url; }
    async skinIsLoaded() { this.skinLoadedAwaited = true; }

    removeTrack(index) {
        this.tracks.splice(index, 1);
        this._emit('store');
    }
}

const start = async (options = {}) => {
    const events = { closed: 0, minimized: 0, tracks: [], lists: [], loads: 0, saves: [], skinFiles: [], skinChoices: [] };
    const urls = { created: [], revoked: [] };
    let next = 0;
    const host = await createHost({ id: 'surface' }, {
        onClose: () => events.closed++,
        onMinimize: () => events.minimized++,
        onTrackChanged: track => events.tracks.push(track),
        onPlaylistChanged: keys => events.lists.push(keys),
        onLoadList: () => events.loads++,
        onSaveList: (entries, unsaved) => events.saves.push({ entries, unsaved }),
        onSkinFile: (name, bytes) => events.skinFiles.push({ name, bytes }),
        onSkinChosen: key => events.skinChoices.push(key),
    }, options, {
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
