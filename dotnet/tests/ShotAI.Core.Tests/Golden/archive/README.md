# Archive fixture

`electron-archive.zip` is an `archive.zip` as Electron's `src/main/archive.ts` writes it:
JSZip 3.10.1, one `zip.file()` per file (JSZip adds the `shots/`, `export/` and
`export/.render/` folder entries, attribute `0x10`), DEFLATE level 6, and the UTF-8 flag
on the one non-ASCII name. `Store/ArchiveVerifyTests` restores it natively (AC-MODEL-13's
"an Electron-archived project restores natively"). Every file in it is synthetic.

| Entry | Bytes |
|---|---|
| `shots/step-0001.png` | the 8-byte PNG signature, then 3 bytes of `0x03` |
| `shots/step-0002.png` | the signature, then 40 bytes of `0x28` |
| `export/.render/r1.png` | the signature, then 7 bytes of `0x07` |
| `export/My Guide.html` | `<p>synthetic</p>` and a newline |
| `export/` + U+00DC + `bersicht.html` | the same |

Every entry, folders included, is dated 2026-01-01 00:00 UTC, so the bytes regenerate
identically; `archive.ts` passes no date and lets JSZip add the folders, dated now. SHA-256
`372c6953b0a579d6bc2792aeae730083afd77f12cf10f9dfd101304e1c822bc2`.

## Regenerate

From the repository root, after `npm ci --ignore-scripts`:

```
NODE_PATH=node_modules node dotnet/tests/ShotAI.Core.Tests/Golden/archive/make-electron-archive.js
```
