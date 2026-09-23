// Builds dotnet/tests/ShotAI.Core.Tests/Golden/archive/electron-archive.zip the way
// src/main/archive.ts packArchive does: zip.file() per file, DEFLATE level 6. archive.ts
// lets JSZip add the folder entries, dated now; here they are added first, in the same
// order and with the same fixed date as the files, so the bytes regenerate identically.
const JSZip = require('jszip');
const fs = require('fs');
const path = require('path');
const out = path.join(__dirname, 'electron-archive.zip');
const date = new Date(Date.UTC(2026, 0, 1, 0, 0, 0));
const png = (n) => Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.alloc(n, n)]);
const files = [
  ['shots/', null],
  ['shots/step-0001.png', png(3)],
  ['shots/step-0002.png', png(40)],
  ['export/', null],
  ['export/.render/', null],
  ['export/.render/r1.png', png(7)],
  ['export/My Guide.html', Buffer.from('<p>synthetic</p>\n', 'utf8')],
  ['export/\u00dcbersicht.html', Buffer.from('<p>synthetic</p>\n', 'utf8')],
];
const zip = new JSZip();
for (const [rel, bytes] of files) zip.file(rel, bytes, bytes === null ? { dir: true, date } : { date });
zip.generateAsync({ type: 'nodebuffer', compression: 'DEFLATE', compressionOptions: { level: 6 } }).then((buf) => {
  fs.writeFileSync(process.argv[2] || out, buf);
  return JSZip.loadAsync(buf);
}).then((check) => {
  for (const e of Object.values(check.files)) console.log((e.dir ? 'dir  ' : 'file ') + JSON.stringify(e.name));
});
