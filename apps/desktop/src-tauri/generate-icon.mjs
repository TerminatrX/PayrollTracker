/**
 * Generates the source app icon as a PNG, with no image-library dependency.
 *
 * The mark is a pay-stub motif: a dark rounded tile with light rows of differing length,
 * one highlighted in the accent green to read as a posted total. Colours come from the
 * Pro-Density Enterprise palette in DESIGN.md.
 */
import { deflateSync } from "node:zlib";
import { writeFileSync } from "node:fs";

const SIZE = 512;

const SURFACE = [19, 27, 46, 255]; // #131b2e
const PRIMARY = [173, 198, 255, 255]; // #adc6ff
const SECONDARY = [78, 222, 163, 255]; // #4edea3
const TRANSPARENT = [0, 0, 0, 0];

/** Signed distance from a rounded rectangle; negative is inside. */
function roundedRectInside(x, y, left, top, right, bottom, radius) {
  const cx = Math.max(left + radius, Math.min(x, right - radius));
  const cy = Math.max(top + radius, Math.min(y, bottom - radius));
  const insideCore =
    x >= left && x <= right && y >= top && y <= bottom;

  if (!insideCore) return false;

  const nearCorner =
    (x < left + radius || x > right - radius) &&
    (y < top + radius || y > bottom - radius);

  if (!nearCorner) return true;

  return Math.hypot(x - cx, y - cy) <= radius;
}

function pixelAt(x, y) {
  // Tile
  if (!roundedRectInside(x, y, 24, 24, SIZE - 24, SIZE - 24, 96)) {
    return TRANSPARENT;
  }

  // Rows of a pay stub: three primary rows plus one accent "total" row.
  const rows = [
    { top: 140, height: 34, left: 112, right: 400, color: PRIMARY },
    { top: 212, height: 34, left: 112, right: 340, color: PRIMARY },
    { top: 284, height: 34, left: 112, right: 368, color: PRIMARY },
    { top: 356, height: 34, left: 112, right: 300, color: SECONDARY },
  ];

  for (const row of rows) {
    if (
      roundedRectInside(
        x,
        y,
        row.left,
        row.top,
        row.right,
        row.top + row.height,
        row.height / 2,
      )
    ) {
      return row.color;
    }
  }

  return SURFACE;
}

// Raw image data: each scanline prefixed with filter byte 0 (None).
const raw = Buffer.alloc(SIZE * (SIZE * 4 + 1));
let offset = 0;

for (let y = 0; y < SIZE; y++) {
  raw[offset++] = 0;
  for (let x = 0; x < SIZE; x++) {
    const [r, g, b, a] = pixelAt(x, y);
    raw[offset++] = r;
    raw[offset++] = g;
    raw[offset++] = b;
    raw[offset++] = a;
  }
}

const CRC_TABLE = (() => {
  const table = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c;
  }
  return table;
})();

function crc32(buf) {
  let c = -1;
  for (const byte of buf) {
    c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  }
  return (c ^ -1) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);

  const typeAndData = Buffer.concat([Buffer.from(type, "ascii"), data]);

  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(typeAndData));

  return Buffer.concat([length, typeAndData, crc]);
}

const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(SIZE, 0);
ihdr.writeUInt32BE(SIZE, 4);
ihdr[8] = 8; // bit depth
ihdr[9] = 6; // colour type: RGBA
ihdr[10] = 0; // deflate
ihdr[11] = 0; // adaptive filtering
ihdr[12] = 0; // no interlace

const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk("IHDR", ihdr),
  chunk("IDAT", deflateSync(raw, { level: 9 })),
  chunk("IEND", Buffer.alloc(0)),
]);

writeFileSync(new URL("./app-icon.png", import.meta.url), png);
console.log(`wrote app-icon.png (${SIZE}x${SIZE}, ${png.length} bytes)`);
