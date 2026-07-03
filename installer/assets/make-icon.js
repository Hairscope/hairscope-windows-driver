// Rasterizes the Hairscope favicon.svg into a multi-size app.ico for the exe.
// The motif is recolored to the brand bronze so it stays visible on both light
// (Explorer) and dark (taskbar) backgrounds. Uses sharp (bundled librsvg).
// Run:  node make-icon.js
const fs = require('fs');
const path = require('path');
const sharp = require('D:/GitHub/hairscope-clinic-web/node_modules/sharp');

const SVG_PATH = 'D:/GitHub/hairscope-clinic-platform-requirements/supporting files/logo/favicon.svg';
// Canonical icon lives with the source (build input for the exe); committed to the repo.
const OUT = path.join(__dirname, '..', '..', 'src', 'HairscopeAgent', 'app.ico');
const BRAND = '#9C754E';
const SIZES = [16, 32, 48, 64, 128, 256];

(async () => {
    // Recolor the black fill to the brand bronze.
    const svg = fs.readFileSync(SVG_PATH, 'utf8').replace(/fill:black/g, `fill:${BRAND}`);
    const svgBuf = Buffer.from(svg);

    const frames = [];
    for (const size of SIZES) {
        const png = await sharp(svgBuf, { density: 512 })
            .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
            .png()
            .toBuffer();
        frames.push({ size, png });
    }

    // Assemble ICO (PNG-compressed frames — accepted by Windows 10/11).
    const count = frames.length;
    const header = Buffer.alloc(6);
    header.writeUInt16LE(0, 0);      // reserved
    header.writeUInt16LE(1, 2);      // type = icon
    header.writeUInt16LE(count, 4);  // image count

    const dir = Buffer.alloc(16 * count);
    let offset = 6 + 16 * count;
    const bodies = [];
    frames.forEach((f, i) => {
        const e = i * 16;
        dir.writeUInt8(f.size >= 256 ? 0 : f.size, e + 0); // width (0 => 256)
        dir.writeUInt8(f.size >= 256 ? 0 : f.size, e + 1); // height
        dir.writeUInt8(0, e + 2);   // palette
        dir.writeUInt8(0, e + 3);   // reserved
        dir.writeUInt16LE(1, e + 4);  // color planes
        dir.writeUInt16LE(32, e + 6); // bits per pixel
        dir.writeUInt32LE(f.png.length, e + 8);  // size of image data
        dir.writeUInt32LE(offset, e + 12);       // offset
        offset += f.png.length;
        bodies.push(f.png);
    });

    fs.writeFileSync(OUT, Buffer.concat([header, dir, ...bodies]));
    console.log(`Wrote ${OUT} (${count} frames, ${offset} bytes)`);

    // Wizard small-image PNGs (bronze motif centered on white) — converted to BMP
    // by the companion PowerShell step (Inno requires 24-bit BMP).
    for (const size of [55, 110]) {
        const logo = await sharp(svgBuf, { density: 512 })
            .resize(Math.round(size * 0.8), Math.round(size * 0.8), { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
            .png().toBuffer();
        const png = await sharp({ create: { width: size, height: size, channels: 4, background: '#ffffff' } })
            .composite([{ input: logo, gravity: 'center' }])
            .png().toBuffer();
        fs.writeFileSync(path.join(__dirname, `wizard-small-${size}.png`), png);
        console.log(`Wrote wizard-small-${size}.png`);
    }
})().catch((e) => { console.error(e); process.exit(1); });
