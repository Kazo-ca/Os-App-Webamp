// Skins of the Webamp app (work item 4, doc/en/architecture.md "Skins").
//
// The C# side (SkinController, SkinLibrary) checks, copies and remembers the skins. This module only
// does what C# cannot reach: it turns a skin's bytes into a Blob address for Webamp's Skins menu,
// and it catches the two ways Webamp takes a skin from the computer, "Skins ▸ Load Skin…" and a
// .wsz dropped on Webamp, so that C# copies that skin to the user's skins folder too and refuses a
// broken one with its own message (Webamp 2.3.1 would only show a browser alert).
//
// Webamp 2.3.1 has no option for either: its Skins menu item opens a hidden file input of its own.
// The module recognises the item in Webamp's context menu (the <li> elements of
// #webamp-context-menu, built by Webamp's Skins menu: "Load Skin...", a separator, "<Base Skin>",
// then one item per skin of the list it was given, in that order).

/** File names Webamp treats as skins (its own test: /(wsz|zip)$/i). */
const SKIN_FILE = /\.(wsz|zip)$/i;

/** Label of Webamp's Skins submenu. */
const SKINS_MENU_LABEL = 'Skins';

/** Items of the Skins submenu before the skins of the list. */
const LOAD_SKIN_INDEX = 0;
const BASE_SKIN_INDEX = 2;
const FIRST_SKIN_INDEX = 3;

/**
 * Whether a file of the computer is a skin for Webamp.
 * @param {string | null | undefined} name Its file name.
 * @returns {boolean}
 */
export function isSkinFileName(name) {
    return typeof name === 'string' && SKIN_FILE.test(name);
}

/**
 * What a click in Webamp's context menu does with skins.
 * @param {any} target The clicked node.
 * @returns {{ kind: 'load' } | { kind: 'base' } | { kind: 'skin', index: number } | null}
 *          "Load Skin…", "<Base Skin>", the n-th skin of the list, or nothing about skins.
 */
export function skinMenuAction(target) {
    const item = target?.closest?.('#webamp-context-menu li');
    const submenu = item?.parentElement;
    const parent = submenu?.parentElement;
    if (!item || !parent || parent.tagName !== 'LI' || ownLabel(parent) !== SKINS_MENU_LABEL) {
        return null;
    }
    const index = Array.prototype.indexOf.call(submenu.children, item);
    if (index === LOAD_SKIN_INDEX) {
        return { kind: 'load' };
    }
    if (index === BASE_SKIN_INDEX) {
        return { kind: 'base' };
    }
    return index >= FIRST_SKIN_INDEX ? { kind: 'skin', index: index - FIRST_SKIN_INDEX } : null;
}

// The text of a submenu item itself, without the text of its submenu.
function ownLabel(element) {
    return Array.from(element.childNodes ?? [])
        .filter(node => node.nodeType === 3)
        .map(node => node.textContent)
        .join('')
        .trim();
}

/**
 * Lets the user choose one skin file of the computer, like Webamp's own "Load Skin…" did.
 * @param {Document} document The page.
 * @returns {Promise<File | null>} The file, or null when the user cancelled.
 */
export function pickSkinFile(document) {
    return new Promise(resolve => {
        const input = document.createElement('input');
        input.type = 'file';
        input.accept = '.wsz,.zip';
        input.addEventListener('change', () => resolve(input.files?.[0] ?? null), { once: true });
        input.addEventListener('cancel', () => resolve(null), { once: true });
        input.click();
    });
}

/**
 * Catches "Load Skin…", the choice of a skin in the menu, and a skin dropped on Webamp.
 * @param {HTMLElement} element The element Webamp renders into.
 * @param {{ onSkinFile: (file: File) => void, onMenuChoice: (action: { kind: 'base' } | { kind: 'skin', index: number }) => void,
 *           pickFile?: () => Promise<File | null> }} handlers What the host hears.
 * @returns {() => void} Removes the listeners.
 */
export function installSkinBridge(element, handlers) {
    const document = element.ownerDocument;
    const view = document.defaultView;
    const pickFile = handlers.pickFile ?? (() => pickSkinFile(document));

    // Capture on the window runs before Webamp's own handlers (React listens lower, on its roots).
    const onClick = event => {
        const action = skinMenuAction(event.target);
        if (action === null) {
            return;
        }
        if (action.kind !== 'load') {
            // Webamp puts the skin on itself; the host only remembers which one.
            handlers.onMenuChoice(action);
            return;
        }
        event.stopImmediatePropagation();
        event.preventDefault();
        // Webamp closes its context menu on a click of the page, which was just stopped.
        document.dispatchEvent(new view.MouseEvent('click', { bubbles: true, button: 0 }));
        pickFile().then(file => {
            if (file) {
                handlers.onSkinFile(file);
            }
        });
    };

    // A single skin file dropped on any of Webamp's windows: the same as "Load Skin…".
    const onDrop = event => {
        const files = event.dataTransfer?.files;
        if (files?.length !== 1 || !isSkinFileName(files[0].name)) {
            return;
        }
        event.stopPropagation();
        event.preventDefault();
        handlers.onSkinFile(files[0]);
    };

    view.addEventListener('click', onClick, true);
    element.addEventListener('drop', onDrop, true);
    return () => {
        view.removeEventListener('click', onClick, true);
        element.removeEventListener('drop', onDrop, true);
    };
}
