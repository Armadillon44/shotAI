# 09 Exports and the shareable package

> Spec for the native rewrite. Sources read: `src/main/export.ts` (925 lines), `src/main/export-css.ts` (155), `src/main/export-geometry.ts` (284), `src/main/avif-encode.ts` (141), `src/main/export-docx.ts` (256), `src/main/export-pptx.ts` (259), `src/main/export-package.ts` (204), `src/shared/export-theme.ts` (109). Tests: `src/main/export-css.test.ts` (177), `src/main/export-geometry.test.ts` (220), `src/main/export-palette-source.test.ts` (187), `src/shared/export-theme.test.ts` (261), `src/shared/brand-narrowing.test.ts` (47). Supporting reads: `src/shared/doc-scale.ts` (166), `src/shared/theme-palette.ts` (530), `src/shared/brand-colors.generated.ts` (184), `src/main/render-gate.ts` (49), `src/main/paths.ts` (44, `brandFontPath` `:29-36`), `src/main/ipc.ts` (981; `:176-184`, `:540-635`), `src/shared/ipc.ts` (624; `:128-172`, `:405-458`), `src/main/settings.ts` (434; `:46-48`, `:389-393`), `src/main/project-store.ts` (1099; `coerceManifest` `:133-160`, `createProjectFromImport` `:345-381`), `src/shared/project.ts` (554; `:222-247`), `src/renderer/project/ProjectDetail.tsx` (701; `:21-28`, `:259-355`). Library behavior the Electron output depends on, read in place (installed versions): `docx` 9.7.1 (`dist/index.mjs`: `DefaultStylesFactory`, `HeadingStyle`, `TitleStyle`, core properties defaults, `ImageRun` EMU conversion), `pptxgenjs` 4.0.1 (`dist/pptxgen.es.js`: `inch2Emu`, `getSmartParseNumber`, `rectRadius` adjust, `genXmlBodyProperties`, `makeXmlTheme`, text line splitting, `DEF_*` constants), `@jsquash/avif` 2.1.1 (`meta.js` `defaultOptions`, `encode.js`), `jszip` 3.10.1 (`lib/load.js` name sanitizing, `lib/utils.js` `resolve`). The two CSS generators were also evaluated directly (Node 22 `--experimental-strip-types`) to produce the byte counts and hashes in 3.4. Context: `docs/NATIVE-WINDOWS-FEASIBILITY.md` (274), `dotnet/README.md` (53), `docs/native/spec/01-model-store.md`, `02-capture.md`, `03-windows-shell.md`, `04-editor-redaction.md`, `05-report.md`, `06-home-settings-ui.md`. Commits read (`git show`): `66d7376` (#56/#57/#58 AVIF and KB paste layout, including the WebP and JPEG detours), `c3f7707` (#52 sized plain images), `f9d5642` (#49 macOS dimensions), `4259bd2` (#45 sections, #46 centered captures), `2e3429d` (#40/#41 step cards, Aptos), `f9f8b10` (#37/#39 save anywhere, self-contained Markdown, bulk), `0e56d05` (#42 plain CSS), `ec76adb` (#70 document scale), `b6ddce5` and `e93093a` (#70 Word clipping, A4), `3c15f57`, `5dec55e`, `32d16b0`, `b51e9fb`, `b244010`, `88dd344`, `1025bfe` (#77 phases 0, 0b, ramp collapse, 4, 1b, 3), `18f3c4d` (#90/#99 unknown callout), `77adda3` (#95/#106 unknown brand preserved). macOS (read-only, `/home/user/armadillon44/shotai_macos`): `Packages/ExportKit/Package.swift` (26), `Sources/ExportKit/{Export.swift (117), ExportCollector.swift (77), ExportGeometry.swift (130), ExportModel.swift (94), ExportPackage.swift (211), ExportText.swift (70), ExportTheme.swift (222), HTMLExport.swift (350), MarkdownExport.swift (109), PdfExport.swift (595)}`, `Sources/PdfSelfTest/main.swift` (96), `Tests/ExportKitTests/*` (6 files, test names), `Packages/ShotModel/Sources/ShotModel/{DocScale.swift, Zip.swift}` (the parts used by ExportKit). Microsoft Learn (confirmed 2026-09-23): `CoreWebView2.PrintToPdfAsync(string, CoreWebView2PrintSettings)` returns `Task<bool>`, only one print per WebView at a time, overwrites an existing file; `CoreWebView2PrintSettings` defaults (PageWidth 8.5 in, PageHeight 11 in, each margin 1 cm "~0.4 inches", `ShouldPrintBackgrounds` false, `ShouldPrintHeaderAndFooter` false, `ScaleFactor` 1.0, portrait); `CoreWebView2Settings.IsScriptEnabled` (default true; injected scripts still run); `NavigateToString` limit of 2 MB; Open XML SDK `WordprocessingDocument`, `MainDocumentPart.AddImagePart`, `DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent` (EMU), `PresentationDocument.Create(path, PresentationDocumentType.Presentation)` with the minimum part set (presentation, presentation properties, slide master, slide layout, theme). Verification pass (2026-09-23, adversarial): every source file above re-read end to end; the 26 CSS digests, the derived-width table (3.2), the `clampScale` examples and the locale strings of 2.18 re-evaluated with Node 22.22.2 (ICU 78.2) against the Electron source; the verbatim `docCss` and `plainCss` templates diffed mechanically against `export-css.ts:91-125` and `:143-150` (identical); library defaults re-read in place (`docx` `DefaultStylesFactory`, section and core property defaults, `tcMar` child order; `pptxgenjs` text splitting, `genXmlBodyProperties`, `rectRadius`, core properties; `jszip` `load.js`, `zipEntry.js` `processAttributes`, `object.js` `fileAdd`, `utils.js` `resolve`). Microsoft Learn additionally confirmed: `PrintToPdfAsync` "If the application exits before printing is complete, the file is not saved" and `CoreWebView2.PrintToPdfStreamAsync(CoreWebView2PrintSettings)` exists; `AddWebResourceRequestedFilter(string, CoreWebView2WebResourceContext)` is deprecated in favor of the overload taking `CoreWebView2WebResourceRequestSourceKinds`; the default WebView2 user data folder for WPF is `<exe path>.WebView2` next to the executable; Open XML SDK core properties are set through `OpenXmlPackage.PackageProperties` (`IPackageProperties.Creator`, `LastModifiedBy`, `Revision`, `Modified`); `System.IO.Compression` validates ZIP CRC-32 only from .NET 11 (not on .NET 10), exposes `ZipArchiveEntry.CompressionMethod` only from .NET 11, and since .NET Core 3.0 truncates an entry's decompressed data at the uncompressed size in its header. Status: extracted and verified.

**Notation used in this document.**

- Several Electron strings contain U+2014 (EM DASH) or U+2192 (RIGHTWARDS ARROW). This document never prints those characters. Where one occurs in a quoted string it is written as the escape `\u2014` or `\u2192`, and the C# literal must contain that exact character (C# accepts the same escape in a string literal). The callout glyphs are given by code point: note `\u2139` (INFORMATION SOURCE), caution `\u26A0` (WARNING SIGN), warning `\u2501` (BOX DRAWINGS HEAVY HORIZONTAL), section the empty string (`src/shared/project.ts:237-242`).
- `round(x)` is JavaScript `Math.round` (half up), ported as `JsMath.Round` from spec 01 7.2.3, never C# `Math.Round`. `floor` is `Math.Floor`. `trim` is JavaScript `String.prototype.trim`, ported as `JsString.Trim` (spec 01). `JS\s` is the ECMAScript whitespace class `[\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]`; .NET `\s` is a different set and must never be used where the source says `\s`.
- A CSS px is 1/96 inch. A twip is 1/1440 inch. An EMU is 1/914400 inch; 1 px = 9525 EMU. A docx half-point `sz` of N is N/2 pt. A pptx `sz` of N is N/100 pt.
- `s` is the project's document scale (`manifest.displayScale`, always passed through `clampScale`, spec 05 and 01). Monitor DPI never enters any formula here.
- Spec numbers: 01 model and store, 02 capture, 03 windows and shell, 04 editor and redaction, 05 report and project detail, 06 home and settings UI, 07 SOP generation, 08 brand and theme (the brand palette, `contract/brand.json` generation, `pinnedBrand`; spec 05 calls it "the brand spec"; spec 01 folds it into 10), 09 this spec, 10 settings, logging, update check and diagnostics, 11 service boundary and threading, 12 packaging, installer and CI. If PLAN.md numbers them differently, the names win.
- Classifications: **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, justified), **ELECTRON-ONLY** (disappears natively; the replacement for its intent is named).

## 1. Scope

**Owns**

| Area | Electron location |
|---|---|
| The six document formats: styled HTML (`html`), HTML for Word (`html-plain`), PDF (`pdf`), Markdown (`markdown`), Word (`docx`), PowerPoint (`pptx`), including every byte of the HTML and Markdown they emit | `src/main/export.ts:423-776`, `src/main/export-docx.ts:1-256`, `src/main/export-pptx.ts:1-259` |
| The shared fail-closed step collector and the export numbering rule | `src/main/export.ts:350-420` |
| The report zoom and pan reproduced as a static crop | `src/main/export.ts:190-209`, `src/main/export-geometry.ts:94-117` |
| The HTML image embed policy (resample, AVIF at 2x, JPEG fallback, PNG for Word, full resolution for PDF) and the size attributes | `src/main/export.ts:246-348`, `src/main/export-geometry.ts:119-284` |
| The AVIF encoder integration | `src/main/avif-encode.ts:1-141` |
| The two export stylesheets (`docCss`, `plainCss`) and the column-on-every-block rule | `src/main/export-css.ts:1-155` |
| The export theme (brand to palette, radii and font stacks) and the brand precedence at export time | `src/shared/export-theme.ts:1-109`, `src/main/export.ts:816-827` |
| Word image geometry (A4, 576 px ceiling) | `src/main/export-geometry.ts:28-92` |
| Export file naming (`safeFileBase`, collision numbering for files and folders), destinations (Save dialog, one shared folder, each project's own folder), reveal after export, progress events, the "Created on" line with the byline | `src/main/export.ts:37-145`, `:778-925`, `src/main/ipc.ts:540-610` |
| The PDF renderer (print copy, brand face injection, Letter, backgrounds, empty-PDF fail-closed, temp file hygiene) | `src/main/export.ts:613-692` |
| The shareable package: export (safe and with originals), the zip layout and marker, import validation (size caps, marker, manifest, folder whitelist, magic bytes) and the Import dialog | `src/main/export-package.ts:1-204`, `src/main/ipc.ts:612-635` |

**Does not own**

| Concern | Owner |
|---|---|
| Flattening every shot before an export (`ensureFlattened`) and the render gate (`resolveSendableRender`) | 04 (09 calls the gate; 09 never flattens) |
| `getProjectForRead`, `coerceManifest` (`ManifestCodec.Decode`), `createProjectFromImport` (the extraction, confinement and manifest restamping), `PathConfine`, `AtomicFile`, the write queue | 01 |
| The export button, the export menu, the package dialog and its checkbox, the "Exporting\u2026 n/m" label, the error notice | 05 |
| Row export, bulk export flows, bulk progress, the Import button and File then Import Project | 06 |
| The brand palettes, radii, font families, `pinnedBrand`, `coerceBrand`, `hexNoHash`, `DocScale` derivations | 08 (palette), 05 (`DocScale.Widths`, shared) |
| `settings.json` fields `userName`, `includeNameInReports`, `brand` | 10 |
| The Explorer reveal and open-folder calls, `AppPaths.BrandFontPath()`, window registration for capture exclusion | 03 |
| Bundling `Archivo.ttf`, `OFL.txt`, the native AVIF DLL and the WebView2 runtime dependency in the installer; CI jobs | 12 |

Specs this one touches: 01, 03, 04, 05, 06, 08, 10, 11, 12.

## 2. Reference behavior (Electron)

### 2.1 Entry points

The format type is `'html' | 'html-plain' | 'pdf' | 'markdown' | 'docx' | 'pptx'` (`src/shared/ipc.ts:129`). The main process validates it with `parseExportFormat`, which throws `` `format must be one of: ${EXPORT_FORMATS.join(', ')}` `` (`src/main/ipc.ts:176-184`). ELECTRON-ONLY: natively the format is a C# enum and cannot be invalid.

| Channel | Caller | Call | Citation |
|---|---|---|---|
| `projects:export` | report export menu (05), Home row export (06) | `exportProject(path, format, { saveAs: true, brand: await getBrand(), onProgress })`; progress is sent to the requesting window as `projects:export-progress` unless it was destroyed | `src/main/ipc.ts:558-574` |
| `projects:export-to-dir` | Home bulk "One shared folder" (06) | `exportProject(path, format, { targetDir: dir, reveal: false, brand })`, no progress | `:576-588` |
| `projects:export-to-own-folder` | Home bulk "Each project's own folder" (06) | `exportProject(path, format, { reveal: false, brand })`, no progress | `:590-600` |
| `projects:choose-export-dir` | Home bulk, before the run | `chooseExportDirectory()` | `:602-605`, `src/main/export.ts:133-145` |
| `projects:reveal-export-dir` | Home bulk, after the run | `revealExportDir(dir)` | `:607-610`, `src/main/export.ts:918-925` |
| `projects:export-package` | report package dialog (05) | `exportPackage(path, includeOriginals === true)` | `:612-618` |
| `projects:import-package` | Home Import (06) | open dialog, then `importPackage(filePaths[0])`, or `null` when cancelled | `:620-635` |

The brand passed as `brand` is read from settings at every export (`brandForExport = async () => getBrand()`, `src/main/ipc.ts:556`), so a Settings change applies to the next export without a restart. REQUIRED.

### 2.2 `exportProject` state machine

`exportProject(projectPath, format, opts)` (`src/main/export.ts:789-910`). `reveal = opts.reveal ?? true` (`:814`).

| # | State | Event | Guard | Action | Next |
|---|---|---|---|---|---|
| 1 | Start | call | | `{dir, manifest} = await getProjectForRead(projectPath)` (01; throws for an unknown path) | Theme |
| 2 | Theme | | | `theme = exportTheme(pinnedBrand(manifest.theme) ?? coerceBrand(opts.brand))` (2.3) | Collect |
| 3 | Collect | | | `items = await collectSteps(dir, manifest)` (2.4); any throw ends the export with that message, BEFORE any dialog | Name |
| 4 | Name | | | `base = safeFileBase(manifest.title)`; `generatedAt = new Date().toLocaleString()`; `byline = await getReportByline()`; `createdLine` (2.18); `exportDir = <dir>/export`; `mkdir -p exportDir`; `stembase = format === 'html-plain' ? base + '-plain' : base`; `ext = extFor(format)` | Markdown or File |
| 5a | Markdown | | `format === 'markdown' && opts.saveAs` | Save dialog (2.16) with title `Export Markdown (saved as a folder with its images)`, `defaultPath = exportDir/<base>.md`, filter `Markdown` `md` | 5b or Canceled |
| 5b | Markdown | dialog result | chosen `filePath` | `stem = basename(filePath).replace(/\.md$/i, '') \|\| base`; `folder = dirname(filePath)/stem`; `mkdir -p folder` (an existing folder is reused, see EDGE-EXP-22) | WriteMd |
| 5c | Markdown | | `format === 'markdown' && !opts.saveAs` | `parent = opts.targetDir ?? exportDir`; `mkdir -p parent`; `folder = parent/nextAvailableDir(parent, base)`; `mkdir -p folder` | WriteMd |
| 5d | WriteMd | | | `outputPath = buildMarkdown(manifest, items, folder, basename(folder), createdLine)` (2.12); log info `` `exported markdown \u2192 ${outputPath}` `` | Done |
| 6a | File | | `opts.saveAs` | Save dialog titled `Export`, `defaultPath = exportDir/<stembase><ext>`, `filters = dialogFilters(format)` | 6b or Canceled |
| 6b | File | dialog result | chosen `filePath` | `outputPath = filePath` (as returned; overwrite confirmation is the dialog's) | Build |
| 6c | File | | `!opts.saveAs` | `targetDir = opts.targetDir ?? exportDir`; `mkdir -p targetDir`; `outputPath = targetDir/<nextAvailableStem(targetDir, stembase, ext)><ext>` | Build |
| 7 | Build | | by format | `docx`: write `buildDocx(...)`; `pptx`: write `buildPptx(...)`; `html-plain`: write `buildPlainHtmlDoc(manifest, items, theme)` as UTF-8; `html`: write `buildHtmlDoc(manifest, items, createdLine, htmlEmbedPolicy('html', s), opts.onProgress, theme)` as UTF-8; `pdf`: `htmlToPdf(dir, buildHtmlDoc(manifest, items, createdLine, htmlEmbedPolicy('pdf', s), opts.onProgress, theme), outputPath, theme)` | Done |
| 8 | Done | | | log info `` `exported ${format} \u2192 ${outputPath}` `` (not for Markdown, which logged in 5d); if `reveal`, `shell.showItemInFolder(outputPath)`; return `{format, outputPath}` | end |
| 9 | Canceled | dialog cancelled or empty path | | return `{format, outputPath: '', canceled: true}`; nothing written (the `export/` folder may have been created in step 4) | end |

Where `s = clampScale(manifest.displayScale)`. Every row is REQUIRED except as the native design (7.3) notes: natively the write in step 7 is atomic (IMPROVEMENT D-EXP-6) and the step 3 read waits for the project's pending writes (INV-EXP-28).

`extFor` (`src/main/export.ts:90-103`): `docx` `.docx`, `pptx` `.pptx`, `markdown` `.md`, `pdf` `.pdf`, `html` and `html-plain` `.html`.

`dialogFilters` (`:106-119`): `docx` `{name: 'Word Document', extensions: ['docx']}`, `pptx` `{name: 'PowerPoint', extensions: ['pptx']}`, `markdown` `{name: 'Markdown', extensions: ['md']}`, `pdf` `{name: 'PDF', extensions: ['pdf']}`, `html` and `html-plain` `{name: 'HTML', extensions: ['html']}`.

`showSaveDialog` parents the dialog to the focused window when there is one (`:122-127`). REQUIRED (natively: owner = the window that asked).

### 2.3 Brand resolution

`theme = exportTheme(pinnedBrand(manifest.theme) ?? coerceBrand(opts.brand))` (`src/main/export.ts:827`).

- `pinnedBrand(v)` returns `v` when `isBrandId(v)`, else `null` (`src/shared/theme-palette.ts:289-291`). `isBrandId(v)` is `typeof v === 'string' && Object.hasOwn(BRANDS, v)` (`:267-269`), so prototype member names (`toString`, `__proto__`, ...) are not brands (#89).
- `coerceBrand(v)` returns `v` when `isBrandId(v)`, else `DEFAULT_BRAND` = `'shotAI'` (`:247`, `:272-274`).
- The narrowing sits INSIDE the `??` (#95, `77adda3`): an unrecognised pin such as `'solarpunk'` (kept on disk since #106) yields `null`, so the app preference wins; `coerceBrand(manifest.theme ?? opts.brand)` would have produced the default brand for an LFI user.

| `manifest.theme` | app brand | exported brand |
|---|---|---|
| absent | `lfi` | `lfi` |
| `'shotAI'` | `lfi` | `shotAI` (an explicit pin of the default wins) |
| `'lfi'` | `shotAI` | `lfi` |
| `'solarpunk'` | `lfi` | `lfi` |
| `42` (non-string) | `lfi` | `lfi` |
| absent | missing or unknown | `shotAI` |

`exportTheme(brand)` (`src/shared/export-theme.ts:62-72`): `id = coerceBrand(brand)`; returns `{ brand: id, palette: BRANDS[id].light, radii: BRANDS[id].radii, fontStack: fontStackFor(id), plainFontStack: plainFontStackFor(id), fontFamily: BRANDS[id].font.family }`. ALWAYS the light palette, whatever the app appearance (`:6-10`): a dark SOP is unreadable printed. REQUIRED.

- `fontStackFor(b)` = `[...(family ? ['"' + family + '"'] : []), ...fallbacks].join(',')` (`:56-59`). shotAI: `-apple-system,"Segoe UI",Roboto,Helvetica,Arial,sans-serif`. LFI: `"Archivo","Helvetica Neue",Helvetica,Arial,"Liberation Sans",sans-serif`.
- `plainFontStackFor(b)` = `[...(family ? ['"' + family + '"'] : []), 'Arial', 'Helvetica', 'sans-serif'].join(',')` (`:94-97`). shotAI: `Arial,Helvetica,sans-serif`. LFI: `"Archivo",Arial,Helvetica,sans-serif`. Arial-first is a compatibility decision (#42).
- `chipRadiusCss(radii)` = `radii.chip === null ? '50%' : radii.chip + 'px'` (`:107-109`). shotAI `50%`, LFI `8px`.
- `DEFAULT_EXPORT_THEME = exportTheme('shotAI')` (`:82`).

The brand values used below come from `contract/brand.json` through the generated table (`src/shared/brand-colors.generated.ts`); spec 08 owns them. The ones every export reads:

| Role | shotAI light | LFI light |
|---|---|---|
| `accent` | `#6344f1` | `#b46b3e` |
| `accentTint` | `#efeafe` | `#f6ede5` |
| `onAccent` | `#ffffff` | `#ffffff` |
| `ink` | `#191826` | `#47443e` |
| `ink2` | `#5a5772` | `#6f695f` |
| `ink3` | `#6f6c88` | `#756c5c` |
| `hair` | `#e7e4f2` | `#d8d2c6` |
| `controlBd` | `#cbc7db` | `#c9c1b3` |
| `surface` | `#ffffff` | `#ffffff` |
| `surface2` | `#faf9ff` | `#faf8f3` |
| `noteBg`, `noteBd`, `noteFg` | `#ecfdf5`, `#6ee7b7`, `#065f46` | `#e9f1eb`, `#3e7d5a`, `#2b5b40` |
| `cautBg`, `cautBd`, `cautFg` | `#fffbeb`, `#fcd34d`, `#92400e` | `#f7efe6`, `#c79a72`, `#8a5f35` |
| `warnBg`, `warnBd`, `warnFg` | `#fef2f2`, `#fca5a5`, `#991b1b` | `#f7eae7`, `#c97f72`, `#7f3227` |
| radii `card`, `figure`, `chip` | 10, 8, null | 8, 6, 8 |
| font `family` | null | `Archivo` |

### 2.4 The step collector

`collectSteps(dir, manifest)` (`src/main/export.ts:357-420`). THE single choke point every document format consumes (the package has its own loop, 2.21). REQUIRED in full.

```
items = []; stepNo = 0
for step in manifest.steps (in array order):
  if step.kind === 'text':
    heading = trim(step.heading ?? ''); body = trim(step.body ?? '')
    if isCalloutKind(step.callout):                      // not truthiness (#90, 18f3c4d)
      if step.callout === 'section' && !heading && !body: continue   // stray divider
      items.push({kind:'text', heading, body, callout: step.callout}) // un-numbered
      continue
    if !heading && !body: continue                        // empty plain text: skipped, no number
    stepNo++; items.push({kind:'text', n: stepNo, heading, body}); continue
  // any other kind is a shot
  stepNo++
  {abs, mediaType, ext} = resolveSendableRender(dir, step, `Step ${stepNo}`, 'export')   // 04, throws
  try fs.stat(abs) catch -> throw Error(
    `Step ${stepNo}'s screenshot render is missing from disk (${step.flattened ?? step.screenshot}). ` +
    `Open it in the editor and save to re-bake the render, then export again.`)
  cropped = zoomCropPng(abs, step.reportZoom ?? 1, step.reportPanX ?? 0.5, step.reportPanY ?? 0.5)
  items.push({kind:'shot', n: stepNo, caption: trim(step.caption ?? ''), body: trim(step.body ?? ''),
              abs, mediaType: cropped ? 'image/png' : mediaType, ext: cropped ? '.png' : (ext || '.png'),
              stepId: step.id, bytes: cropped ?? undefined})
if items.length === 0: throw Error('This project has nothing to export yet \u2014 add a step first.')
```

- `isCalloutKind(v)` is `v === 'note' || v === 'caution' || v === 'warning' || v === 'section'` (`src/shared/project.ts:245-247`). An unknown value (`'tip'`, kept on read for forward compatibility) makes the step a plain numbered text step (#90).
- Callouts (including a non-empty section) are never numbered. A colored callout with empty heading and body is KEPT (the colored box carries the meaning).
- Numbering therefore equals the report's numbering (05): shots and non-empty plain text steps share one contiguous 1..N counter.
- The gate (04, `src/main/render-gate.ts:27-49`) refuses a shot whose redaction or crop is not baked, with `` `${stepLabel} has a redaction or crop that hasn't been baked into a render yet \u2014 refusing to export the raw screenshot. Open it in the editor and save, then retry.` ``, and a shot with no confinable path with `` `${stepLabel} has no readable screenshot.` ``. `mediaType` is `image/jpeg` for `.jpg`/`.jpeg` (lowercased extension), else `image/png`; `ext` is the lowercased extension or `.png` when there is none.
- `fs.stat` succeeding on a directory passes the check (the later read fails). The render missing message quotes `step.flattened ?? step.screenshot` (the manifest-relative path). `fs.stat` follows symbolic links, so a dangling link at the render path gives the render missing message.
- `zoomCropPng` runs for EVERY shot, even at zoom 1 (it decodes the file with `nativeImage.createFromPath` before `zoomCropRect` returns null); a directory or an undecodable file at `abs` decodes to 0 by 0 and silently yields no crop. The crop can never throw out of the collector. Natively the read and decode happen only when `zoom > 1` (identical result, 7.4) and are inside the no-crop fallback.
- The whole list is collected before anything is written, so a refusal on step 7 of 9 writes nothing. REQUIRED (INV-EXP-3).

`ExportItem` (`src/main/export.ts:160-188`): shot `{kind, n, caption, body, abs, mediaType, ext, stepId, bytes?}`; text `{kind, n?, heading, body, callout?}`.

### 2.5 Zoom and pan as a static crop

`zoomCropPng(abs, zoom, panX, panY)` (`src/main/export.ts:197-209`): decode `abs` with `nativeImage.createFromPath`; `rect = zoomCropRect(width, height, zoom, panX, panY)`; `null` rect returns `null` (full image); else `img.crop(rect).toPNG()`, and an empty result returns `null` ("fail open to full image", which is still the gated render). An undecodable file decodes to 0 by 0, so `zoomCropRect` returns null. REQUIRED.

`zoomCropRect(width, height, zoom, panX, panY)` (`src/main/export-geometry.ts:94-112`), exactly:

```
if !(zoom > 1): return null                      // zoom <= 1 or NaN
if !(width >= 2) || !(height >= 2): return null
boxScale = min(zoom, 1)                          // always 1 here; kept for parity
w = max(1, min(width,  round(width  * boxScale / zoom)))
h = max(1, min(height, round(height * boxScale / zoom)))
if w >= width && h >= height: return null
px = clamp01(panX); py = clamp01(panY)          // clamp01: non-finite -> 0.5, <0 -> 0, >1 -> 1
x = max(0, min(width - w,  round(width  * (zoom - boxScale) * px / zoom)))
y = max(0, min(height - h, round(height * (zoom - boxScale) * py / zoom)))
return {x, y, width: w, height: h}
```

Examples (from the tests): `(800, 600, 2, 0.5, 0.5)` gives `{200, 150, 400, 300}`; pan 0 gives `{0, 0, 400, 300}`; pan 1 gives `{400, 300, 400, 300}`; NaN pan gives the centered rect; `(1, 1, 2, ...)` gives null.

The crop only ever makes the gated render smaller; it never reads another file. The cropped bytes are always PNG, so the item's `mediaType` becomes `image/png` and `ext` `.png` (`:409-413`).

### 2.6 HTML image embedding

**Policy** `htmlEmbedPolicy(format, docScale = 1)` (`src/main/export-geometry.ts:248-268`):

| format | `embedMaxW` | `codec` | Why |
|---|---|---|---|
| `html` | `htmlImgEmbedMaxW(s)` = `docWidths(s).htmlImgEmbedMax` (2x the display width) | `avif` | total payload ceiling of the Freshservice KB editor (#56): for a real 13-step SOP PNG@1x 3.12 MB, JPEG@2x 1.02 MB, WebP@2x 524 KB, JPEG@1x 414 KB all failed to paste; AVIF@2x 168 KB pastes |
| `html-plain` | `htmlImgMaxW(s)` (1x) | `png` | Word cannot read WebP or AVIF; PNG@2x would be 11.75 MB |
| `pdf` | `null` | `png` | the PDF prints from the same HTML; `printToPDF` embeds the source bitmap, so a 738 px cap would print near 110 DPI instead of about 355 |
| anything else (`markdown`, `docx`, `pptx`, `''`, unknown) | `null` | `png` | never routed through the HTML builder; never transcode blindly |

**Pipeline** `inlineImageForHtml(it, policy, docScale)` (`src/main/export.ts:277-348`), exactly:

```
{buffer, width, height} = loadItemImage(it)     // it.bytes ?? readFile(it.abs); size from nativeImage.createFromBuffer
shown = htmlImageSize(width, height, docScale)
sizeAttr = shown ? ` width="${shown.w}" height="${shown.h}"` : ''
cap = policy.embedMaxW
if cap == null && policy.codec === 'png': return {bytes: buffer, mediaType: it.mediaType, sizeAttr}   // PDF: untouched
bytes = buffer; mediaType = it.mediaType
try:
  img = decode(buffer)
  scaled = (cap != null && width > cap && height >= 1)
           ? img.resize({width: cap, height: max(1, round(height * (cap / width))), quality: 'best'})
           : img
  out = null
  if policy.codec === 'avif':
    avif = encodeAvif(toRgba(scaled.toBitmap()), scaled.width, scaled.height, HTML_IMG_AVIF_QUALITY, HTML_IMG_AVIF_SPEED)
    if avif: out = {b: avif, t: 'image/avif'}
  if !out:
    out = policy.codec === 'png' ? {b: scaled.toPNG(), t: 'image/png'}
                                 : {b: scaled.toJPEG(HTML_IMG_JPEG_QUALITY), t: 'image/jpeg'}
  if out.b.length > 0: bytes = out.b; mediaType = out.t
catch e: log warn 'export: image re-encode failed, embedding the original:' e
if bytes !== buffer && bytes.length >= buffer.length: return {bytes: buffer, mediaType: it.mediaType, sizeAttr}
return {bytes, mediaType, sizeAttr}
```

Consequences, all REQUIRED:

- Never upscales (`width > cap` guard). An image already narrower than the cap is still re-encoded (to AVIF, or PNG for `html-plain`), and the re-encode is kept only if strictly smaller than the input bytes.
- When the `avif` codec fails (encoder missing or broken, or a non-AVIF result), the fallback for `html` is JPEG at quality 85 (the policy codec is `avif`, not `png`, so the `else` branch is taken).
- Any exception in decode, resize or encode leaves the input bytes and media type in place.
- The size attributes are computed from the INPUT dimensions (after the zoom crop, before the resample), so a 2x embed still lays out at 1x.
- Alpha: `toRgba` swaps BGRA to RGBA and hands Electron's premultiplied bitmap to libavif as straight alpha, which is harmless only because every step render is fully opaque (`:225-244`). JPEG drops alpha.
- An undecodable buffer (size 0 by 0) does NOT throw in Electron: the resize guard is false, `encodeAvif` returns null for a 0 by 0 frame, `toJPEG`/`toPNG` of the empty image is zero bytes, the `out.b.length > 0` guard keeps the input, and NO warning is logged. The warning is logged only when a `nativeImage` call actually throws. Natively: when `ReadSize` gives (0, 0), skip the re-encode and return the input bytes without logging (7.5).
- The code comment at `export.ts:263-267` says an image narrower than the column "is left byte-identical"; the code does not do that (it re-encodes and keeps the result when strictly smaller). The code is the reference.

`htmlImageSize(width, height, docScale = 1)` (`src/main/export-geometry.ts:270-284`): null if either is non-finite or `< 1` (also for `-800` and `Infinity`); `fit = min(1, htmlImgMaxW(docScale) / width)`; `{w: max(1, round(width * fit)), h: max(1, round(height * fit))}`. At scale 1: `2560x1440` gives `738x415`; `400x300` stays; `738x450` stays; `3000x1` gives `738x1`.

`htmlImgMaxW(s) = docWidths(s).htmlImgMax = max(120, round(816 * s) - 78)`; `htmlImgEmbedMaxW(s) = 2 * htmlImgMaxW(s)` (`src/shared/doc-scale.ts:129-147`). RE-DERIVED, never `738 * s` (the 78 px of chrome does not scale).

### 2.7 AVIF encoder

`encodeAvif(rgba, width, height, quality, speed)` (`src/main/avif-encode.ts:114-141`):

1. `width < 1 || height < 1` returns null.
2. The encoder loads once per process (`load`, `:67-105`): resolve `@jsquash/avif/encode.js`, dynamic-import it, check it exports `init` and a default function, read `codec/enc/avif_enc.wasm`, `init({wasmBinary})`. Any failure caches `null` for the process ("one failed attempt per process, not one per image") and logs one of: `avif: @jsquash/avif is not installed:` (warn, with the error), `avif: @jsquash/avif exports look wrong; falling back` (warn), `avif: encoder init failed; falling back:` (warn, with the error). Concurrent first calls share one loading promise.
3. `encode({data: rgba, width, height}, {quality, speed})` with the library defaults for everything else (`@jsquash/avif` 2.1.1 `meta.js`): `qualityAlpha -1, denoiseLevel 0, tileColsLog2 0, tileRowsLog2 0, subsample 1 (YUV 4:2:0), chromaDeltaQ false, sharpness 0, tune 0 (auto), enableSharpYUV false, bitDepth 8, lossless false`. In the Node main process the single-threaded build (`avif_enc.js`) is used (`encode.js`: the multithreaded build only outside Node).
4. Sanity check: `buf.length > 16 && ascii(buf[4..8]) === 'ftyp' && /avif|avis|av01|mif1|miaf/.test(ascii(buf[8..24]))`; otherwise log warn `avif: encoder returned something that is not AVIF; falling back` and return null. Two details of Node's `buf.toString('ascii', start, end)` matter for the port: the end index is clamped to the buffer length (a 20-byte buffer tests bytes 8 to 19, it does not throw), and each byte is decoded with its HIGH BIT CLEARED (verified on Node 22.22.2: bytes `E1 41 61` decode to `aAa`), so a byte `0xE1` matches `a`.
5. A thrown encode logs warn `avif: encode failed; falling back:` (with the error) and returns null. Only a LOAD failure is cached for the process; an encode that throws is retried for the next image (and logs again).
6. The width check runs before `load()`, so a 0 by 0 frame never triggers the encoder load.

Measured (commit `66d7376`, `avif-encode.ts:16-18`): about 10 KB per image, 168 KB of base64 for a 13-step SOP, 11.4 s to encode 13 images at speed 7 (23.3 s at speed 6 for the same size).

### 2.8 Styled HTML (`buildHtmlDoc`)

`buildHtmlDoc(manifest, items, createdLine, policy, onProgress?, theme = DEFAULT_EXPORT_THEME)` (`src/main/export.ts:432-537`). `docScale = clampScale(manifest.displayScale)` (`:442`). `esc` below is `escapeHtml`: replace `&` with `&amp;`, then `<` with `&lt;`, `>` with `&gt;`, `"` with `&quot;`, in that order; `'` is not escaped (`:147-153`).

Progress (`:446-448`, `:493`): `shotTotal` = number of shot items; `onProgress({done: 0, total: shotTotal})` once before the loop (also when `shotTotal` is 0); after each shot's image is embedded, `onProgress({done: ++shotDone, total: shotTotal})`.

Per item (each part is ONE line, no internal newlines except those inside escaped user text):

| Item | Markup | Citation |
|---|---|---|
| section | `<section class="section"><div class="section__inner">` + (heading ? `<h2 class="section__h">{esc heading}</h2>` : '') + (body ? `<p class="section__b">{esc body}</p>` : '') + `</div></section>` | `:451-463` |
| note, caution, warning | `<section class="step step--callout"><div class="step__num step__num--{kind}">{glyph}</div><div class="step__main step__main--{kind}">` + (heading ? `<strong class="callout__h">{esc heading}</strong>` : '') + (body ? `<div class="callout__b">{esc body}</div>` : '') + `</div></section>` | `:464-477` |
| plain text | `<section class="{heading ? 'step' : 'step step--textonly'}"><div class="step__num">{n}</div><div class="step__main">` + (heading ? `<h2 class="step__title">{esc heading}</h2>` : '') + (body ? `<p class="step__instr">{esc body}</p>` : '') + `</div></section>` | `:478-490` |
| shot | `<section class="step"><div class="step__num">{n}</div><div class="step__main"><h2 class="step__title">{esc(caption \|\| 'Step ' + n)}</h2><img class="step__img" src="data:{mediaType};base64,{base64}"{sizeAttr} alt="Screenshot for step {n}">` + (body ? `<p class="step__instr">{esc body}</p>` : '') + `</div></section>` | `:492-507` |

Newlines inside bodies are kept as literal `\n` characters (the CSS `white-space:pre-wrap` renders them). Base64 is standard with padding and no line breaks.

Intro (`:509-520`), only when `intro && (intro.heading || intro.body)` (the intro strings are not trimmed here):

```
<section class="doc__intro">\n
<p class="doc__intro-eyebrow">Overview</p>\n
[<h2 class="doc__intro-h">{esc heading}</h2>\n]
[<p class="doc__intro-b">{esc(body) with every \n replaced by <br>}</p>\n]
</section>\n
```

Document (`:521-536`), with `T = esc(manifest.title)`:

```
<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n
<meta name="viewport" content="width=device-width, initial-scale=1">\n
<title>{T}</title>\n<style>{docCss(docScale, theme)}</style>\n
</head>\n<body>\n<div class="doc">\n<h1 class="doc__title">{T}</h1>\n
<p class="doc__meta">{esc createdLine}</p>\n{introHtml}{parts.join('\n')}\n</div>\n</body>\n</html>\n
```

(Line breaks in this block are only for reading; the output contains exactly the `\n` characters shown and nothing else.) The wrapper is a plain `div`, not `main`, because whole-document wrappers are unwrapped on a KB paste and semantic tags are often off sanitizer allowlists (#57). Written as UTF-8 without a BOM (`fs.writeFile(..., 'utf8')`). REQUIRED byte for byte (INV-EXP-18).

### 2.9 `docCss(scale, theme)` verbatim

`docCss(scale = 1, theme = DEFAULT_EXPORT_THEME)` (`src/main/export-css.ts:86-127`): `COL = docWidths(scale).htmlCol` (= `round(816 * clampScale(scale))`), `C = theme.palette`, `R = theme.radii`. The template below, with `${...}` substituted, then JavaScript `.trim()` (removes the template's leading and trailing newline). The two CSS comments are PART OF THE OUTPUT and must be reproduced exactly, including their leading three spaces. REQUIRED byte for byte.

```
*{box-sizing:border-box}
html{-webkit-print-color-adjust:exact;print-color-adjust:exact}
body{margin:0;font-family:${theme.fontStack};color:${C.ink};background:${C.surface};line-height:1.6}
.doc{padding:40px 32px 64px}
.doc__title{max-width:${COL}px;margin:0 auto 4px;font-size:1.9rem;line-height:1.25}
.doc__meta{max-width:${COL}px;margin:0 auto 28px;color:${C.ink3};font-size:.85rem}
.doc__intro{max-width:${COL}px;margin:0 auto 28px;padding:14px 18px;border:1px solid ${C.hair};border-left:4px solid ${C.accent};border-radius:${R.card}px;background:${C.accentTint}}
.doc__intro-eyebrow{text-transform:uppercase;letter-spacing:.6px;font-size:.7rem;font-weight:700;color:${C.ink3};margin:0 0 6px}
.doc__intro-h{margin:0 0 6px;font-size:1.15rem}
.doc__intro-b{margin:0;color:${C.ink2};white-space:pre-wrap}
/* The 46px left pad is the step gutter (30px badge + 16px gap), so a section's
   rule and text align with the step CONTENT column rather than the badge. The
   rule lives on .section__inner because the padding and the width can't share a
   box once .section carries the column. Values match the macOS export. */
.section{max-width:${COL}px;margin:28px auto 4px;padding-left:46px;break-inside:avoid}
.section__inner{padding:14px 16px 0;border-top:2px solid ${C.hair}}
.section__h{font-size:1.2rem;font-weight:700;margin:0 0 4px;color:${C.ink}}
.section__b{margin:0;color:${C.ink2};white-space:pre-wrap}
.step{display:flex;gap:16px;max-width:${COL}px;margin:0 auto 18px;align-items:flex-start;page-break-inside:avoid;break-inside:avoid}
.step__num{flex:0 0 auto;width:30px;height:30px;margin-top:14px;border-radius:${chipRadiusCss(R)};background:${C.accent};color:${C.onAccent};font-weight:600;display:flex;align-items:center;justify-content:center;font-size:.95rem}
.step__num--note{background:${C.noteBg};color:${C.noteFg};border:1px solid ${C.noteBd}}
.step__num--caution{background:${C.cautBg};color:${C.cautFg};border:1px solid ${C.cautBd}}
.step__num--warning{background:${C.warnBg};color:${C.warnFg};border:1px solid ${C.warnBd}}
.step__main{flex:1 1 auto;min-width:0;padding:14px 16px;border:1px solid ${C.hair};border-radius:${R.card}px;background:${C.surface2}}
.step__main--note{background:${C.noteBg};border-color:${C.noteBd};color:${C.noteFg}}
.step__main--caution{background:${C.cautBg};border-color:${C.cautBd};color:${C.cautFg}}
.step__main--warning{background:${C.warnBg};border-color:${C.warnBd};color:${C.warnFg}}
.step__title{font-size:1.15rem;margin:0 0 10px}
.step__img{display:block;max-width:100%;height:auto;margin-inline:auto;border:1px solid ${C.hair};border-radius:${R.figure}px}
.step__instr{margin:10px 0 0;white-space:pre-wrap;font-size:1.02rem}
.step--textonly .step__instr{margin-top:0}
.callout__h{display:block;font-weight:700;margin-bottom:.25rem}
.callout__b{white-space:pre-wrap}
/* Print/PDF spans the page: lift the column off every block that carries it. */
@media print{.doc{padding:0 6px}.doc__title,.doc__meta,.doc__intro,.step,.section{max-width:none}}
```

The `@media print` selector list is `COL_BLOCKS.join(',')` with `COL_BLOCKS = ['.doc__title', '.doc__meta', '.doc__intro', '.step', '.section']` (`:33`, `:125`), exported as `COLUMN_BLOCK_SELECTORS` (`:155`). Lines are joined with `\n` (LF). Numbers are integers, so `${COL}` is the integer's decimal form. The resulting sizes and hashes are in 3.4.

Why the column is on every block (#57, `66d7376`, `export-css.ts:44-73`): a Freshservice KB paste (Froala) unwraps every whole-document wrapper (at any depth), keeps every other element with computed styles inlined (so `.step__main{flex:1 1 auto}` stretched cards to full width), and strips `max-width` from `<img>`. Layout tables were probed and also came back full width (`table{width:100%}` is forced). So each block self-constrains and self-centers.

### 2.10 HTML for Word (`buildPlainHtmlDoc`, `plainCss`)

`plainCss(scale = 1, theme)` (`src/main/export-css.ts:139-152`): `BODY = round(800 * (docWidths(scale).htmlCol / 816))`; the eight rules below concatenated with NO separator (one line, no trailing newline). REQUIRED byte for byte.

```
body{font-family:${theme.plainFontStack};color:${C.ink};line-height:1.5;max-width:${BODY}px;margin:24px auto;padding:0 20px}
h1{font-size:1.8rem;font-weight:700;margin:0 0 .3rem}
h2{font-size:1.2rem;font-weight:700;margin:1.3rem 0 .4rem}
p{margin:.5rem 0}
strong{font-weight:700}
img{max-width:100%;height:auto}
blockquote{margin:1rem 0;padding:.4rem .85rem;border-left:3px solid ${C.controlBd};color:${C.ink2}}
hr{border:0;border-top:1px solid ${C.hair};margin:1.4rem 0}
```

`buildPlainHtmlDoc(manifest, items, theme)` (`src/main/export.ts:545-611`). There is NO created line and no footer in this format. `br(s) = esc(s).replace(/\n/g, '<br>')`. `docScale = clampScale(manifest.displayScale)`.

`parts = ['<h1>' + esc(title) + '</h1>']`; when `intro && (intro.heading || intro.body)`: push `<h2>{esc heading}</h2>` if heading, `<p>{br body}</p>` if body. Then one block per item, each block's lines joined by `\n`:

| Item | Block lines |
|---|---|
| section | `<h2>{esc heading}</h2>` if heading; `<p>{br body}</p>` if body |
| note, caution, warning | `<blockquote><p><strong>{glyph}{heading ? ' ' + esc heading : ''}</strong>{body ? '<br>' : ''}{body ? br body : ''}</p></blockquote>` |
| plain text with heading | `<h2>{n}. {esc heading}</h2>`; then `<p>{br body}</p>` if body |
| plain text without heading | `<p>{n}. {br body}</p>` (only when body) |
| shot | `<h2>{n}. {esc(caption \|\| 'Step ' + n)}</h2>`; `<p><img src="data:{mediaType};base64,{b64}"{sizeAttr} alt="Screenshot for step {n}"></p>`; `<p>{br body}</p>` if body |

Shots go through `inlineImageForHtml(it, htmlEmbedPolicy('html-plain', docScale), docScale)`. A block with no lines is dropped. If any blocks exist, `parts.push(blocks.join('\n<hr>\n'))` (the rule only BETWEEN items, #40).

Document: `<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n<title>{esc title}</title>\n<style>{plainCss(docScale, theme)}</style>\n</head>\n<body>\n` + `parts.join('\n')` + `\n</body>\n</html>\n`. No viewport meta. UTF-8, no BOM. No progress events. REQUIRED byte for byte.

The width and height attributes are what survive a paste into Word and Google Docs (they drop CSS `max-width`), capped at `htmlImgMaxW(s)` = 738 at 100% (#52, `c3f7707`).

### 2.11 PDF

`htmlToPdf(dir, html, outputPath, theme)` (`src/main/export.ts:641-692`), the HTML being `buildHtmlDoc` with the `pdf` policy (full resolution PNG, untouched source bytes for uncropped shots).

1. `renderDir = <dir>/export/.render`; `mkdir -p`.
2. Best-effort sweep: for every entry of `renderDir` whose name starts with `_print-` and ends with `.html`, `rm -f` (errors ignored; an unreadable directory is ignored).
3. `tmpHtml = renderDir/_print-<randomUUID()>.html`; write `html.replace('<head>\n', '<head>\n' + printFontFace(theme))` as UTF-8 (the first occurrence only). The kept `.html` export never gets the face (#77 phase 3, `1025bfe`).
4. `printFontFace(theme)` (`:626-639`): `''` when `theme.fontFamily` is null; `''` plus log warn `brand face not found on disk; the PDF will use the fallback stack` when `brandFontPath()` is `''`; else `<style>@font-face{font-family:"{family}";src:url("{pathToFileURL(file).href}") format("truetype-variations");font-weight:100 900;font-stretch:62% 125%;font-style:normal}</style>\n`. The weight range is load-bearing: Archivo's variable default instance is weight 600, so without it a plain request renders semibold.
5. A hidden `BrowserWindow` (`show: false`, 900 by 1200, `sandbox: true`, `contextIsolation: true`, `nodeIntegration: false`, `javascript: false`) loads `tmpHtml`.
6. `printToPDF({printBackground: true, pageSize: 'Letter', margins: {marginType: 'default'}})`. In Electron 42's `printToPDF` the margins object takes `top`, `bottom`, `left`, `right`; `marginType` is not one of them, so the default margins (1 cm, about 0.4 in, each side) apply. Portrait, scale 1, no header or footer, no CSS page size preference (all library defaults).
7. Fail closed: a missing or zero-length result throws `'PDF rendering produced an empty document \u2014 printing may have failed on this system. Try the HTML or Markdown export instead.'` (nothing written at `outputPath`).
8. Otherwise write the buffer to `outputPath` (overwrites).
9. Finally: destroy the window if not destroyed; `rm -f tmpHtml` (errors ignored).

Error paths outside the fail-closed message: the `BrowserWindow` is constructed BEFORE the `try` (`:662-673`), so a constructor failure leaves the print copy on disk (swept by the next PDF export) and surfaces the raw error; a `loadFile` rejection (for example a navigation error) and a `printToPDF` rejection also propagate raw, not as the empty-document message. Only a resolved empty buffer gets the message.

REQUIRED: steps 1 to 4 and 6 to 9 in intent; the native engine is WebView2 (7.7).

Print layout facts that follow from the CSS: `@media print` sets `.doc{padding:0 6px}` and lifts `max-width` from the five column blocks so the document spans the Letter text width (8.5 in less 2 x 0.4 in = 7.7 in, about 739 CSS px). The image `width` attribute stays at the display width, and `max-width:100%` shrinks it into the card. `.step` and `.section` avoid page breaks inside.

### 2.12 Markdown

`buildMarkdown(manifest, items, outFolder, mdStem, createdLine)` (`src/main/export.ts:699-776`). The output is a SELF-CONTAINED folder: `<outFolder>/<mdStem>.md` plus `<outFolder>/images/` (#37, `f9f8b10`). The folder is chosen by 2.2 step 5; `mdStem` is always `basename(outFolder)`.

1. `imagesDir = outFolder/images`; `rm -rf imagesDir` (errors ignored); `mkdir -p imagesDir`.
2. `mdEsc(s)` = `` s.replace(/([\\`*_[\]#<>])/g, '\\$1') `` (backslash-escape each of `\` `` ` `` `*` `_` `[` `]` `#` `<` `>`; `:156-158`). `collapse(s)` = `s.replace(/JS\s*\nJS\s*/g, ' ')` (every newline with surrounding whitespace becomes one space).
3. Lines: `# {mdEsc title}`, `''`, `_{mdEsc createdLine}_`, `''`. When `intro && (intro.heading || intro.body)`: `## {mdEsc intro.heading}`, `''` if heading; `{intro.body}` (RAW), `''` if body.
4. One chunk per item:

| Item | Chunk lines |
|---|---|
| section | `## {mdEsc(collapse heading)}` if heading; if body: `''` first when there was a heading, then `{body}` RAW |
| note, caution, warning | `> **{glyph}{heading ? ' ' + mdEsc heading : ''}**`; if body: `>`, then `> ` + body with every `\n` replaced by `\n> ` (the heading is NOT collapsed) |
| plain text with heading | `## {n}. {mdEsc(collapse heading)}`; if body: `''`, `{body}` RAW |
| plain text without heading | `**{n}.** {body}` RAW (a bare `N. ` line would renumber as a Markdown list) |
| shot | copy the image to `images/{imgName}` (write `it.bytes` when present, else `copyFile(it.abs)`), `imgName = 'step-' + String(n).padStart(2, '0') + '-' + stepId + ext`; lines `## {n}. {mdEsc(collapse(caption \|\| 'Step ' + n))}`, `''`, `![Screenshot for step {n}](<images/{imgName}>)`; if body: `''`, `{body}` RAW |

5. Chunks are joined into `lines` with `''`, `'---'`, `''` between consecutive chunks (the blank line before `---` prevents a Setext heading, #40). A chunk with no lines is dropped before joining (cannot happen for items the collector emits).
6. Push `''`; write `lines.join('\n')` (so the file ends with exactly one `\n`) as UTF-8 without BOM to `outFolder/{mdStem}.md`; return that path.

Images are copied at full resolution with their original bytes (a crop is PNG of the same pixels); the shot extension comes from the item (`.png` for a crop). Body text is emitted raw so authored Markdown renders; only titles, the created line, headings and captions are escaped. REQUIRED byte for byte (text) and byte for byte (uncropped images).

### 2.13 Word (`buildDocx`)

`buildDocx(manifest, items, createdLine, theme)` (`src/main/export-docx.ts:94-256`), built with `docx` 9.7.1. Colors go through `hexNoHash(v)` (`src/shared/theme-palette.ts:526-530`): `^#([0-9a-fA-F]{6})$` or throw `` `hexNoHash: expected #rrggbb, got ${JSON.stringify(value)}` ``, then uppercase without `#` (docx renders an unparseable color as black).

Page and document (`:227-255`), all REQUIRED:

| Property | Value |
|---|---|
| Page size | A4 portrait, `w:pgSz w:w="11906" w:h="16838"` (set explicitly; NOT Letter, which would change every existing export, `e93093a`) |
| Margins | top, right, bottom, left `1440` twips (1 in); header and footer distance 708, gutter 0 (library defaults) |
| Default font | `w:docDefaults/w:rPrDefault` run fonts `Aptos` (ascii, hAnsi, eastAsia, cs); no default size, so Word's built-in default applies |
| Styles | the library defaults: `Title` (basedOn `Normal`, next `Normal`, quick format, run `sz 56`), `Heading1` (color `2E74B5`, `sz 32`), `Heading2` (color `2E74B5`, `sz 26`), `Heading3` (`1F4D78`, `sz 24`), `Heading4` (`2E74B5`, italic), `Heading5` (`2E74B5`), `Heading6` (`1F4D78`), `Strong` (bold), `ListParagraph`, `Hyperlink`, footnote and endnote styles. No `Normal` style is defined. The heading colors are NOT brand colors (they come from the library) |
| Core properties | `dc:creator` `shotAI`, `dc:title` the project title, `cp:lastModifiedBy` `Un-named` (library default, `docx` `index.mjs:26807-26809`), revision 1 |
| Section defaults (library) | `w:pgSz` also carries `w:orient="portrait"`; an empty `w:pgNumType`; `w:docGrid w:linePitch="360"`; no columns element |
| Settings (library) | `w:compat` with `compatibilityMode` 15 (`docx` `index.mjs:25816`) |
| Cell margin order | `w:tcMar` children are written `w:top`, `w:left`, `w:bottom`, `w:right` (`docx` `buildMarginChildren`), which is also the schema order |

Helpers: `multiline(text, {color?})` splits on `\n` into runs, every run after the first preceded by a break (`w:br`) (`:53-64`). `stepCard(content, fill, border)` (`:70-87`) is a one-row, one-cell table: table width 100 percent, all six borders (top, bottom, left, right, insideH, insideV) `single`, size 4 (eighths of a point), color `border`; the cell has shading `clear`, color `auto`, fill `fill`, and margins top 120, bottom 120, left 180, right 180 twips. `spacer()` (`:90-92`) is a paragraph with spacing after 60 and one empty run of size 10 (5 pt); Word needs a paragraph between tables.

Colors: `CARD_FILL = C.surface2`, `CARD_BORDER = C.hair`, `INTRO_FILL = C.accentTint`; callouts `note {fill noteBg, bd noteBd, fg noteFg, label 'Note'}`, `caution {cautBg, cautBd, cautFg, 'Caution'}`, `warning {warnBg, warnBd, warnFg, 'Warning'}` (`:44-50`).

Body, in order:

| Element | Construction | Citation |
|---|---|---|
| Title | paragraph, style `Title`, text = raw title | `:108` |
| Created line | paragraph, spacing after 240; run `createdLine`, color `ink3`, size 18 (9 pt) | `:109-114` |
| Overview card (when `intro && (heading \|\| body)`) | `stepCard([eyebrow, heading?, body?], INTRO_FILL, CARD_BORDER)` then `spacer()`. Eyebrow: paragraph spacing before 0 after 40, run `OVERVIEW` bold color `ink3` size 15. Heading: paragraph spacing after (`body ? 40 : 0`), run bold size 26. Body: paragraph `multiline(body)` | `:117-134` |
| Section | no card. With heading: paragraph style `Heading2`, spacing before 280 after (`body ? 60 : 120`), top border `single` size 6 color `hair` space 8, text = heading; then if body a paragraph `multiline(body, {color: ink3})` spacing after 160. Without heading: one paragraph `multiline(body, {color: ink3})`, spacing before 280 after 160, same top border | `:138-161` |
| Callout | `stepCard([p1, p2?], fill, bd)` then `spacer()`. p1: spacing before 0 after (`body ? 60 : 0`), one run `{glyph} {heading \|\| label}` bold color `fg`. p2 (if body): `multiline(body, {color: fg})` | `:165-179` |
| Plain text | `num = n + '. '`. With heading: paragraph style `Heading2`, spacing before 0 after (`body ? 100 : 0`), text `{num}{heading}`; then if body `multiline(body)`. Without heading: paragraph `multiline(num + body)`. `stepCard(..., CARD_FILL, CARD_BORDER)`, `spacer()` | `:181-195` |
| Shot | `{buffer, width, height} = loadItemImage(it)`; `cap = docxImgMaxW(manifest.displayScale)`; `fit = width > cap ? cap / width : 1`; paragraph style `Heading2`, spacing before 0 after 100, text `{n}. {caption \|\| 'Step ' + n}`; paragraph centered (#46), spacing after (`body ? 100 : 0`), one inline image run, type `jpg` when `mediaType === 'image/jpeg'` else `png`, `width = round(width * fit)`, `height = round(height * fit)` px, extent `cx = round(px * 9525)` EMU; if body `multiline(body)`. Card, spacer | `:198-224` |

`docxImgMaxW(s) = min(576, round(560 * clampScale(s)))` (`src/main/export-geometry.ts:90-92`). The 576 is `DOCX_CARD_INNER_W = floor(twipsToPx(11906 - 2*1440 - 2*180 - 2*(4/8)*20))` = `floor(8626 / 20 * 96 / 72)` = 576, and the page column `DOCX_PAGE_COL_W = floor(twipsToPx(11906 - 2*1440))` = 601 (`:65-80`). Word silently clips an over-wide image; the two widths that shipped clipped were 624 (a Letter guess) and 598 (A4 without the card insets) (`b6ddce5`, `e93093a`).

Text strings with `\n` inside a heading paragraph (title, caption, heading) are written as one run; the newline reaches `w:t` as a literal character (Word shows it as white space). REQUIRED (parity).

### 2.14 PowerPoint (`buildPptx`)

`buildPptx(manifest, items, createdLine, theme)` (`src/main/export-pptx.ts:50-259`), built with `pptxgenjs` 4.0.1. `LAYOUT_WIDE` (slide 12192000 by 6858000 EMU, 13.333 by 7.5 in); author `shotAI`; title = project title. Positions are inches converted by `round(914400 * inches)` (`inch2Emu`); font sizes in points (`sz = pt * 100`); colors through `hexNoHash`. Every text box has no explicit insets (PowerPoint defaults, 0.1 in left and right, 0.05 in top and bottom), wraps, and inherits the theme fonts (`Calibri` body, `Calibri Light` headings, from `makeXmlTheme`); vertical anchor is middle unless `valign: 'top'` is given; horizontal alignment is left unless `align` is given. Core properties: `dc:creator` and `cp:lastModifiedBy` are both the author `shotAI` (`pptxgen.es.js:6456-6458`), so PowerPoint already reports `shotAI` as last modified by.

Text splitting, exactly as pptxgenjs 4.0.1 does it (`pptxgen.es.js:6159-6213`): each run's text has every `\r*\n` replaced by CRLF; a run whose text contains CRLF and does NOT end with `\n` is split into one text object per line, each flagged `breakLine`; a run that ENDS with `\n` is kept whole (its trailing CRLF stays inside the run's `<a:t>`, written literally). Lines are then grouped: a `breakLine` object closes the current paragraph after itself; an object with empty text emits no `<a:r>` (only the paragraph's `<a:endParaRPr lang="en-US" sz=... dirty="0"/>`). Consequences:

- Single-string boxes (titles, captions, bodies, the cover intro joined with `\n\n`): one paragraph per line; an empty line (the `\n\n` gap) is an empty paragraph with only `endParaRPr`.
- The callout box: paragraph 1 holds TWO runs: the bold 24 pt run whose text is `{glyph} {heading || label}` followed by a literal CRLF, and then the first line of the body (18 pt); every further body line is its own paragraph. An empty body adds no run, so the box is one paragraph holding only the heading run. How PowerPoint renders the CRLF inside `<a:t>` was not measured (UNVERIFIED, Q-EXP-21).

Constants (`:13-21`): `SLIDE_W = 13.333`, `SLIDE_H = 7.5`, `MARGIN = 0.5`, `CARD = {x 0.45, y 0.45, w SLIDE_W - 0.9, h SLIDE_H - 0.9}`, `PAD = 0.35`, `INNER = {CARD.x + PAD, CARD.y + PAD, CARD.w - 2*PAD, CARD.h - 2*PAD}`.

`CARD_RADIUS = 0.12 * (theme.radii.card / DEFAULT_EXPORT_THEME.radii.card)` inches (`:65-66`), so 0.12 for shotAI and 0.096 for LFI. The rounded rectangle's adjust value is `round(rectRadius * 914400 * 100000 / min(cx, cy))` with `cx, cy` the card's EMU size: 1818 (shotAI) and 1455 (LFI).

`addCard(slide, fill, line)` (`:73-83`): shape `roundRect` at `CARD`, solid fill `fill`, outline `line` 1 pt (12700 EMU).

`fitContain(pxW, pxH, box)` (`:35-48`): `ar = (pxW > 0 && pxH > 0) ? pxW / pxH : 1`; `w = box.w`; `h = w / ar`; if `h > box.h` then `h = box.h`, `w = h * ar`; return `{x: box.x + (box.w - w)/2, y: box.y + (box.h - h)/2, w, h}`.

Slides (every slide background `surface`):

| Slide | Elements (x, y, w, h in inches) | Citation |
|---|---|---|
| Cover (first, no card) | title text (0.5, `hasIntro ? 2.2 : 3.0`, 12.333, 1.2) size 40 bold `ink` centered (anchor middle), where `hasIntro = intro && (heading \|\| body)`; if `intro?.heading` or `intro?.body`: text = the present ones joined by `\n\n` at (1.5, 3.6, 10.333, 2.6) size 16 `ink2` centered, top; created line at (0.5, 6.8, 12.333, 0.4) size 10 `ink3` centered (anchor middle) | `:85-121` |
| Section | no card; line shape (4.6665, 2.9, 4, 0) `controlBd` 1 pt; heading if present (0.5, 3.05, 12.333, 1.0) size 34 bold `ink` centered top; body if present (2.0, 4.2, 9.333, 2) size 16 `ink3` centered top | `:128-164` |
| Callout | `addCard(fill, bd)`; one text box at `INNER` left top with two runs: `{glyph} {heading \|\| label}\n` bold size 24 `fg`, then `{body \|\| ''}` size 18 `fg` | `:166-181` |
| Plain text | `addCard(surface2, hair)`; text `{n}. {heading \|\| body}` at (INNER.x, INNER.y, INNER.w, 1) size 26 bold `ink` top; if heading AND body: body at (INNER.x, INNER.y + 1.15, INNER.w, INNER.h - 1.15) size 18 `ink2` top | `:182-208` |
| Shot | `addCard(surface2, hair)`; caption `{n}. {caption \|\| 'Step ' + n}` at (INNER.x, INNER.y, INNER.w, 0.6) size 20 bold `ink` top; image placed (box formula below) at `fitContain(width, height, box)` from the image's pixel size; if body: body at (INNER.x, INNER.y + INNER.h - 1.1, INNER.w, 1.1) size 15 `ink2` top | `:211-253` |

The shot image box (#70): `shrink = min(1, clampScale(displayScale))`; `fullW = INNER.w`; `fullH = hasBody ? INNER.h - 0.75 - 1.2 : INNER.h - 0.75`; `box = {x: INNER.x + (fullW - fullW*shrink)/2, y: INNER.y + 0.75, w: fullW*shrink, h: fullH*shrink}`. The image is the raw item bytes (crop or source) as a data URI, stretched to the fitted box (same aspect). The deck uses no accent color. REQUIRED.

Exact EMU values at scale 1 are in 3.3.

### 2.15 File naming

`safeFileBase(title)` (`src/main/export.ts:44-57`), exactly:

```
cleaned = Array.from(title || '')                                   // code points
            .filter(ch => ch.codePointAt(0) > 0x1f && !'<>:"/\\|?*'.includes(ch))
            .join('')
            .replace(/JS\s+/g, ' ')
            .trim()                                                 // JsString.Trim
if cleaned.length > 120: cleaned = cleaned.slice(0, 120).trim()     // UTF-16 code units
cleaned = cleaned.replace(/[.JS\s]+$/, '')                          // no trailing dot or space
if !cleaned: return 'shotAI SOP'
if /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i.test(cleaned) || cleaned.startsWith('.'): return '_' + cleaned
return cleaned
```

C0 controls (U+0000 to U+001F) are removed; U+007F and every other character are kept. Lone surrogates survive the filter. The 120 limit keeps the path under MAX_PATH with a deep projects folder. Examples: `'  My   SOP.  '` gives `My SOP`; `'a<b>c'` gives `abc`; `'CON'` gives `_CON`; `'.env'` gives `_.env`; `'...'` gives `shotAI SOP`; `''` gives `shotAI SOP`. REQUIRED (see EDGE-EXP-14 and EDGE-EXP-15 for the two native refinements).

`nextAvailableStem(dir, stem, ext)` (`:64-73`): for `n = 0, 1, 2, ...`, `candidate = n === 0 ? stem : stem + ' (' + n + ')'`; return the first whose `dir/candidate + ext` does not exist (`fs.access` fails). `nextAvailableDir(parent, base)` (`:78-87`) is the same without an extension. Anything existing at that name (file or folder) counts as taken. There is no upper bound. REQUIRED.

Names by format (`stembase`, 2.2 step 4): `html`, `pdf`, `docx`, `pptx`: `{base}{ext}`; `html-plain`: `{base}-plain.html`; `markdown`: folder `{base}` holding `{base}.md` (with collision numbering the folder and the file are both `{base} (n)`); package: `{base} (shotAI package).zip`, numbered as `{base} (shotAI package) (1).zip`.

### 2.16 Destinations and reveal

| Destination | Trigger | Where | Dialog | Collision | Reveal |
|---|---|---|---|---|---|
| Save dialog | `projects:export` (report and Home row) | anywhere the user picks; default `<project>/export/<stembase><ext>` | Save dialog titled `Export` (Markdown: `Export Markdown (saved as a folder with its images)`) with the one format filter | the dialog's own overwrite confirmation; Markdown reuses an existing folder | `shell.showItemInFolder(outputPath)` |
| Own folder | Home bulk "Each project's own folder" | `<project>/export/` | none | `nextAvailableStem` or `nextAvailableDir` | none per file |
| One folder | Home bulk "One shared folder" | the folder chosen once by `chooseExportDirectory` | the folder dialog, once per run (06) | `nextAvailableStem` or `nextAvailableDir`, so two projects with the same title never overwrite (#37) | none per file; after the run `revealExportDir(dir)` once |
| Default (legacy) | `exportProject` with neither option | `<project>/export/` | none | numbered | yes |

`chooseExportDirectory(defaultPath?)` (`src/main/export.ts:133-145`): open dialog titled `Choose a folder for the exports`, properties `openDirectory` and `createDirectory`, `defaultPath` only when given (the IPC passes none); parented to the focused window; returns the first path or null when cancelled.

`revealExportDir(dir)` (`:918-925`): `stat(dir)`; only when it is a directory, `shell.openPath(dir)`; every error is swallowed. It can never open (execute) a file. REQUIRED [SECURITY].

### 2.17 Progress events

`ExportProgress {done, total}` (`src/shared/ipc.ts:136-141`). Only `buildHtmlDoc` reports, so only `html` and `pdf` emit, and only the Save dialog entry point passes a callback (bulk exports never show per-image progress). Sequence for a project with k shots: `{0, k}`, `{1, k}`, ..., `{k, k}`; for k = 0 one `{0, 0}`. The event fires after each shot's image is embedded (AVIF encode included), before the next. The UI shows counts only when `total > 0` (05). REQUIRED.

### 2.18 The "Created on" line and the byline

`generatedAt = new Date().toLocaleString()`; `byline = await getReportByline()`; `createdLine = 'Created on ' + generatedAt + (byline ? ' by ' + byline : '')` (`src/main/export.ts:830-834`). Computed once per export, before the Save dialog opens.

- `getReportByline()` (`src/main/settings.ts:389-393`): `name = settings.userName.trim()`; returns `name` when `settings.includeNameInReports && name`, else null. `userName` is stored as at most 120 UTF-16 units (`coerceUserName`, `:46-48`).
- `toLocaleString()` formats in the main process's ICU default locale (the OS locale in practice): en-US `9/22/2026, 2:03:07 PM` (the space before `PM` is U+0020 with current ICU; Node 22 with ICU 78.2 verified), en-GB `22/09/2026, 14:03:07`, de-DE `22.9.2026, 14:03:07`, fr-FR `22/09/2026 14:03:07`.
- Where it appears: styled HTML and PDF (`p.doc__meta` under the title), Markdown (italic line under the title), Word (paragraph under the title), PowerPoint (bottom of the cover slide). NOT in HTML for Word. It is a meta line under the title, not a page footer.

REQUIRED (the native formatting rule is 7.9).

### 2.19 Document scale per format

| Format | What scales | Formula | Citation |
|---|---|---|---|
| `html` | the five column blocks; image display size; embed size | `COL = round(816*s)`; display `max(120, COL - 78)`; embed 2x display | `export-css.ts:86-87`, `export-geometry.ts:137-178`, `:270-284` |
| `pdf` | the same CSS (print lifts the column); size attributes | image pixels NOT resampled (full resolution) | `export-geometry.ts:231-236`, `:267` |
| `html-plain` | body width; image attributes and embed | `BODY = round(800*COL/816)`; images at display size, 1x | `export-css.ts:141`, `export.ts:589-593` |
| `markdown` | nothing | no layout column; full resolution files | `ec76adb` |
| `docx` | image width, clamped | `min(576, round(560*s))` | `export-geometry.ts:90-92` |
| `pptx` | image box, shrink only, re-centered | `min(1, s)` on both axes | `export-pptx.ts:225-238` |

Chrome and font sizes never scale (`src/shared/doc-scale.ts:11-16`). Table of derived widths for every detent in 3.2. REQUIRED.

### 2.20 Brand theming per format

| Format | Brand inputs | Not brand-driven |
|---|---|---|
| `html` | full `docCss`: palette, `radii.card`, `radii.figure`, chip radius, `fontStack` (face named, never embedded) | layout, sizes |
| `pdf` | as `html`, plus the brand face embedded into the print copy when `fontFamily` is not null | |
| `html-plain` | `plainFontStack`, `ink`, `controlBd`, `ink2`, `hair` | no radii |
| `markdown` | none | |
| `docx` | `surface2`, `hair`, `accentTint`, `ink3`, the nine callout roles | fonts (Aptos), heading and title colors (docx library defaults `2E74B5`, `1F4D78`), no radii |
| `pptx` | `surface`, `surface2`, `hair`, `ink`, `ink2`, `ink3`, `controlBd`, the nine callout roles, card radius ratio | fonts (theme Calibri), no accent |

Why the face is never embedded in HTML (`src/shared/export-theme.ts:35-42`): as base64 it is about 0.84 MB against a measured 0.8 to 1.5 MB paste budget, so the images would silently drop. REQUIRED.

### 2.21 Shareable package: export

`exportPackage(projectPath, includeOriginals)` (`src/main/export-package.ts:56-128`). Constants (`:25-30`): marker file `shotai-package.json`, format `shotai-package`, version `1`.

1. `{dir, manifest} = getProjectForRead(projectPath)`; if `manifest.steps.length === 0` throw `'This project has nothing to export yet \u2014 add a step first.'` (a project of only empty text steps is allowed here, unlike 2.4).
2. `out = JSON.parse(JSON.stringify(manifest))`; `out.sopBackup = null` (never share the sender's revert history).
3. With originals: for each step with `kind !== 'text'`: `addFileRef(screenshot)` if set, `addFileRef(flattened)` if set. `addFileRef(rel)` (`:40-49`): `abs = confinePath(dir, rel)` (lexical); null skips; read bytes; add the zip entry at `rel` with `\` replaced by `/`; a read error skips silently. The gate is NOT consulted in this mode (the originals are the point), so an unbaked redaction does not block it.
4. Safe (default): `shots = zip.folder('shots')` (a `shots/` directory entry); for each step with `kind !== 'text'`, `n += 1`: `{abs, ext} = resolveSendableRender(dir, step, 'Step ' + n, 'export')` (fail-closed; note `n` counts SHOTS only, so the label differs from the report and document numbering whenever a numbered text step precedes the shot, EDGE-EXP-52); `bytes = readFile(abs)` (an unreadable render throws the raw error); `name = 'step-' + String(n).padStart(4, '0') + (ext starts with '.' ? ext : '.' + ext)`; add `shots/{name}`; then in `out`: `screenshot = 'shots/' + name`, `annotations = []`, `crop = null`, `click = null` (the click ring is baked into the render), `flattened` removed, `renderRev = 0`, `markerBaked = false`. Every other step field (caption, body, element, window, report zoom and pan, ...) travels unchanged.
5. Add `project.json` = `JSON.stringify(out, null, 2)` and `shotai-package.json` = `JSON.stringify({format: 'shotai-package', version: 1, app: 'shotAI', includeOriginals, exportedAt: new Date().toISOString()}, null, 2)`.
6. `zip.generateAsync({type: 'nodebuffer', compression: 'DEFLATE'})`.
7. `exportDir = dir/export`; `mkdir -p`; `stem = nextAvailableStem(exportDir, safeFileBase(title) + ' (shotAI package)', '.zip')`; write `exportDir/{stem}.zip`; log info `` `exported package (originals=${includeOriginals}) \u2192 ${outputPath}` ``; `shell.showItemInFolder(outputPath)` (always); return `{outputPath, includeOriginals}`.

There is no Save dialog for the package; it always lands in the project's `export/` folder. `'Failed to assemble the package.'` is thrown only if JSZip cannot create the folder (`:84`). Zip layout (entry order as JSZip writes it):

| Mode | Entries in order |
|---|---|
| safe | `shots/` (directory), `shots/step-0001.<ext>`, `shots/step-0002.<ext>`, ..., `project.json`, `shotai-package.json` |
| originals | for each shot in step order: parent directory entries not yet present (JSZip creates them, for example `shots/`, `export/`, `export/.render/`), then the screenshot, then the flattened render; then `project.json`, `shotai-package.json` |

REQUIRED (entry names and content; directory entries and order are REQUIRED only as far as both importers ignore them, see 7.12).

### 2.22 Package import

IPC (`src/main/ipc.ts:620-635`): open dialog titled `Import a shotAI project package`, `openFile`, filter `{name: 'shotAI package', extensions: ['zip']}`, parented to the requesting window; cancel returns null. Then `importPackage(zipPath)` (`src/main/export-package.ts:136-204`):

| # | Check | Failure message |
|---|---|---|
| 1 | `stat(zipPath).size > 600 * 1024 * 1024` | `This package is too large to import.` |
| 2 | read the whole file; `JSZip.loadAsync(bytes)` (this parses EVERY entry's headers: an encrypted entry or an unknown compression method anywhere in the archive, even in an entry that would be ignored, fails here, EDGE-EXP-55) | `This file is not a valid .zip package.` |
| 3 | `zip.file('shotai-package.json')` returns a non-directory entry (exact name, after JSZip's name sanitizing; a directory-flagged entry of that name is keyed `shotai-package.json/` and not found) | `This file is not a shotAI project package (missing marker).` |
| 4 | the marker inflates, decodes as UTF-8 (`async('string')`, no BOM stripping) and `JSON.parse`s (a leading BOM makes `JSON.parse` throw) | `The package marker is corrupt.` (an inflate error of the marker is also caught here) |
| 5 | `marker.format === 'shotai-package'` | `Unrecognized package format.` (also for a marker that is a JSON array, string, number or boolean, whose `.format` is `undefined`); ONLY a marker of JSON `null` throws a raw TypeError instead (EDGE-EXP-30) |
| 6 | if `typeof marker.version === 'number'`, `version <= 1` (a missing or string version passes; `1e999` parses as `Infinity` and is refused) | `This package was created by a newer version of shotAI. Update to import it.` |
| 7 | entry `project.json` exists | `The package is missing its project.json.` |
| 8 | it inflates and `JSON.parse`s | `The package project.json is corrupt.` |
| 9 | `manifest = coerceManifest(parsed, 'Imported project')` (01) | a raw TypeError for `null` only; a JSON array, string, number or boolean is coerced to an EMPTY manifest (no steps, title `Imported project`) and imports as a blank project (`project-store.ts:148-160`: `raw` falls back to `{}` and the named fields read `undefined`) (EDGE-EXP-30) |
| 10 | for every non-directory entry except the two above, with `rel = name.replace(/\\/g, '/')`: keep only `^shots\/[^/]+$` or `^export\/\.render\/[^/]+$` (everything else is ignored, never extracted) | |
| 11 | inflate; `length > 80 * 1024 * 1024` | `` `A file in the package is too large: ${rel}` `` |
| 12 | running total of kept bytes `> 600 * 1024 * 1024` | `The package contents exceed the size limit.` |
| 13 | the buffer is at least 4 bytes long (`isPngOrJpeg` returns false below 4, so a 3-byte `FF D8 FF` is refused) and starts with PNG `89 50 4E 47` or JPEG `FF D8 FF` | `` `The package contains a non-image file where an image was expected: ${rel}` `` |
| 14 | `createProjectFromImport(manifest, files)` (01: re-checks the whitelist, confines without links, writes with never-overwrite, new id, `createdWith = 'shotAI'`, dates, `sopBackup = null`, not archived; title and `theme` kept) | 01's messages: `` `Package contains an unexpected file path: ${f.rel}` ``, `` `Refusing to extract a path outside the project: ${f.rel}` `` (`project-store.ts:358-364`), and a raw `EEXIST` from the `wx` write. The new folder (with `shots/` and `export/`) is created BEFORE the first write, and Electron does not remove it when a later write fails (01 owns the native cleanup) |

JSZip name handling that the checks depend on (`jszip` 3.10.1 `lib/load.js:66`, `lib/utils.js:328-343`): every entry name is sanitized by `resolve(name)`: split on `/`; drop `.` components and empty components except the first and last; `..` pops the previous component (a `..` above the root just disappears); join with `/`. Entries are then stored in an object keyed by the sanitized name, so a later duplicate replaces an earlier one (keeping the earlier one's position). Names are decoded as UTF-8 (general purpose bit 11 set: UTF-8; else the Info-ZIP Unicode Path extra field `0x7075` when present; else the default `decodeFileName`, also UTF-8; invalid sequences become U+FFFD) (`zipEntry.js:221-231`). CRC32 is not checked (`checkCRC32: false`).

Directory detection, which decides what step 10 skips (`zipEntry.js:135-159`, `object.js:42-53`): an entry is a directory when its external attributes have the DOS directory bit `0x10` (whatever the "made by" host), or, for a Unix-made entry, when the Unix mode (`externalFileAttributes >> 16`) has `S_IFDIR` `0x4000`, or when its UNSANITIZED name ends with `/`. A directory's key gets a trailing `/` forced on (`forceTrailingSlash`), so a directory can never shadow a file of the same name, and its data is discarded.

Title handling: the imported project keeps the package's title (the decode falls back to `Imported project` per 01); no suffix is added and no de-duplication happens, so importing the same package twice gives two projects with the same title and different ids. The sender's `theme` pin travels. REQUIRED.

### 2.23 User-visible strings (complete)

| String | Where | Citation |
|---|---|---|
| `This project has nothing to export yet \u2014 add a step first.` | collector and package | `export.ts:417`, `export-package.ts:62` |
| `` `Step ${n}'s screenshot render is missing from disk (${rel}). Open it in the editor and save to re-bake the render, then export again.` `` | collector | `export.ts:394-397` |
| `` `${label} has a redaction or crop that hasn't been baked into a render yet \u2014 refusing to export the raw screenshot. Open it in the editor and save, then retry.` `` | gate (04) | `render-gate.ts:37-40` |
| `` `${label} has no readable screenshot.` `` | gate (04) | `render-gate.ts:44` |
| `PDF rendering produced an empty document \u2014 printing may have failed on this system. Try the HTML or Markdown export instead.` | PDF | `export.ts:683-685` |
| `Export` | Save dialog title | `export.ts:871` |
| `Export Markdown (saved as a folder with its images)` | Save dialog title | `export.ts:847` |
| `Word Document`, `PowerPoint`, `Markdown`, `PDF`, `HTML` | Save dialog filter names | `export.ts:106-119` |
| `Choose a folder for the exports` | folder dialog | `export.ts:136` |
| `Import a shotAI project package`, `shotAI package` | import dialog title, filter | `ipc.ts:626-628` |
| `Failed to assemble the package.` and the eleven import messages in 2.22 | package | `export-package.ts:84-198` |
| `format must be one of: html, html-plain, pdf, markdown, docx, pptx` | IPC (ELECTRON-ONLY) | `ipc.ts:181` |

Strings that appear inside documents: `Overview` (HTML eyebrow, uppercased by CSS), `OVERVIEW` (Word), `Step {n}` (caption fallback), `Screenshot for step {n}` (alt text), `Note`, `Caution`, `Warning` (Office callout label fallbacks), `Created on `, ` by `, `shotAI SOP` (file name fallback), ` (shotAI package)`, `-plain`, `Imported project` (import title fallback), and the glyphs.

### 2.24 Log lines

| Level | Text | Citation |
|---|---|---|
| warn | `export: image re-encode failed, embedding the original:` + error | `export.ts:340` |
| warn | `brand face not found on disk; the PDF will use the fallback stack` | `export.ts:631` |
| info | `` `exported markdown \u2192 ${outputPath}` `` | `export.ts:862` |
| info | `` `exported ${format} \u2192 ${outputPath}` `` | `export.ts:907` |
| info | `` `exported package (originals=${includeOriginals}) \u2192 ${outputPath}` `` | `export-package.ts:125` |
| warn | the five `avif: ...` lines of 2.7 | `avif-encode.ts:58-133` |

Natively these go to `ILogger` category `export` with the same text (10). The output path is logged at info (it is a local path the user chose; parity).

## 3. Constants

### 3.1 Named constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| `RESERVED_CHARS` | `<>:"/\|?*` (the nine characters `<` `>` `:` `"` `/` `\` `\|` `?` `*`) | chars | removed from file names | `export.ts:40` |
| `RESERVED_NAME` | `^(con\|prn\|aux\|nul\|com[1-9]\|lpt[1-9])$`, case-insensitive | regex | names prefixed with `_` | `export.ts:41` |
| file base length cap | 120 | UTF-16 code units | then re-trimmed | `export.ts:52` |
| file base fallback | `shotAI SOP` | string | empty title | `export.ts:54` |
| plain suffix | `-plain` | string | `html-plain` stem | `export.ts:838` |
| package suffix | ` (shotAI package)` | string | package stem | `export-package.ts:122` |
| collision suffix | ` (` n `)` | string | n = 1, 2, ... | `export.ts:66`, `:80` |
| Markdown images folder | `images` | name | inside the Markdown folder | `export.ts:707` |
| Markdown image name | `step-` + `padStart(n, 2, '0')` + `-` + stepId + ext | name | | `export.ts:757` |
| package image name | `step-` + `padStart(n, 4, '0')` + ext | name | under `shots/` | `export-package.ts:90` |
| print temp | `export/.render/_print-<uuid>.html` | path | PDF print copy | `export.ts:647-659` |
| `HTML_COL_BASE` (`HTML_COL_W`) | 816 | px | document column at scale 1 | `doc-scale.ts:69`, `export-css.ts:23` |
| `HTML_DOC_PAD` | 32 | px per side | `.doc` horizontal padding | `doc-scale.ts:71` |
| `STEP_CHROME` | 78 (= 30 badge + 16 gap + 32 card padding) | px | column minus image | `doc-scale.ts:95-100` |
| image display floor | 120 | px | `max(120, col - 78)` | `doc-scale.ts:133` |
| `HTML_IMG_MAX_W` | 738 | px | display width at scale 1 (the card box is really 736 because of the 1 px borders; 738 kept for macOS parity) | `export-geometry.ts:135` |
| `HTML_IMG_EMBED_MAX_W` | 1476 | px | 2x embed at scale 1 | `export-geometry.ts:172` |
| `PLAIN_BODY_W` | 800 | px | HTML for Word body at scale 1 | `export-css.ts:26` |
| `COL_BLOCKS` | `.doc__title`, `.doc__meta`, `.doc__intro`, `.step`, `.section` | selectors | blocks carrying the column | `export-css.ts:33` |
| `HTML_IMG_JPEG_QUALITY` | 85 | 0..100 | AVIF fallback only | `export-geometry.ts:184` |
| `HTML_IMG_AVIF_QUALITY` | 50 | 0..100 | libavif quality | `export-geometry.ts:188` |
| `HTML_IMG_AVIF_SPEED` | 7 | 0..10, higher is faster | libavif speed; 8+ smears UI text and grows files | `export-geometry.ts:197` |
| AVIF bit depth, subsampling | 8, 4:2:0 | | jsquash defaults | `@jsquash/avif/meta.js` |
| AVIF brand check | `ftyp` at bytes 4..8; `/avif\|avis\|av01\|mif1\|miaf/` in bytes 8..24; length > 16 | | sanity | `avif-encode.ts:128-131` |
| resize quality | `'best'` | | Electron resize filter (macOS `.high`) | `export.ts:306-309` |
| PDF page | Letter, 8.5 by 11 | in | | `export.ts:677` |
| PDF margins | Chromium default, 1 cm (about 0.4) each side | in | | `export.ts:678`, Microsoft Learn |
| PDF print window | 900 by 1200 | px | hidden `BrowserWindow` | `export.ts:664-665` |
| `@font-face` descriptors | `format("truetype-variations");font-weight:100 900;font-stretch:62% 125%;font-style:normal` | | print copy only | `export.ts:636-637` |
| `DOCX_PAGE_W_TWIPS` | 11906 | twips | A4 width | `export-geometry.ts:49` |
| A4 height | 16838 | twips | | `export-docx.ts:235` |
| `DOCX_PAGE_MARGIN_TWIPS` | 1440 | twips per side | | `export-geometry.ts:51` |
| `DOCX_IMG_BASE_W` | 560 | px | Word image width at scale 1 | `export-geometry.ts:53` |
| `DOCX_CELL_INSET_TWIPS` | 180 | twips per side | card cell left and right margin (top and bottom 120) | `export-geometry.ts:55`, `export-docx.ts:80` |
| `DOCX_CARD_BORDER_EIGHTHS` | 4 | eighths of a point | card border size | `export-geometry.ts:57`, `export-docx.ts:71` |
| `DOCX_PAGE_COL_W` | 601 | px | A4 text column | `export-geometry.ts:65-67` |
| `DOCX_CARD_INNER_W` | 576 | px | Word image ceiling | `export-geometry.ts:73-80` |
| px to EMU | 9525 | EMU per px | docx `ImageRun` | `docx` `index.mjs:15036-15037` |
| docx sizes | Title `sz 56`; Heading2 `sz 26`, color `2E74B5`; created line `sz 18`; eyebrow `sz 15`; intro heading `sz 26`; spacer run `sz 10` | half-points | | `export-docx.ts:108-134`, `docx` defaults |
| docx spacing | created after 240; eyebrow after 40; intro heading after 40 or 0; spacer after 60; section before 280, after 60 or 120, body after 160; heading after 100 or 0 | twips | | `export-docx.ts` |
| section rule (docx) | `single`, size 6, space 8, color `hair` | | top border | `export-docx.ts:142` |
| docx font | `Aptos` | | document default | `export-docx.ts:252` |
| `SLIDE_W`, `SLIDE_H` | 13.333, 7.5 | in | `LAYOUT_WIDE`, 12192000 by 6858000 EMU | `export-pptx.ts:13-14` |
| `MARGIN` | 0.5 | in | | `export-pptx.ts:15` |
| `CARD` | x 0.45, y 0.45, w 12.433, h 6.6 | in | | `export-pptx.ts:19` |
| `PAD` | 0.35 | in | | `export-pptx.ts:20` |
| card radius | `0.12 * radii.card / 10` | in | 0.12 shotAI, 0.096 LFI | `export-pptx.ts:65-66` |
| pptx sizes | cover title 40, intro 16, created 10, section heading 34, section body 16, callout 24 and 18, text title 26, text body 18, shot caption 20, shot body 15 | pt | | `export-pptx.ts` |
| pptx outline | 1 | pt (12700 EMU) | card and section rule | `export-pptx.ts:80`, `:136` |
| `PKG_MARKER` | `shotai-package.json` | name | | `export-package.ts:25` |
| `PKG_FORMAT` | `shotai-package` | | | `export-package.ts:26` |
| `PKG_VERSION` | 1 | | newest readable | `export-package.ts:27` |
| `MAX_PKG_BYTES` | 629145600 (600 x 1024 x 1024) | bytes | zip size and total extracted | `export-package.ts:29` |
| `MAX_FILE_BYTES` | 83886080 (80 x 1024 x 1024) | bytes | per extracted image | `export-package.ts:30` |
| import whitelist | `^shots\/[^/]+$`, `^export\/\.render\/[^/]+$` | regex | | `export-package.ts:186` |
| PNG magic, JPEG magic | `89 50 4E 47`, `FF D8 FF` | bytes | | `export-package.ts:32-37` |
| zip compression | DEFLATE | | package | `export-package.ts:118` |
| import title fallback | `Imported project` | | | `export-package.ts:176` |

### 3.2 Derived widths per detent

`col = round(816*s)`; `doc = col + 64`; `img = max(120, col - 78)`; `embed = 2*img`; `plain = round(800*col/816)`; `docx = min(576, round(560*s))`; `pptx shrink = min(1, s)`. Evaluated with the Electron code.

| s | col | doc | img (display, plain embed) | embed (html) | plain body | docx image | pptx shrink |
|---|---|---|---|---|---|---|---|
| 0.65 | 530 | 594 | 452 | 904 | 520 | 364 | 0.65 |
| 0.70 | 571 | 635 | 493 | 986 | 560 | 392 | 0.70 |
| 0.75 | 612 | 676 | 534 | 1068 | 600 | 420 | 0.75 |
| 0.80 | 653 | 717 | 575 | 1150 | 640 | 448 | 0.80 |
| 0.85 | 694 | 758 | 616 | 1232 | 680 | 476 | 0.85 |
| 0.90 | 734 | 798 | 656 | 1312 | 720 | 504 | 0.90 |
| 0.95 | 775 | 839 | 697 | 1394 | 760 | 532 | 0.95 |
| 1.00 | 816 | 880 | 738 | 1476 | 800 | 560 | 1 |
| 1.05 | 857 | 921 | 779 | 1558 | 840 | 576 | 1 |
| 1.10 | 898 | 962 | 820 | 1640 | 880 | 576 | 1 |
| 1.15 | 938 | 1002 | 860 | 1720 | 920 | 576 | 1 |
| 1.20 | 979 | 1043 | 901 | 1802 | 960 | 576 | 1 |
| 1.25 | 1020 | 1084 | 942 | 1884 | 1000 | 576 | 1 |

`clampScale` maps NaN and non-numbers to 1, `99` to 1.25, `-3` to 0.65, `0.825` to 0.85, `1.025` to 1.0 (integer-percent snap, spec 05).

### 3.3 PowerPoint geometry at scale 1 (EMU)

`round(914400 * inches)`, evaluated with the Electron constants.

| Element | x, y, w, h (in) | x, y, cx, cy (EMU) |
|---|---|---|
| cover title, with intro | 0.5, 2.2, 12.333, 1.2 | 457200, 2011680, 11277295, 1097280 |
| cover title, no intro | 0.5, 3.0, 12.333, 1.2 | 457200, 2743200, 11277295, 1097280 |
| cover intro text | 1.5, 3.6, 10.333, 2.6 | 1371600, 3291840, 9448495, 2377440 |
| cover created line | 0.5, 6.8, 12.333, 0.4 | 457200, 6217920, 11277295, 365760 |
| section rule | 4.6665, 2.9, 4, 0 | 4267048, 2651760, 3657600, 0 |
| section heading | 0.5, 3.05, 12.333, 1.0 | 457200, 2788920, 11277295, 914400 |
| section body | 2.0, 4.2, 9.333, 2 | 1828800, 3840480, 8534095, 1828800 |
| card | 0.45, 0.45, 12.433, 6.6 | 411480, 411480, 11368735, 6035040 |
| callout text (INNER) | 0.8, 0.8, 11.733, 5.9 | 731520, 731520, 10728655, 5394960 |
| text step title | 0.8, 0.8, 11.733, 1 | 731520, 731520, 10728655, 914400 |
| text step body | 0.8, 1.95, 11.733, 4.75 | 731520, 1783080, 10728655, 4343400 |
| shot caption | 0.8, 0.8, 11.733, 0.6 | 731520, 731520, 10728655, 548640 |
| shot body | 0.8, 5.6, 11.733, 1.1 | 731520, 5120640, 10728655, 1005840 |
| shot image box, with body | 0.8, 1.55, 11.733, 3.95 | 731520, 1417320, 10728655, 3611880 |
| shot image box, no body | 0.8, 1.55, 11.733, 5.15 | 731520, 1417320, 10728655, 4709160 |

Card corner adjust: `round(r * 914400 * 100000 / 6035040)` = 1818 (r = 0.12) and 1455 (r = 0.096). The image itself is `fitContain` inside the box, each coordinate rounded independently.

### 3.4 CSS golden checksums

UTF-8 byte length and the first 16 hex digits of SHA-256 of `docCss(s, exportTheme(b))` and `plainCss(s, exportTheme(b))`, computed from the Electron source (Node 22, `--experimental-strip-types`). Full digests at scale 1, shotAI: `docCss` `190027cd270f54d6ed8354a3a52854dbc7f4953ca739b11ea12cd218f701b61d`, `plainCss` `9a95c8f88182e91d0b593cd0bfe8004262675b7ea519cf022b2e8c681f0cda2b`. These pin the verbatim port (AC-EXP-1); a change to `contract/brand.json` changes them, so the golden files (8.2) are regenerated from Electron, not typed.

| brand | s | docCss bytes | docCss sha256/16 | plainCss bytes | plainCss sha256/16 |
|---|---|---|---|---|---|
| shotAI | 0.65 | 2867 | `c04d0f64f7916ef9` | 451 | `33194b59769344e0` |
| shotAI | 0.7 | 2867 | `8b544359c582aa9d` | 451 | `628aac54d9ebe527` |
| shotAI | 0.75 | 2867 | `2823e2382d8418f7` | 451 | `fd5473d0584e0c32` |
| shotAI | 0.8 | 2867 | `1951f42060a9bc37` | 451 | `d9f8fbd1102a968d` |
| shotAI | 0.85 | 2867 | `8eb9dd1791d5224a` | 451 | `9a112be8f774012e` |
| shotAI | 0.9 | 2867 | `0e804730049c803f` | 451 | `ef8874941f4e0e62` |
| shotAI | 0.95 | 2867 | `4c90d34fcf84c689` | 451 | `8be9344f26536388` |
| shotAI | 1 | 2867 | `190027cd270f54d6` | 451 | `9a95c8f88182e91d` |
| shotAI | 1.05 | 2867 | `79d9cc07e38c4370` | 451 | `3b7c8bdf54f86595` |
| shotAI | 1.1 | 2867 | `8f631823e0d8c5d8` | 451 | `bf0fc2c5b01410e0` |
| shotAI | 1.15 | 2867 | `2efa96820b858087` | 451 | `9c321965ed03cb29` |
| shotAI | 1.2 | 2867 | `b27fd8ee3187953d` | 451 | `5a60c4ca14235e6e` |
| shotAI | 1.25 | 2872 | `620df3f2c6c83835` | 452 | `18341afff1a26917` |
| lfi | 0.65 | 2878 | `a89a4cd1d0961e88` | 461 | `0e002b4b27e1d832` |
| lfi | 0.7 | 2878 | `370408af9040db9a` | 461 | `d316d58b2019ecdb` |
| lfi | 0.75 | 2878 | `3016a95857c58ec8` | 461 | `858c6e17ffa45436` |
| lfi | 0.8 | 2878 | `7c0ea317baf0da82` | 461 | `54c44d43d3a7ed21` |
| lfi | 0.85 | 2878 | `4ba5983e1c77a5c6` | 461 | `ff7f93e65b033120` |
| lfi | 0.9 | 2878 | `4ad328815ec22aa8` | 461 | `044bd6d9837c644f` |
| lfi | 0.95 | 2878 | `9352bc1da8a3e254` | 461 | `e61f71e1c040e7ae` |
| lfi | 1 | 2878 | `c9eaf69c3325da64` | 461 | `d7f38957fa44d942` |
| lfi | 1.05 | 2878 | `ede31a1a34007f04` | 461 | `aa532524ddee7fb7` |
| lfi | 1.1 | 2878 | `7e6fcf97740ad5b5` | 461 | `de9603a881a84b18` |
| lfi | 1.15 | 2878 | `830511cbb5d880c4` | 461 | `9e6d51ac62a77664` |
| lfi | 1.2 | 2878 | `cb0b6d3f1fc818d0` | 461 | `653007a7b5a8f55e` |
| lfi | 1.25 | 2883 | `f272a1993f8cbf34` | 462 | `21249d4b336443d2` |

## 4. Invariants

**INV-EXP-1 [SECURITY]. Every document format (html, html-plain, pdf, markdown, docx, pptx) obtains its steps only from the one step collector, and the collector obtains every shot image only through 04's render gate.** Why: the fail-closed redaction rule must be identical for every format; a second loop is how a format would start reading raw screenshots. Citation: `src/main/export.ts:1-7`, `:357-420`, `:388`. Test: `StepCollectorTests.EveryShotGoesThroughTheGate` (fake gate records every call; a refusing gate fails the export before anything is written) and `ExportEngineTests.EveryFormatUsesTheCollector` (fake collector counts calls per format).

**INV-EXP-2 [SECURITY]. Image processing (zoom crop, resample, re-encode, AVIF, JPEG) runs only on bytes the gate returned, and can only discard information: it never reads another file and never falls back to a different file.** Why: a crop or resample failure must degrade to the gated render, never to the raw screenshot. Citation: `export.ts:190-209`, `:273-275`, `avif-encode.ts:110-112`. Test: `HtmlImageEmbedderTests.FailuresFallBackToTheGatedBytes` (codec that throws at each stage; output bytes equal the gate's bytes), and 04's `Editor/EndToEndRedactionTests` run over HTML, HTML for Word, Markdown, Word and PowerPoint.

**INV-EXP-3 [SECURITY]. A refused or missing render fails the whole export before any output file or folder content is written, and no Save dialog opens.** Why: fail closed, and a partial export of a sensitive project is worse than none. Citation: `export.ts:828` precedes `:843-905`; `:391-398`. Test: `ExportEngineTests.RefusalWritesNothingAndShowsNoDialog`.

**INV-EXP-4. Numbering: shots and non-empty plain text steps share one contiguous 1..N counter in step order; callouts (note, caution, warning, section) are unnumbered; empty plain text steps and empty sections are skipped without consuming a number; empty colored callouts are kept.** Why: the export must number exactly like the report (05) and like macOS. Citation: `export.ts:350-420`. Test: `StepCollectorTests.NumberingMatchesTheReport` (table of mixed step lists, including the #90 case where a trailing shot must be 3).

**INV-EXP-5. A `callout` value is treated as a callout only when `isCalloutKind` accepts it; any other value makes the step a plain numbered text step, in every format.** Why: #90 (`18f3c4d`): truthiness shifted every later number and crashed the Office exports. Citation: `export.ts:367-371`, `:464`, `:567`, `:735`; `export-docx.ts:162-165`; `export-pptx.ts:165-166`. Test: `StepCollectorTests.UnknownCalloutIsAPlainNumberedStep`, `DocxBuilderTests.UnknownCalloutDoesNotThrow`, `PptxBuilderTests.UnknownCalloutDoesNotThrow`.

**INV-EXP-6. `docCss` puts `max-width:{col}px` and a self-centering `margin:... auto ...` on each of the five column blocks, never on `.doc` (which only pads), and `@media print` resets exactly those five blocks to `max-width:none`, at every scale.** Why: #57, a KB paste unwraps wrappers; a missed print reset renders the PDF at the screen column. Citation: `export-css.ts:28-33`, `:44-73`, `:125`. Test: `ExportCssTests` (8.1).

**INV-EXP-7. The styled HTML and HTML for Word stylesheets are byte-identical to Electron's for every brand and every detent, and invalid scales produce the clamped stylesheet (never `NaN` or a negative width).** Why: the export is the product; the CSS was ported verbatim by macOS too, and byte identity is the only check that catches a dropped declaration. Citation: `export-css.ts:86-152`. Test: `ExportCssGoldenTests` (3.4 digests and the golden files).

**INV-EXP-8. The display width of an exported image is `htmlImgMaxW(s) = max(120, round(816*s) - 78)`, re-derived per scale, never `738 * s`; the styled HTML embed width is exactly twice that.** Why: #70, the chrome does not scale; a fixed embed at 65% would triple the payload. Citation: `doc-scale.ts:121-147`, `export-geometry.ts:137-178`. Test: `ExportGeometryTests.ImageWidthIsReDerivedNotMultiplied`, `.EmbedIsExactlyTwiceTheDisplay`.

**INV-EXP-9. Embed policy: `html` AVIF at 2x (JPEG 85 fallback, then the original bytes); `html-plain` PNG at 1x; `pdf` the untouched source bytes (or the crop PNG); every other format never transcodes.** Why: #56: payload ceiling of the KB editor, Word cannot read AVIF, print needs full resolution. Citation: `export-geometry.ts:210-268`. Test: `ExportGeometryTests` (policy table) and `HtmlImageEmbedderTests.PdfBytesAreUntouched`.

**INV-EXP-10. The pipeline never upscales and never emits an image larger (in bytes) than the input it was given.** Why: re-encoding a crisp screenshot can make it bigger, and inventing pixels makes text soft. Citation: `export.ts:263-271`, `:301-311`, `:343-346`. Test: `HtmlImageEmbedderTests.NeverUpscales`, `.NeverBigger`.

**INV-EXP-11. Every inlined `<img>` in both HTML formats carries `width` and `height` attributes computed from the pre-resample pixel size, capped at `htmlImgMaxW(s)`, unless the size could not be decoded, in which case it carries none.** Why: Word, Google Docs and KB editors drop CSS `max-width` (#52, #57). Citation: `export.ts:283-288`, `export-geometry.ts:270-284`. Test: `ExportGeometryTests` (htmlImageSize cases), `HtmlDocumentBuilderTests.AttributesUseTheInputSize`.

**INV-EXP-12. A Word image is never wider than 576 px (the card's inner width on an explicitly A4 page with 1 in margins), and at 100% it is 560 px or its native width if narrower.** Why: #70, Word silently clips; 624 and 598 both shipped clipped. Citation: `export-geometry.ts:28-92`, `export-docx.ts:227-245`. Test: `ExportGeometryTests` (Word ceiling group), `DocxBuilderTests.PageIsExplicitA4`.

**INV-EXP-13. The PowerPoint image box only shrinks with the document scale (`min(1, s)` on both axes) and stays horizontally centered.** Why: a slide is a fixed canvas (#70). Citation: `export-pptx.ts:225-238`. Test: `PptxBuilderTests.ImageBoxShrinksOnlyAndCenters`.

**INV-EXP-14. The export theme is the LIGHT palette, radii and font stacks of `PinnedBrand(manifest.theme) ?? CoerceBrand(appBrand)`, where the app brand is read at export time.** Why: #77 (a dark SOP is unreadable printed), #95 (an unknown pin must fall back to the app brand, not the default), phase 1b (one read, one decision, where the manifest is). Citation: `export-theme.ts:6-10`, `:62-72`; `export.ts:816-827`; `ipc.ts:545-556`. Test: `ExportThemeTests` (8.1), `ExportEngineTests.BrandIsReadAtExportTime`.

**INV-EXP-15. The brand face is named in the stylesheet font stacks and never embedded in the `.html` output; only the PDF print copy embeds it, and only when the brand has a face and the file exists (else the fallback stack, with a warning).** Why: 0.84 MB of base64 against a 0.8 to 1.5 MB paste budget; the PDF is the one format where the face reaches a reader without it. Citation: `export-theme.ts:35-42`, `export.ts:613-639`, `:660-661`. Test: `ExportThemeTests.FontIsNamedNeverEmbedded`, `PrintHtmlTests.FaceOnlyInThePrintCopy`, `.MissingFaceDegrades`.

**INV-EXP-16. The export surfaces (the CSS generator and the two Office builders) contain no color literal of their own; every color comes from the threaded theme, and the Office builders reach it only through the throwing `#rrggbb` to `RRGGBB` converter.** Why: #77 phase 0, a hardcoded color is how the fifth copy of the palette starts; both Office libraries render a malformed color as black. Exceptions are listed with a reason (the docx library's default heading colors, 7.10). Citation: `export-palette-source.test.ts`, `theme-palette.ts:518-530`. Test: `ExportPaletteSourceTests` (8.1).

**INV-EXP-17. The step badge takes the brand's chip radius (`50%` when the chip is fully round, else `{chip}px`); the card and figure radii come from the theme and the figure radius is smaller than the card's.** Why: macOS shipped LFI documents with circular numbers by treating `50%` as structure (`b244010`); concentric radii (`32d16b0`). Citation: `export-theme.ts:99-109`, `export-css.ts:110-119`. Test: `ExportThemeTests.BadgeIsTheBrandChip`, `ExportPaletteSourceTests.RenderedRadii`.

**INV-EXP-18. The styled HTML, the HTML for Word and the Markdown text are byte-identical to Electron's for the same project, the same created line and the same embedded image bytes; Markdown's uncropped image files are byte copies of the gated renders and cropped ones decode to the same pixels.** Why: the phase D exit test of the feasibility study. Citation: `export.ts:432-776`. Test: `ExportGoldenTests` (8.2).

**INV-EXP-19. The PDF export fails closed: an empty, non-PDF or failed print writes nothing at the destination and shows the empty-document message.** Why: blank prints on software-rendered or headless setups. Citation: `export.ts:680-686`. Test: `PdfRendererTests.EmptyResultThrowsAndLeavesNoFile` (Windows), `ExportEngineTests.PdfFailureWritesNothing` (fake renderer).

**INV-EXP-20 [SECURITY]. The PDF engine renders only the print copy: scripts disabled, every navigation other than the print copy cancelled, every sub-resource request refused (by the request filter and by the print copy's CSP, D-EXP-25), no new windows, no downloads, no dev tools, and it is never shown.** Why: the print document is fully static and must not be a way to reach the network or the file system; Electron used `javascript: false`, `sandbox: true` (`export.ts:662-672`). Test: `PdfRendererTests.PageCannotNavigateOrFetch` (Windows: a print copy containing an `<img src="https://...">`, a `<meta http-equiv="refresh">` and an `<iframe>`; the fake web server sees no request and the PDF still prints).

**INV-EXP-21. Exports to the project folder or a shared folder never overwrite anything: the first free name of `stem`, `stem (1)`, `stem (2)`, ... is used, where anything existing at the name (file, folder, or link) counts as taken.** Why: #37, a previous export may be open in another program (Windows sharing lock), and two projects with the same title share a bulk folder. Citation: `export.ts:59-87`. Test: `ExportNamesTests.NextAvailableStem`, `.NextAvailableDir`, `.ExistingFolderBlocksAFileName`.

**INV-EXP-22. A cancelled Save or folder dialog writes no output file and returns a canceled result (not an error).** Citation: `export.ts:851`, `:875`, `:143`. Test: `ExportEngineTests.CanceledDialogWritesNothing`.

**INV-EXP-23. Markdown is always a self-contained folder: `<name>/<name>.md` plus `<name>/images/`, with image links `<images/NAME>` in angle brackets.** Why: #37 keeps the destination tidy; the stem may contain spaces and parentheses. Citation: `export.ts:694-776`, `:841-865`. Test: `MarkdownDocumentBuilderTests.FolderLayout`, `ExportEngineTests.MarkdownCollisionNumbersTheFolder`.

**INV-EXP-24 [SECURITY]. The bulk-folder reveal opens a path only when it is an existing directory, never a file; bulk exports never reveal per file.** Why: `revealExportDir` must not be coaxed into executing a file (`export.ts:912-925`); #37 N folders popping open. Test: `ExportServiceTests.RevealDirectoryRefusesAFile`, `.BulkDoesNotRevealPerFile`.

**INV-EXP-25 [SECURITY]. A safe package carries no original pixels and no editable state that references them: each shot's only image is its gated render under `shots/step-NNNN.<ext>`, and its `annotations`, `crop`, `click`, `flattened`, `renderRev`, `markerBaked` are reset; `sopBackup` is `null` in every package.** Why: redactions become permanent for the recipient; the sender's revert history never travels. Citation: `export-package.ts:66-100`. Test: `PackageWriterTests.SafeModeStripsEverythingButTheRender`, `.SopBackupNeverTravels`.

**INV-EXP-26 [SECURITY]. Package import treats the zip as untrusted: size caps on the file, each kept entry and the running total (checked before and while inflating), the marker and format checks, the manifest decode, a folder whitelist applied to the sanitized entry name, PNG or JPEG magic bytes, and extraction only through 01's confined, never-overwrite `CreateProjectFromImportAsync` into a fresh folder. Anything not whitelisted is never read or extracted.** Citation: `export-package.ts:130-204`, `project-store.ts:345-381`. Test: `PackageReaderTests` (8.2).

**INV-EXP-27. Exports never flatten and never mutate the project; the caller runs 04's `EnsureFlattenedAsync` first.** Why: one owner for the bake; exports are read-only egress. Citation: `export.ts:786-788`, `ProjectDetail.tsx:310-332`. Test: `ExportEngineTests.NeverWritesTheProject` (the project folder's files and `project.json` bytes are unchanged except under `export/`).

**INV-EXP-28. An export reads the project after every write queued for it before the export started has finished.** Why: native edits are optimistic (fixed decision), so a caption typed just before Export may not be on disk yet; Electron had no such window because every edit waited for the disk. Citation: new (fixed decision); 01 7.7. Test: `ExportEngineTests.WaitsForPendingWrites` (a queued caption edit appears in the export).

**INV-EXP-29. Progress: exactly one `{0, k}` before the first image, then `{i, k}` after each shot's image is embedded, for `html` and `pdf` only, and only when the caller asked for progress.** Citation: `export.ts:444-448`, `:493`. Test: `HtmlDocumentBuilderTests.ProgressSequence`.

**INV-EXP-30. The created line is `Created on {timestamp}` plus ` by {name}` exactly when the byline opt-in is on and the trimmed name is non-empty; it is computed once per export and appears in html, pdf, markdown, docx and pptx, never in html-plain.** Citation: `export.ts:830-834`, `settings.ts:389-393`. Test: `CreatedLineTests`, `ExportEngineTests.BylineOnlyWhenOptedIn`.

**INV-EXP-31. Text written by the HTML builders is HTML-escaped (`&`, `<`, `>`, `"`), and user text in Markdown headings, captions, the title and the created line is Markdown-escaped, while Markdown bodies are raw.** Why: a title or caption must render literally; bodies may carry authored Markdown. Citation: `export.ts:147-158`. Test: `ExportTextTests.EscapeHtml`, `.EscapeMarkdown`, `MarkdownDocumentBuilderTests.BodiesAreRaw`.

**INV-EXP-32 [SECURITY]. With originals, the package reads referenced files only through link-refusing confinement; a reference that escapes the project or crosses a symbolic link or junction is skipped.** Why: a symlinked component could copy an arbitrary off-project file into a package that leaves the machine; macOS already does this (`ExportPackage.swift:128-137`). IMPROVEMENT D-EXP-16. Test: `PackageWriterTests.OriginalsSkipLinkedReferences` (Windows, junction).

**INV-EXP-33. The PDF print copy lives only under `<project>\export\.render\` as `_print-<uuid>.html`, is deleted when the print ends (success or failure), and earlier orphans are swept at the next PDF export.** Citation: `export.ts:647-659`, `:688-691`. Test: `PdfRendererTests.TempCopyIsRemoved`, `ExportEngineTests.OrphanSweep`.

**INV-EXP-34. A package import never modifies an existing project: it creates a new project folder with a new id; the package's title and brand pin are kept and no title is changed or de-duplicated.** Citation: `export-package.ts:203`, `project-store.ts:345-381`. Test: `PackageReaderTests.ImportTwiceMakesTwoProjects`.

**INV-EXP-35 [SECURITY]. A PDF export makes no outbound network request: neither the print copy nor the WebView2 runtime connects to any host between the Save dialog closing and the file being revealed; the only permitted exceptions are ones measured and named in the README privacy section (Q-EXP-27).** Why: the README promises that every network call the app makes is named and that there is no telemetry (`README.md:9`, `:179`); WebView2 is the one web engine the native app adds, and its SmartScreen service is on by default. REQUIRED (parity): Electron's print ran in Electron's Chromium, which has no Safe Browsing or reputation service (`export.ts:661-679`). INV-EXP-20 covers requests the page makes; this invariant covers requests the runtime makes. Mechanism: 7.7 Egress. Test: `PdfRendererTests.ReputationCheckingIsOff` (Windows: `Settings.IsReputationCheckingRequired` reads `false` before `Navigate`, and the environment was created with `PdfEngine.BrowserArguments`), AC-EXP-42 (manual network trace).

## 5. Edge cases and hard-won fixes

**EDGE-EXP-1. An unrecognised callout value.** A step with `callout: 'tip'` (a future kind, kept on read) is exported as a plain numbered text step in every format; Word and PowerPoint do not throw. Came from #90, fixed by #99 (`18f3c4d`); before it, the trailing shot numbered 2 instead of 3 and `CALLOUT[unknown].fg` threw. Citation: `export.ts:367-371`.

**EDGE-EXP-2. An empty section divider.** A `section` with neither heading nor body is skipped (it would emit a stray rule, `<hr>` or `---`). From #45/#47 (`4259bd2`). Citation: `export.ts:372-375`.

**EDGE-EXP-3. An empty colored callout.** Kept. HTML emits the glyph badge and an empty card; HTML for Word `<blockquote><p><strong>{glyph}</strong></p></blockquote>`; Markdown `> **{glyph}**`; Word and PowerPoint `{glyph} Note` (or `Caution`, `Warning`). Citation: `export.ts:372-377`, `export-docx.ts:171`, `export-pptx.ts:173`.

**EDGE-EXP-4. A shot whose render was deleted off disk after the manifest was written.** The export fails with the render missing message naming the manifest-relative path, instead of an opaque ENOENT mid-write. Citation: `export.ts:389-398`.

**EDGE-EXP-5. The PDF inherits whatever the HTML does.** On Windows the PDF is printed from the same builder, so it must opt out of the resample and the codec explicitly (`pdf` policy), or it prints at about 110 DPI. From #56 (`66d7376`). Citation: `export-geometry.ts:231-236`, `export.ts:896-904`. Natively the same builder feeds WebView2, so the trap is unchanged.

**EDGE-EXP-6. Pasting into a Freshservice KB article (Froala).** Wrappers enclosing the whole document are unwrapped at any depth; other elements keep their computed styles inlined; `<img>` loses `max-width`. Required: the column on every block, `.doc` only pads, the section rule on `.section__inner`, image attributes. From #57 (`66d7376`); two inference-based fixes failed first. Citation: `export-css.ts:44-73`.

**EDGE-EXP-7. The print reset must cover all five blocks.** When the column moved from `.doc` to the blocks, `@media print` had to reset all five or the PDF would stop spanning the page. From `66d7376`. Citation: `export-css.ts:124-125`.

**EDGE-EXP-8. Re-encoding can make a screenshot bigger.** Interpolation turns flat color runs into many colors; the re-encode is discarded unless strictly smaller. Citation: `export.ts:263-271`, `:343-346`.

**EDGE-EXP-9. The codec history.** Full-resolution PNG was 48.81 MB of base64; PNG@738 3.12 MB still failed to paste; WebP@2x (524 KB) pasted as broken images ("No link in upload response", Froala error 2: the editor re-uploads every pasted image); JPEG@2x (1.02 MB) and JPEG@1x (414 KB) also failed; AVIF@2x (168 KB) pastes, as macOS's AVIF did. The constraint is a total payload ceiling, not a format allowlist. From `66d7376`. Citation: `export-geometry.ts:210-261`, `avif-encode.ts:1-18`. Required natively: AVIF at 2x; do not "simplify" to JPEG or PNG.

**EDGE-EXP-10. AVIF unavailable.** A missing or broken encoder is detected once per process; the styled export then embeds JPEG 85 at 2x, and if that is not smaller, the original bytes. Citation: `avif-encode.ts:35-105`, `export.ts:313-334`. Natively: a missing `shotai_avif.dll` (or an unsupported CPU path in it) behaves the same, logged once.

**EDGE-EXP-11. Word images clipped at 125%.** The ceiling was the Letter page column (624), then the A4 column without the card insets (598); both clipped because Word silently cuts an over-wide picture. The page is now set explicitly to A4 and the ceiling is 576. From `b6ddce5`, `e93093a` (#70). Citation: `export-geometry.ts:34-92`.

**EDGE-EXP-12. `738 * s` agrees with the truth only at 100%.** Any scaled width must be re-derived from the scaled column. From #70 (`ec76adb`). Citation: `doc-scale.ts:121-128`.

**EDGE-EXP-13. A scale value that is not a detent.** `NaN`, `99`, `-3` and off-detent numbers are clamped and snapped before any width is derived, so no stylesheet contains `NaN` or a negative width. Citation: `export.ts:440-442`, `export-css.test.ts:170-176`.

**EDGE-EXP-14. A title longer than 120 UTF-16 units.** Truncated at 120 units, re-trimmed, then trailing dots and spaces removed (Windows forbids a trailing dot or space). Electron may cut between the two halves of a surrogate pair, leaving a lone high surrogate in the file name. Required natively: IMPROVEMENT D-EXP-10, when unit 120 would split a pair, cut at 119. Citation: `export.ts:50-53`.

**EDGE-EXP-15. Reserved device names.** `CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to `LPT9` (any case) and names starting with `.` get a `_` prefix. Windows also treats a reserved name followed by an extension (`nul.backup.html`) as the device on older builds, and reserves `CONIN$`, `CONOUT$`, `COM¹`, `COM²`, `COM³`, `LPT¹`, `LPT²`, `LPT³`. Electron misses those. Required natively: IMPROVEMENT D-EXP-11 (Q-EXP-12), also prefix when the part before the first `.` matches the extended set. Citation: `export.ts:37-41`, `:55`.

**EDGE-EXP-16. Repeated exports while the previous one is open.** A second export never overwrites (or fails on) a file another program has locked; it takes the next free name. From #37 (`f9f8b10`). Citation: `export.ts:59-63`. The Save dialog path CAN target a locked file; Electron then shows the raw `EBUSY` error. Natively: IMPROVEMENT D-EXP-19 (a clear message, 7.15).

**EDGE-EXP-17. Two selected projects with the same title in one bulk folder.** Each gets its own numbered name. Citation: `export.ts:876-882`.

**EDGE-EXP-18. Bulk exports opened N folders mid-run.** Per-file reveal is suppressed for both bulk destinations; the shared folder is revealed once at the end (06). From #37 (`f9f8b10`). Citation: `export.ts:811-814`, `ipc.ts:576-600`.

**EDGE-EXP-19. Failure before the dialog.** The collector runs before the Save dialog, so "nothing to export", a gate refusal or a missing render is reported without asking where to save. The created line's time is also taken before the dialog opens. Citation: `export.ts:828-834`, `:869-876`.

**EDGE-EXP-20. Markdown pitfalls.** A `---` directly under a text line would make that line a Setext heading (blank line before every `---`); a bare `N. text` line would become a renumbered ordered list (`**N.** text`); two adjacent quoted lines merge into one paragraph (a bare `>` line between the callout heading and body). From #40 (`2e3429d`). Citation: `export.ts:721-770`.

**EDGE-EXP-21. Multi-line headings.** Markdown collapses newlines in section headings, plain text headings and captions to one space (an ATX heading is one line), but NOT in the title, the intro heading or a callout heading (a newline there ends the quoted line; parity). HTML shows them as white space (no `pre-wrap` on headings). Word writes the literal newline in one run. PowerPoint splits the text into paragraphs. REQUIRED (parity in each format). Citation: `export.ts:730`, `:748`, `:760`, `export-pptx.ts` via `pptxgenjs` line splitting.

**EDGE-EXP-22. Markdown Save As onto an existing folder.** The chosen `X.md` becomes the folder `X/`; if `X/` already exists it is reused, `X/images/` is DELETED recursively, and `X/X.md` is overwritten without confirmation (the dialog only confirmed `X.md` in the parent). A user who types the name of an existing unrelated folder loses its `images` subfolder. macOS has the same behavior (`MarkdownExport.swift:24-26`). Required natively: IMPROVEMENT D-EXP-9, reuse the folder only when it is empty or looks like a prior export (it contains `X.md`, and nothing other than `X.md` and `images/`); otherwise use `nextAvailableDir(parent, X)`. Citation: `export.ts:845-854`, `:709`.

**EDGE-EXP-23. A brand this build does not know.** `theme: 'solarpunk'` is preserved on disk (#106) and exports in the app brand (#95), never in the default brand. From `77adda3`. Citation: `export.ts:816-827`.

**EDGE-EXP-24. Prototype member names as brands.** `'toString'`, `'__proto__'` and the like were accepted by `in` and crashed the plain font stack (#89). C# has no prototype chain, but `Enum.TryParse` accepts numeric strings (`"0"`, `"1"`), whitespace and, with `ignoreCase`, `"LFI"`; a case-insensitive dictionary would accept `"Lfi"`. Required natively: brand narrowing by ordinal string equality against the known ids only (8.1 brand-narrowing port). Citation: `theme-palette.ts:249-269`, `brand-narrowing.test.ts`.

**EDGE-EXP-25. The badge is a chip.** A brand whose chip is not fully round (LFI 8 px) must not get circular step numbers. From `b244010`. Citation: `export-theme.ts:99-109`.

**EDGE-EXP-26. The app is in dark mode.** Exports always use the light palette. From #77 phase 4. Citation: `export-theme.ts:6-10`.

**EDGE-EXP-27. The variable brand face renders semibold.** Archivo's default instance is weight 600; the `@font-face` must declare `font-weight:100 900`. From `1025bfe`. Citation: `export.ts:615-621`.

**EDGE-EXP-28. The brand face file is missing.** The PDF degrades to the fallback stack with a warning; the export does not fail. Citation: `export.ts:629-633`, `paths.ts:29-36`.

**EDGE-EXP-29. Blank PDF on software-rendered or headless systems.** Fail closed with the empty-document message, which points to HTML and Markdown. Citation: `export.ts:680-686`.

**EDGE-EXP-30. A package marker or manifest that is not a JSON object.** Only JSON `null` crashes: Electron reads `marker.format` off `null` and shows a raw TypeError, and `coerceManifest(null)` also throws a raw TypeError (`project-store.ts:148-156` records this gap). Any other non-object behaves differently and quietly: a marker that is an array, string, number or boolean has `format` `undefined` and gives `Unrecognized package format.`; a manifest that is an array, string, number or boolean is coerced to an EMPTY manifest and imports as a blank project titled `Imported project` with no images (the whitelisted image entries are still extracted into it). Required natively: IMPROVEMENT D-EXP-13, any non-object marker (including `null`) gives `Unrecognized package format.` (parity for everything but `null`), and any non-object manifest (including `null`) gives `The package project.json is corrupt.` (a deliberate change for arrays and primitives, which Electron imported as a blank project).

**EDGE-EXP-31. Marker version forms.** A missing, string, boolean, `null` or object `version` passes; any JSON number greater than 1 is refused, including `1.5` and `1e999` (which JavaScript parses as `Infinity`); `1.0`, `0` and negative numbers pass. REQUIRED (parity): natively a JSON number token whose value overflows `double` must be treated as greater than 1, not as a parse failure. Citation: `export-package.ts:162-164`.

**EDGE-EXP-32. Traversal names in the zip.** JSZip rewrites every entry name with `resolve` before anything sees it: `shots/../shots/a.png` becomes `shots/a.png` (accepted), `../../a.png` becomes `a.png` (ignored), `./shots/a.png` becomes `shots/a.png`, `/shots/a.png` stays (ignored, leading empty component kept), `shots//a.png` becomes `shots/a.png`. Backslashes are not separators for `resolve` but are turned into `/` afterwards, so `shots\a.png` is accepted as `shots/a.png`. Required natively: apply the same `resolve` then the backslash replacement, then the whitelist (REQUIRED for parity of which entries import); 01's confinement is the second wall. Citation: `jszip/lib/utils.js:328-343`, `export-package.ts:183-186`.

**EDGE-EXP-33. Duplicate entry names.** Two entries whose sanitized names are equal collapse to the LAST one (JSZip object semantics). Two entries that differ only by `\` versus `/` are distinct in JSZip, both pass the whitelist, and the second extraction fails with a raw `EEXIST` from the never-overwrite write. Required natively: last-wins for equal sanitized names (parity); for names equal only after the backslash replacement, fail with `` `The package contains the same file twice: ${rel}` `` (IMPROVEMENT D-EXP-14). Citation: `jszip/lib/load.js:59-80`, `project-store.ts:366`.

**EDGE-EXP-34. A zip bomb.** Electron reads the whole file into memory and fully inflates each whitelisted entry before comparing it with the 80 MB cap, so one highly compressed entry can exhaust memory. macOS checks the declared size first. Required natively: IMPROVEMENT D-EXP-12, reject on the declared uncompressed size, then inflate with a bounded read that stops at `MAX_FILE_BYTES + 1` actual bytes, and add each entry's actual size to the running total. The messages are unchanged. On .NET 10 the declared size is in fact authoritative: since .NET Core 3.0 `ZipArchiveEntry.Open()` checks that the local and central headers agree on the sizes (else `InvalidDataException`) and truncates decompressed data at the declared uncompressed size, so a header that UNDER-declares cannot inflate past it; it yields a truncated entry instead (EDGE-EXP-56). The bounded read stays as defense in depth. Citation: `export-package.ts:137-201`; `macOS:Packages/ExportKit/Sources/ExportKit/ExportPackage.swift:164-208`.

**EDGE-EXP-35. A package of a project with only text steps.** Allowed (only `steps.length === 0` is refused), and an empty text step still counts; the document formats would refuse the same project. REQUIRED (parity). Citation: `export-package.ts:61-63`.

**EDGE-EXP-36. A referenced file missing with originals.** Skipped silently; the package imports with a step whose image is missing. REQUIRED (parity; the recipient's report shows the broken image state of 05). Citation: `export-package.ts:44-48`.

**EDGE-EXP-37. An unreadable render in a safe package.** Electron throws the raw read error (for example `ENOENT: no such file or directory, open '...'`). Required natively: IMPROVEMENT D-EXP-17, the collector's render missing message with `Step {n}` numbered as in the package loop. Citation: `export-package.ts:89`.

**EDGE-EXP-38. Orphaned print copies.** A crash mid-PDF leaves `_print-*.html` in `export/.render/`; the next PDF export sweeps them. Citation: `export.ts:649-658`.

**EDGE-EXP-39. HTML bigger than 2 MB.** Natively, `NavigateToString` refuses more than 2 MB, and a PDF with full-resolution PNGs is routinely tens of MB. Required natively: write the print copy to disk and `Navigate` to its file URI, as Electron does. Source: Microsoft Learn, `CoreWebView2.NavigateToString`; spec 03 2.10.2 must be read with this correction (Q-EXP-17).

**EDGE-EXP-40. WebView2 in a per-machine install.** The default user data folder for a WPF app is `<exe path>.WebView2` next to the executable (Microsoft Learn, "Manage user data folders"), which is not writable under `Program Files`, so environment creation fails with an access-denied error. Required natively: an explicit user data folder `%LOCALAPPDATA%\shotAI\WebView2`. Source: WebView2 environment behavior; applies because the installer is per-machine (fixed decision).

**EDGE-EXP-41. An undecodable render.** `nativeImage` returns 0 by 0: HTML emits no size attributes and embeds the original bytes; Word inserts a 0 by 0 picture (fit 1); PowerPoint fits a square (aspect 1) into the box. REQUIRED (parity) for HTML and Markdown; Q-EXP-9 for Office. Citation: `export.ts:217-223`, `export-docx.ts:199-217`, `export-pptx.ts:40`.

**EDGE-EXP-42. JPEG renders.** A `.jpg` or `.jpeg` render keeps `image/jpeg` everywhere except after a crop (PNG). Word picks the image part type from the media type, not from the bytes. Citation: `render-gate.ts:45-48`, `export-docx.ts:215`.

**EDGE-EXP-43. A text step without a heading in PowerPoint.** Its body becomes the 26 pt bold title line in a 1 in box and a long body overflows the card. REQUIRED (parity; recorded, not fixed). Citation: `export-pptx.ts:182-196`.

**EDGE-EXP-44. Word headings are blue and not branded.** `Heading2` uses the docx library's `2E74B5` and the document font is Aptos, not the brand face. REQUIRED (parity); Q-EXP-10 records the brand question. Citation: `docx` `DefaultStylesFactory`, `export-docx.ts:249-252`.

**EDGE-EXP-45. The CSS comments are output.** The two `/* ... */` comments in the `docCss` template ship inside every styled HTML and PDF. A port that drops them fails byte identity. Citation: `export-css.ts:101-104`, `:124`.

**EDGE-EXP-46. A test regex that happens to pass.** `export-css.test.ts:140-141` writes `/@media print{(.*)}s*$/` and `/([^{}]*){max-width:none}/` without escapes, so `s*` matches the letter `s`. The port asserts the intended pattern (`@media print\{(.*)\}\s*$`). Citation: `export-css.test.ts:137-147`.

**EDGE-EXP-47. Exporting an archived project.** `getProjectForRead` does not unarchive; an archived-on-disk project's renders are inside `archive.zip`, so the collector reports the render missing. Home export opens the project first, which unarchives it (06 EDGE-HOME-14); the report export runs on an open project. REQUIRED (parity), and native callers must open first (section 10, 06 and 05). Citation: `project-store.ts:983-989` (01).

**EDGE-EXP-48. Pending optimistic edits at export time.** Natively only: see INV-EXP-28.

**EDGE-EXP-49. Save As for `html-plain` and Markdown defaults.** The Save dialog default for `html-plain` is `{base}-plain.html`; the Markdown default is `{base}.md` (no suffix). The name the user types is used as given (no `safeFileBase`). Citation: `export.ts:838`, `:848`, `:872`.

**EDGE-EXP-50. The document language.** Both HTML formats declare `lang="en"` whatever the content language. REQUIRED (parity). Citation: `export.ts:522`, `:605`.

**EDGE-EXP-51. A tiny image entry in a package.** `isPngOrJpeg` requires at least 4 bytes, so a 3-byte entry `FF D8 FF` is refused with the non-image message although it carries the JPEG magic. REQUIRED (parity). Citation: `export-package.ts:32-37`.

**EDGE-EXP-52. The safe-package gate label counts shots only.** The package loop numbers `Step {n}` over non-text steps, so for steps `[text "Intro", shot (unbaked blur)]` the refusal says `Step 1` while the report and every document export call that shot step 2. REQUIRED (parity; the message is the gate's). Natively D-EXP-17's render missing message uses the same shot-only `n`. Citation: `export-package.ts:82-88`.

**EDGE-EXP-53. A BOM before the marker or `project.json`.** JSZip's `async('string')` keeps a leading U+FEFF and `JSON.parse` rejects it, so a BOM-prefixed marker gives `The package marker is corrupt.` and a BOM-prefixed manifest `The package project.json is corrupt.`. `System.Text.Json` skips a UTF-8 BOM when parsing from bytes, so the native reader must decode to a string first and reject a leading U+FEFF (or use 01's `JsJson.Parse`, which must have JSON.parse semantics). REQUIRED (parity). Citation: `export-package.ts:154-158`, `:171-175`.

**EDGE-EXP-54. Directory-flagged entries.** An entry with the DOS directory attribute (or a Unix `S_IFDIR` mode, or a name ending in `/`) is a directory to JSZip even when its name looks like a file (`shots/a.png` with attribute `0x10`); it is keyed `shots/a.png/`, skipped by the import loop, and never extracted. REQUIRED (parity; 7.12 step 3). Citation: `jszip/lib/zipEntry.js:135-159`, `jszip/lib/object.js:42-53`.

**EDGE-EXP-55. Whole-archive failures.** JSZip parses every entry at load, so an encrypted entry or an unsupported compression method ANYWHERE in the archive (even in an entry the whitelist would ignore) makes the whole import fail with `This file is not a valid .zip package.`. `System.IO.Compression` on .NET 10 only fails when an affected entry is opened, and `ZipArchiveEntry.CompressionMethod` exists only from .NET 11, so natively such an entry outside the whitelist is ignored and the package imports. Accepted divergence (more permissive only for entries that are never read); recorded, not fixed (Q-EXP-26).

**EDGE-EXP-56. An entry whose header under-declares its size.** JSZip inflates the whole stream, so Electron sees the real bytes. .NET 10 truncates at the declared size (and throws `InvalidDataException` when the local and central headers disagree), so the native reader sees a truncated image that may still pass the magic check. Accepted divergence (the result can only be smaller; the recipient sees a broken image); a truncated read is never an overflow. Test: `PackageReaderTests.UnderDeclaredEntryIsTruncatedNotInflated`.

**EDGE-EXP-57. PDF engine failures other than an empty result.** Electron surfaces raw errors for a failed `BrowserWindow` construction, a failed `loadFile` and a rejected `printToPDF` (2.11). Natively every WebView2 failure (navigation not successful, process failed, timeout, `PrintToPdfAsync` false or throwing) maps to `PdfRenderException(PdfFailure.Empty)` and the empty-document message, except a missing runtime (D-EXP-4). IMPROVEMENT (one clear message instead of a Chromium error code), part of D-EXP-2.

**EDGE-EXP-58. AVIF sanity check on short or high-bit output.** Node clamps the brand slice to the buffer length and clears each byte's high bit (2.7 step 4). A literal C# port with `b[8..24]` throws `ArgumentOutOfRangeException` for an output of 17 to 23 bytes, and a byte-to-char cast without masking differs from Node for bytes at or above `0x80`. Required natively: clamp the slice end to `b.Length` and map each byte as `(char)(b & 0x7F)`. REQUIRED (parity). Citation: `avif-encode.ts:128-131`.

**EDGE-EXP-59. A thrown AVIF encode is not cached.** Only a failed load disables AVIF for the process; an encode that throws logs `avif: encode failed; falling back:` and the next image tries again. REQUIRED (parity): the native `LibavifEncoder` disables itself only when the DLL cannot be loaded. Citation: `avif-encode.ts:67-105`, `:137-140`.

**EDGE-EXP-60. Package export ignores the collector's rules.** The package refuses only `steps.length === 0`; it does not skip empty text steps or empty sections, does not apply the zoom crop (the recipient's report reapplies `reportZoom` and pan, which travel unchanged), and does not check the render with `fs.stat` first. REQUIRED (parity). Citation: `export-package.ts:60-100`.

## 6. macOS port notes

macOS implements the same pipeline in `Packages/ExportKit` (Swift, headless-testable, reads every image through `ShotModel.resolveSendableRender`). It has no Word or PowerPoint export; the Windows native Office exporters are the first native ones and follow Electron only.

| Area | macOS | Windows reference | Lesson for Windows native |
|---|---|---|---|
| Collector | `collectSteps` (`macOS:Packages/ExportKit/Sources/ExportKit/ExportCollector.swift:11-68`): same numbering, empty-section skip, render missing check, zoom crop; trims with `.whitespacesAndNewlines`; still carries the legacy per-step `note` | `export.ts:357-420` | Keep Electron's `JsString.Trim` set; no `note` (removed from Windows in 1.1.0). |
| Unknown callout | dropped on decode (ShotModel), so it is a plain step | kept on read, narrowed at use | Same visible result; Windows keeps the value (01). |
| Brand precedence | `ExportTheme.of(manifest.pinnedBrand ?? brand)` resolved inside `exportProject` (`Export.swift:34-50`) | same (`export.ts:827`) | Resolve where the manifest is read; one read. |
| Destination | `.projectFolder` (numbered) or `.custom(dir, stem)` (overwrites; the Save dialog owns the prompt), Markdown always a folder (`Export.swift:54-77`) | same model | Same; Windows adds bulk folder and progress. |
| Created line | `DateFormatter` short date and short time, no seconds (`Export.swift:8-15`) | `toLocaleString()` with seconds | Do not copy macOS; follow Electron (7.9). |
| Styled HTML | same markup except an extra `div.doc__col` wrapper (`HTMLExport.swift:261-274`) and `.doc__col` plus a `.section{break-inside:avoid}` inside `@media print` (`:138`, `:165`); `.step__note` | no wrapper; `break-inside` on the screen `.section` rule | Port Electron's stylesheet, not macOS's (byte identity is against Electron). |
| AVIF | ImageIO `public.avif`, quality 0.85, at 1x (`HTMLExport.swift:30-32`, `ExportGeometry.swift:107-120`); PNG when ImageIO cannot write AVIF | libavif quality 50, speed 7, at 2x | Windows parity target is Electron's 2x; macOS proved AVIF is what gets under the paste ceiling. |
| Size attributes | measured from the bytes actually inlined (`HTMLExport.swift:90-95`) | measured from the pre-resample input | Follow Electron (INV-EXP-11). |
| Plain CSS | rules separated by newlines (`HTMLExport.swift:281-293`); body `round(800*s)` | joined with no separator; body `round(800*col/816)` | Follow Electron. |
| Markdown | same structure; separator lines differ (it appends `---`, `''` after a chunk's trailing blank) | Electron's exact join | Follow Electron. The images folder wipe is the same data-loss risk (EDGE-EXP-22). |
| PDF | native CoreText and CoreGraphics renderer on US Letter, because WKWebView printing spun forever in `-[WKPrintingView rectForPage:]` (`PdfExport.swift:1-14`); fails closed and deletes a 0-byte file (`:58-65`); a `PdfSelfTest` executable drives the real path under a watchdog | Chromium print of the same HTML | WebView2 is Chromium, not WebKit, so the hang does not transfer; keep a watchdog (7.7) and a live self-test (8.2 `PdfRendererTests`). |
| Package export | same safe collapse, `sopBackup` nil, `confinePathNoSymlinks` for originals (`ExportPackage.swift:128-137`), STORED zip, marker with sorted keys | lexical confinement, DEFLATE, insertion-order keys | Adopt no-link confinement (D-EXP-16); keep DEFLATE and Electron's key order. |
| Package import | lists metadata first, caps on the declared size before inflating, then per-file and total caps (`ExportPackage.swift:156-211`); a missing marker says `This file is not a shotAI project package.` | inflate then check; `... (missing marker).` | Adopt the declared-size check (D-EXP-12); keep Electron's messages. |
| Tests | 61 ExportKit tests, including `testStyledHtmlSurvivesStylesheetStripping`, `testPdfAndMarkdownKeepFullResolution`, `testUnknownPinFallsBackAndDoesNotFail`, `testIgnoresZipSlipEntry` | the five files of 8.1 | Port the macOS ideas that Electron lacks (8.2): stylesheet stripping, zip slip, round trip. |

The macOS port also confirms two process lessons: every behavior fix in this subsystem was found by measuring a real export (unzipping a .docx, reading the KB editor's code view, diffing both apps' HTML), and each regression test was written against the measured value. The native port keeps that discipline: golden files generated from Electron, not typed from this document.

## 7. Native design (C#)

### 7.1 Placement

| Namespace | Project | Types |
|---|---|---|
| `ShotAI.Core.Export` | ShotAI.Core | `ExportFormat`, `ExportFormats`, `ExportProgress`, `ExportResult`, `PackageResult`, `ExportItem`, `ShotItem`, `TextItem`, `StepCollector`, `IExportFileProbe`, `ManagedExportFileProbe`, `ExportGeometry`, `CropRect`, `EmbedPolicy`, `EmbedCodec`, `ExportCss`, `ExportTheme`, `ExportText`, `ExportNames`, `CreatedLine`, `IReportBylineSource`, `IAppBrandSource`, `IBrandFontSource`, `IExportImageCodec`, `IAvifEncoder`, `AvifSanity`, `InlineImage`, `IHtmlImageEmbedder`, `HtmlImageEmbedder`, `HtmlDocumentBuilder`, `PlainHtmlDocumentBuilder`, `MarkdownDocumentBuilder`, `MarkdownDocument`, `MarkdownImage`, `PrintHtml`, `IPdfRenderer`, `PdfRenderException`, `ExportEngine`, `PreparedExport`, `ResolvedDestination`, `ExportException`, `ExportMessages` |
| `ShotAI.Core.Export.Office` | ShotAI.Core | `OfficeImage`, `DocxBuilder`, `DocxDefaultStyles`, `PptxBuilder`, `PptxLayout`, `PptxText`, `PptxTheme` |
| `ShotAI.Core.Export.Package` | ShotAI.Core | `PackageWriter`, `PackageReader`, `PackageLimits`, `JsZipNames`, `PackageImport` (the decoded manifest plus 01's `ImportFile` list) |
| `ShotAI.Platform.Export` | ShotAI.Platform | `WicExportImageCodec` (extends 02's `WicImageCodec`), `LibavifEncoder`, `NativeAvif` (P/Invoke), `WebView2PdfRenderer`, `PdfHostWindow`, `WindowsExportFileProbe` |
| `ShotAI.App.Export` | ShotAI.App | `IExportService`, `ExportService`, `IExportDialogs`, `WpfExportDialogs`, DI registration |

Everything that decides bytes (collector, geometry, CSS, the three text builders, both Office builders, the package writer and reader, names, the created line) is in Core and runs on Linux. `DocumentFormat.OpenXml` and `System.IO.Compression` are platform-neutral, so the Office builders and the package code are Core, not Platform. Core references no Windows API (dotnet/README.md rule); images, AVIF and PDF reach Core only through `IExportImageCodec`, `IAvifEncoder` and `IPdfRenderer`.

Rule for every template: output newlines are `"\n"`. C# raw string literals take their line breaks from the source file, which may be CRLF on a Windows checkout, so every multi-line literal is built line by line with `'\n'` (or passed once through `.ReplaceLineEndings("\n")` in a static initializer). `ExportCssGoldenTests` runs on the Windows CI job too, to catch a CRLF checkout.

### 7.2 Core types

```csharp
namespace ShotAI.Core.Export;

public enum ExportFormat { Html, HtmlPlain, Pdf, Markdown, Docx, Pptx }

public static class ExportFormats
{
    public static string Wire(ExportFormat f);        // "html" "html-plain" "pdf" "markdown" "docx" "pptx" (log text)
    public static string Extension(ExportFormat f);   // ".html" ".html" ".pdf" ".md" ".docx" ".pptx"
    public static string StemSuffix(ExportFormat f);  // "-plain" for HtmlPlain, else ""
    public static (string Name, string Pattern) Filter(ExportFormat f);
    // Html, HtmlPlain ("HTML","*.html"); Pdf ("PDF","*.pdf"); Markdown ("Markdown","*.md");
    // Docx ("Word Document","*.docx"); Pptx ("PowerPoint","*.pptx")
}

public readonly record struct ExportProgress(int Done, int Total);
public sealed record ExportResult(ExportFormat Format, string OutputPath, bool Canceled = false);
public sealed record PackageResult(string OutputPath, bool IncludeOriginals);

public abstract record ExportItem;
public sealed record ShotItem(int N, string Caption, string Body, string AbsPath, string MediaType,
                              string Ext, string StepId, byte[]? CroppedPng) : ExportItem;
public sealed record TextItem(int? N, string Heading, string Body, CalloutKind? Callout) : ExportItem;
// CalloutKind and the narrowing CalloutKinds.TryParse(raw) come from 01 (isCalloutKind semantics).

public interface IExportFileProbe
{
    bool Exists(string path);          // lstat semantics: a file, a folder or a link (even dangling) counts
    bool StatSucceeds(string path);    // Node fs.stat parity: follows links; true for a file OR a folder (7.4).
                                       // Deliberately not named IsFile: a folder returns true.
    byte[] ReadAllBytes(string path);
}

public interface IReportBylineSource { Task<string?> GetBylineAsync(CancellationToken ct); }   // 10: trimmed name iff opted in
public interface IAppBrandSource     { Task<BrandId> GetBrandAsync(CancellationToken ct); }     // 10: read at every export
public interface IBrandFontSource    { string BrandFontPath(); }                                 // 03: AppPaths.BrandFontPath(), "" when missing
```

### 7.3 `ExportEngine` (Core)

```csharp
public sealed class ExportEngine(
    IProjectStore store,               // 01: GetProjectForReadAsync, WhenIdleAsync (requested), CreateProjectFromImportAsync
    IRenderGate gate,                  // 04: Resolve(dir, step, label, EgressVerb.Export)
    IExportImageCodec codec, IHtmlImageEmbedder embedder, IPdfRenderer pdf,
    IReportBylineSource byline, IAppBrandSource appBrand, IBrandFontSource font,
    IExportFileProbe probe, IPathConfine confine /*01*/, IAtomicFile atomic /*01*/,
    TimeProvider time, Func<CultureInfo> culture, ILogger<ExportEngine> log)
{
    public Task<PreparedExport> PrepareAsync(string projectPath, ExportFormat format, CancellationToken ct);
    public Task<ExportResult> WriteAsync(PreparedExport p, ResolvedDestination dest,
                                         IProgress<ExportProgress>? progress, CancellationToken ct);
    public Task<PackageResult> ExportPackageAsync(string projectPath, bool includeOriginals, CancellationToken ct);
    public Task<ProjectSummary> ImportPackageAsync(string zipPath, CancellationToken ct);
}

public sealed record PreparedExport(string ProjectPath, string Dir, ProjectManifest Manifest, ExportFormat Format,
    ExportTheme Theme, IReadOnlyList<ExportItem> Items, string BaseName, string StemBase,
    string CreatedLine, string ExportDir, string SuggestedFileName);   // stembase+ext, or base+".md"

public abstract record ResolvedDestination
{
    public sealed record ChosenFile(string Path) : ResolvedDestination;   // Save dialog, any format
    public sealed record Folder(string Directory) : ResolvedDestination;  // bulk shared folder, or the project's export folder
}
```

`PrepareAsync` is 2.2 rows 1 to 4, in that order: `await store.WhenIdleAsync(projectPath, ct)` (INV-EXP-28), `GetProjectForReadAsync`, theme (`ExportTheme.Resolve(manifest.RawTheme, await appBrand.GetBrandAsync(ct))`), `StepCollector.CollectAsync`, `SafeFileBase`, `CreatedLine.Build(time.GetLocalNow(), culture(), await byline.GetBylineAsync(ct))`, `Directory.CreateDirectory(<dir>\export)`. Any exception propagates before the App shows a dialog (EDGE-EXP-19).

`WriteAsync` is 2.2 rows 5 to 8 for the given destination:

| Format | `ChosenFile(path)` | `Folder(dir)` |
|---|---|---|
| Markdown | `stem = Path.GetFileName(path)` with a trailing `.md` removed case-insensitively (`Regex(@"\.md$", IgnoreCase)`), or `BaseName` if that leaves `""`; `parent = Path.GetDirectoryName(path)`; folder = `parent\stem` when it does not exist, is empty, or looks like a prior export (contains `stem.md` and nothing else except `images\`); otherwise `parent\NextAvailableDir(parent, stem)` (D-EXP-9) | `Directory.CreateDirectory(dir)`; folder = `dir\NextAvailableDir(dir, BaseName)` |
| single-file formats | `outputPath = path` | `Directory.CreateDirectory(dir)`; `outputPath = dir\NextAvailableStem(dir, StemBase, ext) + ext` |

Then build (7.8 to 7.12), write atomically through 01's `AtomicFile` (temp sibling then replace; D-EXP-6; Markdown images are written directly, then the `.md` atomically, parity order), log info `$"exported {Wire(format)} \u2192 {outputPath}"` (or `$"exported markdown \u2192 {outputPath}"`), return `new ExportResult(format, outputPath)`. The engine never reveals; the App decides (7.13).

Cancellation (D-EXP-8): `ct` is checked between items, between images, before the PDF print and before the final replace; after the replace starts the write completes. On `OperationCanceledException` every temp file is deleted and nothing exists at the destination that did not exist before.

### 7.4 Collector

`StepCollector.CollectAsync(dir, manifest, gate, codec, probe, ct)` is 2.4 line for line:

- `heading`, `body`, `caption` via `JsString.Trim(value ?? "")`.
- Callout narrowing via 01's `CalloutKinds.TryParse(step.CalloutRaw, out var kind)`; an unknown value is not a callout.
- `gate.Resolve(dir, step, $"Step {stepNo}", EgressVerb.Export)` (04; throws `RenderRefusedException` with 04's message).
- The "missing from disk" check: `probe.StatSucceeds(abs)` returns true for a file or a directory (Node `fs.stat` parity, links followed, so a dangling link is false); false throws `ExportException(ExportMessages.RenderMissing(stepNo, step.Flattened ?? step.Screenshot))`.
- Zoom crop: `zoom = step.ReportZoom ?? 1`; only when `zoom > 1` (a NaN zoom is not `> 1`), inside ONE try whose catch gives no crop: `bytes = probe.ReadAllBytes(abs)` (a directory at `abs` throws here and must give no crop, not an error, as Electron's `createFromPath` does), `frame = codec.Decode(bytes)`, `rect = ExportGeometry.ZoomCropRect(frame.Width, frame.Height, zoom, step.ReportPanX ?? 0.5, step.ReportPanY ?? 0.5)`; null gives no crop; else `png = codec.EncodePng(codec.Crop(frame, rect))`, and an empty array gives no crop. The catch logs at debug only (Electron logs nothing). `MediaType`, `Ext` follow 2.4. The `?? 1` and `?? 0.5` defaults apply only to a missing value; a present non-finite pan reaches `clamp01`, which centers it.
- Empty result throws `ExportException(ExportMessages.NothingToExport)`.

CPU work runs on the thread pool (`Task.Run` per shot, sequential order preserved).

### 7.5 Images for HTML

```csharp
public interface IExportImageCodec          // Platform: WicExportImageCodec; tests: a managed fake
{
    ImageSize ReadSize(ReadOnlySpan<byte> encoded);            // (0,0) when undecodable (nativeImage parity)
    PixelFrame Decode(ReadOnlySpan<byte> encoded);             // 32bpp premultiplied BGRA (02's PixelFrame); throws
    PixelFrame Crop(PixelFrame f, CropRect r);
    PixelFrame Resize(PixelFrame f, int width, int height);    // "best": WIC HighQualityCubic (Q-EXP-19)
    byte[] EncodePng(PixelFrame f);
    byte[] EncodeJpeg(PixelFrame f, int quality);              // 0..100, WIC ImageQuality = quality / 100f
    byte[] ToStraightRgba(PixelFrame f);                       // BGRA -> RGBA, un-premultiplied (D-EXP-21); WIC: IWICFormatConverter to GUID_WICPixelFormat32bppRGBA
}

public interface IAvifEncoder { byte[]? Encode(ReadOnlySpan<byte> rgba, int width, int height, int quality, int speed); }

public sealed record InlineImage(byte[] Bytes, string MediaType, string SizeAttr);
public interface IHtmlImageEmbedder
{
    Task<InlineImage> EmbedAsync(ShotItem item, EmbedPolicy policy, double docScale, CancellationToken ct);
}
public sealed class HtmlImageEmbedder(IExportImageCodec codec, IAvifEncoder? avif, IExportFileProbe probe,
                                      ILogger<HtmlImageEmbedder> log) : IHtmlImageEmbedder;
```

`HtmlImageEmbedder.EmbedAsync` is the 2.6 pipeline exactly: `buffer = item.CroppedPng ?? probe.ReadAllBytes(item.AbsPath)`; `(w, h) = codec.ReadSize(buffer)`; `shown = ExportGeometry.HtmlImageSize(w, h, docScale)`; `sizeAttr = shown is {} s ? $" width=\"{s.W}\" height=\"{s.H}\"" : ""`; PDF short circuit; decode; resize when `cap != null && w > cap && h >= 1` to `(cap, Math.Max(1, (int)JsMath.Round(h * ((double)cap / w))))`; AVIF via `avif?.Encode(codec.ToStraightRgba(scaled), ...)` then `AvifSanity.LooksLikeAvif`; fallback PNG or JPEG 85; the `> 0` and "never bigger" guards; any exception logs warn `export: image re-encode failed, embedding the original:` with the exception. `AvifSanity.LooksLikeAvif(b)` = `b.Length > 16 && ascii(b, 4, 8) == "ftyp" && Regex.IsMatch(ascii(b, 8, Math.Min(24, b.Length)), "avif|avis|av01|mif1|miaf", RegexOptions.CultureInvariant)`, where `ascii(b, start, end)` maps each byte to `(char)(b[i] & 0x7F)` (Node's `ascii` decoding clears the high bit, so `0xE1` reads as `a`) and the end is clamped to the length (a C# `b[8..24]` range throws for a 17 to 23 byte output) (EDGE-EXP-58). Undecodable input: when `ReadSize` returns (0, 0), `sizeAttr` is `""` and the method returns the input bytes and media type immediately, without calling `Decode` and without logging (2.6; Electron logs nothing on this path). The whole method runs on the thread pool.

### 7.6 AVIF (Platform)

Native dependency: `shotai_avif.dll` per architecture, placed NEXT TO `shotAI.exe` in each architecture's build output (the x64 and ARM64 builds are separate, fixed decision; a `runtimes\<rid>\native\` folder is only probed for assets declared by a NuGet package in `deps.json`, and the `DllImportSearchPath.AssemblyDirectory` probe below looks in the assembly folder only, so the two layouts must not be mixed; 12 owns the final layout, Q-EXP-23), a small C shim statically linking libavif 1.x (BSD-2-Clause) and libaom (BSD-2-Clause), built by spec 12 from pinned sources. A shim is used instead of binding libavif directly because `avifEncoder` and `avifImage` are structs whose layout changes between libavif releases; the shim exposes a stable ABI:

```c
int  shotai_avif_encode(const uint8_t* rgba, uint32_t width, uint32_t height, uint32_t rowBytes,
                        int quality, int speed, int maxThreads, uint8_t** outData, size_t* outSize);
void shotai_avif_free(uint8_t* data);
const char* shotai_avif_version(void);     /* for the diagnostics bundle (10) */
```

Shim body (normative): `avifImageCreate(width, height, 8, AVIF_PIXEL_FORMAT_YUV420)`; CICP and range per Q-EXP-2 (default: `yuvRange = AVIF_RANGE_FULL`, primaries BT.709, transfer sRGB, matrix BT.601); `avifRGBImageSetDefaults(&rgb, image)`, `rgb.format = AVIF_RGB_FORMAT_RGBA`, `rgb.depth = 8`, `rgb.pixels`, `rgb.rowBytes`; `avifImageRGBToYUV`; `enc = avifEncoderCreate()`, `enc->codecChoice = AVIF_CODEC_CHOICE_AOM`, `enc->quality = quality`, `enc->qualityAlpha = AVIF_QUALITY_DEFAULT`, `enc->speed = speed`, `enc->maxThreads = maxThreads`, tiles 0, no alpha plane written for an opaque image; `avifEncoderWrite`; copy the output into a `malloc` buffer the caller frees with `shotai_avif_free`; destroy the encoder and image; return 0 on success, else the `avifResult`.

```csharp
internal static partial class NativeAvif
{
    [LibraryImport("shotai_avif", EntryPoint = "shotai_avif_encode")]
    internal static unsafe partial int Encode(byte* rgba, uint width, uint height, uint rowBytes,
        int quality, int speed, int maxThreads, out nint data, out nuint size);
    [LibraryImport("shotai_avif", EntryPoint = "shotai_avif_free")]
    internal static partial void Free(nint data);
}
```

(`LibraryImport`, not CsWin32: CsWin32 generates only from Win32 metadata and cannot describe a third-party DLL. Q-EXP-1. The source generator requires `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in `ShotAI.Platform.csproj`, and with warnings as errors the `byte*` signature needs that switch anyway. `out nint`/`out nuint` are blittable, so no marshalling code is generated.)

`LibavifEncoder : IAvifEncoder`: availability is probed once per process with `NativeLibrary.TryLoad("shotai_avif", typeof(LibavifEncoder).Assembly, DllImportSearchPath.AssemblyDirectory, out _)`; failure logs warn `avif: encoder init failed; falling back:` once and every later call returns null without retrying (Electron's cached null). `Encode`: `width < 1 || height < 1` returns null; pin `rgba`; call with `quality` 50, `speed` 7, `maxThreads = Environment.ProcessorCount` (D-EXP-5); a non-zero result logs warn `avif: encode failed; falling back:` with the code and returns null; copy the buffer to a managed array and `Free` it in `finally`. Thread-safe (one libavif encoder per call).

### 7.7 PDF (Platform, WebView2)

`WebView2PdfRenderer : IPdfRenderer, IAsyncDisposable`, `Task RenderAsync(string printHtmlPath, string outputPdfPath, CancellationToken ct)`.

- Thread: every WebView2 call runs on the WPF UI thread through the `SynchronizationContext` captured at construction (App composition); awaits never block the UI thread. One render at a time (`SemaphoreSlim(1)`; one `PrintToPdfAsync` per WebView).
- Environment: created once, lazily: `CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: %LOCALAPPDATA%\shotAI\WebView2, options: new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--disable-features=msSmartScreenProtection" })` (EDGE-EXP-40, INV-EXP-35). `WebView2RuntimeNotFoundException` becomes `PdfRenderException(PdfFailure.RuntimeMissing)`.
- Egress (INV-EXP-35, REQUIRED: Electron's Chromium build ships without Safe Browsing, so its `printToPDF` of a static local file had no browser service to reach the network). SmartScreen is on by default in WebView2 and sends navigated URLs to Microsoft; Microsoft Learn states that if an app does not disable it the app must give users a SmartScreen privacy notice, and that "all other services in `edge://settings/privacy` are turned off, for WebView2". Two layers turn it off: `CoreWebView2Settings.IsReputationCheckingRequired = false` (below; the documented API) and the `--disable-features=msSmartScreenProtection` argument above (the older documented switch, kept because it applies at browser-process start, before any setting is read). Reputation checking is per user data folder (it stays on if any WebView on the folder leaves it `true`), and a newly created WebView that has not set it `false` applies the default at its first navigation; the app has exactly one WebView2 user (App `Architecture/SingleWebViewTests`), so setting it on every controller before `Navigate` is sufficient. Further runtime-initiated traffic (component updates, the Edge Update service, variations or diagnostic data governed by the Windows diagnostic data setting) is UNVERIFIED: candidate arguments `--disable-background-networking` and `--disable-component-update` are Chromium switches whose effect inside the WebView2 runtime is not documented on Microsoft Learn, and they are added to `AdditionalBrowserArguments` only if the AC-EXP-42 trace shows a connection they stop (Q-EXP-27). The argument string is one constant, `PdfEngine.BrowserArguments`, logged at info as `webview2 env: args=<args>` when the environment is created.
- Host window: `PdfHostWindow` creates one never-shown popup through CsWin32, `CreateWindowEx(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, "Static", "shotAI PDF", WS_POPUP, 0, 0, 900, 1200, ...)`, on the UI thread, destroyed at app exit (`DestroyWindow`). It is registered with 03's own-window set so capture never sees it.
- Per render: `controller = await env.CreateCoreWebView2ControllerAsync(hwnd)`; `controller.Bounds = new Rectangle(0, 0, 900, 1200)`; `controller.IsVisible = false` (Q-EXP-3). `CoreWebView2Settings`: `IsScriptEnabled = false`, `AreDefaultScriptDialogsEnabled = false`, `IsWebMessageEnabled = false`, `AreDevToolsEnabled = false`, `AreDefaultContextMenusEnabled = false`, `IsStatusBarEnabled = false`, `IsZoomControlEnabled = false`, `AreBrowserAcceleratorKeysEnabled = false`, `IsPasswordAutosaveEnabled = false`, `IsGeneralAutofillEnabled = false`, `IsReputationCheckingRequired = false` (INV-EXP-35). All settings are applied to `controller.CoreWebView2.Settings` before the first `Navigate`.
- Hardening (INV-EXP-20): `docUri = new Uri(printHtmlPath)`. `NavigationStarting`: cancel unless it is the first navigation and `new Uri(e.Uri) == docUri` (compare `Uri` objects, not strings: the engine may percent-encode a non-ASCII user name or a space in the path differently from `AbsoluteUri`). `FrameNavigationStarting`: cancel. `NewWindowRequested`: `Handled = true`. `DownloadStarting`: `Cancel = true`. `PermissionRequested`: `State = Deny`. `AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All)` (the two-argument overload is deprecated, fires only for the main frame and same-origin iframes, and with warnings as errors an `[Obsolete]` call would not build); `WebResourceRequested`: for any URI other than `docUri`, `e.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", "")`. The print copy needs no sub-resource: images are `data:` URIs and the face is a `data:` URI (D-EXP-1). `ProcessFailed`: log error `webview2 process failed: kind=<k> reason=<r> exitCode=<n>` (03) and fail the render.
- Load: `CoreWebView2.Navigate(docUri.AbsoluteUri)` (never `NavigateToString`, EDGE-EXP-39); await `NavigationCompleted`; `IsSuccess == false` or the 60 s timeout becomes `PdfRenderException(PdfFailure.Empty)` (EDGE-EXP-57). Whether `WebResourceRequested` is raised for `file://` sub-resources was not confirmed on Microsoft Learn (UNVERIFIED); the print copy therefore also carries a Content Security Policy (D-EXP-25), which blocks every fetch whatever the scheme.
- Print: `s = env.CreatePrintSettings()`; `s.ShouldPrintBackgrounds = true`; `s.Orientation = CoreWebView2PrintOrientation.Portrait`; `s.PageWidth = 8.5`; `s.PageHeight = 11`; margins left at their defaults (1 cm, the same Chromium default Electron used; Q-EXP-4); `s.ShouldPrintHeaderAndFooter = false`; `s.ScaleFactor = 1.0`. `ok = await CoreWebView2.PrintToPdfAsync(outputPdfPath, s)` under a 120 s watchdog (macOS lesson, 6). `!ok`, timeout or exception becomes `PdfRenderException(PdfFailure.Empty)`.
- Finally `controller.Close()`.

Engine side (Core, `ExportEngine.WritePdfAsync`), replacing `htmlToPdf`:

1. `renderDir = <dir>\export\.render`; create; sweep `_print-*.html` and `_print-*.pdf` (best effort; D-EXP-2 adds the `.pdf`).
2. `html = await HtmlDocumentBuilder.BuildAsync(..., ExportGeometry.HtmlEmbedPolicy(ExportFormat.Pdf, s), ...)`.
3. Face: if `theme.FontFamily is null`, none; else `path = font.BrandFontPath()`; `""` or unreadable logs warn `brand face not found on disk; the PDF will use the fallback stack` and uses none; else `PrintHtml.FontFaceStyle(family, File.ReadAllBytes(path))`.
4. Write `PrintHtml.Inject(html, face)` to `renderDir\_print-<guid>.html` (UTF-8, no BOM).
5. `tempPdf = renderDir\_print-<guid>.pdf`; `await pdf.RenderAsync(tmpHtml, tempPdf, ct)`.
6. Fail closed (INV-EXP-19): `tempPdf` must exist, be longer than 0 bytes and start with `%PDF-`; otherwise throw `ExportException(ExportMessages.PdfEmpty)`. `PdfFailure.RuntimeMissing` throws `ExportException(ExportMessages.PdfRuntimeMissing)` (D-EXP-4).
7. Publish through 01's `AtomicFile` (a temp sibling of `outputPath` in the destination folder, then replace), copying from `tempPdf`. Not `File.Move(tempPdf, outputPath, overwrite: true)`: when the Save dialog target is on another volume (a USB stick, a mapped drive) .NET implements the move as copy then delete, which is not atomic and breaks D-EXP-6.
8. Finally delete `tmpHtml` and `tempPdf` if present, ignoring errors.

`PrintHtml.FontFaceStyle(family, ttf)` = `"<style>@font-face{font-family:\"" + family + "\";src:url(\"data:font/ttf;base64," + Convert.ToBase64String(ttf) + "\") format(\"truetype-variations\");font-weight:100 900;font-stretch:62% 125%;font-style:normal}</style>\n"`. `PrintHtml.Inject(html, face)` replaces the FIRST `"<head>\n"` with `"<head>\n" + PrintHtml.Csp + face` (D-EXP-25; the face part is empty when there is none). `PrintHtml.Csp` = `<meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src data:; font-src data:; style-src 'unsafe-inline'">` followed by `"\n"`. The first `<head>\n` is always the document's own: no user text precedes it, and all user text is HTML-escaped, so it can never contain `<head>`.

### 7.8 HTML and Markdown builders (Core)

`HtmlDocumentBuilder.BuildAsync(manifest, items, createdLine, policy, embedder, progress, theme, ct)`, `PlainHtmlDocumentBuilder.BuildAsync(manifest, items, embedder, theme, ct)` and `MarkdownDocumentBuilder.Build(manifest, items, createdLine)` produce exactly the strings of 2.8, 2.10 and 2.12 with a `StringBuilder`, `'\n'` only, and:

| Helper | Definition |
|---|---|
| `ExportText.EscapeHtml(s)` | one pass: `&` to `&amp;`, `<` to `&lt;`, `>` to `&gt;`, `"` to `&quot;`; nothing else (equivalent to the four chained replaces) |
| `ExportText.HtmlBreaks(escaped)` | `"\n"` to `"<br>"` |
| `ExportText.EscapeMarkdown(s)` | prefix `\` before each of `\` `` ` `` `*` `_` `[` `]` `#` `<` `>` |
| `ExportText.CollapseNewlines(s)` | `Regex.Replace(s, JsWs + "*\n" + JsWs + "*", " ")` with `JsWs = @"[\t\n\v\f\r \u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]"`, `RegexOptions.CultureInvariant` |
| quote lines | `"> " + body.Replace("\n", "\n> ")` |
| image name | `$"step-{n.ToString("00", CultureInfo.InvariantCulture)}-{stepId}{ext}"` (`padStart(2,'0')`: 100 stays `100`) |
| numbers | `n.ToString(CultureInfo.InvariantCulture)` everywhere |
| base64 | `Convert.ToBase64String` (standard alphabet, padding, no line breaks) |
| file encoding | `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` (a lone surrogate becomes `EF BF BD`, as in Node) |

`MarkdownDocument(string Text, IReadOnlyList<MarkdownImage> Images)` with `MarkdownImage(string FileName, string? SourcePath, byte[]? Bytes)`; the engine deletes `images\` recursively (never following a reparse point; 01's `ReparseSafeDelete`), recreates it, writes or copies each image in order, then writes the `.md`.

`ExportCss.DocCss(double scale, ExportTheme theme)` and `ExportCss.PlainCss(double scale, ExportTheme theme)` are the templates of 2.9 and 2.10 verbatim; `ExportCss.ColumnBlockSelectors` is the five-item list. The `theme` parameter has NO default on any builder, so forgetting it is a compile error (the Electron source-scan test becomes a compile-time guarantee).

`ExportGeometry` holds `ZoomCropRect`, `HtmlImageSize`, `HtmlImgMaxW`, `HtmlImgEmbedMaxW`, `HtmlEmbedPolicy(ExportFormat? format, double docScale = 1)` (null means "not an HTML format", giving full-resolution PNG), `DocxImgMaxW`, and the constants of 3.1; widths come from 05's `DocScale.Widths(scale)`, clamping from `DocScale.Clamp`. `DocxPageColW` and `DocxCardInnerW` are computed from the twip constants with `Math.Floor((t / 20.0) * (96.0 / 72.0))`, not typed.

`ExportNames.SafeFileBase(string? title)` is 2.15 with: a `char` loop dropping `c <= '\u001F'` and the nine reserved characters (all removed characters are BMP, so this equals the code-point filter and keeps lone surrogates); `JsWs + "+"` collapse; `JsString.Trim`; the 120 cut, backing off to 119 when `char.IsHighSurrogate(s[119]) && char.IsLowSurrogate(s[120])` (D-EXP-10); `Regex(@"[." + jsWsClassBody + @"]+$")` strip; the reserved test with explicit ASCII classes (`^(?:[cC][oO][nN]|[pP][rR][nN]|[aA][uU][xX]|[nN][uU][lL]|[cC][oO][mM][1-9]|[lL][pP][tT][1-9])$`) extended per D-EXP-11 to the part before the first `.` and to `CONIN$`, `CONOUT$`, `COM`/`LPT` followed by `¹` `²` `³`. `NextAvailableStem` and `NextAvailableDir` use `probe.Exists` (D-EXP-22: a dangling link counts as taken).

### 7.9 Created line

```csharp
public static class CreatedLine
{
    public static string Build(DateTimeOffset localNow, CultureInfo culture, string? byline)
        => "Created on " + Timestamp(localNow.DateTime, culture) + (string.IsNullOrEmpty(byline) ? "" : " by " + byline);
    public static string Timestamp(DateTime local, CultureInfo culture)
        => (local.ToString(culture.DateTimeFormat.ShortDatePattern, culture) + ", "
            + local.ToString(culture.DateTimeFormat.LongTimePattern, culture))
           .Replace('\u202F', ' ').Replace('\u00A0', ' ');
}
```

For en-US this is exactly Electron's `9/22/2026, 2:03:07 PM`. Other locales follow the Windows culture's patterns and may differ from Electron's CLDR output (for example de-DE `22.09.2026, 14:03:07` against `22.9.2026, 14:03:07`, fr-FR with a comma); IMPROVEMENT-neutral, Q-EXP-6. `culture` is `CultureInfo.CurrentCulture` of the UI thread captured at composition (the user's regional format). Golden tests always inject the created line.

### 7.10 Word (Core, Open XML SDK)

```csharp
public sealed record OfficeImage(byte[] Bytes, int Width, int Height, string MediaType);
public static class DocxBuilder
{
    public static void Build(Stream output, ProjectManifest manifest, IReadOnlyList<ExportItem> items,
                             string createdLine, ExportTheme theme, Func<ShotItem, OfficeImage> loadImage);
}
```

`loadImage` is the engine's `loadItemImage`: bytes = `CroppedPng ?? ReadAllBytes(AbsPath)`, size = `codec.ReadSize(bytes)`.

Package: `WordprocessingDocument.Create(output, WordprocessingDocumentType.Document)`; `MainDocumentPart`; `StyleDefinitionsPart`; `DocumentSettingsPart` (`compatibilityMode` 15, the docx 9.7.1 default, so Word does not open the file in Compatibility Mode); core properties through `document.PackageProperties` (`IPackageProperties`: `Creator = "shotAI"`, `Title = manifest.Title`, `LastModifiedBy = "shotAI"` (D-EXP-18), `Revision = "1"`, `Created` and `Modified` = now in UTC); do not hand-build a `CoreFilePropertiesPart`. The output stream must be seekable and read/write (the SDK sits on `System.IO.Packaging`); the engine passes the temp `FileStream` of 7.3.

Schema order rule for every Open XML element built here: children are appended in the order of the schema sequence, because the SDK's typed constructors keep the order they are given and `OpenXmlValidator` rejects any other. In particular `w:pPr` is `pStyle`, `pBdr`, `spacing`, `jc`; `w:rPr` is `b`, `i`, `color`, `sz`, `szCs`; `w:tblBorders` is `top`, `left`, `bottom`, `right`, `insideH`, `insideV`; `w:tcMar` is `top`, `left`, `bottom`, `right` (the order docx writes).

`DocxDefaultStyles.Create()` reproduces docx 9.7.1's `DefaultStylesFactory`: `DocDefaults(RunPropertiesDefault(RunPropertiesBaseStyle(RunFonts { Ascii = "Aptos", HighAnsi = "Aptos", EastAsia = "Aptos", ComplexScript = "Aptos" })))`; paragraph styles `Title` (name `Title`), `Heading1` to `Heading6` (names `Heading 1` to `Heading 6`), each `BasedOn = "Normal"`, `NextParagraphStyle = "Normal"`, `PrimaryStyle` (quick format), with the run properties of 2.13; character style `Strong` (bold); `ListParagraph`, `Hyperlink` and the footnote and endnote styles as in the library (unused). No `Normal` style. The two heading color literals live only in this file (INV-EXP-16 exception list).

Mapping of 2.13 to Open XML (all dimensions as in 2.13):

| docx construct | Open XML |
|---|---|
| paragraph spacing | `ParagraphProperties(SpacingBetweenLines { Before = "N", After = "N" })` (only the given sides) |
| heading style | `ParagraphStyleId { Val = "Title" \| "Heading2" }` |
| run | `Run(RunProperties(Bold?, Color { Val = hex }?, FontSize { Val = sz }?, FontSizeComplexScript { Val = sz }?), Text(t) { Space = SpaceProcessingModeValues.Preserve })` |
| `multiline` | one run per line; every run after the first starts with `Break()` |
| section rule | `ParagraphBorders(TopBorder { Val = BorderValues.Single, Size = 6, Color = hair, Space = 8 })` |
| card | `Table(TableProperties(TableWidth { Type = Pct, Width = "5000" }, TableBorders(TopBorder, LeftBorder, BottomBorder, RightBorder, InsideHorizontalBorder, InsideVerticalBorder, each { Val = BorderValues.Single, Size = 4, Color = border })), TableGrid(GridColumn { Width = "100" }), TableRow(TableCell(TableCellProperties(TableCellMargin(TopMargin 120, LeftMargin 180, BottomMargin 120, RightMargin 180, each Type = Dxa), Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill }), ...)))` (schema order: in `w:tcPr`, `tcMar` precedes `shd`, and the border and margin children follow the rule above) (docx writes the width as `100%`; `5000` fiftieths is the same 100 percent in the form every Word version reads) |
| spacer | `Paragraph(ParagraphProperties(SpacingBetweenLines { After = "60" }), Run(RunProperties(FontSize { Val = "10" }), Text("")))` |
| image | `ImagePart` of type PNG or JPEG by `MediaType`; `Paragraph(ParagraphProperties(Justification { Val = Center }, SpacingBetweenLines { After = body ? "100" : "0" }), Run(Drawing(Inline(Extent { Cx = pxW * 9525, Cy = pxH * 9525 }, EffectExtent(0,0,0,0), DocProperties { Id = unique, Name = $"Picture {n}", Description = $"Screenshot for step {n}" }, NonVisualGraphicFrameDrawingProperties(GraphicFrameLocks { NoChangeAspect = true }), Graphic(GraphicData(Picture(...BlipFill(Blip { Embed = relId }, Stretch(FillRectangle())), ShapeProperties(Transform2D(Offset(0,0), Extents(cx,cy)), PresetGeometry { Preset = Rectangle })...)) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })) { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 })))`; `pxW = (int)JsMath.Round(w * fit)`, `pxH = (int)JsMath.Round(h * fit)`; the alt text is D-EXP-20 |
| page | `SectionProperties(PageSize { Width = 11906, Height = 16838 }, PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 708, Footer = 708, Gutter = 0 })` as the body's last child |

Colors pass through 08's `HexColor.NoHash(value)` (throws on anything but `#rrggbb`, returns uppercase). Tests validate every generated file with `OpenXmlValidator(FileFormatVersions.Office2019)`.

### 7.11 PowerPoint (Core, Open XML SDK)

```csharp
public static class PptxBuilder
{
    public static void Build(Stream output, ProjectManifest manifest, IReadOnlyList<ExportItem> items,
                             string createdLine, ExportTheme theme, Func<ShotItem, OfficeImage> loadImage);
}
public static class PptxLayout      // inches, exactly the Electron expressions of 2.14, evaluated in double
{
    public const double SlideW = 13.333, SlideH = 7.5, Margin = 0.5, Pad = 0.35;
    public static long Emu(double inches) => (long)JsMath.Round(914400 * inches);
    public static (double X, double Y, double W, double H) FitContain(double pxW, double pxH, (double X, double Y, double W, double H) box);
    public static int RoundRectAdjust(double radiusIn, long cx, long cy) => (int)JsMath.Round(radiusIn * 914400 * 100000 / Math.Min(cx, cy));
}
```

Parts: `PresentationDocument.Create(output, PresentationDocumentType.Presentation)`; `PresentationPart` with `SlideSize { Cx = 12192000, Cy = 6858000 }` and `NotesSize { Cx = 6858000, Cy = 9144000 }`; `PresentationPropertiesPart`; one `SlideMasterPart` with one blank `SlideLayoutPart`; `ThemePart` from `PptxTheme.Create()` (Office color scheme, `fontScheme` major `Calibri Light`, minor `Calibri`, as pptxgenjs writes); core properties through `PackageProperties`: `Creator = "shotAI"`, `Title`, `LastModifiedBy = "shotAI"` (already what pptxgenjs writes, `pptxgen.es.js:6456-6458`), `Revision = "1"`. pptxgenjs also writes a notes master, one notes slide per slide, `viewProps.xml` and `tableStyles.xml`; the native deck may omit them (`PptxDump` ignores them; AC-EXP-14's manual open check covers PowerPoint's tolerance).

Per slide: a `SlidePart` linked to the layout; `Background(BackgroundProperties(SolidFill(RgbColorModelHex { Val = surface }), EffectList()))`. Shapes in the order of 2.14:

| Element | Open XML |
|---|---|
| card | `Shape` with `PresetGeometry { Preset = RoundRectangle }` and `AdjustValueList(ShapeGuide { Name = "adj", Formula = $"val {adj}" })`, `SolidFill(fill)`, `Outline { Width = 12700 }(SolidFill(line))` |
| section rule | `Shape` with `PresetGeometry { Preset = Line }`, `Outline { Width = 12700 }(SolidFill(controlBd))`, extents `(3657600, 0)` |
| text box | `Shape` with `NonVisualShapeDrawingProperties { TextBox = true }`, `PresetGeometry { Preset = Rectangle }`, `NoFill`; `TextBody(BodyProperties { Wrap = Square, RightToLeftColumns = false, Anchor = Top or Center }, ListStyle(), paragraphs)`; each paragraph `ParagraphProperties { Alignment = Center or Left }` and runs `RunProperties { Language = "en-US", FontSize = pt * 100, Bold = true? , Dirty = false }(SolidFill(color))` |
| paragraph split | `PptxText.Paragraphs(runs)` reproduces 2.14's pptxgenjs algorithm, not a plain split: per run, `Regex.Replace(text, "\r*\n", "\r\n")`; a run containing CRLF and not ending in `\n` becomes one piece per line, each closing its paragraph; a run ending in `\n` stays whole; an empty piece emits no `a:r`, only `a:endParaRPr` (with `sz` when the box has a size). Single-string boxes therefore get one paragraph per line (the cover intro's `\n\n` gives an empty paragraph). The callout gives paragraph 1 = [bold 24 run `{glyph} {heading or label}` + CRLF, first body line at 18], then one paragraph per further body line; an empty body leaves only the heading run. Whether the native run keeps the literal CRLF in `a:t` (byte parity) or ends paragraph 1 after the heading is Q-EXP-21; `PptxDump` compares whichever is chosen against Electron's file |
| image | `Picture` with `NonVisualDrawingProperties { Description = $"Screenshot for step {n}" }`, `PictureLocks { NoChangeAspect = true }`, `BlipFill(Blip { Embed }, Stretch(FillRectangle()))`, `Transform2D` from `FitContain` with each value through `Emu`; `ImagePart` PNG or JPEG by `MediaType` |

Positions and sizes are the expressions of 2.14 evaluated in double (never retyped as the rounded table of 3.3), then `Emu`; 3.3 is the test oracle. Colors through `HexColor.NoHash`.

### 7.12 Shareable package (Core, System.IO.Compression)

`PackageWriter.WriteAsync(Stream zipOut, string dir, ProjectManifest manifest, bool includeOriginals, IRenderGate gate, IPathConfine confine, IExportFileProbe probe, TimeProvider time, CancellationToken ct)`:

1. `manifest.Steps.Count == 0` throws `ExportException(ExportMessages.NothingToExport)`.
2. `node = ManifestCodec.Encode(manifest)` (01 7.4; the same tree the store would write, extras included); `node["sopBackup"] = null`.
3. `new ZipArchive(zipOut, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: new UTF8Encoding(false))`; every file entry `CompressionLevel.Optimal` (DEFLATE), `LastWriteTime = time.GetLocalNow()`; directory entries are `CreateEntry("shots/")` style names ending in `/`, written before the first file inside them, as JSZip does (2.21 layout).
4. Originals: for each non-text step, screenshot then flattened: `abs = confine.ConfineNoLinks(dir, rel)` (D-EXP-16; null skips); read (failure skips); entry name `rel.Replace('\\', '/')`.
5. Safe: `n` counter over non-text steps; `render = gate.Resolve(dir, step, $"Step {n}", EgressVerb.Export)`; read `render.Abs` (failure throws `ExportMessages.RenderMissing(n, rel)`, D-EXP-17); entry `shots/step-{n:0000}{ext}` (`ext` prefixed with `.` if missing); in the step's `JsonObject`: `screenshot` = the entry name, `annotations` = `[]`, `crop` = null, `click` = null, remove `flattened`, `renderRev` = 0, `markerBaked` = false. An existing key is replaced in place and a missing key is appended, which is what JavaScript assignment does to key order.
6. `project.json` = UTF-8 of `JsJson.Stringify(node, 2)` (01); `shotai-package.json` = UTF-8 of `JsJson.Stringify` of an object built in the order `format`, `version`, `app`, `includeOriginals`, `exportedAt` (`IsoTime.ToIsoString(time.GetUtcNow())`), indent 2.

The engine writes the zip to a temp file in `<dir>\export\` and replaces `NextAvailableStem(exportDir, SafeFileBase(title) + " (shotAI package)", ".zip") + ".zip"` atomically, logs info `$"exported package (originals={(includeOriginals ? "true" : "false")}) \u2192 {outputPath}"`, and returns `PackageResult`; the App reveals it (always, parity). Written from a seekable `FileStream`, so no data descriptors; zip64 appears only past 4 GiB (Q-EXP-16).

`PackageReader.ReadAsync(string zipPath, CancellationToken ct)` returns `PackageImport(ProjectManifest Manifest, IReadOnlyList<ImportFile> Files)`:

1. `new FileInfo(zipPath).Length > PackageLimits.MaxPackageBytes` throws `ExportMessages.PackageTooLarge`.
2. `ZipFile.OpenRead(zipPath)` (streamed from disk, not read into memory); `InvalidDataException` or any open failure throws `ExportMessages.NotAZip`.
3. Index: for each entry in central directory order, `safe = JsZipNames.Resolve(entry.FullName)`; `isDir = entry.FullName.EndsWith('/') || (entry.ExternalAttributes & 0x10) != 0 || (madeByUnix && ((entry.ExternalAttributes >> 16) & 0x4000) != 0)` (JSZip's three rules, EDGE-EXP-54; the unsanitized name is tested for the slash, as JSZip does; `madeByUnix` is the high byte of "version made by" equal to 3, which `ZipArchiveEntry` does not expose, so `JsZipNames` reads it from the central directory record, or, UNVERIFIED alternative, treats bit `0x4000` of the upper word as a directory whatever the host, which differs only for a non-Unix entry with that bit set); when `isDir` and `safe` does not end with `/`, append `/` (`forceTrailingSlash`); keep an insertion-ordered map `safe -> entry` where a later equal name replaces the value but keeps the first position (EDGE-EXP-32, 33). Open with `ZipFile.Open(zipPath, ZipArchiveMode.Read, new UTF8Encoding(false))` so names without the UTF-8 flag decode as UTF-8, as JSZip's default does (the `0x7075` Unicode Path extra field that JSZip honors is ignored by .NET; accepted, it only matters for non-UTF-8 legacy names).
4. Marker: `shotai-package.json` absent throws `ExportMessages.MissingMarker`. `ReadBounded(entry, MaxFileBytes)` (declared `Length` checked first, then at most `MaxFileBytes + 1` bytes read; over the cap throws `A file in the package is too large: shotai-package.json`); any read or inflate error, or `JsJson.Parse` failure, throws `ExportMessages.MarkerCorrupt`; the bytes are decoded to a string with replacement (U+FFFD) and a leading U+FEFF is a parse failure, as `JSON.parse` treats it (EDGE-EXP-53; never hand the raw bytes to `System.Text.Json`, which skips a BOM). Not a JSON object, or `format` not the string `shotai-package` (ordinal), throws `ExportMessages.UnrecognizedFormat` (D-EXP-13). `version` a JSON number greater than 1 throws `ExportMessages.TooNew`.
5. Manifest: `project.json` absent throws `ExportMessages.MissingManifest`; bounded read (same cap); read or parse failure, or a non-object, throws `ExportMessages.ManifestCorrupt`; `manifest = ManifestCodec.Decode(node, "Imported project")` (01).
6. Files: for each non-directory entry in map order except the two names: `rel = safe.Replace('\\', '/')`; whitelist `^shots/[^/]+$` or `^export/\.render/[^/]+$` (ordinal, `RegexOptions.CultureInvariant`); declared `Length > MaxFileBytes` throws `A file in the package is too large: {rel}`; bounded read, same message when exceeded; running total of actual bytes `> MaxPackageBytes` throws `ExportMessages.ContentsTooLarge`; an inflate error throws `ExportMessages.NotAZip` (D-EXP-15); magic check throws `ExportMessages.NonImage(rel)`; a `rel` already seen throws `ExportMessages.DuplicateFile(rel)` (D-EXP-14); add `new ImportFile(rel, bytes)`.

`JsZipNames.Resolve(name)`: `parts = name.Split('/')`; for each index `i`: skip `"."`, skip `""` unless `i == 0` or `i == parts.Length - 1`; `".."` removes the last kept part if any; else keep; join with `/`.

`ExportEngine.ImportPackageAsync(zipPath, ct)` = `PackageReader.ReadAsync` then `store.CreateProjectFromImportAsync(import.Manifest, import.Files, ct)` (01). Cancellation is honored until 01's call starts.

### 7.13 App: `IExportService`, dialogs, reveal

```csharp
namespace ShotAI.App.Export;

public interface IExportService
{
    Task<ExportResult> ExportWithSaveDialogAsync(string projectPath, ExportFormat format, Window owner,
                                                 IProgress<ExportProgress>? progress, CancellationToken ct);  // 05, 06 row export
    Task<ExportResult> ExportToOwnFolderAsync(string projectPath, ExportFormat format, CancellationToken ct);      // 06 bulk
    Task<ExportResult> ExportToDirectoryAsync(string projectPath, ExportFormat format, string directory, CancellationToken ct); // 06 bulk
    Task<string?> ChooseExportDirectoryAsync(Window owner);                                                      // 06 bulk
    Task RevealExportDirectoryAsync(string directory);                                                           // 06 bulk
    Task<PackageResult> ExportPackageAsync(string projectPath, bool includeOriginals, CancellationToken ct);      // 05
    Task<ProjectSummary?> ImportPackageAsync(Window owner, CancellationToken ct);                                // 06
}
```

`ExportService` (App) composes `ExportEngine` with `IExportDialogs` (WPF, `Microsoft.Win32`, shown on the UI thread with `owner`):

| Dialog | WPF API and properties |
|---|---|
| Save, single file | `SaveFileDialog { Title = "Export", InitialDirectory = p.ExportDir, FileName = p.SuggestedFileName, Filter = $"{name}\|{pattern}", DefaultExt = ext, AddExtension = true, OverwritePrompt = true, CheckPathExists = true }` |
| Save, Markdown | same with `Title = "Export Markdown (saved as a folder with its images)"`, `FileName = BaseName + ".md"`, `Filter = "Markdown\|*.md"` |
| Folder | `OpenFolderDialog { Title = "Choose a folder for the exports", Multiselect = false }` (the Windows picker always offers New folder, which covers Electron's `createDirectory`) |
| Import | `OpenFileDialog { Title = "Import a shotAI project package", Filter = "shotAI package\|*.zip", Multiselect = false, CheckFileExists = true }` |

`ExportWithSaveDialogAsync` = `PrepareAsync` (errors before the dialog), dialog (cancel returns `new ExportResult(format, "", Canceled: true)`), `WriteAsync(ChosenFile)`, then 03's `IShellReveal.RevealInExplorer(outputPath)`. `ExportToOwnFolderAsync` = `WriteAsync(Folder(p.ExportDir))`, no progress, no reveal. `ExportToDirectoryAsync` = `WriteAsync(Folder(directory))`, no progress, no reveal. `RevealExportDirectoryAsync(dir)`: only if `Directory.Exists(dir)` and the path is not a link that resolves to a file, `IShellReveal.OpenFolder(dir)`; every error is logged at debug and swallowed (INV-EXP-24). `ExportPackageAsync` = engine then `RevealInExplorer`. `ImportPackageAsync` = dialog (cancel returns null) then the engine.

Callers (05, 06) run 04's `EnsureFlattenedAsync` before any of these (INV-EXP-27); Home callers open the project first (EDGE-EXP-47).

### 7.14 Threading, cancellation, disposal

| Work | Thread |
|---|---|
| `PrepareAsync`, collector, embedder, builders, Office, zip | thread pool (`Task.Run` around CPU-bound parts); never the UI thread |
| AVIF encode | thread pool, libavif with `maxThreads = ProcessorCount`; shots encoded one after another so progress order is kept |
| Dialogs, reveal | UI thread |
| WebView2 | UI thread, awaited, never blocking |
| Progress | `IProgress<ExportProgress>` created by the caller on the UI thread (05), reported from the pool |

Disposal: the WebView2 controller is closed after each PDF; the environment and the host HWND live until app shutdown (03 disposes the renderer through DI); `ZipArchive`, streams and `PixelFrame`s are disposed with `using`. No export holds a lock on the project folder after returning.

### 7.15 Errors and messages

`ExportException(string message)` carries the user-visible text; 05 and 06 show `Export failed: {message}` or `Import failed: {message}`. Messages are the 2.23 strings verbatim, in `ExportMessages`, plus the IMPROVEMENT strings:

| Constant | Text | Why |
|---|---|---|
| `PdfRuntimeMissing` | `PDF export needs the Microsoft Edge WebView2 Runtime, which isn't installed on this computer. Try the HTML or Markdown export instead.` | D-EXP-4; Electron bundled its engine |
| `FileInUse(name)` | `` $"Couldn't write {name} because another program has it open. Close it and export again." `` | D-EXP-19; replaces a raw sharing-violation message (`IOException` with `HResult` 0x80070020 or 0x80070021) |
| `DuplicateFile(rel)` | `` $"The package contains the same file twice: {rel}" `` | D-EXP-14; replaces a raw `EEXIST` |

Every other exception from the pipeline (`IOException`, `UnauthorizedAccessException`) propagates with its message, as Electron did. `RenderRefusedException` (04) passes through unchanged.

### 7.16 APIs and packages

| Kind | Names |
|---|---|
| NuGet (Directory.Packages.props) | `DocumentFormat.OpenXml` (Core), `Microsoft.Web.WebView2` (Platform) |
| BCL | `System.IO.Compression` (`ZipArchive`, `ZipArchiveEntry`, `ZipFile.OpenRead`, `CompressionLevel.Optimal`), `System.Text.RegularExpressions` (`[GeneratedRegex]`), `System.Runtime.InteropServices` (`LibraryImport`, `NativeLibrary`), 01's `JsJson` over `System.Text.Json.Nodes` |
| CsWin32 (`NativeMethods.txt`) | `CreateWindowEx`, `DestroyWindow`, `WINDOW_STYLE`, `WINDOW_EX_STYLE`; WIC additions to 02's list: `GUID_ContainerFormatJpeg`, `IWICBitmapClipper`, `IWICFormatConverter`, `GUID_WICPixelFormat32bppPBGRA`, `GUID_WICPixelFormat32bppRGBA` (straight RGBA for AVIF), `IWICBitmapScaler` with `WICBitmapInterpolationModeHighQualityCubic` (the resize), `IWICBitmapFrameEncode`, `IPropertyBag2` (JPEG `ImageQuality`) |
| WebView2 | `CoreWebView2Environment.CreateAsync`, `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments`, `CoreWebView2Settings.IsReputationCheckingRequired`, `CreateCoreWebView2ControllerAsync(IntPtr)`, `CoreWebView2Controller.Bounds`, `IsVisible`, `Close`, `CoreWebView2Settings` (7.7), `Navigate`, `NavigationStarting`, `NavigationCompleted`, `FrameNavigationStarting`, `NewWindowRequested`, `DownloadStarting`, `PermissionRequested`, `AddWebResourceRequestedFilter(string, CoreWebView2WebResourceContext, CoreWebView2WebResourceRequestSourceKinds)`, `WebResourceRequested`, `CreateWebResourceResponse`, `ProcessFailed`, `CreatePrintSettings`, `PrintToPdfAsync` |
| WPF | `Microsoft.Win32.SaveFileDialog`, `OpenFileDialog`, `OpenFolderDialog` |
| Native | `shotai_avif.dll` (libavif 1.x + libaom, 7.6) |
| Other specs | 01 `IProjectStore`, `ManifestCodec`, `JsJson`, `JsMath`, `JsString`, `IsoTime`, `IPathConfine`, `IAtomicFile`, `ReparseSafeDelete`, `ImportFile`; 02 `PixelFrame`, `WicImageCodec`; 03 `IShellReveal`, `AppPaths.BrandFontPath`, own-window registry; 04 `IRenderGate`, `EgressVerb`, `RenderRefusedException`; 05 `DocScale`; 08 `BrandId`, `Brands.PinnedBrand`, `Brands.Coerce`, `BrandPalette`, `BrandRadii`, `HexColor.NoHash`; 10 settings sources, logging |

### 7.17 Divergence register

| ID | Change | Class | Justification |
|---|---|---|---|
| D-EXP-1 | The PDF print copy embeds the brand face as a `data:font/ttf;base64` URI instead of a `file://` URL | IMPROVEMENT | The page then needs no sub-resource at all, so the WebView2 request filter can refuse everything; avoids file-origin font loading rules. Only the temporary print copy grows; the `.html` export is unchanged |
| D-EXP-2 | The PDF prints to `export\.render\_print-<guid>.pdf`, is checked for `%PDF-`, then published over the destination through 01's `AtomicFile` (never a cross-volume `File.Move`); every engine failure maps to the empty-document message (EDGE-EXP-57); the sweep also removes `_print-*.pdf` | IMPROVEMENT | `PrintToPdfAsync` writes directly to a path; printing to the destination would leave a partial file on failure, breaking INV-EXP-19 |
| D-EXP-3 | WebView2 hardening beyond `javascript: false`: navigation, frame, window, download, permission and resource blocking; explicit user data folder | IMPROVEMENT [SECURITY] | Same intent as Electron's sandboxed hidden window; WebView2 needs explicit handlers to get it |
| D-EXP-4 | A missing WebView2 runtime gives a specific message | IMPROVEMENT | Electron could not lack its engine; a native build can |
| D-EXP-5 | AVIF encodes with all cores (`maxThreads`) | IMPROVEMENT | Electron ran single-threaded WASM (11.4 s for 13 images); same quality and speed settings |
| D-EXP-6 | Single-file outputs and packages are written atomically | IMPROVEMENT | A crash mid-write no longer leaves a truncated `.docx` or `.zip` that looks valid by name |
| D-EXP-7 | The export waits for the project's pending writes | IMPROVEMENT (required by the fixed optimistic-edit decision) | INV-EXP-28 |
| D-EXP-8 | Cancellation is honored until the final write, leaving nothing behind | IMPROVEMENT | Leaving a project mid-export (05 EDGE-REP-35) should not finish writing files the user walked away from |
| D-EXP-9 | Markdown Save As into an existing unrelated folder numbers a new folder instead of deleting its `images\` | IMPROVEMENT | Data loss (EDGE-EXP-22) |
| D-EXP-10 | The 120-unit title cut never splits a surrogate pair | IMPROVEMENT | A lone surrogate in a file name is a garbled character in Explorer and in cloud sync |
| D-EXP-11 | Reserved device names are also detected before the first `.` and for `CONIN$`, `CONOUT$`, superscript-digit `COM` and `LPT` | IMPROVEMENT | Those names are devices on supported Windows builds; Q-EXP-12 |
| D-EXP-12 | Import checks declared sizes before inflating and reads each entry with a hard bound; the zip is streamed, not loaded whole | IMPROVEMENT [SECURITY] | Zip bomb (EDGE-EXP-34); macOS already does the declared-size check |
| D-EXP-13 | A non-object marker (including `null`) gives `Unrecognized package format.`; a non-object manifest (including `null`, arrays and primitives) gives `The package project.json is corrupt.` | IMPROVEMENT | Electron showed a raw TypeError for `null`, and silently imported a blank `Imported project` for an array or primitive manifest (EDGE-EXP-30) |
| D-EXP-14 | Two entries equal after the backslash replacement fail with a clear message | IMPROVEMENT | Electron showed a raw EEXIST (EDGE-EXP-33) |
| D-EXP-15 | A corrupt compressed entry reports `This file is not a valid .zip package.` | IMPROVEMENT | Electron surfaced the inflate library's error |
| D-EXP-16 | Originals-mode references are confined with link refusal | IMPROVEMENT [SECURITY] | INV-EXP-32; macOS parity |
| D-EXP-17 | An unreadable render in a safe package gives the render missing message | IMPROVEMENT | EDGE-EXP-37 |
| D-EXP-18 | Word `lastModifiedBy` is `shotAI` (PowerPoint's already is, parity) | IMPROVEMENT | Electron's Word file said `Un-named` (a library default) in File, Info; pptxgenjs writes the author there |
| D-EXP-19 | A destination locked by another program gives a clear message | IMPROVEMENT | EDGE-EXP-16 |
| D-EXP-20 | Word and PowerPoint pictures carry the alt text `Screenshot for step {n}` | IMPROVEMENT | Accessibility; the HTML already has it; screen readers announce Office pictures by their description |
| D-EXP-21 | The RGBA handed to AVIF is un-premultiplied | IMPROVEMENT | Correct for any alpha; identical output for the fully opaque renders shotAI produces |
| D-EXP-22 | A dangling link at a candidate name counts as taken | IMPROVEMENT [SECURITY] | Node's `fs.access` follows links, so a write could follow a planted link out of the folder |
| D-EXP-23 | The created line is built from the Windows culture's short date and long time patterns | IMPROVEMENT-neutral | ICU's CLDR `toLocaleString` is not available; en-US is exact (Q-EXP-6) |
| D-EXP-24 | IPC format validation, `BrowserWindow`, `nativeImage`, `@jsquash/avif`, `jszip`, `docx`, `pptxgenjs`, the `extraResource` font packaging and the `projects:export-progress` channel | ELECTRON-ONLY | Replaced by the enum, WebView2, WIC, libavif, `System.IO.Compression`, Open XML SDK, 12's packaging and `IProgress<T>` |
| D-EXP-25 | The PDF print copy carries a Content Security Policy meta (`default-src 'none'; img-src data:; font-src data:; style-src 'unsafe-inline'`) injected with the face, never in the kept `.html` | IMPROVEMENT [SECURITY] | Defense in depth for INV-EXP-20 independent of which schemes `WebResourceRequested` covers; the print copy needs nothing else (D-EXP-1); output pixels are unchanged |

## 8. Tests

### 8.1 Electron test files

| File | Purpose | Cases (grouped) | Port | Target class |
|---|---|---|---|---|
| `src/main/export-css.test.ts` | Pins the #57 paste layout and the #70 per-scale invariants of both stylesheets | **Paste survival** (`:43-103`): the exported block list equals an independent literal list of the five selectors (never iterate the module's own list); every block's FIRST rule (`\.{sel}\{([^}]*)\}`) carries `max-width:816px`; every block centers itself (`margin:[^;]*auto`); `.doc` has no `max-width` and has `padding`; `.section` has `max-width:816px`, `padding-left:46px`, `break-inside:avoid` and no `border-top`, `.section__inner` has `border-top:2px solid #e7e4f2`; the `@media print` rule setting `max-width:none` lists all five selectors; `HTML_IMG_MAX_W == 816 - 30 - 16 - 32`. **Plain CSS** (`:105-110`): contains `font-family:Arial` and `img{max-width:100%;height:auto}`. **Per-scale** (`:112-177`): scale 1 equals the default argument and contains `max-width:816px`; every block carries `max-width:{col}px` at all 13 detents; print lifts all five at every detent (the Electron regex lacks escapes, EDGE-EXP-46; the port uses `@media print\{(.*)\}\s*$` and `([^{}]*)\{max-width:none\}`); `htmlImgMaxW(s) == col - 78` and `!= round(738 * s)` for `s != 1`; the plain body `max-width` is positive at every detent and `w(0.65) < w(1) < w(1.25)`; `NaN`, `99`, `-3` produce no `NaN` and no `max-width:-` | ShotAI.Core.Tests (Linux) | `Export/ExportCssTests` |
| `src/main/export-geometry.test.ts` | Pins the zoom crop, the size attributes, the #56 embed boundary and the #70 Word ceiling | **zoomCropRect** (`:22-78`): null at zoom 1, below 1, NaN and 0; zoom 2 centered `{200,150,400,300}`; pan 0 `{0,0,400,300}`; pan 1 `{400,300,400,300}`; 801 by 601 at zoom 3 pan 1 stays in bounds; NaN pan is centered; 1 by 1 is null. **htmlImageSize** (`:80-103`): `2560x1440` to `738x415`; `400x300` and `738x450` unchanged; `3000x1` to `738x1`; null for `(0, 0)`, `(NaN, 100)`, `(100, NaN)`, `(-800, 600)`, `(Infinity, 600)`. **htmlEmbedPolicy** (`:105-149`): `html` is `{1476, avif}` and `1476 == 2 * 738`; `html-plain` `{738, png}`; `pdf` `{null, png}`; `markdown`, `docx`, `pptx` `{null, png}`; `''` and an unknown format `{null, png}`. **Word ceiling** (`:151-220`): at every detent `<= 576`, and `576 < 601`; the derivation in twips equals 576 and 601; never 624 or 598 at any detent; `docxImgMaxW(1) == docxImgMaxW() == 560`; `0.65` gives `round(560*0.65)` = 364; `1.25` and `1.2` give 576, and `round(560*1.25) > 576`; `NaN`, `99`, `-1` give an integer in `(0, 576]` | ShotAI.Core.Tests (Linux). The two string-format cases call `HtmlEmbedPolicy(null)`, the native "not an HTML format" | `Export/ExportGeometryTests` |
| `src/main/export-palette-source.test.ts` | Asserts from SOURCE that the export surfaces hold no color or radius literal and read the threaded theme; asserts the RENDERED radii | **Per surface** (`export-css.ts`, `export-docx.ts`, `export-pptx.ts`, `:56-89`): after stripping comments, no `#[0-9a-fA-F]{3,8}\b` and no quoted six-hex literal outside `ALLOWED`; each imports the export theme, contains `= theme.palette`, and names no `APP_LIGHT`, `APP_DARK` or `BRANDS`. **Allowances** (`:91-105`): each has a reason over 10 characters; none is stale. **Office conversion** (`:108-136`): every `C.<role>` read sits on a line that calls `hexNoHash` (the Electron check finds the line of the FIRST textual occurrence of each hit with `src.indexOf(hit)`, not the line of the match itself, so a second, unwrapped read of the same role on another line passes; the port checks each match's own line). **Geometry** (`:138-152`): `export-css.ts` has no `border-radius:` px or percent literal. **Rendered** (`:154-187`): `.step__main` and `.doc__intro` render the card radius (10), `.step__img` the figure radius (8) which is smaller, `.step__num` contains `border-radius:50%` | ShotAI.Core.Tests (Linux), scanning `dotnet/src/ShotAI.Core/Export/ExportCss.cs`, `Export/Office/DocxBuilder.cs`, `Export/Office/PptxBuilder.cs`, `Export/Office/DocxDefaultStyles.cs` with C# comment stripping (`//`, `/* */`) and C#-shaped patterns (`"[0-9A-Fa-f]{6}"`); `ALLOWED` = `2E74B5` and `1F4D78` in `DocxDefaultStyles.cs` ("docx library default heading colors, reproduced for parity, EDGE-EXP-44, not a brand color"); theme read = `theme.Palette`; the generated brand table's type name (08) must not appear; every `C.<Role>` read on a line with `HexColor.NoHash(`; rendered radii from `ExportCss.DocCss(1, ExportTheme.Default)` | `Export/ExportPaletteSourceTests` |
| `src/shared/export-theme.test.ts` | An export is a brand, never an appearance; the document reskins; only the PDF embeds the face; the brand reaches every builder | **Brand not appearance** (`:14-36`): every brand's export palette is its light palette and not its dark one; `lfi` carries `lfi`, `'nonsense'` falls back to the default; the default theme is the default brand's light palette. **Reskin** (`:38-98`): the LFI stylesheet contains no shotAI-only light value; LFI and shotAI differ and each contains its own accent; LFI contains its card and figure radii; the badge is `50%` for shotAI and `8px` for LFI, and the LFI stylesheet has no `50%`; `fontStackFor('lfi')` starts with `"Archivo",`, shotAI's has no Archivo, the LFI stylesheet has `font-family:"Archivo"`; no stylesheet (both brands, plain LFI) contains `@font-face`; plain stacks are exactly `Arial,Helvetica,sans-serif` and `"Archivo",Arial,Helvetica,sans-serif`. **PDF face** (`:100-173`): `fontFamily` is `Archivo` and null; the face is injected into the print copy only, with the weight range and the empty-string degrade (source regex); `extraResource` ships the `.ttf` and `paths.ts` joins the same basename (source scan); `OFL.txt` ships beside it. **Reaches the exporters** (`:175-261`): every builder call in `exportProject` passes `theme` (paren-balanced scan); the three IPC export handlers pass `brand:` from `getBrand()`; the precedence expression has the narrowing inside the `??`; `resolve('solarpunk','lfi')`, `(undefined,'lfi')`, `('shotAI','lfi')`, `(42,'lfi')` give `lfi`, `lfi`, `shotAI`, `lfi` | ShotAI.Core.Tests (Linux) for the brand, reskin, face-fact and precedence cases. The "print copy only" case becomes behavioral (`PrintHtmlTests.FaceOnlyInThePrintCopy`: the engine's `.html` output never contains `@font-face`, the print copy does, with `font-weight:100 900`; a missing face yields no style and a warning). The two packaging cases are ELECTRON-ONLY (Forge `extraResource`); their intent moves to 12 as `Packaging/BrandFontShipsTests` (Linux source scan of `ShotAI.App.csproj`: `Fonts\Archivo.ttf` and `Fonts\OFL.txt` are copied to output from the same folder, and `AppPaths.BrandFontPath` uses that relative path). The "every builder gets theme" case is a compile-time guarantee (no default parameter) plus `ExportEngineTests.EveryBuilderGetsTheResolvedTheme`; the IPC case becomes `ExportEngineTests.BrandIsReadAtExportTime` | `Export/ExportThemeTests`, `Export/PrintHtmlTests` |
| `src/shared/brand-narrowing.test.ts` | #89: prototype members are not brands | (`:16-47`) exactly the real brands are accepted; each of `toString`, `constructor`, `valueOf`, `hasOwnProperty`, `isPrototypeOf`, `propertyIsEnumerable`, `toLocaleString`, `__proto__`, `__defineGetter__` is rejected and coerces to the default; each still yields the default light palette and a font stack without throwing; `solarpunk` is rejected; `42`, `null`, `undefined`, `{}`, `[]`, `true` are rejected without throwing | ShotAI.Core.Tests (Linux); the brand type is 08's, the export consequence is 09's. Native additions: `"0"`, `"1"`, `"-1"` (what `Enum.TryParse` would accept), `" lfi"`, `"lfi "`, `"LFI"`, `"ShotAI"`, `"lfi\0"` are rejected; JSON inputs (number, bool, object, array, null node) are rejected; `ExportTheme.Resolve(x, BrandId.Lfi)` gives LFI for every rejected `x` | `Brand/BrandNarrowingTests` |

### 8.2 New tests the native code needs

**Linux (ShotAI.Core.Tests)**

| Class | What it proves |
|---|---|
| `Export/ExportCssGoldenTests` | `DocCss` and `PlainCss` for both brands at all 13 detents plus `NaN`, `99`, `-3` equal the committed golden files `Golden/Export/css/{doc,plain}-{brand}-{scale}.css` byte for byte, and their SHA-256 equal 3.4 (the table is the cross-check that the goldens themselves came from Electron) |
| `Export/StepCollectorTests` | numbering tables (INV-EXP-4) including the #90 case; unknown callout; empty section and empty text skipped; empty colored callout kept; trimming uses `JsString.Trim` (a caption of `"\u00A0x\u0085"` keeps the `\u0085`); render missing message exact; nothing-to-export exact; the gate is called once per shot with `Step {n}`; a directory at the render path passes the check; a crop sets PNG media type and `.png` |
| `Export/ZoomCropTests` | the collector's crop uses the gated bytes; a decode failure, an unreadable file, a directory at the render path or an empty PNG falls back to the full render without throwing; zoom `<= 1` or NaN never reads the file |
| `Export/HtmlImageEmbedderTests` | the 2.6 pipeline with a fake codec and fake AVIF: PDF bytes untouched; never upscales; never bigger (a fake PNG encoder that grows); AVIF null gives JPEG 85; JPEG not smaller gives the original; a throwing decode keeps the original and logs the exact warning; size attributes from the input size; resize height `max(1, round(h * cap / w))`; `AvifSanity` accepts `ftypavif`, `ftypmif1` and rejects 16-byte inputs and other brands, accepts a 20-byte `ftypavif` output without throwing and treats byte `0xE1` as `a` (EDGE-EXP-58); an undecodable input returns the original bytes and logs nothing |
| `Export/HtmlDocumentBuilderTests` | each item kind's exact markup (2.8 table); intro variants (heading only, body with newlines to `<br>`); escaping of `& < > "` but not `'`; progress sequence `{0,k}..{k,k}` and `{0,0}` for no shots |
| `Export/PlainHtmlDocumentBuilderTests` | exact blocks; `<hr>` only between items; no created line; PNG at 1x policy |
| `Export/MarkdownDocumentBuilderTests` | exact lines; `---` separation with blank lines; Setext, list and blockquote guards; escapes; bodies raw; heading collapse only where Electron collapses; image names with `padStart(2)` and `100`; trailing single newline |
| `Export/ExportGoldenTests` | for each fixture under `Golden/Export/fixtures/<name>/` (below): with the replay embedder (Electron's recorded `InlineImage` per shot and policy) the native `html`, `html-plain` and `.md` equal Electron's outputs byte for byte; Markdown image files equal Electron's (byte for byte uncropped, same decoded pixels cropped); with the real `HtmlImageEmbedder` and a deterministic fake codec, the outputs equal Electron's after replacing every `data:<type>;base64,<payload>` payload with a placeholder |
| `Export/StylesheetStrippingTests` | macOS idea (`testStyledHtmlSurvivesStylesheetStripping`): with `<style>...</style>` removed, every `<img>` still has `width` and `height`, and every top-level block element is a direct child of `div.doc` (no second wrapper) |
| `Export/ExportTextTests`, `Export/ExportNamesTests` | escapes; `SafeFileBase` table (2.15 examples, controls, reserved names in any case, the D-EXP-10 and D-EXP-11 cases, 130-char titles, trailing dots after truncation, lone surrogates kept); `NextAvailableStem` and `NextAvailableDir` with a fake probe (file, folder, dangling link all taken) |
| `Export/CreatedLineTests` | en-US `Created on 9/22/2026, 2:03:07 PM`; with byline ` by Jane`; null and empty byline; U+202F normalized |
| `Export/ExportEngineTests` | with fakes for store, gate, codec, PDF, dialogs: refusal writes nothing and shows no dialog; canceled dialog writes nothing; brand read at export time; every builder receives the resolved theme; pending writes awaited; destination table of 7.3 including D-EXP-9; atomic replace; orphan sweep of `_print-*`; PDF failure writes nothing; cancellation leaves nothing; never writes the project |
| `Export/Office/DocxBuilderTests` | `OpenXmlValidator(Office2019)` reports no errors; A4 and margins; Aptos defaults; styles present with the library values; every element of 2.13 (spacing, borders, shading, margins, run sizes and colors) read back; image extents `round(w*fit)*9525` with `fit` from `DocxImgMaxW` at 0.65, 1, 1.25; unknown callout; core properties |
| `Export/Office/PptxBuilderTests` | validator clean; slide size; slide count = items + 1; every position of 3.3 at scale 1; `adj` 1818 and 1455; image `FitContain` inside the box at 0.65 and 1.25 (shrink only, centered); run sizes (`sz = pt*100`), bold, colors, anchors, alignment; paragraph splitting of `\r\n` and `\n`; unknown callout |
| `Export/Office/OfficeLayoutDumpTests` | `DocxDump` and `PptxDump` (a normalized list of paragraphs, runs, tables, images, shapes and their properties, ignoring ids, rsids, names and dates) of the native file equal those of Electron's golden `.docx` and `.pptx` for each fixture, with a tolerance of 1 EMU on picture offsets |
| `Export/Package/PackageWriterTests` | safe mode entries, names and order; reset fields; `sopBackup` null in both modes; marker bytes (key order, indent, ISO time); originals mode ships exactly the referenced files; `nothing to export` for zero steps; the zip opens with `ZipFile.OpenRead` and has no data descriptor flag (bit 3) on any entry |
| `Export/Package/PackageReaderTests` | each rejection of 2.22 with a crafted zip and the exact message; marker `null`, `[]`, `"x"`; version `2` rejected, `"2"`, `1.0`, missing accepted, `1.5` rejected; traversal names per EDGE-EXP-32; last-wins duplicates; backslash duplicate message; non-image under `shots/`; a whitelisted entry whose header declares more than 80 MB is rejected before any inflate; an entry that under-declares its size (1 MB of zeros declared as 1 KB) yields exactly the declared bytes and never more (`UnderDeclaredEntryIsTruncatedNotInflated`, EDGE-EXP-56); the bounded reader never returns more than `MAX_FILE_BYTES + 1` bytes from a fake stream; marker and manifest with a UTF-8 BOM are corrupt (EDGE-EXP-53); a manifest `[]` gives the corrupt message (D-EXP-13); version `1e999` refused; a 3-byte `FF D8 FF` entry is a non-image (EDGE-EXP-51); `shots/a.png` flagged as a directory (DOS `0x10`; Unix `S_IFDIR` with made-by 3) is skipped (EDGE-EXP-54); an encrypted entry, or one with an unsupported compression method, outside the whitelist is ignored (EDGE-EXP-55); running total cap; entries outside the whitelist are never inflated (a spy stream); `JsZipNames.Resolve` table |
| `Export/Package/PackageRoundTripTests` | native export then native import reproduces steps, captions and image bytes; the safe package's project opens in the store and every shot's `screenshot` resolves |
| `Export/EgressRedactionTests` | runs 04's end-to-end redaction scenario through Word, PowerPoint and the safe package (04 covers HTML and Markdown): no image in the `.docx`, `.pptx` or `.zip` contains a 4 by 4 block of the redacted region's original pixels |

Fixtures (committed under `dotnet/tests/ShotAI.Core.Tests/Golden/Export/fixtures/`, generated once by the Electron golden generator, Q-EXP-5): (1) `mixed-default`: default brand, scale 1, intro with heading and multi-line body, a section with heading and body, a section with body only, an empty section, note, caution and warning (one empty), a plain text step with heading, one without, an empty text step, an unknown `tip` callout, five shots (one zoomed at 2 with pan 0.25 and 0.75, one JPEG render, one narrower than 738, one 2924 by 1224, one with a caption containing `<&">` and Markdown specials and a newline); (2) `lfi-065`: pinned `lfi`, scale 0.65; (3) `unknown-pin-125`: `theme: 'solarpunk'`, app brand LFI, scale 1.25; (4) `byline`: created line with a byline; each fixture carries `created-line.txt`, the Electron outputs, the recorded `InlineImage` list per policy, and `.docx` and `.pptx` outputs.

**Windows only**

| Project | Class | What it proves |
|---|---|---|
| ShotAI.Platform.Tests | `Export/WicExportImageCodecTests` | PNG and JPEG sizes; `Crop` returns the exact pixels; `Resize` dimensions; `EncodeJpeg(85)` decodes; `ToStraightRgba` byte order and un-premultiply |
| ShotAI.Platform.Tests | `Export/LibavifEncoderTests` | a 1476 by 618 opaque frame encodes, `LooksLikeAvif` holds, the output is smaller than its PNG; with the DLL renamed, `Encode` returns null and the warning is logged once for ten calls |
| ShotAI.Platform.Tests | `Export/PdfRendererTests` | a real print: file starts with `%PDF-`, every page `MediaBox [0 0 612 792]`; an LFI document with the face present contains `Archivo` in a font dictionary, a shotAI one does not; a print copy containing `<script>document.body.innerText='SCRIPT RAN'</script>` leaves `document.body.innerText` (read through `ExecuteScriptAsync`, which runs even with scripts disabled) without `SCRIPT RAN`; a local `HttpListener` referenced by `<img>`, `<link>`, `<iframe>` and `<meta http-equiv="refresh">` receives no request; a `file:///` `<img>` pointing at an existing PNG does not load (Q-EXP-25; checked with the filter alone and with the CSP alone, so each layer is proven); the temp copy is removed; a renderer forced to return false leaves no destination file; a bogus `browserExecutableFolder` maps to `RuntimeMissing`; `ReputationCheckingIsOff` (INV-EXP-35: `IsReputationCheckingRequired` is `false` before the first `Navigate` and the environment's `AdditionalBrowserArguments` equal `PdfEngine.BrowserArguments`) |
| ShotAI.Platform.Tests | `Export/PackageWriterTests.OriginalsSkipLinkedReferences` | a junction inside the project pointing outside is not followed |
| ShotAI.App.Tests | `Export/ExportServiceTests` | with a fake `IExportDialogs`: Save dialog titles, filters, initial folder and file names per format; cancel returns `Canceled`; single export reveals the file; bulk methods never reveal; `RevealExportDirectoryAsync` refuses a file and a missing path; import dialog title and filter; import cancel returns null |

## 9. Acceptance criteria

**AC-EXP-1.** `ExportCssGoldenTests` passes on Linux and Windows: 52 stylesheets plus the three bad-scale cases are byte-identical to the Electron goldens, and their digests equal 3.4.

**AC-EXP-2.** The five ported suites of 8.1 (`ExportCssTests`, `ExportGeometryTests`, `ExportPaletteSourceTests`, `ExportThemeTests`, `BrandNarrowingTests`) pass on Linux with every case listed there.

**AC-EXP-3.** `ExportGoldenTests`: for every fixture, native `html`, `html-plain` and Markdown text are byte-identical to Electron's with replayed images, and equal after payload normalization with the native embedder.

**AC-EXP-4.** On the Windows x64 reference PC, the styled HTML of the 13-step reference SOP embeds `image/avif` for every shot, a 2924 px capture is embedded 1476 px wide with `width="738"`, and the total base64 is at most 200 KB (Electron: 168 KB). Manual: pasting the file's rendered content into a Freshservice KB article and saving keeps every image.

**AC-EXP-5.** With `shotai_avif.dll` removed, the styled HTML export succeeds with `image/jpeg` images and exactly one `avif: encoder init failed; falling back:` warning in the log.

**AC-EXP-6.** HTML for Word embeds PNG at 1x with size attributes. Manual: pasting it into Word 365 lays each capture out 738 px (7.69 in at 96 DPI) wide or narrower.

**AC-EXP-7.** The PDF of fixture 1 has Letter pages, step card backgrounds visible, and the 2924 px capture embedded at 2924 px (image XObject `/Width 2924`); the LFI fixture's PDF names `Archivo` among its fonts when the face is installed with the app, and the shotAI fixture's does not.

**AC-EXP-8.** A PDF render that produces nothing shows `Export failed: PDF rendering produced an empty document \u2014 printing may have failed on this system. Try the HTML or Markdown export instead.` and nothing exists at the chosen path.

**AC-EXP-9.** On a VM with the WebView2 runtime uninstalled, PDF export shows the `PdfRuntimeMissing` message; every other format still exports.

**AC-EXP-10.** `PdfRendererTests` hardening cases pass: no script runs, no request reaches the local listener, no window opens.

**AC-EXP-11.** Markdown to the project folder twice gives `<title>\<title>.md` and `<title> (1)\<title> (1).md`, each with `images\step-01-<id>.png` and links `![Screenshot for step 1](<images/step-01-<id>.png>)`.

**AC-EXP-12.** Markdown Save As naming an existing folder that holds other files creates `<name> (1)\` and leaves the existing folder's `images\` untouched; naming a prior export's folder replaces its `.md` and `images\`.

**AC-EXP-13.** The `.docx` of every fixture passes `OpenXmlValidator(Office2019)` with no errors, has `w:pgSz w:w="11906" w:h="16838"` and 1440 margins, no image wider than 5486400 EMU (576 px) at 125%, and its `DocxDump` equals Electron's. Manual: it opens in Word without a repair prompt and every step is a bordered, shaded card with a centered image.

**AC-EXP-14.** The `.pptx` of every fixture validates with no errors, has `items + 1` slides of 12192000 by 6858000 EMU, the 3.3 geometry, and a `PptxDump` equal to Electron's. Manual: it opens in PowerPoint without a repair prompt.

**AC-EXP-15.** A project with an unknown `tip` callout exports in all six formats with the step numbered as a plain text step and later numbers unshifted; no exception.

**AC-EXP-16.** A project with an unbaked redaction on step 3 of 5 fails every format with 04's refusal message for `Step 3`, no Save dialog opens, and no file or folder appears in `export\` or the bulk folder.

**AC-EXP-17.** Deleting a flattened render from disk makes the export fail with `Step {n}'s screenshot render is missing from disk (<rel>). Open it in the editor and save to re-bake the render, then export again.`.

**AC-EXP-18.** A project whose only steps are empty text steps fails every document format with `This project has nothing to export yet \u2014 add a step first.` and succeeds as a package.

**AC-EXP-19.** `ExportNamesTests` passes; an export of a project titled `CON` is named `_CON.html`, titled `  a/b:c?  ` is `abc.html`, titled with 130 characters is cut at 120.

**AC-EXP-20.** Bulk export of two projects titled `Onboarding` to one folder writes `Onboarding.html` and `Onboarding (1).html`; the folder opens once after both (06 `HomeExportFlowTests`).

**AC-EXP-21.** `ExportServiceTests` confirms the dialog titles `Export` and `Export Markdown (saved as a folder with its images)`, the filters of 2.2, the default name `{base}-plain.html` for HTML for Word, and `Choose a folder for the exports`.

**AC-EXP-22.** A styled HTML export of a 3-shot project reports `{0,3}`, `{1,3}`, `{2,3}`, `{3,3}` in order; a bulk export reports nothing.

**AC-EXP-23.** With `includeNameInReports` on and `userName` `  Jane  `, every format except HTML for Word shows `Created on <timestamp> by Jane`; with it off, or the name blank, no ` by `.

**AC-EXP-24.** With culture en-US and local time 2026-09-22 14:03:07, the created line is exactly `Created on 9/22/2026, 2:03:07 PM`.

**AC-EXP-25.** The brand precedence table of 2.3 holds for the styled HTML's accent color in each row.

**AC-EXP-26.** A safe package of fixture 1 is `<title> (shotAI package).zip` in `export\`, is revealed, contains `shots/`, `shots/step-0001.png` ... in order, then `project.json`, then `shotai-package.json`; its manifest has every shot's `annotations` `[]`, `crop` and `click` null, no `flattened`, `renderRev` 0, `markerBaked` false, and `sopBackup` null; the marker is `{"format": "shotai-package", "version": 1, "app": "shotAI", "includeOriginals": false, "exportedAt": ...}` with two-space indentation.

**AC-EXP-27.** A package with originals contains exactly the files the manifest references (screenshots and renders), and none reached through a junction.

**AC-EXP-28.** A package exported natively imports natively with identical steps and image bytes; a native package imports in Electron 1.3.0 and in the macOS app, and an Electron package imports natively (manual, during the pilot while both builds are installed).

**AC-EXP-29.** `PackageReaderTests` produces each of the eleven Electron import messages from crafted zips, plus the IMPROVEMENT cases: a `null` or array marker (`Unrecognized package format.`), a `null` or array manifest (`The package project.json is corrupt.`), and the backslash duplicate (`DuplicateFile`).

**AC-EXP-30.** A 2 MB zip whose single `shots/a.png` entry declares (truthfully) and inflates to 2 GB is rejected with `A file in the package is too large: shots/a.png`, and the process's peak working set during the import stays below 300 MB.

**AC-EXP-31.** A zip whose entries are `shots/../shots/a.png`, `../../b.png`, `./shots/c.png`, `/shots/d.png` and `shots\e.png` imports `a.png`, `c.png` and `e.png` only.

**AC-EXP-32.** Typing a caption and immediately exporting (before the caption's write completes) exports the new caption (`ExportEngineTests.WaitsForPendingWrites`).

**AC-EXP-33.** After a successful and after a failed PDF export, `export\.render\` holds no `_print-*` file; an orphan planted there is removed by the next PDF export.

**AC-EXP-34.** `Export/EgressRedactionTests` and 04's end-to-end redaction test pass for all six formats and the safe package.

**AC-EXP-35.** Leaving the project during a styled HTML export of the reference SOP leaves no new file in `export\` or the chosen folder.

**AC-EXP-36.** Exporting over a `.docx` that is open in Word shows `Export failed: Couldn't write <name>.docx because another program has it open. Close it and export again.`.

**AC-EXP-37.** File then Import Project shows `Import a shotAI project package` with the `shotAI package` filter; cancelling returns to Home with no notice.

**AC-EXP-38.** The styled HTML export of the 13-step reference SOP completes in at most 11.4 s on the x64 reference PC (Electron's measured time), measured from the Save dialog closing to the file revealed.

**AC-EXP-39.** The PDF print copy written to `export\.render\` contains the D-EXP-25 CSP meta and (for LFI) the `data:font/ttf` face; the kept `.html` export contains neither (`PrintHtmlTests`).

**AC-EXP-40.** A package whose `project.json` is `[]` fails with `Import failed: The package project.json is corrupt.` and creates no project folder; a marker or manifest starting with a UTF-8 BOM fails with the corrupt message, as in Electron.

**AC-EXP-41.** For a callout with a two-line body, the native slide's `PptxDump` equals Electron's paragraph and run structure (2.14) under the Q-EXP-21 decision.

**AC-EXP-42.** Manual: with a network trace running (Wireshark or a proxy log), export the reference SOP to PDF on a machine where WebView2 was not started before (no `%LOCALAPPDATA%` WebView2 user data folder for shotAI): no connection leaves the machine between the Save dialog closing and the file being revealed. Repeat once with the user data folder present. Any unavoidable runtime connection is recorded as a named exception in the README privacy section (Q-EXP-27). Work package: WP-D12.

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides |
|---|---|---|
| 01 Model and store | `GetProjectForReadAsync`, a new `WhenIdleAsync(projectPath, ct)` (waits for the project's queued writes; Q-EXP-11), `CreateProjectFromImportAsync(manifest, files, ct)`, `ManifestCodec.Decode(node, "Imported project")`, `ManifestCodec.Encode`, `JsJson`, `JsMath`, `JsString`, `IsoTime`, `IPathConfine` (`Confine`, `ConfineNoLinks`), `IAtomicFile`, `ReparseSafeDelete`, `ImportFile`, `CalloutKinds`, `ProjectManifest`, `ProjectStep`, `SopIntro` | the package reader producing `(ProjectManifest, ImportFile[])` with the whitelist and magic checks already applied (01 2.9.9) |
| 02 Capture | `PixelFrame`, `WicImageCodec` (extended with JPEG, clipper and high-quality resize) | nothing |
| 03 Shell | `IShellReveal.RevealInExplorer(path)`, `IShellReveal.OpenFolder(dir)`, `AppPaths.BrandFontPath()`, the own-window registry, the `ProcessFailed` log line format, shutdown disposal | the WebView2 host (the only web engine); correction to 03 2.10.2: the print copy is loaded with `Navigate(fileUri)`, not `NavigateToString` (EDGE-EXP-39) |
| 04 Editor and redaction | `IRenderGate.Resolve(dir, step, label, EgressVerb.Export)`, `RenderRefusedException`, the end-to-end redaction scenario; callers' `EnsureFlattenedAsync` | the rule that exports never flatten; the zoom crop of `reportZoom`, `reportPanX`, `reportPanY` |
| 05 Report | `DocScale.Clamp`, `DocScale.Widths`; the export menu, the package dialog, `Export failed: ` notice, the flatten-first call | `IExportService.ExportWithSaveDialogAsync(path, format, owner, progress, ct)` (05's `ExportAsync` maps to it, Q-EXP-18), `ExportPackageAsync(path, includeOriginals, ct)`, `ExportProgress`, `ExportResult.Canceled` |
| 06 Home | the row and bulk flows, `Import failed: ` notice, opening a project before export (unarchive) | `ExportWithSaveDialogAsync`, `ExportToOwnFolderAsync`, `ExportToDirectoryAsync`, `ChooseExportDirectoryAsync`, `RevealExportDirectoryAsync`, `ImportPackageAsync` |
| 07 SOP | nothing | nothing (07 uses the same gate from 04) |
| 08 Brand and theme | `BrandId`, `Brands.PinnedBrand`, `Brands.Coerce`, `BrandPalette` (light), `BrandRadii`, font family and fallbacks, `HexColor.NoHash`, the brand-narrowing tests | `ExportTheme.For`, `ExportTheme.Resolve`, `FontStackFor`, `PlainFontStackFor`, `ChipRadiusCss` |
| 10 Settings and infra | `IReportBylineSource` (`userName`, `includeNameInReports`), `IAppBrandSource` (`brand`), `ILogger` category `export`, the diagnostics bundle | log lines of 2.24; `shotai_avif_version()` and the WebView2 version for diagnostics |
| 11 Service boundary | nothing | the deletion of `projects:export`, `projects:export-to-dir`, `projects:export-to-own-folder`, `projects:choose-export-dir`, `projects:reveal-export-dir`, `projects:export-package`, `projects:import-package` and the `projects:export-progress` event, replaced by `IExportService` |
| 12 Packaging and CI | `Fonts\Archivo.ttf` and `Fonts\OFL.txt` next to the exe; `shotai_avif.dll` built from pinned libavif and libaom sources for x64 and ARM64 with their licences; the WebView2 Evergreen runtime prerequisite; CI jobs running Core tests on Linux and Windows, Platform and App tests on Windows | the golden files and fixtures; `Packaging/BrandFontShipsTests` |

## 11. Open questions and risks

**Q-EXP-1. `LibraryImport` for libavif versus "CsWin32 for every P/Invoke".** CsWin32 can only generate from Win32 metadata; libavif (and the shim) is not in it. Recommended default: the shim is bound with source-generated `LibraryImport` in `ShotAI.Platform.Export.NativeAvif`, and PLAN.md records this as the one sanctioned exception to the rule, limited to `shotai_avif.dll`.

**Q-EXP-2. AVIF encoder parameters beyond quality and speed.** Electron's output depends on squoosh's `avif_enc.cpp` (the C++ inside `@jsquash/avif` 2.1.1): YUV range, CICP values, `tune`, `sharpness`, `qualityAlpha`. Recommended default: 8-bit 4:2:0, full range, BT.709 primaries, sRGB transfer, BT.601 matrix, tune default, no alpha plane for opaque images; verify each against the `avif_enc.cpp` at the jsquash 2.1.1 tag before the shim is built, and compare file sizes on the reference SOP (target within 20 percent of 168 KB).

**Q-EXP-3. Printing from a hidden WebView2 controller.** It is not documented that `PrintToPdfAsync` works while `IsVisible = false` on a never-shown parent. Recommended default: hidden; if the Windows test shows blank or failed prints, make the popup visible at `(-32000, -32000)` with `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE` and `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.

**Q-EXP-4. PDF margins.** Electron's `margins: {marginType: 'default'}` should mean Chromium's 1 cm default, which is WebView2's default too. Recommended default: leave WebView2's margins unset; confirm by measuring the content box of the same fixture's PDF from both apps (tolerance 1 pt).

**Q-EXP-5. The Electron golden generator.** The builders are internal to `export.ts` and depend on `nativeImage`, so goldens need Electron itself. Recommended default: a small Electron PR adding a `SHOTAI_EXPORT_GOLDENS=<fixtures root>` run mode (like `SHOTAI_CAPTURE_TEST`) that, per fixture, calls the collector and the builders with the fixture's created line, records every `inlineImageForHtml` result, writes the outputs plus `.docx` and `.pptx`, and quits; run once on Windows and commit the results. The CSS goldens can be produced without Electron (Node 22 `--experimental-strip-types`, as done for 3.4).

**Q-EXP-6. Created line outside en-US.** .NET culture patterns are not CLDR's `toLocaleString` (de-DE and fr-FR differ). Recommended default: accept (D-EXP-23); the line is informational and en-US, the fleet's locale, is exact.

**Q-EXP-7. A4 versus Letter for Word.** `e93093a` kept A4 to avoid changing every export and flagged Letter for a US audience. Recommended default: parity (A4) until cutover; raise it as a product decision afterwards, since the PDF is already Letter.

**Q-EXP-8. The Markdown Save As folder rule (D-EXP-9).** Recommended default: as specified (reuse only an empty folder or a prior export, else number); confirm with the product owner, and file the same fix for macOS.

**Q-EXP-9. Undecodable renders in Office.** Electron inserts a 0 by 0 Word picture and a square PowerPoint picture. Recommended default: parity for now, logged at warning; revisit if it is ever seen (a render that does not decode is already a broken project).

**Q-EXP-10. Word and PowerPoint do not follow the brand's fonts or accent.** Headings are the docx library's blue and the deck uses Calibri. Recommended default: parity; raise a brand follow-up (08) after cutover.

**Q-EXP-11. `WhenIdleAsync` in 01.** 01 does not yet expose a per-project "queue drained" wait. Recommended default: 01 adds `Task WhenIdleAsync(string projectPath, CancellationToken ct)` completing when every write queued for that project before the call has finished (success or rollback).

**Q-EXP-12. Extended reserved names (D-EXP-11).** Changes the file name only for titles like `nul.backup` or `COM¹`. Recommended default: adopt; the golden fixtures avoid such titles.

**Q-EXP-13. Save dialog behavior parity.** Electron's Windows Save dialog details (default extension, overwrite prompt) were not verified. Recommended default: `AddExtension = true`, `OverwritePrompt = true`; check once against the Electron build.

**Q-EXP-14. Byte identity of image payloads.** WIC, libavif and Electron's Skia and libjpeg will never produce the same image bytes, so the "byte-identical" exit test is defined over text with replayed or normalized payloads plus per-image media type and dimensions (8.2). Recommended default: accept this definition in PLAN.md; the "never bigger" guard can flip the media type for tiny images, so fixtures use images where the outcome is unambiguous.

**Q-EXP-15. `%PDF-` check strength.** A truncated PDF passes it. Recommended default: also require the `%%EOF` marker in the last 1024 bytes; cheap and still fail-closed.

**Q-EXP-16. macOS reading .NET-written zips.** macOS's reader supports STORED and DEFLATE without zip64. Recommended default: never write zip64 (fail the package export with a clear message above 4 GiB, which the 600 MB import cap makes unimportable anyway); confirm a native package imports on macOS during the pilot (AC-EXP-28).

**Q-EXP-17. Spec 03's `NavigateToString` statement.** 03 2.10.2 says the PDF engine allows only "the initial `NavigateToString`". That API refuses more than 2 MB. Recommended default: 03 is corrected to "the initial navigation to the print copy's file URI" (EDGE-EXP-39).

**Q-EXP-18. Method names across 05 and 06.** 05 calls the single export `IExportService.ExportAsync(path, format, progress, ct)`; 06 calls it `ExportWithSaveDialogAsync(path, format, owner)`. Recommended default: this spec's signature (7.13) wins; 05 and 06 adopt it, with the owner window passed by the caller.

**Q-EXP-19. Resize filter for "best".** Skia's `'best'` is not reproducible; WIC offers Fant and HighQualityCubic. Recommended default: HighQualityCubic (sharper on UI text at 2x); compare legibility against Electron's output on the reference SOP, as 02 Q-CAP-16 does for capture.

**Q-EXP-20. WebView2 user data folder.** One folder per user in `%LOCALAPPDATA%\shotAI\WebView2`, never cleaned. Recommended default: keep it (it is small and reused); delete it on uninstall (12).

**Q-EXP-21. The PowerPoint callout's first paragraph.** pptxgenjs writes the callout heading run with a literal trailing CRLF inside `<a:t>` and puts the first body line in the same paragraph (2.14). How PowerPoint renders a CR LF inside `a:t` was not measured. Recommended default: open Electron's deck in PowerPoint 365 once; if the heading shows on its own line, reproduce the structure exactly (the SDK writes `\r` as `&#xD;`, which a parser reads back as CR, so compare after XML parsing); if it does not, use `a:br` between the heading run and the body and record an IMPROVEMENT.

**Q-EXP-22. `PrintToPdfStreamAsync` instead of a temp PDF.** WebView2 also offers `PrintToPdfStreamAsync(CoreWebView2PrintSettings)`, which returns the PDF as a stream and needs no temp file in the project folder. Recommended default: keep `PrintToPdfAsync` to a temp file (D-EXP-2) for the first release, since the stream variant holds the whole PDF in memory (tens of MB for full-resolution renders); revisit if the temp file ever causes a sharing violation.

**Q-EXP-23. Where `shotai_avif.dll` lives.** 7.6 puts it next to `shotAI.exe` per architecture; an alternative is a local NuGet package with `runtimes\win-x64\native` and `runtimes\win-arm64\native` assets resolved through `deps.json`. Recommended default: next to the exe (simplest for the per-architecture MSI or MSIX of 12); 12 decides.

**Q-EXP-24. Electron 42's ICU for the created line.** 2.18's locale strings were verified with Node 22.22.2 (ICU 78.2), not inside the shipping Electron 42 main process, whose ICU comes from Chromium. Recommended default: capture one created line from the shipping Electron build in en-US and compare with AC-EXP-24 before golden files are generated; the native normalization of U+202F and U+00A0 covers the known ICU difference either way.

**Q-EXP-25. `WebResourceRequested` for `file://` sub-resources.** Not confirmed on Microsoft Learn. Recommended default: rely on D-EXP-25's CSP as the primary block for the print copy's sub-resources and on the request filter for `http(s)`; the `PdfRendererTests` case with a `file:///` `<img>` confirms the combination.

**Q-EXP-26. Import leniency of `System.IO.Compression` on .NET 10.** Entries outside the whitelist are never opened, so an encrypted or unsupported-method entry there is ignored (Electron refused the whole zip, EDGE-EXP-55); CRC-32 is not validated on .NET 10 (parity with JSZip's `checkCRC32: false`) but IS validated from .NET 11, where a mismatching whitelisted entry would throw `InvalidDataException` and map to `This file is not a valid .zip package.`. Recommended default: accept both; pin the behavior with a test that runs on the shipping runtime, so a runtime upgrade that changes it is noticed.

**Q-EXP-27. WebView2 runtime egress beyond SmartScreen.** Microsoft Learn documents SmartScreen as the one WebView2 service on by default and gives `IsReputationCheckingRequired` and `--disable-features=msSmartScreenProtection` to turn it off (7.7). Whether the runtime makes any other connection during a first or later PDF export (component updater, variations, crash or diagnostic upload under the Windows diagnostic data setting, the separate Edge Update service) was not measured (UNVERIFIED). Recommended default: run AC-EXP-42 in WP-D12 before the PDF UI ships; add `--disable-background-networking` or `--disable-component-update` to `PdfEngine.BrowserArguments` only if the trace shows a connection they stop and the print still passes `PdfRendererTests`; any connection that cannot be stopped from the app (for example the machine-wide Edge Update service, which is not started by the export) is named in the README privacy section, never left silent.

**Risk R-EXP-1. The AVIF DLL is the only third-party native binary.** It needs a reproducible build for x64 and ARM64, code signing with the app, licence notices, and a security update path (libaom CVEs). Mitigation: pin versions in 12, rebuild on advisories, and the JPEG fallback keeps exports working if the DLL is ever removed.

**Risk R-EXP-2. Office file fidelity.** Open XML SDK is lower level than `docx` and `pptxgenjs`; missing a default (styles, theme, compatibility settings) shows up only in Word or PowerPoint. Mitigation: the layout dump comparison against Electron's files (8.2) and the manual open checks of AC-EXP-13 and AC-EXP-14.
