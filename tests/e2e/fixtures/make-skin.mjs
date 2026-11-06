// Builds small Winamp skins for the end-to-end runs (no community skin in the repository, decision
// 11): a .wsz is a ZIP archive of bitmaps; this one holds only main.bmp, the main window, in one
// colour. Webamp takes the rest from its base skin.
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

/**
 * A 24-bit BMP of one colour.
 * @param {number} width
 * @param {number} height
 * @param {[number, number, number]} rgb
 * @returns {Buffer}
 */
export function solidBitmap(width, height, [r, g, b]) {
    const rowSize = Math.ceil((width * 3) / 4) * 4;
    const pixels = Buffer.alloc(rowSize * height);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            pixels.set([b, g, r], y * rowSize + x * 3);
        }
    }
    const header = Buffer.alloc(54);
    header.write('BM', 0, 'ascii');
    header.writeUInt32LE(54 + pixels.length, 2);
    header.writeUInt32LE(54, 10);
    header.writeUInt32LE(40, 14);
    header.writeInt32LE(width, 18);
    header.writeInt32LE(height, 22);
    header.writeUInt16LE(1, 26);
    header.writeUInt16LE(24, 28);
    header.writeUInt32LE(pixels.length, 34);
    return Buffer.concat([header, pixels]);
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
    let c = n;
    for (let k = 0; k < 8; k++) {
        c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    return c >>> 0;
});

function crc32(bytes) {
    let crc = 0xffffffff;
    for (const byte of bytes) {
        crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
    }
    return (crc ^ 0xffffffff) >>> 0;
}

/**
 * A ZIP archive whose files are stored (not compressed).
 * @param {{ name: string, data: Buffer }[]} files
 * @returns {Buffer}
 */
export function storedZip(files) {
    const locals = [];
    const centrals = [];
    let offset = 0;
    for (const { name, data } of files) {
        const fileName = Buffer.from(name, 'utf8');
        const crc = crc32(data);
        const local = Buffer.alloc(30);
        local.writeUInt32LE(0x04034b50, 0);
        local.writeUInt16LE(20, 4);
        local.writeUInt32LE(crc, 14);
        local.writeUInt32LE(data.length, 18);
        local.writeUInt32LE(data.length, 22);
        local.writeUInt16LE(fileName.length, 26);
        const central = Buffer.alloc(46);
        central.writeUInt32LE(0x02014b50, 0);
        central.writeUInt16LE(20, 4);
        central.writeUInt16LE(20, 6);
        central.writeUInt32LE(crc, 16);
        central.writeUInt32LE(data.length, 20);
        central.writeUInt32LE(data.length, 24);
        central.writeUInt16LE(fileName.length, 28);
        central.writeUInt32LE(offset, 42);
        locals.push(local, fileName, data);
        centrals.push(central, fileName);
        offset += local.length + fileName.length + data.length;
    }
    const directory = Buffer.concat(centrals);
    const end = Buffer.alloc(22);
    end.writeUInt32LE(0x06054b50, 0);
    end.writeUInt16LE(files.length, 8);
    end.writeUInt16LE(files.length, 10);
    end.writeUInt32LE(directory.length, 12);
    end.writeUInt32LE(offset, 16);
    return Buffer.concat([...locals, directory, end]);
}

/**
 * Writes a one-colour skin, and a broken one, in a new temporary folder.
 * @param {string} name The skin's file name, for example "Green.wsz".
 * @param {[number, number, number]} rgb The colour of its main window.
 * @returns {{ skin: string, broken: string }} Their paths.
 */
export function makeSkins(name, rgb) {
    const folder = mkdtempSync(path.join(tmpdir(), 'webamp-skins-'));
    const skin = path.join(folder, name);
    writeFileSync(skin, storedZip([{ name: 'main.bmp', data: solidBitmap(275, 116, rgb) }]));
    const broken = path.join(folder, 'broken.wsz');
    writeFileSync(broken, 'this is not a skin\n');
    return { skin, broken };
}
