// Tests of the skins bridge (src/Kazo.Webamp/wwwroot/js/webamp-skins.js) under node, with a few
// fake DOM nodes shaped like Webamp 2.3.1's context menu.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { installSkinBridge, isSkinFileName, skinMenuAction } from '../../src/Kazo.Webamp/wwwroot/js/webamp-skins.js';

/** A minimal element: tag, children, text nodes, closest() on the #webamp-context-menu li selector. */
class FakeElement {
    constructor(tagName, { id = null, text = null } = {}) {
        this.tagName = tagName;
        this.id = id;
        this.parentElement = null;
        this.children = [];
        this.childNodes = text === null ? [] : [{ nodeType: 3, textContent: text }];
    }

    append(...children) {
        for (const child of children) {
            child.parentElement = this;
            this.children.push(child);
            this.childNodes.push(child);
        }
        return this;
    }

    closest(selector) {
        assert.equal(selector, '#webamp-context-menu li');
        let menu = this;
        while (menu && menu.id !== 'webamp-context-menu') {
            menu = menu.parentElement;
        }
        return menu && this.tagName === 'LI' ? this : null;
    }
}

const li = text => new FakeElement('LI', { text });

/** Webamp's main context menu: Skins ▸ Load Skin..., separator, <Base Skin>, the skins; and another submenu. */
function contextMenu(skinNames) {
    const skins = new FakeElement('UL').append(li('Load Skin...'), li(''), li('<Base Skin>'), ...skinNames.map(li));
    const skinsItem = new FakeElement('LI').append(skins);
    skinsItem.childNodes.push({ nodeType: 3, textContent: 'Skins' });
    const play = new FakeElement('UL').append(li('File...'));
    const playItem = new FakeElement('LI').append(play);
    playItem.childNodes.push({ nodeType: 3, textContent: 'Play' });
    const root = new FakeElement('DIV', { id: 'webamp-context-menu' }).append(new FakeElement('UL').append(playItem, skinsItem, li('Exit')));
    return { root, skins, play };
}

test('skin files are .wsz and .zip, like for Webamp', () => {
    assert.equal(isSkinFileName('Bento.wsz'), true);
    assert.equal(isSkinFileName('Bento.ZIP'), true);
    assert.equal(isSkinFileName('song.mp3'), false);
    assert.equal(isSkinFileName(undefined), false);
});

test('the Skins menu items are recognised by their place', () => {
    const { skins, play } = contextMenu(['Bento', 'Green']);
    assert.deepEqual(skinMenuAction(skins.children[0]), { kind: 'load' });
    assert.equal(skinMenuAction(skins.children[1]), null);
    assert.deepEqual(skinMenuAction(skins.children[2]), { kind: 'base' });
    assert.deepEqual(skinMenuAction(skins.children[4]), { kind: 'skin', index: 1 });
    assert.equal(skinMenuAction(play.children[0]), null);
    assert.equal(skinMenuAction(new FakeElement('LI', { text: 'Load Skin...' })), null);
    assert.equal(skinMenuAction(null), null);
    assert.equal(skinMenuAction({}), null);
});

/** A window, a document and an element that record their listeners and dispatched events. */
function fakePage() {
    const listeners = { window: [], element: [] };
    const dispatched = [];
    class MouseEvent {
        constructor(type, init) { this.type = type; Object.assign(this, init); }
    }
    const view = {
        MouseEvent,
        addEventListener: (type, listener, capture) => listeners.window.push({ type, listener, capture }),
        removeEventListener: (type, listener) => { listeners.window = listeners.window.filter(l => l.listener !== listener); },
    };
    const document = { defaultView: view, dispatchEvent: event => dispatched.push(event) };
    const element = {
        ownerDocument: document,
        addEventListener: (type, listener, capture) => listeners.element.push({ type, listener, capture }),
        removeEventListener: (type, listener) => { listeners.element = listeners.element.filter(l => l.listener !== listener); },
    };
    return { listeners, dispatched, element };
}

const event = target => ({
    target,
    stopped: false,
    prevented: false,
    stopImmediatePropagation() { this.stopped = true; },
    stopPropagation() { this.stopped = true; },
    preventDefault() { this.prevented = true; },
});

test('"Load Skin…" is taken from Webamp: the menu closes and the chosen file goes to the host', async () => {
    const page = fakePage();
    const { skins } = contextMenu(['Bento']);
    const files = [];
    const file = { name: 'Green.wsz' };
    installSkinBridge(page.element, { pickFile: async () => file, onSkinFile: f => files.push(f), onMenuChoice: () => assert.fail() });
    const click = page.listeners.window.find(l => l.type === 'click');
    assert.equal(click.capture, true);

    const clicked = event(skins.children[0]);
    click.listener(clicked);
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(clicked.stopped, true);
    assert.equal(clicked.prevented, true);
    assert.equal(page.dispatched[0].type, 'click');
    assert.equal(page.dispatched[0].button, 0);
    assert.deepEqual(files, [file]);
});

test('a cancelled "Load Skin…" sends nothing; a skin picked in the menu is left to Webamp and reported', async () => {
    const page = fakePage();
    const { skins } = contextMenu(['Bento', 'Green']);
    const choices = [];
    installSkinBridge(page.element, { pickFile: async () => null, onSkinFile: () => assert.fail(), onMenuChoice: a => choices.push(a) });
    const click = page.listeners.window.find(l => l.type === 'click').listener;

    click(event(skins.children[0]));
    await new Promise(resolve => setImmediate(resolve));
    const picked = event(skins.children[4]);
    click(picked);
    click(event(skins.children[2]));

    assert.equal(picked.stopped, false);
    assert.deepEqual(choices, [{ kind: 'skin', index: 1 }, { kind: 'base' }]);
});

test('one skin dropped on Webamp goes to the host; music and several files stay Webamp\'s', () => {
    const page = fakePage();
    const files = [];
    const uninstall = installSkinBridge(page.element, { onSkinFile: f => files.push(f), onMenuChoice: () => {} });
    const drop = page.listeners.element.find(l => l.type === 'drop');
    assert.equal(drop.capture, true);

    const skinDrop = { ...event(null), dataTransfer: { files: [{ name: 'Bento.wsz' }] } };
    drop.listener(skinDrop);
    const musicDrop = { ...event(null), dataTransfer: { files: [{ name: 'song.mp3' }] } };
    drop.listener(musicDrop);
    const twoDrop = { ...event(null), dataTransfer: { files: [{ name: 'a.wsz' }, { name: 'b.wsz' }] } };
    drop.listener(twoDrop);

    assert.equal(skinDrop.stopped, true);
    assert.equal(skinDrop.prevented, true);
    assert.equal(musicDrop.stopped, false);
    assert.equal(twoDrop.stopped, false);
    assert.deepEqual(files.map(f => f.name), ['Bento.wsz']);

    uninstall();
    assert.deepEqual(page.listeners, { window: [], element: [] });
});
