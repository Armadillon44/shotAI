# 05 Report and project detail view

> Spec for the native rewrite. Sources read: `src/renderer/project/Report.tsx` (1193 lines), `src/renderer/project/ProjectDetail.tsx` (701), `src/renderer/project/store.ts` (213), `src/renderer/project/merge.ts` (106), `src/renderer/project/report-geometry.ts` (110), `src/renderer/project/CaptureInsertModal.tsx` (220), `src/renderer/project/useCaptureTarget.ts` (185), `src/shared/doc-scale.ts` (166), `src/renderer/project/project.css` (3216; the rules at `:23-330`, `:437-440`, `:473-510`, `:560-805`, `:946-1080`, `:1127-2076`, `:2371-2400`, `:2544-2690`, `:2760-2835`). Tests: `src/renderer/project/report-geometry.test.ts` (95), `src/shared/doc-scale.test.ts` (184), `src/shared/report-matches-export.test.ts` (50), `src/renderer/project/command-bar.test.ts` (76). Supporting reads: `src/renderer/project/OverflowMenu.tsx` (106), `src/renderer/useConfirm.tsx` (91), `src/renderer/Notice.tsx` (39), `src/renderer/notice.css` (95), `src/renderer/project/sop-prepare.ts` (74), `src/renderer/editor/annotations.ts` (192, `markerColorFor`, `createMarker`), `src/renderer/project/App.tsx` (the view lifecycle `:258-299,630-650`, the `setDetailView` effect `:330-350`, the brand menu handler `:420-427`, `onRecord` `:432-458`, the detail mount `:631-641`), `src/main/main.ts` (`setDetailView` `:224-261`), `src/main/ipc.ts` (`parseStepPatch` `:194-227`, `importStep` `:435-456`), `src/main/project-store.ts` (`addTextStep` `:936-976`), `src/main/export-geometry.ts` (`zoomCropRect` `:94-114`, image ceiling notes `:115-135`), `src/main/export-css.ts` (`docCss` `:86-125`), `src/shared/ipc.ts` (`ExportFormat`, `ExportProgress` `:125-141`), `docs/NATIVE-WINDOWS-FEASIBILITY.md` (274), `dotnet/README.md` (53), `docs/native/spec/01-model-store.md`, `02-capture.md`, `03-windows-shell.md`, `04-editor-redaction.md`. Commits read (`git show`): `746b012` (report crop), `58131a8` (pan guard), `f9d5642` (#49 macOS dimensions), `4259bd2` (#45 sections, #46 centering), `2e3429d` (#40 step cards), `ec76adb` (#70 size slider), `b6ddce5` (slider fights the window), `9a8d887` (size box stepping), `62b4b7d` (nine slider defects), `861749c` (content-sized column), `701d4aa` (#77 export menu off-screen), `d50c029` (#81/#102 report width equals export), `18f3c4d` (#90/#99 unknown callout numbering), `38908cd` (#107), `77adda3` (#106), `88dd344` (#77 phase 1b). macOS (read-only, `/home/user/armadillon44/shotai_macos` at `f445bca`): `shotAI/ReportView.swift` (1485), `shotAI/DocScaleControl.swift` (186), `shotAI/PercentField.swift` (123), `Packages/ShotModel/Sources/ShotModel/ReportPresentation.swift` (140), `Packages/ShotModel/Sources/ShotModel/DocScale.swift` (149), `Packages/ShotModel/Sources/ShotModel/StepPatch.swift` (76), `shotAI/AppModel.swift` (the doc scale commit `:415-470`, the report edit methods `:1185-1460`), `Packages/ShotModel/Tests/ShotModelTests/DocScaleTests.swift` (test names and the derived-width table). Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- Several Electron strings contain U+2014 (EM DASH). This document never prints that character. Wherever it occurs in a quoted string it is written as the escape `\u2014`, and the C# literal must contain that exact character (C# accepts the same escape inside a string literal, so copying the quoted text into C# source is exact). The same convention is used for a few other characters to avoid ambiguity: `\u2026` (HORIZONTAL ELLIPSIS), `\u2192` (RIGHTWARDS ARROW), `\u2212` (MINUS SIGN), `\u00D7` (MULTIPLICATION SIGN), `\u00B7` (MIDDLE DOT), `\u2019` (RIGHT SINGLE QUOTATION MARK). Glyphs used as button faces are given by code point in 2.22.
- `round(x)` means JavaScript `Math.round`, ported as `JsMath.Round` from spec 01 (one copy, in `ShotAI.Core.Json`, R-ARCH-1; `var f = Math.Floor(x); return (x - f >= 0.5) ? f + 1 : f;`). Never C# `Math.Round`, which rounds half to even. `floor` is `Math.Floor`. `min`, `max` are `Math.Min`, `Math.Max` on doubles.
- A CSS px in the Electron window is one DIP (device-independent pixel, 1/96 inch), the same unit as a WPF device-independent unit. CSS `rem` is 16 DIP (Chromium default; the app never changes it).
- `s` in formulas is the project's document scale (`displayScale`), never a monitor DPI factor. Monitor DPI never enters any formula in this spec: all geometry is in DIP and WPF layout rounding maps it to device pixels.
- Spec numbers follow the spec index of ARCHITECTURE.md: 01 model and store, 02 capture engine, 03 windows and shell, 04 editor and redaction, 05 this spec, 06 home list and settings UI, 07 SOP generation, 08 auth, secrets and policy, 09 exports, 10 brand, settings, logging and infrastructure, 11 service boundary, 12 packaging and CI. The brand and theme owner is 10 (R-ARCH-14): the brand functions are 10's `BrandPalette.PinnedBrand`, `CoerceBrand`, `IsBrandId` and `PinIsUnrecognised` over string brand ids. Where this document says "the brand spec", it means 10.
- Classifications: **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, justified), **ELECTRON-ONLY** (disappears natively; the replacement for its intent is named).

## 1. Scope

**Owns**

| Area | Electron location |
|---|---|
| The project detail view: the sticky command bar (Back, title, step count, the SOP panel slot, the size control, Resume capturing, Export), the export menu and its placement flip, the package export dialog, the notices, the loading state, the editor overlay mount point | `src/renderer/project/ProjectDetail.tsx:203-701`, `project.css:1127-1307` |
| The per-project document size control (slider plus percent box), the preview versus committed scale, and the call that asks the shell to grow the window | `ProjectDetail.tsx:30-202`, `store.ts:22-47,192`, `App.tsx:340-350` |
| All document geometry: the detents and snap rule (`clampScale`, owned jointly with 01, which uses it in the codec), `docWidths`, `detailWindowWidth` (the formula; 03 owns the window call), the report fit (`reportFit`), and the report-equals-export invariant | `src/shared/doc-scale.ts:1-166`, `src/renderer/project/report-geometry.ts:1-110` |
| The in-app report: overview block, numbering, every step card variant, inline editors, per-step zoom and pan, the click marker overlay, reorder (buttons, number entry, drag), delete, callout conversion, the right-click merge, insert gaps and the insert menu, the empty state | `src/renderer/project/Report.tsx:1-1193`, `merge.ts:1-106`, `project.css:1308-2076,2760-2835` |
| The capture-insert modal and the capture target picker logic it uses | `CaptureInsertModal.tsx:1-220`, `useCaptureTarget.ts:1-185` |
| The open-project view state (the Zustand store): open, adopt, close, replace-from-manifest | `src/renderer/project/store.ts:1-213` |
| The policy of which UI edits are optimistic, the rollback notice text, and the image loading rules for report images (EDGE-MODEL-39) | new (fixed decision; spec 01 7.10 supplies the mechanism) |
| The report's uses of the in-app confirm and alert dialogs (the messages, labels and when they are asked); the dialog itself is 06's `IConfirmService` and `ConfirmHost` (06 7.11, ARCHITECTURE 5.4) | `src/renderer/useConfirm.tsx:1-91` |

**Does not own**

| Concern | Owner |
|---|---|
| The `project.json` schema, codec, `ProjectStore` (consumed only as `IProjectService`, R-ARCH-4), the serialized write queue, the `IProjectSession` contract (ARCHITECTURE 7.4, R-ARCH-23) and its mechanics, path confinement | 01 |
| Recording, the one-shot screenshot, `ListTargetsAsync`, what a captured step contains, the origin recovery formula as captured data | 02 |
| The main window, its sizing call (`SetDetailView`), the overlay layer, the area selection overlay, the app menu (View, Brand), the shared notice control, window registration for capture exclusion | 03 |
| The annotation editor, `StepPatch`, `StepPatchApplier`, `IStepFlattener` (`FlattenStepAsync`, `EnsureFlattenedAsync`), `AnnotationStyle.MarkerColorFor`, `AnnotationFactory.Marker`, `RenderGate` | 04 |
| The Home list, project rename, the Home capture mode picker used by "Resume capturing" (reached as `ICaptureTargetSelection`, R-ARCH-26), the confirm and alert dialog service, the global notice service | 06 |
| The SOP panel inside the command bar (`SopPanel`), its modals, SOP apply and revert | 07 |
| Export engines, the package format, where exports are written, export progress events | 09 |
| Settings, logging sinks, the brand palette tokens (colors, radii, fonts) used by every rule quoted here | 10 (R-ARCH-14) |

Specs this one touches: 01, 02, 03, 04, 06, 07, 09, 10, 11, 12.

## 2. Reference behavior (Electron)

### 2.1 The open-project store and view lifecycle

The detail view renders from one Zustand store (`store.ts:90-207`). Its fields and their exact derivations:

| Field | Value | Set by | Citation |
|---|---|---|---|
| `projectId` | opaque session id for `shot://` URLs | `open`, `applyOpened`; nulled by `close` and a failed open | `store.ts:15,111-114` |
| `projectPath` | absolute folder path | same | `:17` |
| `title`, `steps`, `intro`, `sopBackup`, `updatedAt` | copied from the manifest | `open`, `applyOpened`, `applyManifest` | `:115-123,196-205` |
| `displayScale` | `clampScale(manifest.displayScale)`; also set by `previewDisplayScale(scale)` to `clampScale(scale)` | `open`, `applyOpened`, `applyManifest`, `previewDisplayScale` | `:118,161,192,199` |
| `committedScale` | `clampScale(manifest.displayScale)`; ONLY ever from a manifest | `open`, `applyOpened`, `applyManifest` | `:39-47,119,162,200` |
| `projectTheme`, `projectPinUnrecognised` | `pinnedBrand(manifest.theme)`, `pinIsUnrecognised(manifest.theme)` (natively 10's `BrandPalette.PinnedBrand`, `PinIsUnrecognised`, R-ARCH-14) | same | `:120-121,163-164,201-202` |
| `manifestRev` | incremented on every `open`, `applyOpened`, `applyManifest` (not on `close`) | same | `:52-58,124,167,205` |
| `selectedStepId`, `selectStep` | never read by any view | | `:59,190` |
| `loading` | true during `open` | `open` | `:108,126` |
| `error` | the failed-open message unless the project is "gone" | `open`; cleared by `applyOpened`, `close`, the notice dismiss | `:128-150`, `ProjectDetail.tsx:667` |

`open(projectPath)` state machine (`store.ts:107-152`):

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| any | `open(path)` | | `loading = true`, `error = null` | Loading |
| Loading | IPC returns `{projectId, manifest}` | | set every field from the manifest, `manifestRev + 1`, `selectedStepId = null`, `loading = false` | Loaded |
| Loading | IPC throws `msg` | `/ENOENT\|no such file\|not found/i` matches `msg` | clear every field to its initial value, `loading = false`, `error = null` (a discarded project is not worth a banner) | Closed (the view falls back to Home) |
| Loading | IPC throws `msg` | no match | same clearing, `error = msg` | Closed with error |
| Loaded | `close()` | | clear every field except `loading` and `manifestRev` | Closed |
| Loaded | `applyManifest(m)` | | replace the manifest-derived fields, `manifestRev + 1` | Loaded |

`applyOpened(projectId, path, manifest)` (`:154-171`) is the capture flow's adopt (02, 06): same as a successful open plus `error = null`, no IPC. REQUIRED.

`applyManifest` checks nothing about which project the manifest belongs to (`:194-206`). A late IPC result from project A applied after the user opened project B replaces B's steps with A's (EDGE-REP-30). ELECTRON-ONLY defect; the native session-per-project design removes it (7.3).

While `loading` is true the view shows `Loading\u2026` in a `project__hint` paragraph instead of the report (`ProjectDetail.tsx:673-675`). REQUIRED.

`open` has no generation guard: `open(A)` followed quickly by `open(B)` sets whichever IPC result resolves LAST, so a slow A can overwrite B (and the first result to resolve clears `loading` while the other is still in flight) (`store.ts:107-152`, EDGE-REP-44). `open` does not clear the previous project's fields when it starts, only `loading` and `error`, so the old manifest stays in the store (hidden behind `Loading\u2026`) until the result arrives.

### 2.2 Layout skeleton (Electron as shipped)

```
section.detail (flex column, position: relative)                          project.css:1127-1132
  div.detail__bar (sticky top 0, z 5, bg --ground, bottom hairline,
                   margin 0 -2rem 1.25rem, padding .6rem 2rem .9rem,
                   flex column, gap .55rem)                               :1134-1149
    div.detail__barhead (flex, center, gap .85rem)                        :1151-1155
      button "\u2190 Back" | h2.detail__title | span.detail__count
    div.detail__baractions (flex, WRAP, center, gap .5rem)                :1157-1162
      SopPanel | SizeSlider | Resume capturing | div.export
    input[type=file] hidden
  [package dialog] [capture-insert modal]
  div.notice-stack (absolute, top .6rem, centered, z 60)                  notice.css:4-17
  p.project__hint "Loading\u2026"  |  Report
  [div.ed__overlay (editor, 04)]
```

The scroll container is `.project__body` (`project.css:150-166`): `padding: 1.75rem 2rem` with `padding-top: 0` in detail mode, `overflow-y: auto`. So the detail bar pins to the top of the scrolling body and the report scrolls beneath it. The notice stack is positioned inside `.detail`, which scrolls, so a notice scrolls away with the top of the document (EDGE-REP-31).

The report root `.rep` (`project.css:1356-1384`): `margin: 0 auto; width: 100%; max-width: calc(880px * var(--doc-scale, 1)); display: flex; flex-direction: column; gap: 0`. `width: 100%` is load-bearing: `.rep` is a flex item of `.detail`, and an auto cross-axis margin disables stretch, so without it the column sized to its content (286 px for a small project) and the slider appeared to do nothing (`861749c`, EDGE-REP-8).

Step row `.rep__step` (`:1386-1395`): a grid `2.4rem 1fr` with `gap: .85rem`, `align-items: start`, `margin-bottom: .6rem`. The card `.rep__bodywrap` (`:1622-1636`): `min-width: 0; max-width: calc(820px * var(--doc-scale, 1)); border: 1px solid var(--hair-2); border-radius: var(--radius-card); background: var(--surface-2); padding: .85rem 1rem`. Global `* { box-sizing: border-box }` (`:84-86`).

**Measured consequence at scale 1 (Electron as shipped):** frame 880, rail 38.4 + gap 13.6 = 52, card `min(820, 828) = 820`, card content `820 - 2 - 32 = 786` (the "real column" of the geometry tests), figure fit `min(816, 786) - 2 = 784`. The export at scale 1 has content column 816, card `816 - 46 = 770`, image ceiling 738 (export actual layout 736). So the shipped report card is 50 DIP wider and the figure 46 DIP wider than the export's, even though `docWidths(1).reportCol === docWidths(1).htmlCol` holds. The CSS literals `880` and `820` were never switched to the derived `docWidths` values by `d50c029` (only the TS constants and the fit cap were). See EDGE-REP-1 and INV-REP-9 for what native does.

### 2.3 Command bar

**Head row** (`ProjectDetail.tsx:439-449`, `project.css:1151-1155,1285-1306`), REQUIRED:

| Element | Behavior | Style |
|---|---|---|
| `\u2190 Back` button (`btn btn--small`) | calls `close()` (2.1); App then shows Home and asks the shell to shrink the window (03) | `.btn--small`: padding `.3rem .7rem`, font `.8rem` (`:437-440`) |
| Title `h2.detail__title` | `{title}` with `title={title}` tooltip; NOT editable here (rename lives on Home, 06); updates whenever a manifest is applied (for example after SOP generation renames the project, 07) | flex 1, min-width 0, `1.15rem`, weight 650, single line, ellipsis |
| Count `span.detail__count` | `` `${steps.length} step${steps.length === 1 ? '' : 's'}` ``, counting EVERY step (shots, text, callouts, sections) | margin-left auto, `--ink-2`, `.85rem`, tabular numerals, no wrap |

**Actions row** (`ProjectDetail.tsx:450-578`), order fixed, `flex-wrap: wrap` (the command-bar test pins `wrap`):

1. `SopPanel` (07) with `sopEnabled` from `settings.getSop().enabled` (read once on mount; a failure is ignored and leaves it false) and `onOpenSettings` (`:251-257,453`).
2. The size control (2.5), only when `projectPath` is set (`:454`).
3. `\u23FA Resume capturing` (`btn btn--small`), only when App passes `onResumeCapture`; `disabled` while a text step editor is open; title `Finish editing the text step first` when disabled, else `Resume capturing \u2014 click through more steps; they append to this project` (`:455-469`). App's handler opens the project again and starts a recording with the Home picker's current target (02, 06; `App.tsx:432-458,632-634`).
4. The Export control (2.4).

The per-project brand control is NOT in the bar (it moved to View, Brand in `701d4aa` because its width wrapped the bar; the command-bar test asserts `BrandPicker` and `Document brand` are absent). REQUIRED: no brand control in the bar (03 owns the menu).

### 2.4 Export control, export menu, package dialog

**Button** (`ProjectDetail.tsx:471-500`):

- `disabled` when `!hasShots || exporting !== null || packageBusy || textEditing || importing`, where `hasShots = steps.some(s => s.kind !== 'text')` (`:285`).
- `title`: `Add a screenshot before exporting` if `!hasShots`; else `Finish editing the text step first` if `textEditing`; else `Export as HTML, Word, PowerPoint, PDF, Markdown, or a shareable package`.
- Label: `Packaging\u2026` while `packageBusy`; else while `exporting`: `` `Exporting ${EXPORT_LABEL[exporting]}\u2026 ${done}/${total}` `` when a progress event with `total > 0` has arrived, else `` `Exporting ${EXPORT_LABEL[exporting]}\u2026` ``; else `\u2B07 Export`.
- `EXPORT_LABEL` (`:21-28`): `html` `HTML`, `html-plain` `HTML (for Word)`, `pdf` `PDF`, `markdown` `Markdown`, `docx` `Word`, `pptx` `PowerPoint`.
- Progress: `onExportProgress` subscription for the view's lifetime sets `exportProgress` (`{done, total}`); cleared to null at the end of every export (`:264-268,330`).

**Open and flip** (`:475-481`): on a click that OPENS the menu, measure the `.export` wrapper's bounding rect and set `exportMenuLeft = rect.right - EXPORT_MENU_MIN_W < EXPORT_MENU_GUTTER` with `EXPORT_MENU_MIN_W = 230`, `EXPORT_MENU_GUTTER = 8` (`:16-19`). The menu is `.export__menu` (`project.css:1215-1234`: absolute, `top: calc(100% + 4px)`, `right: 0`, z 20, `min-width: 230px`, column, padding 4, `--surface`, hairline border, `--radius-control`, shadow `0 8px 24px rgba(15, 23, 42, 0.16)`), plus `.export__menu--left` (`:1235-1238`: `left: 0; right: auto`) when flipped. The bug this fixes: a wrapped bar put Export at the left margin and the right-anchored menu rendered at about x = -100 (`701d4aa`). REQUIRED.

**Close** (`:292-308`): a `mousedown` anywhere outside the `.export` wrapper, or `Escape` (a `document` keydown listener, so it works wherever focus is), closes the menu. Choosing an item closes it. There is no backdrop: the outside mouse down that closes the menu is NOT consumed, so the same click still reaches whatever is under the pointer (for example a click on a caption both closes the menu and opens the caption editor). A second click on Export toggles the menu closed. The menu is `role="menu"` with `role="menuitem"` buttons and a `role="separator"`; there is no arrow-key navigation (Tab only). REQUIRED.

**Items** (`:506-574`), each `button.export__item` (`:1239-1255`: flex baseline, gap .5rem, padding `.5rem .6rem`, hover `--accent-tint`) with a label and a `span.export__hint` (`--ink-2`, `.8rem`), each `disabled` while `exporting !== null`:

| # | Label | Hint | Action |
|---|---|---|---|
| 1 | `HTML` | `one self-contained file \u2014 best for sharing` | `doExport('html')` |
| 2 | `Word` | `.docx \u2014 edit in Microsoft Word` | `doExport('docx')` |
| 3 | `PowerPoint` | `.pptx \u2014 one slide per step` | `doExport('pptx')` |
| 4 | `HTML (for Word)` | `paste into Word or Google Docs to edit` | `doExport('html-plain')` |
| 5 | `PDF` | `print-ready \u2014 best for printing` | `doExport('pdf')` |
| 6 | `Markdown` | `.md + images/ \u2014 for wikis & version control` | `doExport('markdown')` |
| sep | `div.export__sep` (1 px `--hair`, margin `.3rem 0`) | | |
| 7 | `Project package` | `.zip \u2014 re-open & edit in shotAI` | close menu, open the package dialog |

**`doExport(format)`** (`:310-332`), REQUIRED:

1. Return if no project. Abort any previous export's controller; create a new `AbortController`.
2. Close the menu, clear `exportErr`, `exporting = format`.
3. `flattened = await ensureFlattened(projectId, projectPath, steps, signal)` (04 owns the function); if it returned a manifest, `applyManifest(flattened)`.
4. `await projects.export(projectPath, format)` (09 owns the save dialog and the engines).
5. On an `AbortError`: return silently. On any other error: `exportErr = message`.
6. Finally: clear the controller if it is still ours, `exporting = null`, `exportProgress = null`.

The controller is aborted when the view unmounts or `projectId` changes (`:287-290`). Only the flatten stage observes it; an export already inside `projects.export` runs to completion.

**Package dialog** (`:588-629`), REQUIRED except as noted:

- An overlay `div.sop__overlay` (`role="dialog"`, `aria-label="Export a shareable project package"`); clicking the backdrop closes it; clicks inside the modal do not propagate.
- Title `Share this project` (`h3.sop__modal-title`).
- Paragraph (`p.sop__warn`): `Exports a ` **`.zip`** ` another shotAI user can import and edit. By default it ships only the redaction-baked images, so blurred or cropped-out content stays hidden.` (the `.zip` is bold).
- Checkbox row `label.pkg__opt`: a checkbox bound to `includeOriginals`, then **`Include original screenshots`** ` \u2014 lets the recipient fully re-edit (re-crop, adjust or remove blur). \u26A0 Blurred/redacted content becomes ` **`recoverable`** ` from the package. Only for people you trust with the raw captures.`
- Buttons: `Cancel` (closes) and a primary button labelled `Export with originals` when checked, else `Export (redacted)`, which runs `doExportPackage`.
- `includeOriginals` is component state that is NOT reset when the dialog reopens (EDGE-REP-22; native IMPROVEMENT resets it).
- `doExportPackage` (`:334-355`): same shape as `doExport` with `setPackageDialog(false)`, `packageBusy = true`, `ensureFlattened`, then `projects.exportPackage(projectPath, includeOriginals)`; errors go to `exportErr`.

### 2.5 The document size control (`SizeSlider`)

Markup (`ProjectDetail.tsx:127-200`, `project.css:1166-1209`): `span.detail__scale` (flex, gap .4rem, `.8rem`, `--ink-2`, no wrap) holding:

1. `label` `Size` (weight 600) with `htmlFor="doc-scale-range"`, so clicking it focuses the range (the whole `span.detail__scale` has `cursor: pointer`).
2. `input[type=range]` id `doc-scale-range`, `min 0`, `max SCALE_STEPS.length - 1` (12), `step 1`, `value = idx`, width 88 px fixed, `aria-label="Document size"`, `aria-valuetext={`${Math.round(displayScale * 100)} percent`}`.
3. `input[type=number]` `min 65`, `max 125`, `step 5`, `aria-label="Document size, percent"`, width `3.6em`, right-aligned tabular numerals, value `draft ?? Math.round(displayScale * 100)`.
4. `span` `%` (`aria-hidden`).

`idx = Math.max(0, SCALE_STEPS.indexOf(clampScale(displayScale)))` (`:53`).

State: `draft: string | null` mirrored in `draftRef` (every handler reads the ref, because `setDraft` is asynchronous and an Escape-then-blur read the stale state and committed the abandoned value, `62b4b7d`); `seqRef` a monotonic write counter (`:58-72`).

Operations (`:74-125`):

```
persist(value):
  if value === committedScale: return                  // no-op guard (62b4b7d, #77 class)
  seq = ++seqRef
  projects.setDisplayScale(projectPath, value)
    .then(m => { if (seq === seqRef) applyManifest(m) })
    .catch(() => { if (seq === seqRef) preview(committedScale) })   // committedScale as captured by the closure
apply(next):        setDraft(null); preview(next); persist(next)
step(n):            at = max(0, indexOf(clampScale(displayScale))); to = min(12, max(0, at + n)); if (to !== at) apply(SCALE_STEPS[to])
commitDraft():      held = draftRef; if held === null return
                    pct = Number(held)
                    next = (isFinite(pct) && held.trim() !== '') ? clampScale(pct / 100) : displayScale
                    setDraft(null); preview(next); persist(next)
abandonDraft():     setDraft(null); preview(committedScale)
```

Event table (REQUIRED unless noted):

| Control | Event | Guard | Action |
|---|---|---|---|
| range | `change` (drag or arrow key moves the index) | | `preview(SCALE_STEPS[index] ?? 1)` (layout previews, nothing is written) |
| range | `pointerup` | | `persist(displayScale)` |
| range | `keyup` (any key) | | `persist(displayScale)` |
| number | `change` | `raw !== '' && isLegalScale(Number(raw) / 100)` | `apply(Number(raw) / 100)` (a spinner click or a typed value that lands exactly on a detent takes effect at once, `9a8d887`) |
| number | `change` | otherwise | `setDraftBoth(raw)` (partial entry buffered; clamping per keystroke would turn `1` into 65 before `10` could be typed) |
| number | `keydown ArrowUp` / `ArrowDown` | | `preventDefault`; `step(+1)` / `step(-1)` (from the live scale, the draft is discarded) |
| number | `keydown Enter` | | `preventDefault`; `commitDraft()`; blur |
| number | `keydown Escape` | | `preventDefault`; `abandonDraft()` (clears the REF too); blur |
| number | `blur` | | `commitDraft()` (returns early when the draft ref is null, which is what makes Escape-then-blur safe) |

Typed-entry examples from `9a8d887`: `1` buffers, `10` buffers, `100` applies; `8` buffers, `85` applies; `83` buffers then Enter snaps to 85; `200` buffers then Enter clamps to 125; clearing the box then Enter or blur reverts to the live scale (it does NOT snap to 65).

Why persist on release (`:38-43`): a write per change would be up to 13 serialized writes per drag, and the window resize after each write pulled the slider out from under the pointer, so a drag from 125% ran away to 65% (`b6ddce5`). The layout previews from `displayScale`; the window follows `committedScale` only (2.6).

A failed `setDisplayScale` falls back silently to `committedScale` (no notice) (`:86-91`). Native changes this to rollback plus notice (7.5).

### 2.6 Window width linkage

App runs `window.shotai.setDetailView(!!openPath, committedScale)` in an effect keyed on `[openPath, committedScale]` (`App.tsx:340-350`), so the window is sized on entering and leaving a project and on every COMMITTED scale change, never on a preview. Main (`main.ts:239-261`, fully specified by 03 2.3.2): returns early when opening and the window is maximized; `target = detailWindowWidth(scale, workArea.width)`; `newW = open ? max(current, target) : 720` (GROW-ONLY while open, `62b4b7d`); centered, clamped to the work area. The formula (REQUIRED, `doc-scale.ts:158-166`):

```
detailWindowWidth(s, W) = max(1010, min(docWidths(s).repFrame + 130, usable))
usable = (isFinite(W) && W > 0) ? floor(W) : docWidths(s).repFrame + 130
```

### 2.7 Notices

`div.notice-stack` holds up to three notices, each a `Notice kind="error"` with a `\u00D7` dismiss button (`aria-label` and `title` `Dismiss`) (`ProjectDetail.tsx:654-672`, `Notice.tsx:18-39`):

| Slot | Text | Set by |
|---|---|---|
| `importErr` | `Import failed: {importErr}` | image import failure, `addTextStep` failure, `+ Screenshot` failure, and the `+ Capture` text-draft guard (`Finish editing the text step before capturing.`), so that guard renders as `Import failed: Finish editing the text step before capturing.` (EDGE-REP-23) |
| `exportErr` | `Export failed: {exportErr}` | `doExport`, `doExportPackage` |
| `error` | `Error: {error}` | a failed `open` (2.1); dismiss sets the store's `error = null`. Unreachable in practice: the same failed open nulls `projectPath`, App then renders Home instead of the detail view (`App.tsx:260-264`), so the message is never displayed (EDGE-REP-39) |

Each slot holds one message; a new failure replaces the previous one. Notices never auto-dismiss. Stack order top to bottom is import, export, error. Each notice is `role="status"` (`Notice.tsx:26`), so screen readers announce it politely. The stack is rendered only while at least one slot is set (`ProjectDetail.tsx:654`). Styling (`notice.css:4-47`): absolute top `.6rem`, centered, `max-width: min(92%, 680px)`, error fill `rgba(185, 28, 28, 0.6)`, white text with a text shadow, radius 8. REQUIRED (03 owns the shared control).

### 2.8 Report root and overview

`Report` returns null when `projectId` is null (`Report.tsx:691`). When `steps.length === 0` it renders the EMPTY STATE (2.17) and nothing else (the overview is not shown even if the manifest has one, EDGE-REP-19). Otherwise (`:1138-1192`): `div.rep` with `role="list"` and the CSS custom property `--doc-scale` set to `String(displayScale)` on the root, then the confirm modal portal, the overview block, insert zone 0, and for each step its row followed by insert zone `idx + 1`. React keys are `s.id`.

**Overview (intro)** (`:1149-1183`, `project.css:1308-1354`). `hasIntro = !!(intro && (intro.heading || intro.body))`. Three states, REQUIRED:

| State | Renders |
|---|---|
| editing (`editingIntro`) | `div.rep__intro` containing a `TextStepEditor` (2.12) seeded with `intro?.heading ?? ''` and `intro?.body ?? ''`; Save calls `saveIntro(h, b)`, Cancel sets `editingIntro = false` |
| `hasIntro` | `div.rep__intro role="note"`: an actions row (right-aligned, gap .4rem) with `Edit overview` (`btn btn--small`, sets `editingIntro = true`) and `Remove` (`btn btn--small btn--danger`, calls `saveIntro('', '')`, NO confirmation); then `h2.rep__intro-h` with the heading if non-empty (`1.1rem`); then `p.rep__intro-b` with the body if non-empty (`--ink-2`, `pre-wrap`) |
| neither | `button.rep__addintro` `+ Add an overview` (block, dashed hairline border, transparent, `--ink-3`, left-aligned, `max-width: calc(880px * var(--doc-scale, 1))`, width 100%; hover: accent border, `--ink-2`) which sets `editingIntro = true` |

`.rep__intro`: width 100%, margin `0 0 .4rem`, padding `.9rem 1.1rem`, hairline border with a 4 px `--accent` left border, `--radius-card`, `--surface-2`.

`saveIntro(heading, body)` (`:659-671`): `h = heading.trim()`, `b = body.trim()`; call `projects.setIntro(projectPath, (h || b) ? {heading: h, body: b} : null)`; on success `applyManifest` and close the editor; on failure do nothing (the editor stays open). The store's `setProjectIntro` sets `introEditedByUser = true` for a non-null intro and deletes the key for null, and always writes (01 2.9.3). JS `trim` semantics (`JsString.Trim`, 01).

The intro editor state is independent of the text-step editor latch: it does not count as `textEditing` for the command bar (EDGE-REP-20).

### 2.9 Numbering and the rail

`isCalloutStep(s) = s.kind === 'text' && isCalloutKind(s.callout)` (`Report.tsx:34-40`); `isCalloutKind` is the exact four-literal test of 01 2.12 (`note`, `caution`, `warning`, `section`). Numbers (`:465-471`): walk `steps` in order, `n = 0`; for every step that is NOT a callout step, `++n` is its display number. `numberedTotal` = count of numbered steps. An unknown callout value (`tip`, `NOTE`, ...) is numbered like a plain text step (#90, `18f3c4d`). A section is a callout and is not numbered. REQUIRED.

The rail (`:823-879`, `project.css:1414-1492,1864-1895`) is the first grid column, a flex column centered with gap `.3rem`, holding the badge on top and the drag grip below it (the CSS comment at `:1413` saying the grip is above is stale; the TSX order wins):

| Step | Badge |
|---|---|
| callout `section` | none (the grip still renders) |
| callout `note`, `caution`, `warning` | `span.rep__num rep__num--callout rep__num--{kind}` with the glyph `CALLOUT_GLYPH[kind]` (`\u2139`, `\u26A0`, `\u2501`), `title` `` `${callout} callout \u2014 not a numbered step` ``, `aria-hidden`; tinted `--{note,caut,warn}-bg` fill, `-fg` text, 1 px `-bd` border; cursor default; glyph `1.05rem`; hover pinned to the same colors |
| numbered, number editor open for it | `InlineInput type="number"` class `rep__num-input` (2.12), initial `String(displayNums.get(id) ?? '')`, `min 1`, `max numberedTotal`, spinners hidden; commit `commitNum(idx, v)` (2.14); cancel closes |
| numbered | `button.rep__num` (plus `rep__num--text` when `kind === 'text'`, which paints `--ink-2` instead of `--accent`, hover unchanged), text = display number, `title` `Click to set this step's position`, click opens the number editor |

Badge box: 2rem square, `margin-top: .85rem` (aligns with the card's first line), radius `--radius-chip`, `--accent` fill, `--on-accent` text, weight 650, `.95rem`, tabular numerals; hover `--accent-press`.

Grip: `button.rep__grip` `\u283F` (BRAILLE PATTERN DOTS-123456), `draggable`, `aria-label` and `title` `Drag to reorder`, color `--control-bd` (hover `--ink-2`), cursor grab/grabbing, `.85rem`, `user-select: none`. Drag behavior in 2.14.

### 2.10 Step card variants

Row classes (`Report.tsx:937-942`): `rep__step`, plus `rep__step--text` when `kind === 'text'`, plus `rep__step--co rep__step--co-{callout}` when `isCalloutStep && callout`, plus `rep__step--dragging` when this row is the drag source (opacity .45), plus `rep__step--over` when it is the current drag-over target and not the source (a 2 px `--accent` line at `top: -0.5rem` spanning the row). `role="listitem"`.

Card tint (`project.css:1638-1664,1979-1983`): `co-note`, `co-caution`, `co-warning` repaint `.rep__bodywrap` with the kind's bg, border and fg tokens and flatten the inner `.rep__callout` box (no margin, padding, border, radius, background; inherited color; no hover ring). `co-section` removes the card frame (`background: none; border: 0; padding: .4rem 0 0`).

Rendering branches inside `.rep__bodywrap` (`Report.tsx:959-1133`), in this order, REQUIRED:

1. **Text step whose editor is open** (`kind === 'text' && editingTextId === id`): the `TextStepEditor` (2.12) seeded with `heading ?? ''`, `body ?? ''`; Save calls `saveText`, Cancel calls `cancelText`.
2. **Section** (`callout === 'section'`): `div.rep__calloutrow` (flex, gap .6rem, top-aligned) holding a clickable `div.rep__section rep__section--clickable` (`title` `Click to edit this section divider`, click opens the text editor) and the actions (`div.rep__actions`, 2.13). Inside: `hr.rep__section-rule` (1 px `--hair` top border, margin `.2rem 0 .6rem`) ABOVE the heading; `h3.rep__section-h` with the heading if non-empty (`1.15rem`, weight 700; hover turns it `--accent`); then `p.rep__section-b` with the body if non-empty (`--ink-2`, line height 1.55, `pre-wrap`), else, only when the heading is also empty, `p.rep__section-b rep__callout-empty` `Empty \u2014 click to add a section heading.` (italic, opacity .7).
3. **Known callout** (`isCalloutKind(callout)`, so note, caution or warning here): `div.rep__calloutrow` holding a clickable `div.rep__callout rep__callout--clickable rep__callout--{kind}` (`title` `Click to edit`) and the actions. Inside: `strong.rep__callout-h` (block, margin-bottom .25rem) with the heading if non-empty; then `p.rep__callout-b` with the body if non-empty (`pre-wrap`, line height 1.55), else, only when the heading is empty, `p.rep__callout-b rep__callout-empty` `Empty \u2014 click to add text.`
4. **Plain text step** (any other text step, including an unknown callout): `div.rep__caprow` (flex, center, gap .6rem, min-height 2rem) holding the primary line and the actions. Primary line: the heading as `h3.rep__textheading rep__textheading--edit` (`title` `Click to edit this text step`, `1.05rem`, weight 650, margin `.2rem 0 .4rem`) if non-empty; else the body as `p.rep__textbody rep__textbody--edit` (`title` `Click to edit`, `--ink-2`, line height 1.6, `pre-wrap`) if non-empty; else `button.rep__addline` `+ Add text` (`--ink-3`, `.82rem`, hover `--accent`). Every one of them opens the text editor. Below the row, ONLY when BOTH heading and body are non-empty, the body again as `p.rep__textbody rep__textbody--edit` (`title` `Click to edit`). Hover on heading and body turns them `--accent`.
5. **Shot step** (`kind !== 'text'`), top to bottom:
   - `div.rep__caprow`: the caption as `h3.rep__caption` (`title` `Click to edit caption`, `1rem`, weight 600, margin `.2rem 0 .6rem`, padding `.1rem .3rem`, margin-left `-.3rem`, cursor text) showing `caption`, or `span.rep__caption-empty` `Add a caption\u2026` (italic, `--ink-3`, weight 500) when the caption is empty; click opens the caption editor (`InlineInput` class `rep__cap-input`, placeholder `Caption\u2026`, 2.12). Then the actions.
   - The merge suggestion (2.16) when `click?.button === 'right' && canMergeInto(steps, idx)`.
   - The figure (2.11).
   - The instructions: when the body editor is open for it, `InlineTextarea` class `rep__body-input`, placeholder `Write the instruction for this screenshot\u2026` (2.12); else if `body` non-empty, `p.rep__shotbody` (`title` `Click to edit instructions`, margin-top `.65rem`, padding-left `.75rem`, a 3 px `--accent-soft` left border, `--ink`, `1.05rem`, line height 1.6, `pre-wrap`, cursor text) showing the body; else `button.rep__addline` `+ Add instructions`. Clicks open the body editor.
   - The window line, when `step.window` is truthy: `p.rep__meta` (`--ink-3`, `.8rem`, single line, ellipsis) showing `window.app` followed by `` ` \u2014 ${window.title}` `` when the title is non-empty. An empty `app` with a title renders ` \u2014 title` with a leading space (EDGE-REP-25).

### 2.11 Shot figure: fit, zoom, pan, marker, controls

`StepFigure` (`Report.tsx:42-252`, `project.css:1679-1813`).

**Image source** (`:66-71`): if `step.flattened` is truthy, `shot://<projectId>/<flattened>?v=<renderRev ?? 0>`; else `shot://<projectId>/<screenshot>`. The query string changes only when the render is rebuilt, so a re-save refreshes the image and a zoom change does not reload it. `shotUrl` URI-encodes each `/`-separated segment (`store.ts:210-213`). The protocol serves only `.png`, `.jpg`, `.jpeg` from inside the project (01 2.9.10). `loading="lazy"`, `draggable={false}`, `alt={caption}`. REQUIRED intent (image from inside the project, flattened preferred, reload keyed on renderRev); the URL mechanics are ELECTRON-ONLY.

**Natural size** `dims = {w: naturalWidth, h: naturalHeight}` is set on the image `load` event (`:191-196`). `dims` is never reset when `src` changes (a re-save bumps `renderRev`, a crop changes the render's size), so until the new image loads the fit and the marker use the PREVIOUS image's size (EDGE-REP-50). Before it loads, the image renders with `max-width: 816px; max-height: 600px` and the wrap has no explicit size.

**Measured width** `availW` (`:104-116`): the figure element's `clientWidth` (or null when 0), read on mount and on every `ResizeObserver` callback.

**Fit** `fit = reportFit(dims, availW, zoom, docScale)` (`report-geometry.ts:57-98`), exactly:

```
reportFit(dims, availW, zoom = 1, scale = 1):
  if dims is null or dims.w <= 0 or dims.h <= 0: return {baseW: 0, baseH: 0, wrapW: 0, wrapH: 0}
  s       = clampScale(scale)
  capW    = round(816 * s)                      // REPORT_BASE_W * s
  capH    = round(600 * s)                      // REPORT_BASE_H * s
  budgetW = max(1, min(capW, availW ?? capW) - 2 * 1)
  budgetH = max(1, capH - 2 * 1)
  fitScale = min(budgetW / dims.w, budgetH / dims.h, 1)
  baseW   = floor(dims.w * fitScale)
  baseH   = floor(dims.h * fitScale)
  boxScale = min(zoom, 1)
  wrapW   = round(baseW * boxScale) + 2
  wrapH   = round(baseH * boxScale) + 2
```

`wrapContentSlack(fit) = {x: fit.wrapW - 2 - fit.baseW, y: fit.wrapH - 2 - fit.baseH}` (`:105-110`) must never be negative. The name `fitScale` is deliberate: the project scale must never become an image scale.

**DOM** (`:167-250`): `figure.rep__figure` (width 100%, `text-align: center`, so a narrow capture is centered, #46) contains `div.rep__figbox` (relative, inline-block, max-width 100%; the positioning context of the controls) which contains `div.rep__imgwrap` (inline-block, max-width 100%, `line-height: 0`, 1 px `--hair` border, `--radius-figure`, `overflow: hidden`, `--surface` background; explicit `width: wrapW; height: wrapH` once `dims` is known; class `rep__imgwrap--pan` with cursor grab/grabbing when `zoom > 1`) which contains `div.rep__imginner` (relative, inline-block, `line-height: 0`) holding the `img.rep__img` (block; `width: baseW * zoom; height: baseH * zoom` once `dims` is known) and the marker. So the box stays at the zoom-1 fit and the image overflows it for zoom above 1; `overflow: hidden` means the mouse wheel scrolls the page, not the image.

**Zoom** (`:30-31,76,473-483`): `zoom = max(ZOOM_MIN, step.reportZoom ?? 1)` with `ZOOM_MIN = 1`, `ZOOM_MAX = 4`. Read path floors (an older build allowed 0.5); it does NOT cap, so a stored 6 renders at 6 (EDGE-REP-11). `setZoom(step, next)`: `z = max(1, min(4, next))`, then `updateStep(projectPath, id, {reportZoom: z})`, `applyManifest`, errors ignored. The IPC layer clamps again to `[1, 6]` (`ipc.ts:216`).

**Pan restore** (`:120-130`): runs only once `dims` is known; an effect keyed on `[dims, zoom, availW, docScale, reportPanX, reportPanY]` sets `scrollLeft = (scrollWidth - clientWidth) * (reportPanX ?? 0.5)` and `scrollTop = (scrollHeight - clientHeight) * (reportPanY ?? 0.5)`. `docScale` is in the key because on a plateau where the measured width does not change the scale can still change the scrollable range (`62b4b7d`). The browser clamps `scrollLeft` to `[0, range]`, so a stored fraction outside `[0, 1]` behaves as clamped, and a non-number fraction makes the product NaN, which Chromium treats as 0 (EDGE-REP-47). A non-number `reportZoom` makes `zoom` NaN and the figure's sizes NaN (EDGE-REP-47).

**Pan drag** (`:132-165`), state machine, REQUIRED:

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | mousedown on the wrap | `zoom <= 1` | nothing (gate on zoom FIRST: phantom overflow at zoom 1 would otherwise let a stray drag overwrite a framing saved at a higher zoom, `58131a8`) | Idle |
| Idle | mousedown | `scrollWidth <= clientWidth && scrollHeight <= clientHeight` | nothing | Idle |
| Idle | mousedown | otherwise | `preventDefault`; record `{x, y, scrollLeft, scrollTop}`; listen on window | Panning |
| Panning | window mousemove | | `scrollLeft = sl - (clientX - x)`, `scrollTop = st - (clientY - y)` (the browser clamps to `[0, range]`) | Panning |
| Panning | window mouseup (anywhere) | | stop listening; `reframe(step, rangeX > 0 ? scrollLeft / rangeX : 0.5, rangeY > 0 ? scrollTop / rangeY : 0.5)` | Idle |

`reframe` (`:485-497`): `updateStep(projectPath, id, {reportPanX, reportPanY})`, `applyManifest`, errors ignored. IPC clamps both to `[0, 1]` (`ipc.ts:217-218`). A click without movement at zoom above 1 still writes (EDGE-REP-12). `onMouseDown` has no button check, so a right or middle drag at zoom above 1 also pans and persists (EDGE-REP-51); its `preventDefault` also keeps focus where it was, so an open inline editor does not blur when a pan starts.

Zoom and pan are not display-only: every export crops to the framing when `reportZoom > 1` (`export-geometry.ts:94-114`, 09). They are the "what persists" of this feature: `reportZoom`, `reportPanX`, `reportPanY` on the step, nothing else. Reset zoom writes `reportZoom: 1` and leaves the pan fractions in the manifest.

**Marker overlay** (`:87-102,198-204`, `project.css:1798-1808`): drawn only when `dims` is known, `step.click` is set, `step.markerBaked` is falsy, and `dims.w > 0 && dims.h > 0`. `offX = (flattened && crop) ? crop.x : 0`, same for y. `fx = (click.image.x - offX) / dims.w`, `fy = (click.image.y - offY) / dims.h`; drawn only when both are in `[0, 1]`, at `left: fx * 100%`, `top: fy * 100%` of the image inner box (so it scales and pans with the image). A 22 px circle centered on the point (`margin: -11px 0 0 -11px`), `border: 2.5px solid <color>`, `background: <color> + '2e'` (hex alpha 0x2E, 18%), `box-shadow: 0 0 0 2px rgba(255, 255, 255, 0.7)`, `pointer-events: none`, `aria-hidden`. `<color> = markerColorFor(step) = step.markerColor ?? (click.button === 'right' ? '#2563eb' : '#e11d48')` (`annotations.ts:45-47`). The CSS class defaults (`#ef4444` border, `rgba(239, 68, 68, 0.18)` fill) apply only when the inline color is invalid CSS (EDGE-REP-13). The marker colors are deliberately not brand tokens: they must match the rings baked into PNGs on disk (`project.css:1791-1797`). REQUIRED.

**Floating controls** (`:207-248`, `project.css:1738-1790`): `div.rep__imgctl` absolutely positioned at `top: 8px; right: 8px` of the figbox, a column with gap 4, opacity .3 at rest and `pointer-events: none` at rest; opacity 1 and pointer events on while the figbox is hovered or the strip has focus within (the pointer-events rule fixes a stale hit rectangle swallowing clicks after a delete reflowed the list, B4). Buttons 1.9rem square, radius `--radius-control-sm`, background `rgba(17, 24, 39, 0.62)` (hover `0.88`), `--on-accent` glyphs at `1.05rem`, disabled opacity .4:

| Button | Face | `title` and `aria-label` | Disabled when | Click |
|---|---|---|---|---|
| zoom in | `+` | `Zoom in` | never (EDGE-REP-14) | `onZoom(zoom * 1.25)` |
| zoom out | `\u2212` | `Zoom out` | `zoom <= 1` | `onZoom(zoom / 1.25)` |
| reset | `\u21BA` | `Reset zoom` | `zoom === 1` | `onZoom(1)` |
| delete (6 px extra top margin; hover `--danger` at 90%) | U+1F5D1 (WASTEBASKET) | `Delete step` | never | `del(step)` (2.15) |

Zoom sequence from 1 by repeated zoom-in: 1.25, 1.5625, 1.953125, 2.44140625, 3.0517578125, 3.814697265625, then 4 (clamped). Zoom-out from 4: 3.2, 2.56, 2.048, 1.6384, 1.31072, 1.0485760000000002 (the IEEE result of the repeated division, not 1.048576), then 1.

### 2.12 Inline editors and the edit latches

Three editor components, REQUIRED:

**`InlineInput`** (single line; used for the caption and the number badge) (`Report.tsx:301-343`): local `value` seeded from `initial`; `autoFocus`; on focus selects all text; `Enter` commits, `Escape` cancels, `blur` commits; a `done` flag makes the first of these win so Enter followed by the blur it causes commits once. `type="number"` adds `min=1` and `max`. For a number input Chromium's ArrowUp and ArrowDown step the value by 1 within `[1, max]` (hidden spinners, `project.css:1486-1490`).

**`InlineTextarea`** (multi-line; the shot instructions) (`:347-383`): `rows=3`; `autoFocus`; no select-all; `Escape` cancels; `Ctrl+Enter` (or Meta+Enter) commits; plain `Enter` inserts a newline; `blur` commits; same `done` guard.

**`TextStepEditor`** (text, callout and section steps, and the overview) (`:254-297`, `project.css:2030-2075`): a column (gap .5rem) with a heading input (`placeholder="Heading (optional)"`, `autoFocus`, `1rem` weight 600), a body textarea (`placeholder="Text\u2026"`, `rows=3`, vertical resize, min height 3.25rem), and a row with `Save` (`btn btn--small btn--primary`) and `Cancel` (`btn btn--small`). Values are seeded once at mount and are NOT reseeded if the manifest changes while open. No keyboard shortcuts: Enter in the heading does nothing, Escape does nothing, blur does NOT commit. Save and Cancel are the only exits.

**Latches** (`:413-428`): `editingTextId`, `editingIntro`, `editingCapId`, `editingNumId`, `editingBodyId`, `insertMenuAt`, `dragIdx`, `dragOverIdx`, `merging`, plus the refs `busyRef` and `freshTextIdRef`.

Text editor latch state machine (`:430-461,626-643,676-689`), REQUIRED:

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Closed | parent passes `autoEditId = id` (after a text or callout step was added) | | `editingTextId = id`; `fresh = id`; call `onAutoEditConsumed()` so the parent resets the trigger (single shot: a stale trigger re-opened and re-locked the editor, B4) | Open(id, fresh) |
| any | `openTextEdit(step)` (Edit button, heading, body, callout box, section, `+ Add text`) | `editingTextId === step.id` | nothing | unchanged |
| any | `openTextEdit(step)` | otherwise | `fresh = null`; `editingTextId = step.id` (switches away from any open editor; its draft is dropped; never blocked by a lingering latch) | Open(step.id) |
| Open(id) | Save `(h, b)` | | `updateStep(id, {heading: h, body: b})` (NO equality guard; always writes); on success `applyManifest`, `if (fresh === id) fresh = null`, `editingTextId = null`; on failure nothing (editor stays open) | Closed on success |
| Open(id) | Cancel | | `wasFresh = (fresh === id)`; `fresh = null`; `editingTextId = null`; if `wasFresh && !step.heading && !step.body && !step.callout`: `deleteStep(id)` and `applyManifest`, errors ignored (a fresh blank PLAIN step is removed; a callout or section is kept because its box is meaningful even empty) | Closed |
| any | manifest replaced (`manifestRev` or `steps` changed) | `editingTextId` names a step no longer in `steps` | `editingTextId = null` | Closed |
| any | manifest replaced | `fresh` names a vanished step | `fresh = null` | unchanged |
| any | manifest replaced | | `editingCapId = editingNumId = editingBodyId = null`; `insertMenuAt = null` | unchanged |

The reconciliation only DROPS a latch whose step vanished; it never re-asserts one (an earlier version re-opened the fresh draft here and locked the report permanently, because a callout's id survives delete, SOP generation and reorder, B4, `:446-451`). `onEditingChange(editingTextId !== null)` informs the parent (`:442-444`), which sets `textEditing`.

Caption, number and body editors each have one latch; opening one of them is always a click elsewhere, whose blur commits and closes the previous one, so at most one of the three is open at a time. The text editor latch is independent: a text editor can stay open while a caption is edited.

`saveCaption(step, caption)` (`:537-547`): close the caption editor; return if `caption === step.caption` (exact, not trimmed); else `updateStep(id, {caption})`, `applyManifest`, errors ignored. The store sets `captionEditedByUser = true` for any string caption, including `''` (04 INV-EDIT-13).

`saveBody(step, body)` (`:551-561`): close the body editor; return if `body === (step.body ?? '')`; else `updateStep(id, {body})`, `applyManifest`, errors ignored. A body patch carries no annotations or crop, so the render is untouched.

### 2.13 Per-step actions: Edit and the overflow menu

`div.rep__ctl` (inline-flex, gap .3rem) inside `div.rep__actions` (margin-left auto, flex, gap .4rem) holds (`Report.tsx:884-934`):

1. `Edit` (`btn btn--small`, `title="Edit"`): text steps call `openTextEdit(s)`; shot steps call `onEditStep(s)`, which opens the annotation editor overlay (`ProjectDetail.tsx:358-361,685-698`, 04).
2. `OverflowMenu` with trigger `\u22EF` (`btn btn--small btn--ghost btn--icon`, `title="More step actions"`, `aria-haspopup="menu"`, `aria-expanded`).

Menu items, in order:

| Item | Shown for | Label | Disabled | Action |
|---|---|---|---|---|
| 1 | all | `\u2191 Move up` | `idx === 0` | `move(idx, -1)` |
| 2 | all | `\u2193 Move down` | `idx === steps.length - 1` | `move(idx, +1)` |
| sep | text steps | | | |
| 3 to 5 | text steps, for `k` in `note`, `caution`, `warning` with `k !== cur`, where `cur = s.callout ?? null` (the RAW value) | `` cur ? `Change to ${k}` : `Make ${k} callout` `` | | `setCallout(s, k)` |
| 6 | text steps with `cur !== 'section'` | `cur ? 'Change to section divider' : 'Make section divider'` | | `setCallout(s, 'section')` |
| 7 | text steps with `cur` truthy | `Convert to plain text` | | `setCallout(s, null)` |
| sep | text steps | | | |
| 8 | text steps | `Delete step` (danger) | | `del(s)` |

Because `cur` is raw, a step with an unknown callout (rendered as plain numbered text) shows the "Change to" labels and `Convert to plain text` (`18f3c4d` deliberately left the toggle on the raw value). REQUIRED.

`setCallout(step, callout)` (`:647-656`): return if `(step.callout ?? null) === callout`; else `updateStep(id, {callout})`, `applyManifest`, errors ignored. `callout: null` removes the key (01 EDGE-MODEL-42). Converting changes numbering of every later step.

**`OverflowMenu`** (`OverflowMenu.tsx:18-106`, `project.css:995-1070`): on open, `estHeight = items.length * 34 + 16` (separators count as items); `below = window.innerHeight - rect.bottom`; open upward iff `below < estHeight && rect.top > below`. The popover (`.menu__pop`: absolute, `top: calc(100% + 4px)`, `right: 0`, z 51, `min-width: 172px`, padding .3rem, hairline border, `--radius-control`, `--menu-shadow`, column, gap 1; upward variant `top: auto; bottom: calc(100% + 4px)`) sits over a full-window transparent backdrop (z 50) that closes it on click; the backdrop CONSUMES that click, so clicking another step's control while a step menu is open only closes the menu (the opposite of the export menu, 2.4). `Escape` closes it. An item click closes the menu first, then runs the action. Items: `menu__item` (padding `.42rem .6rem`, `--fs-body`, no wrap, hover `--accent-tint`, disabled opacity .5), danger items `--danger-ink` with `--danger-tint` hover; separators 1 px `--hair-2`, margin `.25rem .2rem`. REQUIRED.

Merge is NOT in the overflow menu; it is offered only by the suggestion banner (2.16) (`:881-883`).

### 2.14 Reorder: buttons, number entry, drag and drop

`reorderTo(from, to)` (`Report.tsx:501-515`):

```
dest = max(0, min(to, steps.length - 1))
if busyRef or no project or from === dest: return          // busyRef: a click during a write is DROPPED
ids = steps.map(id); moved = ids.splice(from, 1); ids.splice(dest, 0, moved)
busyRef = true
try { applyManifest(await projects.reorderSteps(projectPath, ids)) } catch {} finally { busyRef = false }
```

`move(idx, dir) = reorderTo(idx, idx + dir)` (`:517`).

**Number entry** `commitNum(idx, raw)` (`:519-535`): close the number editor; `n = parseInt(raw, 10)`; return if not finite; `target = max(1, min(n, numberedTotal))`; `absTarget` = the array index of the `target`-th non-callout step (fallback `steps.length - 1`); `reorderTo(idx, absTarget)`. Worked examples: `[A1, B2, C3]` move A to 3 gives `[B, C, A]`; `[A1, note, B2, C3]` move A to 2 gives `[note, B, A, C]` (A is number 2); move C to 1 gives `[C, A, note, B]`; entering the current number is a no-op; `0` or negative clamps to 1; `2.7` parses as 2; empty is ignored. REQUIRED.

**Drag and drop** (`:563-572,856-876,947-956`), state machine, REQUIRED:

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | `dragstart` on a grip | | `dragIdx = idx`; `effectAllowed = 'move'`; `setData('text/plain', String(idx))` (errors ignored) | Dragging(from) |
| Dragging(from) | `dragover` on a row `i` | | `preventDefault`; `dropEffect = 'move'`; `dragOverIdx = i` | Over(from, i) |
| any | `dragover` on a row | `dragIdx === null` (an external drag, for example a file) | nothing, so the drop is not allowed | unchanged |
| Over(from, i) | `drop` on row `i` | `from !== i` | `reorderTo(from, from < i ? i - 1 : i)` (drop means "insert before the target row", the line is drawn above it) | Idle |
| any | `drop` | `from === i` or no drag | nothing | Idle |
| any | `dragend` | | `dragIdx = dragOverIdx = null` | Idle |

Insert zones are not drop targets, so a step cannot be dragged to the LAST position (dropping on the last row puts it just before it) (EDGE-REP-16). Chromium auto-scrolls the report while dragging near the viewport's top or bottom edge (native parity needs explicit code, 7.12).

### 2.15 Delete

`del(step)` (`Report.tsx:579-597`): return if `busyRef` or no project; `await confirm('Delete this step? (its image file stays on disk)', {confirmLabel: 'Delete', danger: true})`; return on Cancel; `busyRef = true`; `deleteStep(projectPath, id)` (files stay on disk, 01 2.9.12); `applyManifest`; errors ignored; `busyRef = false`. The same message is used for text steps, which have no image (EDGE-REP-18). Shot steps delete from the floating control over the image; text steps from the overflow menu. REQUIRED.

**Confirm and alert** (`useConfirm.tsx:28-91`): an in-page modal (`sop__overlay` with `role="dialog"`, `aria-modal`, `aria-label="Confirm"`) portaled to `document.body`; a `.confirm` card (max width 420, padding `1.25rem 1.5rem`) with the message (`1rem`, line height 1.5) and a right-aligned row: `Cancel` (`btn`) unless alert-only, then the confirm button (`btn btn--danger` when `danger`, else `btn btn--primary`), labelled `confirmLabel ?? 'OK'`, with `autoFocus` (so Enter confirms). Escape (keydown inside the overlay, so only while focus is inside it) resolves false. Alerts resolve on OK only. The backdrop has no click handler. The hook holds ONE pending dialog: a second `confirm` or `alert` while one is open replaces it and the first promise never settles (`useConfirm.tsx:35-54`); unreachable today because the dialog is modal, but native must not port it (the native dialog service queues or rejects a second request). The portal is rendered only by the non-empty report root (`Report.tsx:1148`); the empty state (2.17) renders no `confirmModal`, which is harmless because nothing there can ask for one. The reason for an in-page modal was that native `window.confirm` stole keyboard focus on this Electron build (B4): ELECTRON-ONLY reason; the in-window modal is kept natively for a different reason (7.13).

### 2.16 Right-click merge

`canMergeInto(steps, idx)` (`merge.ts:26-37`): `steps[idx]` and `steps[idx + 1]` both exist, neither has `kind === 'text'`, and both have a truthy `screenshot`.

**Suggestion banner** (`Report.tsx:1077-1092`, `project.css:2792-2814`), shown on a shot step when `click?.button === 'right' && canMergeInto(steps, idx)`, between the caption row and the figure: `div.rep__merge-suggest` (flex, gap .75rem, margin-top .55rem, padding `.5rem .7rem`, `--focus-ring` border with a 3 px `--accent` left border, `--accent-tint`, `--accent-ink`, `.86rem`) with the text `This right-click likely opened a menu \u2014 merge it into the next step (the menu selection) to keep one screenshot showing the menu.` and a `btn btn--small` labelled `Merging\u2026` while this step is merging, else `Merge \u2193`, disabled while ANY merge is in flight.

`merge(idx)` (`Report.tsx:603-618`): return if `busyRef`, no project, or `!canMergeInto`; `drop = steps[idx]`, `keep = steps[idx + 1]`; `busyRef = true`; `merging = drop.id`; `applyManifest(await mergeStepInto(projectId, projectPath, keep, drop))`; on error `alert('Could not merge these steps: ' + message)`; finally `merging = null`, `busyRef = false`.

`mergeStepInto(projectId, projectPath, keep, drop)` (`merge.ts:46-106`), REQUIRED:

1. Load keep's RAW `screenshot` (never its `flattened` render); `natW`, `natH` = its natural size. A load failure throws `Could not load a screenshot to flatten (<url>).` (`sop-prepare.ts:10-20`, message at `:17`).
2. If `drop.click`: when `keep.click` exists, `ks = keep.click.imageScale ?? 1`; `originX = keep.click.global.x - keep.click.image.x / ks`; `originY` likewise; `mx = (drop.click.global.x - originX) * ks`; `my = (drop.click.global.y - originY) * ks`. Otherwise `mx = drop.click.image.x`, `my = drop.click.image.y`. Push `createMarker(clamp(mx, 0, max(0, natW - 1)), clamp(my, 0, max(0, natH - 1)), markerColorFor(drop))`, which is `{id: <random UUID>, type: 'marker', x, y, color}` (`annotations.ts:175-177`).
3. `annotations = [...keep.annotations, ...markers]`.
4. `keepMarker = keep.click ? {x: keep.click.image.x, y: keep.click.image.y, color: markerColorFor(keep)} : null`; `png = flattenToPng(img, annotations, keep.crop, keepMarker)` (04). Keep's own redactions and crop are therefore re-baked.
5. `projects.mergeSteps(projectPath, keep.id, drop.id, {annotations, caption: joinText(drop.caption, keep.caption, ' \u2192 '), body: joinText(drop.body, keep.body, '\n\n'), markerBaked: true}, png)`, where `joinText(a, b, sep) = [a, b].map(s => (s ?? '').trim()).filter(Boolean).join(sep)`. The store writes the render, then applies the patch to keep in keep's own slot, removes drop and renumbers (01 2.9.12). The caption key is always sent (`''` when both are empty), so `captionEditedByUser` becomes true (EDGE-REP-26).

### 2.17 Insert zones, the insert menu and the empty state

Insert zone `atIndex` (`Report.tsx:697-801`, `project.css:1523-1620,2012-2021`): rendered only when the parent passes `onInsert`. Zone 0 sits after the overview and before the first step; zone `idx + 1` after each step, so `n + 1` zones. `div.rep__insert`: min-height 1.5rem, centered, and a dashed 1 px `--control-bd` line across the zone at 50% height that is visible only while hovered.

Closed: `button.rep__insert-btn` `+` (1.5rem square with radius `--radius-chip`, dashed `--control-bd` border, `--ground` fill, `--ink-2`, `1rem`), `title="Insert a step here"`, opacity 0 until the zone is hovered or the button has keyboard focus (`transition: opacity .12s`); hover turns the border solid `--accent` and the glyph `--accent`. Click sets `insertMenuAt = atIndex` (only one menu open at a time).

Open (`insertMenuAt === atIndex`): `div.rep__insert-menu` (`role="menu"`, relative, z 4 so it is above neighboring rows and below the sticky bar, column, gap .35rem, padding .4rem, `--surface`, hairline border, `--radius-control`, `--menu-shadow`), grown in place (the zone's min-height lets it push rows down):

| Row | Label (`span.rep__insert-label`, uppercase micro-label, `--ink-3`, weight 700; sublabel not uppercased, weight 400) | Buttons (`btn btn--small`), `title`, `kind` |
|---|---|---|
| 1 | `Numbered step` | `+ Text` (`text`); `+ Image` (`image`); `+ Capture` title `Record more steps starting here \u2014 pick screen, a window, or an area` (`capture`); `+ Screenshot` title `Insert one screenshot here (no click) \u2014 full screen, a window, or a dragged area` (`screenshot`); `\u2715` (`btn--icon rep__insert-x`) title `Cancel`, closes the menu |
| 2 | `Callout ` + sublabel `\u2014 a highlighted note, not a numbered step` | `+ Note` title `Note \u2014 extra info or a helpful tip` (`note`); `+ Caution` title `Caution \u2014 a potential issue to watch for` (`caution`); `+ Warning` title `Warning \u2014 a serious risk; read before proceeding` (`warning`); each with a 3 px left border in its kind's `-bd` token |
| 3 | `Section ` + sublabel `\u2014 a phase-divider heading, not a numbered step` | `+ Section` title `Section \u2014 a divider heading marking a new phase (not numbered)` (`section`) |

Choosing a button: `insertMenuAt = null`, then `onInsert(atIndex, kind)` (`:574-577`). The menu has no Escape or click-outside close; it closes on `\u2715`, on a choice, and on any manifest replacement. Inserting is never blocked by an open text editor (B4, `:693-696`).

**Empty state** (`:803-818`): `div.rep rep--empty` carrying `--doc-scale` (without it the empty state fell back to scale 1 and its insert zone sat at a different width, `861749c`), containing insert zone 0 and `p.project__hint` `No steps yet. Resume capturing, Import an image, or Add a text step.` (`max-width: 52ch`, `--ink-2`, line height 1.55). The zone's `+` is invisible until hovered (EDGE-REP-19). REQUIRED.

### 2.18 Insert handlers (ProjectDetail)

`onInsert(atIndex, kind)` (`ProjectDetail.tsx:428-434`):

| kind | Handler | Behavior |
|---|---|---|
| `text` | `addTextAt(atIndex)` | below |
| `note`, `caution`, `warning`, `section` | `addTextAt(atIndex, kind)` | below |
| `image` | `pickImageAt(atIndex)` | remember `atIndex`, click the hidden `input[type=file accept="image/png,image/jpeg"]` |
| `capture`, `screenshot` | `captureModal = {atIndex, variant: kind}` | opens 2.19 |

`addTextAt(atIndex, callout?)` (`:363-382`): clear `importErr`; `m = await addTextStep(projectPath, atIndex, callout)`; `applyManifest(m)`; `i = max(0, min(round(atIndex), m.steps.length - 1))`; if `m.steps[i]` is a text step with empty heading and body, `autoEditId = m.steps[i].id` (identified from the RETURNED manifest, never by diffing a possibly stale snapshot, so a rapid double insert opens the right step). Errors go to `importErr`. The store builds the step exactly as `{id: randomUUID(), order: 0, kind: 'text', screenshot: '', trigger: 'hotkey', click: null, monitor: null, window: null, element: {available: false, name: null, controlType: null, bounds: null}, caption: '', heading: '', body: '', [callout], crop: null, annotations: []}` and splices it at `max(0, min(round(atIndex), len))`, then renumbers (`project-store.ts:938-976`). A callout insert also auto-opens its editor. REQUIRED.

`onImportFile` (`:389-411`): read the first chosen file; reset the input (so the same file can be picked again); take and clear the remembered index; return if no file; `importing = true`, clear `importErr`; `m = await importStep(projectPath, bytes, atIndex ?? undefined)`; `applyManifest(m)`; errors to `importErr` (store messages: `Unsupported file \u2014 please choose a PNG or JPEG image.`, IPC: `No image data received`, `Image too large (max 60 MB)`, 01 2.9.12); finally `importing = false`. `importing` disables Export. No auto-edit. A header "Import image" button no longer exists; the append path `pickImageAt(null)` is unreachable (ELECTRON-ONLY dead code).

`screenshotAt(atIndex, target)` (`:417-426`): clear `importErr`; `m = await capture.screenshot(projectPath, target, atIndex)` (02: hide, grab, insert, synchronous in main); `applyManifest(m)`; errors to `importErr`. The report stays mounted, so an open text draft survives.

Capture modal confirm (`:631-652`): close the modal; `screenshot` calls `screenshotAt`; `capture` first checks `textEditing` and, if a text draft is open, sets `importErr = 'Finish editing the text step before capturing.'` and stops (a recording unmounts the report); else `onCaptureInsert(atIndex, target)`, which App routes to `onRecord(openPath, false, {atIndex, target})` (`App.tsx:635-639`, 02).

Editor overlay (`:685-698`): `editing` holds the step snapshot; `onSaved(manifest)` calls `applyManifest` and closes; `onClose` closes. This is the editor save path (04), the exception to optimistic editing.

### 2.19 Capture-insert modal

`CaptureInsertModal` (`CaptureInsertModal.tsx:19-220`), REQUIRED:

| Variant | Title (also `aria-label`) | Confirm label | Helper | Modes offered (order) |
|---|---|---|---|---|
| `capture` | `Record more steps here` | `Start capture` | `Pick what to capture, then click through the steps as usual \u2014 they\u2019ll be inserted at this spot. shotAI hides while you record.` | `screen`, `auto`, `window`, `area` |
| `screenshot` | `Add a screenshot here` | `Capture` | `Grabs one image right now \u2014 no clicking. shotAI is left out of the shot.` | `screen`, `window`, `area` (no `auto`: it classifies off a clicked window) |

Structure: `sop__overlay` (fixed, z 50, centered, `rgba(17, 19, 24, 0.55)`, padding 1.5rem; `role="dialog"`, `aria-modal`) closing on a backdrop click; `sop__modal capmodal` (max width 560, max height 85vh, `--surface`, `--radius-panel`, padding `1.25rem 1.5rem`); `Escape` anywhere in the window closes it (`:37-43`), including while the target dropdown is open (Escape closes the whole modal, not just the dropdown). Nothing is focused on open and focus is not trapped: Tab can move to controls of the page behind the overlay.

Content top to bottom:

1. `h3.sop__modal-title` title; `p.capmodal__help` helper (`--ink-2`, `.9rem`).
2. Mode chips: `div.home__mode capmodal__modes` `role="radiogroup"` `aria-label="Capture mode"` with the micro-label `Mode`, then one `button.capmode__chip` per offered mode (`role="radio"`, `aria-checked`, `title` = hint, `capmode__chip--on` when selected: accent border, `--accent-tint`, `--accent-ink`, weight 600). Labels and hints (`useCaptureTarget.ts:22-27`): `Screen` / `Capture one full monitor`; `Auto` / `Best-effort smart capture \u2014 may include extra/unintended context`; `Window` / `Capture one specific window`; `Area` / `Drag-select a fixed region to capture`.
3. For `window` and `screen`: a dropdown (`home__dd capmodal__dd`, min width 280, max 460): a trigger (`aria-haspopup="listbox"`, `aria-expanded`) showing `pickerLabel` and a `\u25BE` caret; when open, a full-window backdrop that closes it and a popover (`role="listbox"`, `aria-label` `Window to capture` or `Monitor to capture`) with a head row (`Windows` or `Monitors`, and `\u21BB Refresh` (`btn btn--small btn--ghost`, `title="Refresh the list"`, disabled while loading) which reloads the targets) and a list (max height 220, scrolls). Window rows: `role="option"`, `aria-selected`, the app name (when non-empty) and `title || '(untitled)'`; clicking picks and closes. Monitor rows: the name and `` `${width}\u00D7${height}` `` plus `` ' \u00B7 primary' `` when primary. Empty list: `Loading\u2026` while loading, else `No windows found` or `No monitors found`.
4. For `area`: a `btn` labelled `Selecting\u2026` while selecting (disabled), else `Re-select area` when an area is picked, else `Select area\u2026`; when picked, `` `${width} \u00D7 ${height}px @ (${x}, ${y})` ``.
5. Warnings (`capmode__warn`, `--caut-fg`, `.82rem`): `window` with no pick: `Pick a window above to capture.`; `area` with no pick: `Drag out the area you want to capture.`
6. An error line `p.capmodal__err` (`--danger`, `.85rem`) with the last target-load or area-selection error message. It is never cleared while the modal is open: a later successful Refresh or area selection leaves the old error on screen (`CaptureInsertModal.tsx:28-33,202`). REQUIRED (parity; low value, see EDGE-REP-49).
7. Actions: `Cancel` (`btn`) and the confirm (`btn btn--primary`), disabled when `!modeReady || selectingArea`.

Picker logic (`useCaptureTarget.ts:58-185`):

- Initial mode `screen`. The first render loads the targets once if the mode needs them (`window` or `screen`).
- `loadTargets()`: `targetsLoading = true`; `t = await capture.listTargets()` (02); keep the previous picked window if its id is still present, else `t.windows[0] ?? null`; keep the previous monitor id if present, else the primary monitor's id, else the first monitor's id, else null; errors go to `onError`; finally `targetsLoading = false`.
- `selectMode(m)`: set the mode, close the dropdown, load targets if `m` is `window` or `screen` and nothing is loaded yet.
- `selectArea()`: `selectingArea = true`; `r = await region.selectArea()` (03 area overlay); if `r` is non-null it becomes the picked area (a canceled selection keeps any previous pick); errors to `onError`; finally `selectingArea = false`.
- `pickerLabel`: window mode: `` `${app ? app + ' \u2014 ' : ''}${title || '(untitled)'}` `` for the pick, else `Loading\u2026` while loading, else `Select a window\u2026`. Screen mode: `` `${name} \u00B7 ${width}\u00D7${height}${isPrimary ? ' \u00B7 primary' : ''}` `` for the picked monitor, else `Loading\u2026`, else `Whole screen (primary monitor)`.
- `modeReady`: `window` requires a picked window; `area` requires a picked area; `screen` and `auto` are always ready.
- `buildTarget()`: `window` gives `{mode: 'window', window: {id, pid, title}}` (or `{mode: 'auto'}` without a pick); `screen` gives `{mode: 'screen', monitorId}` when a monitor is picked, else `{mode: 'screen'}`; `area` gives `{mode: 'area', area}` (or `{mode: 'auto'}`); otherwise `{mode: 'auto'}`.
- Confirm: return if `!modeReady || selectingArea`; else `onConfirm(buildTarget())`.

### 2.20 Every edit path (the eleven in Report.tsx and the ones in ProjectDetail.tsx)

Every Report edit follows the same Electron pattern: call the main process, which queues a job that re-reads `project.json`, mutates and atomically rewrites it; the full manifest comes back; `applyManifest` replaces the step list and every card re-renders (no memoization). A caption shows the old text until the write returns; reorder, delete and merge drop clicks that arrive mid-write (`busyRef`). Native behavior for each row is in 7.5.

| # | Function (citation) | Trigger | UI guard | Call | Electron on failure |
|---|---|---|---|---|---|
| R1 | `setZoom` (`Report.tsx:473-483`) | zoom in, zoom out, reset | clamp `[1, 4]` | `updateStep {reportZoom}` | ignored |
| R2 | `reframe` (`:485-497`) | pan mouseup | zoom above 1, overflow | `updateStep {reportPanX, reportPanY}` | ignored |
| R3 | `reorderTo` (`:501-515`), via `move` (`:517`), `commitNum` (`:519-535`), `onRowDrop` (`:563-572`) | up, down, number entry, drop | `busyRef`, `from === dest` | `reorderSteps(ids)` | ignored; concurrent clicks dropped |
| R4 | `saveCaption` (`:537-547`) | caption Enter or blur | unchanged caption | `updateStep {caption}` | ignored; old caption stays |
| R5 | `saveBody` (`:551-561`) | instructions blur or Ctrl+Enter | unchanged body | `updateStep {body}` | ignored |
| R6 | `del` (`:579-597`) | delete control, menu | `busyRef`; confirm | `deleteStep` | ignored; concurrent clicks dropped |
| R7 | `merge` (`:603-618`) via `mergeStepInto` (`merge.ts:46-106`) | `Merge \u2193` | `busyRef`, `canMergeInto` | `mergeSteps(keep, drop, patch, png)` | alert `Could not merge these steps: {message}` |
| R8 | `saveText` (`:632-643`) | text editor Save | none | `updateStep {heading, body}` | ignored; editor stays open |
| R9 | `setCallout` (`:647-656`) | menu conversions | raw equality | `updateStep {callout}` | ignored |
| R10 | `saveIntro` (`:659-671`) | overview Save, Remove | none | `setIntro(intro or null)` | ignored; editor stays open |
| R11 | `cancelText` delete (`:676-689`) | Cancel on a fresh blank plain text step | fresh, blank, not a callout | `deleteStep` | ignored |
| P1 | `addTextAt` (`ProjectDetail.tsx:363-382`) | insert menu Text, Note, Caution, Warning, Section | none | `addTextStep(atIndex, callout)` | `importErr` notice |
| P2 | `onImportFile` (`:389-411`) | insert menu Image | file chosen | `importStep(bytes, atIndex)` | `importErr` notice |
| P3 | `screenshotAt` (`:417-426`) | capture modal, screenshot variant | modal ready | `capture.screenshot(target, atIndex)` (02) | `importErr` notice |
| P4 | `SizeSlider.persist` (`:74-92`) | slider release, key up, percent box | `value === committedScale` | `setDisplayScale(value)` | silent fall back to `committedScale` |
| P5 | `doExport` flatten (`:310-332`) | export menu | not exporting | `ensureFlattened` (each step `updateStep {markerBaked: true}` with PNG) then `export` | `exportErr` notice |
| P6 | `doExportPackage` flatten (`:334-355`) | package dialog | | same, then `exportPackage` | `exportErr` notice |
| P7 | Editor `onSaved` (`:692-695`) | editor Save (04) | | editor's `updateStep` with PNG | editor shows its own notice (04) |
| P8 | View, Brand (`App.tsx:420-427`, 03) | app menu | | `setProjectTheme(choice)` | App `fail` notice (06) |

### 2.21 Keyboard summary (Electron)

| Where | Key | Effect |
|---|---|---|
| caption and number editors | Enter / Escape / blur | commit / cancel / commit |
| instructions editor | Ctrl+Enter / Escape / blur / Enter | commit / cancel / commit / newline |
| text step editor | none | Save and Cancel buttons only |
| percent box | ArrowUp, ArrowDown / Enter / Escape | step a detent / commit / abandon |
| range | arrows, Home, End, PageUp, PageDown (Chromium defaults) | move the index (preview), persist on key up |
| export menu | Escape | close |
| overflow menu | Escape | close |
| capture modal | Escape | close |
| confirm dialog | Escape / Enter (focus on the confirm button) | cancel / confirm |
| everything else | Tab | normal focus order; hidden-until-hover controls are still focusable and appear on focus |

### 2.22 User-visible strings (complete)

Code points for glyph faces: `\u2190` LEFTWARDS ARROW, `\u23FA` BLACK CIRCLE FOR RECORD, `\u2B07` DOWNWARDS BLACK ARROW, `\u283F` BRAILLE PATTERN DOTS-123456, `\u22EF` MIDLINE HORIZONTAL ELLIPSIS, `\u2191` UPWARDS ARROW, `\u2193` DOWNWARDS ARROW, `\u21BA` ANTICLOCKWISE OPEN CIRCLE ARROW, `\u2212` MINUS SIGN, U+1F5D1 WASTEBASKET (C# `"\U0001F5D1"`), `\u2715` MULTIPLICATION X, `\u21BB` CLOCKWISE OPEN CIRCLE ARROW, `\u25BE` BLACK DOWN-POINTING SMALL TRIANGLE, `\u26A0` WARNING SIGN, `\u2139` INFORMATION SOURCE, `\u2501` BOX DRAWINGS HEAVY HORIZONTAL.

| Key (native constant) | Text | Citation |
|---|---|---|
| `Back` | `\u2190 Back` | `ProjectDetail.tsx:441` |
| `StepCount(n)` | `` `${n} step${n === 1 ? '' : 's'}` `` | `:447` |
| `Loading` | `Loading\u2026` | `:674` |
| `SizeLabel` | `Size` | `:129` |
| `SizeSliderName` | `Document size` | `:139` |
| `SizeValueText(pct)` | `` `${pct} percent` `` | `:140` |
| `SizeBoxName` | `Document size, percent` | `:151` |
| `Percent` | `%` | `:198` |
| `Resume` | `\u23FA Resume capturing` | `:467` |
| `ResumeTip` | `Resume capturing \u2014 click through more steps; they append to this project` | `:464` |
| `FinishTextFirst` | `Finish editing the text step first` | `:463,486` |
| `ExportButton` | `\u2B07 Export` | `:499` |
| `Packaging` | `Packaging\u2026` | `:491` |
| `Exporting(label)` | `` `Exporting ${label}\u2026` `` | `:498` |
| `ExportingProgress(label, done, total)` | `` `Exporting ${label}\u2026 ${done}/${total}` `` | `:497` |
| `ExportNeedsShot` | `Add a screenshot before exporting` | `:484` |
| `ExportTip` | `Export as HTML, Word, PowerPoint, PDF, Markdown, or a shareable package` | `:487` |
| export labels | `HTML`, `HTML (for Word)`, `PDF`, `Markdown`, `Word`, `PowerPoint`, `Project package` | `:21-28,513-573` |
| export hints | `one self-contained file \u2014 best for sharing`; `.docx \u2014 edit in Microsoft Word`; `.pptx \u2014 one slide per step`; `paste into Word or Google Docs to edit`; `print-ready \u2014 best for printing`; `.md + images/ \u2014 for wikis & version control`; `.zip \u2014 re-open & edit in shotAI` | `:513-573` |
| package dialog | `Export a shareable project package` (name); `Share this project`; the paragraph and checkbox text of 2.4; `Cancel`; `Export with originals`; `Export (redacted)` | `:592-625` |
| `CaptureNeedsNoDraft` | `Finish editing the text step before capturing.` | `:645` |
| notice prefixes | `Import failed: `, `Export failed: `, `Error: ` | `:658,663,668` |
| `Dismiss` | `Dismiss` (the button face is `\u00D7`) | `Notice.tsx:31-33` |
| `EditScreenshot` | `Edit screenshot` (editor overlay name) | `ProjectDetail.tsx:686` |
| zoom controls | `Zoom in`, `Zoom out`, `Reset zoom`, `Delete step` | `Report.tsx:213-243` |
| text editor | `Heading (optional)`, `Text\u2026`, `Save`, `Cancel` | `:271-292` |
| `CaptionPlaceholder` | `Caption\u2026` | `:1060` |
| `DeleteConfirm` | `Delete this step? (its image file stays on disk)`; confirm `Delete`; `Cancel` | `:582-584`, `useConfirm.tsx:72` |
| `MergeFailed(msg)` | `` `Could not merge these steps: ${msg}` ``; alert button `OK` | `:613`, `useConfirm.tsx:44` |
| confirm dialog name | `Confirm` | `useConfirm.tsx:62` |
| insert menu | `Numbered step`, `+ Text`, `+ Image`, `+ Capture`, `+ Screenshot`, `Cancel` (title of `\u2715`), `Callout `, `\u2014 a highlighted note, not a numbered step`, `+ Note`, `+ Caution`, `+ Warning`, `Section `, `\u2014 a phase-divider heading, not a numbered step`, `+ Section`, and the six titles quoted in 2.17 | `Report.tsx:703-786` |
| `InsertHere` | `Insert a step here` | `:793` |
| `EmptyHint` | `No steps yet. Resume capturing, Import an image, or Add a text step.` | `:814` |
| rail | `` `${kind} callout \u2014 not a numbered step` ``; `Click to set this step's position`; `Drag to reorder` | `:831,850,860-861` |
| step menu | `\u2191 Move up`, `\u2193 Move down`, `` `Change to ${k}` ``, `` `Make ${k} callout` ``, `Change to section divider`, `Make section divider`, `Convert to plain text`, `Delete step`, `Edit` (face and title), `More step actions` | `:886-931` |
| section | `Click to edit this section divider`; `Empty \u2014 click to add a section heading.` | `:974,984` |
| callout | `Click to edit`; `Empty \u2014 click to add text.` | `:996,1003` |
| plain text | `Click to edit this text step`; `Click to edit`; `+ Add text` | `:1018,1026,1037` |
| caption | `Click to edit caption`; `Add a caption\u2026` | `:1067,1071` |
| merge | `This right-click likely opened a menu \u2014 merge it into the next step (the menu selection) to keep one screenshot showing the menu.`; `Merge \u2193`; `Merging\u2026` | `:1080-1089` |
| instructions | `Write the instruction for this screenshot\u2026`; `Click to edit instructions`; `+ Add instructions` | `:1104-1122` |
| window line | `{app}` + `` ` \u2014 ${title}` `` | `:1127-1128` |
| overview | `Edit overview`, `Remove`, `+ Add an overview` | `:1162-1181` |
| capture modal | every string of 2.19 | `CaptureInsertModal.tsx:46-214`, `useCaptureTarget.ts:22-27,130-144` |
| merge load failure | `Could not load a screenshot to flatten (<url>).` (native: the relative path, 04) | `sop-prepare.ts:17` |

### 2.23 App-level lifecycle around the detail view

These App behaviors decide when the detail view and the report are mounted, so they decide which Report state survives. REQUIRED unless noted.

| Trigger | Electron behavior | Citation |
|---|---|---|
| Opening a project (or Settings) | the shared scroll container `.project__body` jumps to the top; returning Home restores the Home list's scroll position, recorded as it scrolls (06 owns the Home half) | `App.tsx:266-290` |
| `onOpenSettings` from the SOP panel | App renders the detail view only when `showDetail && !showSettings`, so opening Settings UNMOUNTS `ProjectDetail` and `Report` while the project stays open in the store and the window keeps its detail width: every text draft, inline editor, insert menu, the package dialog's checkbox and the latches are discarded, and the unmount cleanup aborts an export flatten in flight (`exportAbortRef`, 2.4). Returning from Settings re-renders the report from the store (EDGE-REP-41) | `App.tsx:630,644`, `ProjectDetail.tsx:287-290` |
| A recording that ran on the open project ends | App calls `open(openPath)` again, so `loading` shows `Loading\u2026` and the report remounts with fresh state | `App.tsx:291-298` |
| While recording | neither Home nor the detail view is mounted (`showDetail = !recording && !!openPath`), so the same state loss as Settings applies; Resume capturing and `+ Capture` are disabled while a text draft is open for that reason (INV-REP-24) | `App.tsx:263-264` |
| Opening a project from Home, import or the capture flow | `open` is called without awaiting a result check (`App.tsx:296,488,507,857`), so a failed open is silent (EDGE-REP-39) | as cited |

## 3. Constants

All lengths are DIP. "Electron CSS" rows describe the shipped stylesheet and are reference only; the native layout values are in 7.9.

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| `SCALE_MIN` | 0.65 | ratio | smallest document scale | `src/shared/doc-scale.ts:19` |
| `SCALE_MAX` | 1.25 | ratio | largest document scale | `:20` |
| `SCALE_STEP` | 0.05 | ratio | detent spacing | `:22` |
| `SCALE_DEFAULT` | 1 | ratio | absent or unusable `displayScale` | `:23` |
| `SCALE_STEPS` | `[0.65, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 1, 1.05, 1.1, 1.15, 1.2, 1.25]` | ratio | the 13 legal values, each exactly `k / 100` | `:26-34` |
| clamp percent bounds | 65, 125; snap 5 | percent | the normative snap (INV-REP-10) | `:52-55` |
| `HTML_COL_BASE` | 816 | DIP | export content column at scale 1 | `:69` |
| `HTML_DOC_PAD` | 32 | DIP per side | export `.doc` horizontal padding; does not scale | `:71` |
| `REPORT_COL_BASE` | `HTML_COL_BASE` (816) | DIP | the report column; the SAME number, not an equal one | `:84` |
| `REP_FRAME_BASE` | `816 + 32 * 2` = 880 | DIP | report frame at scale 1 | `:92` |
| `STEP_BADGE_W` | 30 | DIP | export badge width | `:95` |
| `STEP_GAP` | 16 | DIP | export badge-to-card gap | `:96` |
| `STEP_CARD_PAD` | 32 | DIP (both sides) | export card horizontal padding | `:98` |
| `STEP_CHROME` | 78 | DIP | `30 + 16 + 32`, fixed chrome between column and image | `:100` |
| image ceiling floor | 120 | DIP | `htmlImgMax` never below this | `:133` |
| embed factor | 2 | ratio | `htmlImgEmbedMax = htmlImgMax * 2` | `:145` |
| `DETAIL_WINDOW_BASE` | 1010 | DIP | detail window width at scale 1 and the floor | `:103` |
| `WINDOW_CHROME` | 130 | DIP | `1010 - 880` | `:104` |
| `LIST_WIDTH` | 720 | DIP | home window width (03 owns) | `src/main/main.ts:235` |
| `REPORT_BASE_W` | `REPORT_COL_BASE` (816) | DIP | fit cap at zoom 1, scale 1 | `src/renderer/project/report-geometry.ts:27` |
| `REPORT_BASE_H` | 600 | DIP | fit height cap at scale 1 | `:28` |
| `WRAP_BORDER` | 1 | DIP per side | image wrap border | `:31` |
| test real column | 786 | DIP | Electron card content width used by the tests | `report-geometry.test.ts:13` |
| `ZOOM_MIN` | 1 | ratio | zoom floor (read and write) | `Report.tsx:30` |
| `ZOOM_MAX` | 4 | ratio | zoom cap (write) | `:31` |
| zoom factor | 1.25 | ratio | per zoom click, multiply or divide | `:215,225` |
| IPC zoom clamp | `[1, 6]` | ratio | `parseStepPatch` | `src/main/ipc.ts:216` |
| pan range, default | `[0, 1]`, 0.5 | fraction of scroll range | `reportPanX`, `reportPanY` | `ipc.ts:217-218`, `Report.tsx:125-126` |
| `EXPORT_MENU_MIN_W` | 230 | DIP | export menu min width; pinned to CSS by a test | `ProjectDetail.tsx:17`, `project.css:1224` |
| `EXPORT_MENU_GUTTER` | 8 | DIP | clearance from the window's left edge | `ProjectDetail.tsx:19` |
| popover offset | 4 | DIP | menu top below trigger | `project.css:1010,1217` |
| overflow height estimate | `items * 34 + 16` | DIP | drop-up decision | `OverflowMenu.tsx:51` |
| overflow min width | 172 | DIP | `.menu__pop` | `project.css:1012` |
| marker ring | 22 diameter, 2.5 border, 2 halo at `rgba(255, 255, 255, 0.7)`, fill alpha `0x2E` | DIP | click overlay | `project.css:1798-1808`, `Report.tsx:201` |
| `ACCENT` | `#e11d48` | color | left-click ring | `src/renderer/editor/annotations.ts:35` |
| `RIGHT_CLICK_COLOR` | `#2563eb` | color | right-click ring | `:36` |
| ring CSS fallback | `#ef4444`, `rgba(239, 68, 68, 0.18)` | color | used only when the inline color is invalid | `project.css:1803,1805` |
| image controls | offset 8, gap 4, button 1.9rem, rest opacity 0.3, disabled opacity 0.4, fill `rgba(17, 24, 39, 0.62)` (hover 0.88), delete top margin 6, fade 0.12 s | DIP, s | floating controls | `project.css:1738-1790` |
| size slider width | 88 | DIP | fixed so the bar does not reflow | `project.css:1186` |
| percent box | `min 65`, `max 125`, `step 5`, width 3.6em | percent, em | typed entry | `ProjectDetail.tsx:148-150`, `project.css:1195` |
| Electron CSS frame | `calc(880px * s)` | DIP | shipped `.rep` max width (NOT the derived frame) | `project.css:1378` |
| Electron CSS card | `calc(820px * s)` | DIP | shipped `.rep__bodywrap` max width (NOT the derived column) | `project.css:1628` |
| Electron CSS rail | `2.4rem` column, `.85rem` gap, row margin `.6rem`, badge 2rem with `.85rem` top margin, card padding `.85rem 1rem` | rem | shipped row geometry | `project.css:1386-1395,1440-1458,1633` |
| insert zone | min height 1.5rem, button 1.5rem | rem | gap between cards | `project.css:1523-1562` |
| drag visuals | source opacity 0.45; 2 px indicator at `top: -0.5rem` | | | `project.css:1397-1411` |
| modal shell | overlay `rgba(17, 19, 24, 0.55)`, padding 1.5rem, z 50; modal max width 560, max height 85vh; confirm max width 420 | | | `project.css:2544-2575` |
| notice stack | top `.6rem`, max width `min(92%, 680px)`, error fill `rgba(185, 28, 28, 0.6)` | | | `notice.css:4-40` |
| picker dropdown | min width 280, max width 460, list max height 220 | DIP | capture modal | `project.css:560-632` |
| native: rollback notice | `Your last change couldn't be saved and was undone. ` + message | text | IMPROVEMENT (Q-MODEL-12, Q-REP-1) | new |
| native: drag auto-scroll | band 64, max speed 26 per tick, tick 30 ms | DIP, ms | IMPROVEMENT parity with Chromium's drag auto-scroll, values from macOS | `macOS:shotAI/ReportView.swift:1402-1421` |

Derived widths per detent (`docWidths`, `doc-scale.ts:129-147`; identical to `macOS:Packages/ShotModel/Tests/ShotModelTests/DocScaleTests.swift:65-78`), and `detailWindowWidth(s, 1920)`:

| s | `htmlCol` = `reportCol` | `repFrame` = `htmlDoc` | `htmlImgMax` | `htmlImgEmbedMax` | `detailWindowWidth(s, 1920)` |
|---|---|---|---|---|---|
| 0.65 | 530 | 594 | 452 | 904 | 1010 |
| 0.70 | 571 | 635 | 493 | 986 | 1010 |
| 0.75 | 612 | 676 | 534 | 1068 | 1010 |
| 0.80 | 653 | 717 | 575 | 1150 | 1010 |
| 0.85 | 694 | 758 | 616 | 1232 | 1010 |
| 0.90 | 734 | 798 | 656 | 1312 | 1010 |
| 0.95 | 775 | 839 | 697 | 1394 | 1010 |
| 1.00 | 816 | 880 | 738 | 1476 | 1010 |
| 1.05 | 857 | 921 | 779 | 1558 | 1051 |
| 1.10 | 898 | 962 | 820 | 1640 | 1092 |
| 1.15 | 938 | 1002 | 860 | 1720 | 1132 |
| 1.20 | 979 | 1043 | 901 | 1802 | 1173 |
| 1.25 | 1020 | 1084 | 942 | 1884 | 1214 |

## 4. Invariants

**INV-REP-1. A step is un-numbered iff `kind === 'text'` and `isCalloutKind(callout)`; every other step takes the next number in array order.** Why: an unknown callout was treated as a callout by truthiness, which shifted every later number and disagreed with macOS (#90, fixed in #99 `18f3c4d`). Citation: `Report.tsx:34-40,465-471`. Test: `Report/ReportPresentationTests.NumbersSkipKnownCalloutsOnly` (the #90 regression: `[shot, text(callout: 'tip'), shot]` numbers 1, 2, 3; `[shot, note, shot]` numbers 1, null, 2; `[shot, section, shot]` numbers 1, null, 2).

**INV-REP-2. Report zoom is IN-only: the displayed zoom is `max(1, reportZoom ?? 1)`, every UI write is clamped to `[1, 4]`, zoom-out is disabled at zoom 1 or below, reset is disabled at exactly 1.** Why: the zoom-1 view already fits the column; an older build allowed 0.5. Citation: `Report.tsx:20-31,76,224,234,475`. Test: `Report/ReportOperationsTests.ZoomClampsToOneAndFour`, `Report/ReportPresentationTests.ZoomFloorsLegacyValues`.

**INV-REP-3. No pan gesture starts, and nothing is persisted, while the displayed zoom is 1 or below, regardless of measured overflow.** Why: phantom overflow at zoom 1 let a stray drag overwrite a framing saved at a higher zoom (`58131a8`). Citation: `Report.tsx:132-142`. Test: `App.Tests Report/StepFigureTests.NoPanAtZoomOne` (drag at zoom 1 raises no operation).

**INV-REP-4. The image wrap's content box is never smaller than the image: `wrapW - 2 - baseW >= 0` and `wrapH - 2 - baseH >= 0` for every image shape and available width.** Why: the report cropped screenshots horizontally because the fit used the card width instead of the space inside it (`746b012`). Citation: `report-geometry.ts:4-21,105-110`. Test: `Geometry/ReportGeometryTests.NeverGivesTheWrapAContentBoxNarrowerThanItsImage`.

**INV-REP-5. The fit uses the MEASURED width available to the figure, falling back to the scaled cap only before the first measurement, and the whole wrap fits inside the measured width.** Why: same bug; a constant that predates the card padding crops again the moment styling changes. Citation: `Report.tsx:58-62,104-116`, `report-geometry.ts:47-56,77`. Test: `Geometry/ReportGeometryTests.KeepsTheWholeWrapInsideTheMeasuredColumn`, `.FallsBackToTheConstantBeforeTheFirstMeasurement`.

**INV-REP-6. A capture smaller than the fit box is never upscaled (`fitScale <= 1`).** Why: an upscaled screenshot is blurry and misrepresents the capture. Citation: `report-geometry.ts:55,82`. Test: `Geometry/ReportGeometryTests.NeverUpscalesACaptureSmallerThanTheColumn`.

**INV-REP-7. For zoom above 1 the box stays at the zoom-1 fit and only the image grows (`wrap = round(base * min(zoom, 1)) + 2`, image `base * zoom`).** Why: panning in both axes instead of a box that grows taller. Citation: `report-geometry.ts:89-97`, `Report.tsx:177,188`. Test: `Geometry/ReportGeometryTests.HoldsTheBoxAtTheZoomOneFit`.

**INV-REP-8. `baseW`, `baseH`, `wrapW`, `wrapH` are whole DIP.** Why: a fractional box invites a 1 px disagreement between layout and paint. Citation: `report-geometry.ts:83-87`. Test: `Geometry/ReportGeometryTests.ReturnsWholePixels`.

**INV-REP-9. The report renders at the width it exports: `reportCol(s) == htmlCol(s)` and `REPORT_COL_BASE` is the same constant as `HTML_COL_BASE`; the frame is `htmlCol(s) + 2 * 32` (padding does not scale); natively, in addition, the zoom-1 figure's outer width for a capture at least as wide as the column equals `htmlImgMax(s)` at every detent when the window is wide enough.** Why: the report card was 4 px wider than the export (#81, fixed by #102 `d50c029`); macOS pins the figure form (`macOS:Packages/ShotModel/Tests/ShotModelTests/DocScaleTests.swift:95-100`). The native figure clause is an IMPROVEMENT over Electron's shipped CSS (EDGE-REP-1). Citation: `doc-scale.ts:73-92,129-147`, `report-matches-export.test.ts:12-49`. Test: `Geometry/ReportMatchesExportTests` (Linux) and `App.Tests Report/ReportLayoutTests.FigureWidthEqualsExportAtEveryDetent` (real WPF layout).

**INV-REP-10. `clampScale` is exactly: not a finite number gives 1; `pct = round(v * 100)`; `pct = clamp(pct, 65, 125)`; `pct = round(pct / 5) * 5`; return `pct / 100`. It is applied on read and write, is idempotent, and always returns a member of `SCALE_STEPS`.** Why: cross-platform contract (#70, macOS #83); the float formulation rounds 0.825 down; `1.025 * 100` is `102.49999999999999` and must snap to 1.00 on every platform. `round` must be `JsMath.Round`: .NET `Math.Round(82.5)` is 82 and would give 0.80. Citation: `doc-scale.ts:36-56`, `doc-scale.test.ts:60-82`. Test: `Geometry/DocScaleTests.MatchesTheNormativeTable`.

**INV-REP-11. The window is sized only from the COMMITTED scale (never from a preview), only grows while a project is open, and is not resized when maximized.** Why: resizing on every drag step ran the slider away from the pointer (`b6ddce5`); setting the width outright undid a hand-widened or maximized window on every nudge (`62b4b7d`). Citation: `store.ts:39-47`, `App.tsx:340-350`, `main.ts:239-261`. Test: `Report/DocScaleEditorTests.PreviewNeverChangesCommitted`; 03's `WindowLayoutTests` own the resize arithmetic.

**INV-REP-12. Committing a scale equal to the committed value writes nothing and does not bump `updatedAt`.** Why: focusing or clicking the slider re-dated the project and jumped it to the top of Home (`62b4b7d`, #77 class). Citation: `ProjectDetail.tsx:74-79`. Test: `Report/DocScaleEditorTests.NoOpCommitQueuesNothing` and 01 D-11.

**INV-REP-13. Escape in the percent box abandons the draft and the blur that follows cannot commit it.** Why: `setDraft(null); blur()` committed the abandoned value because the blur read stale state (`62b4b7d`, HIGH). Citation: `ProjectDetail.tsx:58-67,121-125,188-194`. Test: `Report/DocScaleEditorTests.EscapeThenBlurDoesNotCommit`; `App.Tests Report/DocScaleControlTests.EscapeThenClickAwayKeepsCommittedValue`.

**INV-REP-14. A persistence result older than the newest request for the same field never changes what is on screen.** Why: a slow scale write landing after a newer one yanked the layout back (`62b4b7d`). Natively this holds for every edit because the session applies results in queue order and adopts the disk state only when nothing is pending (ARCHITECTURE 7.4 S3 and S4, R-ARCH-23). Citation: `ProjectDetail.tsx:69-91`. Test: `Report/OptimisticPolicyTests.OutOfOrderCompletionCannotRevertNewerEdit`.

**INV-REP-15. The screen never shows an edit the disk rejected: a failed persist rolls the view back to the persisted state plus still-pending edits and shows the rollback notice.** Why: a swallowed scale failure left every export rendering at a size the manifest lacked (`62b4b7d`); the fixed decision generalizes it to every edit. Citation: `ProjectDetail.tsx:86-91`; fixed decision. Test: `Report/OptimisticPolicyTests.FailedPersistRollsBackAndNotifies`.

**INV-REP-16. The marker overlay is drawn iff `click` is set, `markerBaked` is falsy, the image size is known and positive, and the mapped fraction is inside `[0, 1]` on both axes; the crop origin is subtracted only when the displayed image is the flattened render and a crop exists.** Why: a baked render already carries the ring (drawing again doubles it); the flattened render is cropped, the raw screenshot is not. Citation: `Report.tsx:87-102`. Test: `Report/ReportPresentationTests.MarkerOverlayRules` (ports `macOS:Packages/ShotModel/Tests/ShotModelTests/ReportPresentationTests.swift:70-106`).

**INV-REP-17. The report shows `flattened` when it is a non-empty string, else `screenshot`, and reloads the image when (path, `renderRev`) changes, never on a zoom, pan or scale change.** Why: a re-saved redaction must replace the cached image; a display change must not reload. Citation: `Report.tsx:66-71`. Test: `Report/ReportPresentationTests.ImageKeyIsPathAndRenderRev`.

**INV-REP-18 [SECURITY]. Report images are read only through `ProjectStore.ResolveImage` (01's static helper, not a dependency on the concrete store, R-ARCH-4; confined to the project folder, extension `.png`, `.jpg` or `.jpeg`) and decoded only by the explicit PNG or JPEG WIC decoder chosen by the magic bytes (R-ARCH-21); a manifest path that resolves outside the project or to another type is never read, bytes that are neither PNG nor JPEG are never decoded, and in both cases the figure shows the missing-image state.** Why: manifest paths and image files are untrusted (a synced or imported project); Electron enforced the path rule with the `shot://` handler (01 2.9.10) and decoded the pixels in its sandboxed renderer, which no longer exists natively (ARCHITECTURE 9.1 item 2, D-ARCH-3). Citation: `store.ts:209-213`, `src/main/main.ts:52-86`. Test: `App.Tests Report/ReportImageLoaderTests.RefusesPathsOutsideTheProject` (`..\..\secret.png`, an absolute path, `shots/x.exe`) and `RefusesBytesThatAreNotPngOrJpeg` (a `.png` file holding GIF or BMP bytes shows the missing-image state; no decoder is created).

**INV-REP-19 [SECURITY]. A merge re-bakes the kept step from its RAW screenshot with the kept step's own annotations, crop and click ring plus the mapped marker, writes the render before the manifest references it, and never shows a merged result that is not on disk.** Why: the kept step's redactions must survive into the only render that egress reads (04 INV-EDIT-7); the render is the PNG that export and Claude use. Citation: `merge.ts:46-106`. Test: `Report/MergePlannerTests.BakeRequestCarriesKeepAnnotationsCropAndMarkers`; merge uses the durable path (7.5).

**INV-REP-20 [SECURITY]. Every export and package export first brings every shot step's render up to date through `EnsureFlattenedAsync` and does not start the export if that fails; the package dialog defaults to redacted-only every time it opens.** Why: export refuses raw screenshots for any step with a redaction or crop; fail closed (04, 09). The per-open reset is an IMPROVEMENT (EDGE-REP-22). Citation: `ProjectDetail.tsx:319-323,343-346,602-607`. Test: `Report/ExportFlowTests.FlattenFailureStopsExport`, `.PackageDialogOpensRedacted`.

**INV-REP-21. The editor save path (flatten, persist, then report success) stays synchronous: the report adopts the editor's manifest only after the store call returned, and no optimistic change is shown for it.** Why: the redaction bake must be on disk before anything can read it (fixed decision; 04). Citation: `ProjectDetail.tsx:692-695`. Test: `Report/OptimisticPolicyTests.EditorSaveIsDurable`.

**INV-REP-22. An auto-open request for a new text step is consumed once; manifest reconciliation only drops a latch whose step vanished and never re-opens an editor.** Why: a never-reset trigger and a re-asserting reconciliation locked the report permanently (B4). Citation: `Report.tsx:393-398,430-439,446-461`. Test: `Report/ReportEditStateTests.AutoOpenIsSingleShot`, `.ReconcileNeverReasserts`.

**INV-REP-23. Opening a text editor and inserting a step are never blocked by another open editor; opening a different text step's editor switches to it.** Why: a lingering `editingTextId` made callouts look un-editable (B4). Citation: `Report.tsx:620-630,693-696`. Test: `Report/ReportEditStateTests.OpenAlwaysSwitches`.

**INV-REP-24. While a text step editor is open, Resume capturing, Export and "+ Capture" are unavailable (a recording unmounts the report and would discard the draft); "+ Screenshot" and image import stay available.** Citation: `ProjectDetail.tsx:234-236,459,474,644-647`. Test: `Report/ProjectDetailStateTests.TextDraftBlocksRecordingAndExport`.

**INV-REP-25. Cancel on a fresh text step (auto-opened after insert, never saved) deletes it only when it is a plain text step with empty heading and body and no callout; callouts and sections are kept.** Citation: `Report.tsx:673-689`. Test: `Report/ReportEditStateTests.CancelDeletesOnlyFreshBlankPlainText`.

**INV-REP-26. The export menu always opens where it can be reached: it opens to the right of its trigger when `trigger.Right - 230 < 8` (window client coordinates), else to the left, and its minimum width constant equals the menu's styled minimum width.** Why: a wrapped command bar put the right-anchored menu at negative x (`701d4aa`). Citation: `ProjectDetail.tsx:16-19,271-277,475-481`, `project.css:1215-1238`. Test: `Report/PopoverPlacementTests` (Linux), `App.Tests Report/CommandBarTests.ExportMenuMinWidthMatchesConstant`.

**INV-REP-27. The command bar wraps to a second row instead of clipping, and carries no per-project brand control.** Why: `701d4aa`. Citation: `project.css:1157-1162`, `command-bar.test.ts:61-75`. Test: `App.Tests Report/CommandBarTests.BarWrapsAndHasNoBrandControl`.

**INV-REP-28. The floating image controls are not hit-testable until their figure is hovered or they have keyboard focus.** Why: a stale idle hit rectangle after a delete reflow swallowed clicks meant for another row (B4). Citation: `project.css:1738-1760`. Test: `App.Tests Report/StepFigureTests.IdleControlsAreNotHitTestable`.

**INV-REP-29. Every user edit is honored in the order it was made; no click is silently dropped because an earlier write is in flight.** Why: IMPROVEMENT replacing `busyRef`, which dropped reorder, delete and merge clicks mid-write (feasibility doc, "Edits wait for the disk"). Citation: `Report.tsx:422-424,503,580,605`. Test: `Report/OptimisticPolicyTests.RapidMovesAllApply` (three quick "Move down" on step 1 of 5 move it to index 3).

**INV-REP-30. A report edit updates the screen before its write completes, and re-renders only the cards whose step or number changed.** Why: IMPROVEMENT (fixed decision). Citation: feasibility doc; `Report.tsx:1185-1190` re-renders every card. Test: `Report/CardListDiffTests.CaptionEditTouchesOneCard`; `App.Tests Report/ReportResponsivenessTests.CaptionShowsBeforeWriteCompletes`.

**INV-REP-31. Each open project has its own session; a result, event or notice from a previous project's session never changes the current view.** Why: Electron's `applyManifest` accepted a late result from a project the user had left (EDGE-REP-30). IMPROVEMENT. Test: `Report/ProjectDetailStateTests.LateResultFromClosedSessionIgnored`.

**INV-REP-32. Loading a report image never keeps the file open after decode, and bypasses the image cache per (path, `renderRev`).** Why: an open handle blocks the atomic rename of a re-baked render, archiving and `deleteSteps` (01 EDGE-MODEL-39). Test: `App.Tests Report/ReportImageLoaderTests.DoesNotHoldTheFile` (load, then `File.Move` and `File.Delete` succeed).

**INV-REP-33. The merge suggestion is offered only on a shot step whose click was a right click and whose next step is also a shot step, both with a non-empty screenshot.** Citation: `merge.ts:26-37`, `Report.tsx:1077`. Test: `Report/MergePlannerTests.CanMergeInto`.

**INV-REP-34. The report column's width never depends on its content: it is `min(repFrame(s), width offered)`, derived from the space offered, never measured off the content.** Why: a content-sized column made small projects cramped and the slider inert (`861749c`); macOS hit the same feedback loop (`macOS:shotAI/ReportView.swift:132-142`). Test: `App.Tests Report/ReportLayoutTests.EmptyAndSmallProjectsUseTheFullColumn` (an empty project and a one-text-step project both lay out at `repFrame(s)`).

**INV-REP-35. `detailWindowWidth(s, W)` never returns less than 1010 or NaN, never exceeds a valid usable width `floor(W)` unless that is below 1010, and equals `repFrame(s) + 130` when there is room.** Citation: `doc-scale.ts:149-166`. Test: `Geometry/DocScaleTests` detailWindowWidth group.

## 5. Edge cases and hard-won fixes

**EDGE-REP-1. Electron's shipped CSS does not implement the derived widths.** `.rep` is `calc(880px * s)` and `.rep__bodywrap` is `calc(820px * s)` (`project.css:1378,1628`); `d50c029` changed only the TS constants and the fit cap, so at 125% the frame is 1100 (not 1084) and at 100% the card is 820 and a full-width figure 784 (the export's image is 738). Required natively: the derived layout of 7.9, where the figure equals the export figure (INV-REP-9). IMPROVEMENT, visible: native cards are narrower than Electron's (Q-REP-2). Source: `d50c029`, reading of `project.css`.

**EDGE-REP-2. The fit before the first measurement.** `availW` is null on the first frame, so the fit uses the scaled cap and can over-size by the card padding for one frame, which is a brief over-size rather than a persistent crop. Required: the same fallback; natively the first measure pass normally provides the width before the first render, so the fallback is rarely visible. Source: `746b012`, `report-geometry.ts:50-54`.

**EDGE-REP-3. Degenerate images.** `dims` null, zero or negative returns an all-zero fit; the figure then keeps its unsized wrap. Required: all-zero fit; natively a decoded image with a zero dimension shows the missing-image state (7.11). Source: `report-geometry.ts:63-65`, `report-geometry.test.ts:90-94`.

**EDGE-REP-4. The height cap scales with the project scale.** Without it, a portrait capture grows sideways and then hits a fixed 600 ceiling so the slider looks inert on exactly the captures that need it. Required. Source: `report-geometry.ts:69-76`; `macOS:Packages/ShotModel/Tests/ShotModelTests/DocScaleTests.swift:220-228`.

**EDGE-REP-5. The pan restore must include the document scale in its dependencies.** On a plateau where the measured width is unchanged, the scale can still change the scrollable range. Required natively: recompute the pan offset whenever the image size, zoom, measured width, scale or stored pan changes. Source: `62b4b7d`, `Report.tsx:127-130`.

**EDGE-REP-6. Strut descender.** A reported 4 px phantom height under the inline image (scrollHeight 494 against clientHeight 490) was never reproduced; `line-height: 0` was added as a precaution. Not applicable natively (WPF has no line box around an `Image`), but the pan range must be computed from the image and box sizes, never from a measured scroll extent that could include padding. Source: `58131a8`, `project.css:1690-1700`.

**EDGE-REP-7. The midpoint asymmetry of `clampScale`.** `clampScale(0.825)` is 0.85 but `clampScale(1.025)` is 1.00 because `1.025 * 100` is `102.49999999999999`. Required: reproduce exactly by running the same double operations (`v * 100` then `JsMath.Round`); never "fix" it. Source: `doc-scale.test.ts:60-82`, `ec76adb`.

**EDGE-REP-8. The content-sized column.** A flex parent with an auto cross margin shrank the report to its content (286 px), predating #70. Required natively: INV-REP-34. Source: `861749c`, `scripts/report-width-probe.cjs`.

**EDGE-REP-9. Empty state scale.** The empty state is a separate root and needed its own `--doc-scale`. Required natively: the empty state's insert zone spans the same scaled column. Source: `861749c`.

**EDGE-REP-10. `+ Add an overview` width.** It carried a fixed 880 inside a scaled column and snapped wider when an overview was saved. Required: the placeholder and the overview box span the same column. Source: `62b4b7d`.

**EDGE-REP-11. Stored zoom above 4.** The IPC clamp allows up to 6 and the read path does not cap, so a hand-edited or older value of 6 renders at 6; zoom-in then REDUCES it to 4 (`min(4, 7.5)`). Exports crop at the stored 6 too. Required: parity (no read cap), so the report keeps matching the export (Q-REP-9). Source: `Report.tsx:76,475`, `ipc.ts:216`.

**EDGE-REP-12. A click without movement at zoom above 1 writes the pan.** The mouseup handler persists even when nothing moved, re-dating the project. Required natively: IMPROVEMENT, the pan operation returns Unchanged when both fractions equal the stored ones (7.4). Source: `Report.tsx:151-162`.

**EDGE-REP-13. Marker color strings that are not six hex digits.** `markerColor` is validated as `^#[0-9a-fA-F]{3,8}$`, so 3- to 8-digit values reach the overlay. Appending `2e` gives 5, 6, 7, 8, 9 or 10 hex digits: a 3-, 5-, 7- or 8-digit input yields invalid CSS and the fill falls back to the class default (`rgba(239, 68, 68, 0.18)`); a 4-digit `#rgba` input yields a VALID 6-digit `#rgba2e`, read as an unrelated opaque `#rrggbb` fill (for example `#f00c` gives the fill `#f00c2e`); only 6-digit input gets the intended 18% fill. The border uses the raw value, so 5- and 7-digit values also fall back to `#ef4444`, while 3-, 4-, 6- and 8-digit values draw as CSS reads them. Required natively: IMPROVEMENT, parse with 04's `CssColor.TryParse` (`#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`), draw the border in the parsed RGB and the fill in the parsed RGB at alpha `0x2E`; an unparseable value uses `#ef4444` and `rgba(239, 68, 68, 0.18)` (the CSS fallback). Source: `ipc.ts:209-211`, `Report.tsx:201`, `project.css:1798-1808`.

**EDGE-REP-14. Zoom-in is never disabled.** At zoom 4 the button stays enabled and each click writes an unchanged 4. Required natively: IMPROVEMENT, disable zoom-in at `zoom >= 4` (macOS does, `macOS:shotAI/ReportView.swift:1218`), and the operation is Unchanged for an equal value. Source: `Report.tsx:210-218`.

**EDGE-REP-15. Rapid zoom clicks coalesce.** Each click computes from the PERSISTED zoom, so two quick zoom-in clicks both write 1.25 instead of 1.5625. Required natively: IMPROVEMENT, each click computes from the displayed (optimistic) zoom, so clicks compound (macOS does the same with `pendingZoom`, `macOS:shotAI/ReportView.swift:1136-1142,1229-1234`). Source: `Report.tsx:215`.

**EDGE-REP-16. No drag to the last position.** Drop inserts before the target row and insert zones are not drop targets. Required natively: parity for rows, plus IMPROVEMENT: the trailing insert zone accepts a drop and moves the step to the end (macOS, `macOS:shotAI/ReportView.swift:115-121`) (Q-REP-6). Source: `Report.tsx:563-572,947-956`.

**EDGE-REP-17. External drags.** A file dragged from Explorer over a row does nothing because `dragIdx` is null. Required natively: only the private step format is accepted; files and text are refused (no drop-to-import). Source: `Report.tsx:947-952`.

**EDGE-REP-18. The delete message mentions an image for text steps.** `Delete this step? (its image file stays on disk)` is shown for text and callout steps too. Required: parity string (Q-REP-8). Source: `Report.tsx:582`.

**EDGE-REP-19. The empty state hides an existing overview and shows an invisible "+".** A project whose steps were all deleted keeps its intro in `project.json`, but the empty state does not render it; the only insert affordance is the hover-revealed `+`. Required: parity for 2.0.0 (Q-REP-7 proposes showing the overview). Source: `Report.tsx:803-818`.

**EDGE-REP-20. Overview editing does not count as a text draft.** Resume capturing and Export stay enabled while the overview editor is open; a recording then unmounts it and loses the draft. Required: parity (Q-REP-10). Source: `Report.tsx:442-444`.

**EDGE-REP-21. `saveText` and `saveIntro` always write.** Saving unchanged text still writes and re-dates; for the intro it also sets `introEditedByUser = true`, which is meaningful to SOP regeneration (07). Required natively: IMPROVEMENT for text (Unchanged when heading and body equal the stored values); for the intro, Unchanged only when both the intro value AND the `introEditedByUser` state already equal what the store would write (7.4). Source: `Report.tsx:632-671`, 01 2.9.3.

**EDGE-REP-22. The package dialog remembers "Include original screenshots".** The checkbox state lives in the detail component, so after one export with originals, the next open of the dialog is still checked. Required natively: IMPROVEMENT [SECURITY], reset to unchecked on every open, so the recoverable-originals mode is always a fresh choice. Source: `ProjectDetail.tsx:281,569,602-607`.

**EDGE-REP-23. The capture guard renders with the import prefix.** `Finish editing the text step before capturing.` is stored in `importErr` and shows as `Import failed: Finish editing the text step before capturing.` Required natively: IMPROVEMENT, show it without the prefix, as an info notice (Q-REP-5). Source: `ProjectDetail.tsx:644-647,656-660`.

**EDGE-REP-24. Reconciliation discards open single-line editors.** Every manifest replacement closes the caption, number and body editors and the insert menu. Whether the removed editor commits its text depends on whether Chromium dispatches `blur`/`focusout` to React for a focused element that is removed from the DOM; this was not tested (UNVERIFIED), and the `done` guard does not decide it. The native choice below (abandon) is therefore a decision, not a measured parity. So an instructions editor opened right after a caption commit is torn down when that caption's write returns, and what the user typed is lost. Required natively: reconciliation runs only on external replacements and rollbacks (7.6); when it does close an editor it abandons (no commit) for parity. Source: `Report.tsx:452-461`.

**EDGE-REP-25. The window line with an empty app.** `{app}{title ? ' \u2014 ' + title : ''}` renders ` \u2014 Title` with a leading space when `app` is empty, and nothing but an empty paragraph when both are empty. Required natively: IMPROVEMENT, the macOS rule (both empty: no line; app only; title only; both joined with ` \u2014 `) (`macOS:shotAI/ReportView.swift:743-753`). Source: `Report.tsx:1125-1130`.

**EDGE-REP-26. Merge always sends a caption.** `joinText` returns `''` when both captions are empty and the patch still carries `caption: ''`, which sets `captionEditedByUser = true` and blanks the caption. macOS omits empty captions. Required: parity (the patch always carries `caption` and `body`), because `captionEditedByUser` affects SOP regeneration identically on both Windows builds. Source: `merge.ts:98-103`; `macOS:shotAI/AppModel.swift:1410-1415`.

**EDGE-REP-27. Merge when the kept step has no click.** The dropped click's own image coordinates are used unmapped (best effort; menus open at the cursor). When the dropped step has no click, no marker is added but the merge still re-bakes and deletes. Required. Source: `merge.ts:57-82`.

**EDGE-REP-28. Merge races with edits on the same steps.** The patch (captions, annotations) is computed at click time; an edit to either step made while the flatten runs is overwritten or lost when the merge lands, and a delete of either makes the merge fail with `step <id> not found`. Required natively: while a merge is in flight, the Delete, Move and caption and body editors of the two involved steps are disabled (IMPROVEMENT; replaces the implicit `busyRef` protection for reorder and delete, which did not cover captions). Source: `Report.tsx:603-618`.

**EDGE-REP-29. The number editor accepts out-of-range numbers.** `0`, negatives and values above `numberedTotal` clamp; decimals truncate (`parseInt`); an entry equal to the current position is a no-op. Required. Source: `Report.tsx:519-535`.

**EDGE-REP-30. A late manifest from a project the user left.** `applyManifest` never checks project identity, so a write for project A that completes after the user went Back and opened project B replaces B's steps on screen (B's disk is untouched; the next B edit re-reads B). Required natively: INV-REP-31. Source: `store.ts:194-206`.

**EDGE-REP-31. Notices scroll away.** The notice stack is positioned in the scrolled document, so a failure notice raised while the user is scrolled down is off-screen. Required natively: IMPROVEMENT, the notice stack is pinned to the top of the report viewport, below the command bar (rollback notices depend on being seen). Source: `project.css:1127-1132`, `notice.css:4-17`.

**EDGE-REP-32. The range persists on key up of ANY key and on pointer up over the control only.** Releasing a slider drag outside the control may not fire `pointerup` on it, leaving a previewed but uncommitted scale on screen until the next interaction. Required natively: IMPROVEMENT, commit on `Thumb.DragCompleted` (always raised, the thumb captures the mouse), on a track click release, and on key up of a key that changed the value. Source: `ProjectDetail.tsx:141-143`.

**EDGE-REP-33. Typing after an immediate apply.** Typing `7` then `0` applies 70% at once (a legal detent), the draft clears, and a further `5` makes `705`, which buffers and then clamps to 125% on Enter. Required: parity (the rule is "a legal detent applies immediately"); documented so it is not mistaken for a native bug. Source: `ProjectDetail.tsx:163-175`, `9a8d887`.

**EDGE-REP-34. Import error texts.** Import, text insert and screenshot failures all use the `Import failed: ` prefix. Required: parity for import; see Q-REP-5 for the others. Source: `ProjectDetail.tsx:365,380,396,407,419,424`.

**EDGE-REP-35. Leaving mid-export.** Only the flatten stage is aborted; an export already writing continues in main. The aborted run shows no error. Required: parity. Source: `ProjectDetail.tsx:287-290,312-331`.

**EDGE-REP-36. Duplicate step ids.** React keys by id, so two steps with the same id (a hand edit or an old bug) produce duplicate keys; the store operates on the first match. Numbering is also id-keyed: `displayNums` is a `Map` from id, so both duplicates show the number of the LATER occurrence, `numberedTotal` counts distinct ids, and the number sequence skips (for `[A, A, B]` the badges read 2, 2, 3 and the number editor's `max` is 2). Required natively: cards are keyed by (id, occurrence) so both render; operations act on the first match (01); numbers are assigned by position (IMPROVEMENT: `[A, A, B]` reads 1, 2, 3). Source: `Report.tsx:465-471,1186`, 01 EDGE-MODEL-25.

**EDGE-REP-37. Resume capturing uses the Home picker's target.** `onRecord(openPath)` calls `buildTarget()` from App's inline Home mode state, not anything in the detail view. Required: parity (06 owns that state). Source: `App.tsx:432-458,632-634`.

**EDGE-REP-38. Stale store fields.** `selectedStepId`, `selectStep` and the store's `updatedAt` (its comment claims a cache-bust use; `renderRev` does that) are never read by any view. ELECTRON-ONLY; not ported. Source: `store.ts:50-51,59,190`.

**EDGE-REP-39. A failed open is silent.** `open` catches every error, nulls `projectPath` and stores a non-"gone" message in `error`, but `error` is rendered only by the detail view, which is no longer mounted, so a damaged `project.json` opens nothing and says nothing (App calls `open` without checking, `App.tsx:296,488`). Required natively: IMPROVEMENT, a failed open that is not "gone" raises `OpenFailed(message)` for 06 to show on Home (01 Q-MODEL-11 recommends the wording). Source: `store.ts:128-151`.

**EDGE-REP-40. EXIF orientation of imported JPEGs.** Chromium applies a JPEG's EXIF orientation when it displays `<img>` and reports the rotated `naturalWidth`/`naturalHeight`; WPF's `BitmapImage` does not. Required natively: the report decodes with the same orientation rule as 04's codec, so the fit, the marker fraction and the merge clamp use the displayed (oriented) size. Source: Chromium `image-orientation: from-image` default; 04 EDGE-EDIT-57 and `IRenderCodec.Decode` (04 7.4), implemented by 02's `WicImageCodec` on WIC COM, which reads the EXIF orientation from the frame metadata and applies it with `IWICBitmapFlipRotator` (04 7.13; WIC does not apply orientation itself, and `ExifOrientationMode` is a WinRT enum that the codec does not use). The decoder is created explicitly for the container the magic bytes name, never by `CreateDecoderFromStream` (R-ARCH-21, 7.11).

**EDGE-REP-41. Settings unmounts the detail view.** Opening Settings from the SOP panel discards every Report and ProjectDetail state (text drafts, inline editors, the insert menu, the package dialog checkbox) and aborts an export flatten in flight, while the project itself stays open (2.23). Required natively: the project stays open and its `IProjectSession` is NOT disposed while Settings is shown (parity); the export flatten is canceled on leaving (parity with the abort); `ReportEditState` and open editor drafts are kept in the view model so they survive the round trip (IMPROVEMENT, Q-REP-20). Source: `App.tsx:630`, `ProjectDetail.tsx:287-290`.

**EDGE-REP-42. A project opens at the top.** The shared scroll container is reset to 0 on entering a project, because it never unmounts and a project would otherwise open part-way down. Required natively: `ProjectDetailView`'s `ScrollViewer` starts at offset 0 for every open, including a reopen after a recording. Source: `App.tsx:266-290`.

**EDGE-REP-43. Reload after a recording.** When a recording on the open project ends, App reopens the project, which shows `Loading\u2026` and remounts the report. Required natively: the capture flow's result is adopted into the existing session through `ApplyDurable`, which raises a `Durable` change that the report reconciles exactly like an `External` one (7.3, 7.6; the ARCHITECTURE 7.4 contract has no member that raises `External` for a caller-supplied manifest); IMPROVEMENT: no `Loading\u2026` flash when the session is still open. Source: `App.tsx:291-298`.

**EDGE-REP-44. Racing opens.** `open` has no generation guard, so the IPC result that resolves last wins even if it belongs to the project the user left, and the first result to resolve clears `loading`. Required natively: IMPROVEMENT, `OpenAsync` carries a generation; a superseded open's result is discarded (its session, if created, is disposed without being shown). Source: `store.ts:107-152`.

**EDGE-REP-45. Outside clicks differ per popover.** The export menu closes on an outside `mousedown` and the click still reaches its target; the step overflow menu and the capture modal's target dropdown close through a full-window backdrop that swallows the click. Required natively: parity for each (7.13). Source: `ProjectDetail.tsx:292-308`, `OverflowMenu.tsx:72-75`, `CaptureInsertModal.tsx:100-102`.

**EDGE-REP-46. Dialog keyboard handling is inconsistent.** The capture modal closes on `Escape` from anywhere (window listener); the confirm dialog only while focus is inside it (the confirm button is auto-focused); the package dialog has no `Escape` handling and no `aria-modal`; none of the three traps Tab. Required natively: `Escape` closes (cancels) all three and Tab cycles inside each (IMPROVEMENT, D-REP-22); the confirm button keeps initial focus so Enter confirms (parity). Source: `CaptureInsertModal.tsx:37-43`, `useConfirm.tsx:63-65`, `ProjectDetail.tsx:588-629`.

**EDGE-REP-47. Out-of-range or non-number framing values on disk.** The IPC clamps writes, but a hand-edited or foreign manifest can hold `reportPanX: 5`, `-1`, `"x"` or `reportZoom: "x"`. Electron's report clamps the pan through the scroll range (a non-number becomes 0) and renders a non-number zoom as NaN (a broken figure); the export treats a non-finite pan as 0.5 and any zoom that is not `> 1` as the full image (`export-geometry.ts:101,114-117`). Required natively: `PanX`, `PanY` = the raw value when it is a finite number, clamped to `[0, 1]`, else 0.5; `Zoom` = `Math.Max(1, raw)` when the raw value is a finite number, else 1 (IMPROVEMENT for the non-number cases, chosen so the report agrees with the export; numbers above 4 are still not capped, EDGE-REP-11). Source: `Report.tsx:76,125-126`, `ipc.ts:216-218`.

**EDGE-REP-48. Numbering with duplicate ids.** See EDGE-REP-36 (the `Map` gives both duplicates the later number). Required natively: numbering by position.

**EDGE-REP-49. The capture modal's error line is sticky.** A failed `listTargets` or area selection leaves its message on screen after a later success. Required: parity for 2.0.0 (the modal is short-lived); clearing it on the next successful load is a safe later change. Source: `CaptureInsertModal.tsx:28-33,202`, `useCaptureTarget.ts:93-95,123-125`.

**EDGE-REP-50. A new render is fitted with the old image's size for one load.** `dims` survives a `src` change until the new image's `load`, so after a crop the new, smaller render is briefly sized and marked with the previous aspect. Required natively: IMPROVEMENT, keep showing the previous decoded image AND its size until the new decode completes, then swap image and natural size together (no loading placeholder flash, no wrong aspect). Source: `Report.tsx:57,66-71,191-196`.

**EDGE-REP-51. Pan on any mouse button.** `onMouseDown` does not check the button, so a right or middle drag at zoom above 1 pans and writes the framing. Required natively: IMPROVEMENT, left button only (7.10). Source: `Report.tsx:132-165,176`.

## 6. macOS port notes

The Swift report (`macOS:shotAI/ReportView.swift`) is a single SwiftUI view over `AppModel`, with the pure rules in `ShotModel` (`ReportPresentation`, `DocScale`) so they are unit tested.

| Area | macOS implementation | Divergence from Electron | Lesson for Windows |
|---|---|---|---|
| Numbering | `ReportPresentation.displayNumbers` (`macOS:Packages/ShotModel/Sources/ShotModel/ReportPresentation.swift:44-52`); `isCalloutStep = kind == .text && callout != nil` (`:28-30`) where `callout` is an enum, so an unknown value decodes to nil | same numbers as Electron after #99; macOS DROPS an unknown callout on decode (data retention differs, not numbering) | keep Electron's raw retention (01) and the `isCalloutKind` narrowing at use |
| Geometry | `DocScale.reportFrame(s) = htmlColumn(s) + 2 * docPadding` (`macOS:Packages/ShotModel/Sources/ShotModel/DocScale.swift:102-104`), `reportColumnFitting(s, available) = min(reportFrame(s), max(1, available))` (`:118-120`), `reportFigureChrome = 2 * 32 + 78 = 142` (`:136`); the report spends exactly that chrome (32 outer padding, badge 32 plus spacing 14, card padding 16) (`macOS:shotAI/ReportView.swift:123-131,523-532,649-651`) | the figure equals the export figure (`DocScaleTests.reportFigureMatchesExport`, `macOS:Packages/ShotModel/Tests/ShotModelTests/DocScaleTests.swift:95-100`); Electron's CSS does not (EDGE-REP-1) | adopt the macOS layout arithmetic (7.9); derive the column from the space OFFERED, never from the content (`macOS:shotAI/ReportView.swift:132-142`, the same feedback loop as `861749c`) |
| Viewport | `ReportPresentation.viewport` (`ReportPresentation.swift:97-139`): fractional sizes (no floor), zoom clamped `[1, 4]` on READ, height cap `600 * docScale`, pan clamped `[0, 1]` | no integer flooring; read cap at 4 (Electron floors only) | Windows keeps Electron's integer floor (INV-REP-8) and no read cap (EDGE-REP-11) |
| Marker | `markerFraction` (`:67-80`), `markerColorHex` (`:23-25`), `MarkerRing` 22 pt, 2.5 stroke, 18% fill, white halo (`macOS:shotAI/ReportView.swift:1328-1340`) | same rule | port the tests (`ReportPresentationTests.swift:70-106`) |
| Optimistic zoom and pan | `pendingZoom` and `liveOffset` in the figure, cleared when the persisted framing round-trips (`macOS:shotAI/ReportView.swift:1127-1177,1229-1262`); zoom-in disabled at the max (`:1218`) | macOS is optimistic for zoom and pan only; every other edit waits for the store (`AppModel.swift:1185-1350`) | Windows generalizes this to every edit through `IProjectSession` (7.5), so no per-control pending state is needed |
| Inline editing | `InlineEditable`: an always-present plain `TextField`, commit on focus loss and Return (Shift+Return newline in multi-line), Esc discards, writes only when changed (`:1000-1060`); a shared `FocusState` so a click on dead space commits; Tab moves between report fields in document order via a key monitor (the monitor `:158-174`, `TabNavigator` `:950-974`) | no Save/Cancel text editor, no fresh-step delete-on-cancel, Tab navigation between fields | keep Electron's three editor kinds for parity (2.12); Tab-between-fields is a candidate IMPROVEMENT (Q-REP-12) |
| Title | click-to-edit title in the report header with placeholder `Untitled guide`, rejects an empty title (`:355-374`); meta line `Created`, `Updated`, `N numbered step(s)` (`:376-386`) | Electron has no title editing or meta line in the detail view | not in parity scope (Q-REP-3) |
| Insert menu | a native `Menu`: Screenshot submenu (Capture area, Capture a window, Capture the screen), Capture steps, Image, Text block, Section heading, Note, Caution, Warning (`:446-459`); area captures immediately without a modal (`:326-331`) | different structure and wording | keep Electron's inline three-row menu and the modal (2.17, 2.19) |
| Drop to end | the trailing insert zone is a drop destination (`:115-121`); drag auto-scroll timer at 30 ms, 64 pt band, 26 pt max step (`:1366-1451`) | Electron cannot drop at the end (EDGE-REP-16) | adopt both (IMPROVEMENT and parity with Chromium's auto-scroll) |
| Number entry | `moveStep(id, toPosition)` inserts after the target when moving down, before when moving up (`macOS:shotAI/AppModel.swift:1312-1331`) | same final order as Electron's `commitNum` for numbered targets | port Electron's absolute-index form (2.14) |
| Delete | confirmation `Delete this step?` with `This removes the step and its screenshot. This can't be undone.`; deletes the files (`deleteSteps`) (`macOS:shotAI/ReportView.swift:183-195`, `AppModel.swift:1284-1291`) | Electron keeps the files | keep Electron's `deleteStep` (files stay) and its message |
| Merge | also in the step menu (`Merge into next step`); `Undo merge` banner (`:342-353`, `AppModel.swift:1434-1444`); omits empty joined captions; sets `crop` explicitly; marker id `merge-<dropId>` with the click radius (`AppModel.swift:1364-1430`) | extra entry points and undo; caption omission | keep Electron's banner-only merge and patch (EDGE-REP-26); no undo in 2.0.0 |
| Scale control | `DocScaleControl` plus `PercentField` (`macOS:shotAI/DocScaleControl.swift:1-186`, `PercentField.swift:1-123`): slider over detent indices, preview on drag, persist on release; stepper and arrows apply immediately; typed legal detents apply immediately; Return commits and KEEPS focus; Escape clears the preview (not "set to committed"); a 4th digit is refused (never truncated); the edit target is captured at edit start | Escape semantics, focus after Return, 3-digit cap | three lessons adopted (7.7): the preview is a nullable override, never a copy of the committed value (setting it to the committed value shadowed the next project's scale, `DocScaleControl.swift:169-179`, `AppModel.swift:429-437`); capture the session at edit start (a blur during teardown otherwise writes to nothing, `DocScaleControl.swift:37-42`); never truncate typed text (`:136-144`) |
| Stepper hold | the window resize is held while the pointer is over the stepper, because each click commits and the center-preserving resize walks the button away (`macOS:shotAI/DocScaleControl.swift:72-82`, `AppModel.swift:410-424`) | Electron has the same walk on spinner clicks, unmitigated | adopt it for the percent box spinner buttons (answers 03 Q-SHELL-13) |
| Failure handling | every edit catches, sets `errorMessage` and logs (`AppModel.swift:1190-1350`) | Electron swallows most report failures | Windows shows the rollback notice (7.16) |
| Image load | `confinePath` then `CGImageSource`, pixel sizes; missing file shows `Image missing: <path>` (`ReportView.swift:1155-1160,1264-1279`); reload keyed on `renderRev` (`:1169`) | Electron shows a broken image | adopt the missing-image state (7.11) |

## 7. Native design (C#)

### 7.1 Placement

| Namespace | Project | Types |
|---|---|---|
| `ShotAI.Core.Geometry` | ShotAI.Core | `DocScale` (constants, `Clamp` shared with 01, `Detents`, `IsLegal`, `DetentIndex`, `DetentAt`, `Widths`, `DetailWindowWidth`), `DocWidths`, `ReportGeometry` (`Fit`, `WrapContentSlack`), `ReportFit`, `ImageSize`, `ReportLayout` (native layout constants, 7.9) |
| `ShotAI.Core.Report` | ShotAI.Core | `ReportPresentation`, `StepContext`, `StepNumberEntry`, `ReorderPlanner`, `MergePlanner`, `MergePlan`, `ReportEditState`, `DocScaleEditor`, `CaptureTargetPicker`, `PopoverPlacement`, `CardListDiff`, `WindowLine`, `ReportStrings`, `ReportEditPolicy`, `IImageSizeProbe` (the Core seam that Platform's `WicImageSizeProbe` implements, ARCHITECTURE 2.3). `ManifestChangeKind`, `ManifestChangedEventArgs` and `PersistFailedEventArgs` are NOT here: they live in 01's `ShotAI.Core.Store` beside `IProjectSession`, which raises them (R-ARCH-20) |
| `ShotAI.Core.Report.Operations` | ShotAI.Core | `ReportOperation` and the eleven concrete operations of 7.4, `TextStepFactory` |
| `ShotAI.Platform.Imaging` | ShotAI.Platform | `ReportImageDecoder` (public; WIC COM through CsWin32, using `ShotAI.Platform.Imaging.WicFactory` (02 7.1), the shared MTA factory, and 02's `WicImageCodec` orientation and color helpers; magic-byte check, explicit PNG or JPEG decoder (R-ARCH-21), scaled and oriented decode), `WicImageSizeProbe` (`internal sealed`, registered only as `IImageSizeProbe`, INV-ARCH-4; the same explicit decoder, metadata only), `CursorPosition` (public, CsWin32 `GetCursorPos`) |
| `ShotAI.App.Report` | ShotAI.App | Views: `ProjectDetailView`, `CommandBar`, `DocScaleControl`, `ExportButton`, `PackageExportDialog`, `ReportView`, `StepCardTemplates` (resource dictionary with the four card templates and `StepCardTemplateSelector`), `StepRail`, `ReportFigure`, `InsertZone`, `InsertMenu`, `InlineEditBox`, `TextStepEditor`, `OverflowMenuButton`, `CaptureInsertDialog`, `NoticeStack` (03's control, hosted). The confirm and alert dialogs are 06's `ConfirmHost`, reached through `IConfirmService` (7.13); 05 has no dialog control of its own for them. View models: `ProjectDetailViewModel`, `ReportViewModel`, `StepCardViewModel`, `IntroViewModel`, `DocScaleViewModel`, `ExportViewModel`, `CaptureInsertViewModel`, `NoticeStackViewModel`. Services: `ReportImageLoader`, `DragReorderController`, `DragAutoScroller`, `MergeCoordinator`, and the per-open factories for `ReportViewModel` and `DocScaleEditor` (ARCHITECTURE 4.1 C6). Registrations are added to `AddShotAIApp` and `AddShotAIPlatform` (7.19, ARCHITECTURE 4.1 C7) |

As built in WP-A17: `ShotAI.Core.Report` also has `CardContext` (a step's index, number and first and last flags, beside `StepContext`), `ReportImageKey`, `ReportFraming`, `MarkerStyle`, `ReportMarker` (the ring's point in the pixels of the image shown, and its colours) and `MarkerFraction` (beside `ReportPresentation`), `OpenFailure` (the gone rule of 7.3) and `ReportImageFile` (the read of 7.11 step 2, which the loader and the probe share). 04's `CssColor`, `Rgba` and `AnnotationStyle` (`ShotAI.Core.Editor`) and `ScreenshotLoadException` land here too, for the ring's colour and the probe's refusal. `ShotAI.Platform.Imaging` also has `DecodedImage` and, internal, `WicDecoding` (the one decode path of 7.11, which 02's `WicImageCodec`, as 04's `IRenderCodec`, is to call rather than copy, WP-C10) and `WicScope` (the release of one decode's WIC objects); `CursorPosition` joins with WP-C3. In `ShotAI.App.Report`, `ReportView` is not a view of its own: the frame, the overview and the cards are part of `ProjectDetailView`, through `ReportFrame` (a `Decorator`, 7.9) and `ReportList` (7.18). There is no `StepCardTemplateSelector`: `StepCard.Row` picks the layout with a `DataTrigger` on the card's `Kind`, because WPF runs a selector once per container, and a card whose kind changes (a callout conversion, WP-C2) would keep its old layout. The App also has `StepCardKind`, `ReportNoticeSlot` and `ReportImage` (a decoded image as a figure holds it).

Nothing in `ShotAI.Core` references WPF or Windows. Every rule that can be stated without pixels on screen lives in Core and is tested on Linux (8.2).

### 7.2 Geometry (Core)

```csharp
namespace ShotAI.Core.Geometry;

public static class DocScale
{
    public const double Min = 0.65, Max = 1.25, Step = 0.05, Default = 1.0;
    public const int HtmlColumnBase = 816, DocPadding = 32, ReportColumnBase = HtmlColumnBase;
    public const int ReportFrameBase = HtmlColumnBase + DocPadding * 2;         // 880
    public const int StepBadgeWidth = 30, StepGap = 16, StepCardPadding = 32;
    public const int StepChrome = StepBadgeWidth + StepGap + StepCardPadding;   // 78
    public const int DetailWindowBase = 1010, WindowChrome = DetailWindowBase - ReportFrameBase; // 130
    public const int ImageMaxFloor = 120;

    /// k / 100.0 for k = 65, 70, ..., 125 (13 values). Built from integers so every
    /// element has the same bits as the literal and as Clamp's result.
    public static IReadOnlyList<double> Detents { get; }

    /// INV-REP-10, exactly: object? so the codec can pass any JSON value.
    /// Non-number or non-finite -> 1.0; pct = JsMath.Round(v * 100); clamp [65, 125];
    /// pct = JsMath.Round(pct / 5) * 5; return pct / 100.
    public static double Clamp(object? value);
    public static double Clamp(double value);

    public static bool IsLegal(double value);             // exact membership in Detents (== on doubles)
    public static int DetentIndex(double value);          // IndexOf(Clamp(value)); never -1 (falls back to the index of 1.0)
    public static double DetentAt(int index);             // Detents[Math.Clamp(index, 0, 12)]

    public static DocWidths Widths(double scale);         // docWidths: s = Clamp(scale); htmlCol = (int)JsMath.Round(816 * s); ...
    public static int DetailWindowWidth(double scale, double workAreaWidth);   // 2.6 formula; floor via Math.Floor
}

public readonly record struct DocWidths(int RepFrame, int ReportColumn, int HtmlColumn, int HtmlDoc,
                                        int HtmlImageMax, int HtmlImageEmbedMax);

public readonly record struct ImageSize(double Width, double Height);
public readonly record struct ReportFit(double BaseW, double BaseH, double WrapW, double WrapH);

public static class ReportGeometry
{
    public const int BaseWidth = DocScale.ReportColumnBase;   // REPORT_BASE_W
    public const int BaseHeight = 600;                         // REPORT_BASE_H
    public const int WrapBorder = 1;

    /// reportFit (2.11) with the same double operations in the same order:
    /// capW = JsMath.Round(816 * s); budgetW = Math.Max(1, Math.Min(capW, availW ?? capW) - 2); ...
    /// baseW = Math.Floor(w * fitScale); wrapW = JsMath.Round(baseW * Math.Min(zoom, 1)) + 2.
    public static ReportFit Fit(ImageSize? image, double? availableWidth, double zoom = 1, double scale = 1);
    public static (double X, double Y) WrapContentSlack(ReportFit fit);
}
```

`Clamp(object?)`: `double` and the JSON number kinds are numbers; anything else (null, string, bool, object, array, `JsonNode` of another kind) gives 1.0. The codec (01) and the UI use the same function. `Widths`: `htmlCol = JsMath.Round(816 * s)`, `htmlImgMax = Math.Max(120, htmlCol - 78)`, `repFrame = htmlDoc = htmlCol + 64`, `reportCol = htmlCol`, `embed = htmlImgMax * 2`. `DetailWindowWidth`: `want = Widths(s).RepFrame + 130`; `usable = (double.IsFinite(W) && W > 0) ? Math.Floor(W) : want`; `return (int)Math.Max(1010, Math.Min(want, usable))`. As built in WP-A15, with 03's window sizing: `DetailWindowWidth` and the constants it needs (`HtmlColumnBase`, `DocPadding`, `ReportFrameBase`, `DetailWindowBase`, `WindowChrome`) landed ahead of the report, computing the frame as `JsMath.Round(816 * Clamp(s)) + 64`, which is `Widths(s).RepFrame`; `Widths` and the detents join in WP-A17, and the `detailWindowWidth` group of `doc-scale.test.ts` is `Geometry/DocScaleDetailWindowWidthTests`.

The arithmetic `816 * s` is done in `double` exactly as JS does (`816 * 0.7` is `571.1999999999999`, rounding to 571); tests compare against the table in section 3.

### 7.3 Session and view lifecycle (App)

One `IProjectSession` (ARCHITECTURE 7.4, the consolidated contract S1 to S10, R-ARCH-23; `ShotAI.Core.Store`) per open project, owned by `ProjectDetailViewModel`. The view model never constructs a session: it receives `IProjectService` and `IProjectSessionFactory` by injection (R-ARCH-4, INV-ARCH-3) and every session comes from `IProjectSessionFactory.Create(OpenedProject)` called on the UI thread, because the session captures the UI synchronization context at creation and posts `Changed` and `PersistFailed` to it (R-ARCH-5, ARCHITECTURE 6.3). App code awaits without `ConfigureAwait(false)` (T4), so the continuation after `OpenProjectAsync` is already on the UI thread when it calls `Create`.

| State | Event | Action | Next |
|---|---|---|---|
| Closed | `OpenAsync(path)` (from 06 or the capture flow) | `IsLoading = true`; `gen = ++openGeneration`; `opened = await projects.OpenProjectAsync(path)` (`IProjectService`, returns `OpenedProject(Dir, Manifest)`); if `gen != openGeneration` (a newer `OpenAsync` or a `Back` happened meanwhile) return without creating a session; `session = sessionFactory.Create(opened)` (UI thread; the factory registers it with `IProjectSettle`, S7); subscribe `Changed` and `PersistFailed`; create the per-session `ReportEditState` and `DocScaleEditor` and the `ReportViewModel` through their factories (7.19), and 07's `SopPanelViewModel` through `SopPanelViewModelFactory.Create(IProjectSession)` with this session (07 7.10, R-ARCH-16), subscribing its `SopChanged` event; `ReportViewModel.Sync(session.Current, ManifestChangeKind.External, null)` | Loaded |
| Closed | `OpenAsync` throws | if the exception is `FileNotFoundException`, `DirectoryNotFoundException`, or its message matches `ENOENT\|no such file\|not found` (`RegexOptions.IgnoreCase \| RegexOptions.CultureInvariant`, parity with `store.ts:134`): navigate Home silently; else navigate Home and raise `OpenFailed(message)` for 06 (EDGE-REP-39, IMPROVEMENT) | Closed |
| Closed | `Adopt(dir, manifest)` (the capture flow's `applyOpened`) | as a successful open without the store call: `session = sessionFactory.Create(new OpenedProject(dir, manifest))` on the UI thread, then the same subscriptions and initial `Sync(..., External, null)` | Loaded |
| Loaded | `Adopt(dir, manifest)` for the open project (`Path.GetFullPath(dir)` equal to the session's `ProjectDir`, `OrdinalIgnoreCase`, 02 D9; a recording on the open project ended, EDGE-REP-43) | `await session.ApplyDurable(_ => Task.FromResult(manifest))`: `Current` becomes the adopted manifest with any later optimistic operation re-applied (S5), and the session raises `Changed(Durable)`, which 7.6 reconciles exactly like `External`; no `Loading\u2026`, no new session | Loaded |
| Loaded | `Adopt(dir, manifest)` for another project | as `Back` without navigating, then as the Closed row | Loaded |
| Loaded | `Back` | `++openGeneration`; cancel the view's `CancellationTokenSource` (image loads, merge flatten, export flatten); unsubscribe; `await session.DisposeAsync()` in the background (it stops raising events at once; the session stays registered with `IProjectSettle` until every write it accepted has finished, so a Home export or SOP run started right after Back still waits for them; pending writes drain on the shared queue and are never canceled, S9, R-ARCH-27); navigate Home; ask the shell `IMainWindowLayout.SetDetailView(false, 1)` (03) | Closed |
| Loaded | session `Changed` | `ReportViewModel.Sync(session.Current, e.Kind, e.AffectedStepIds)` (7.8; `e` is 01's `ManifestChangedEventArgs`) | Loaded |
| Loaded | session `PersistFailed` | the rollback notice (7.5) and draft recovery from `e.Operation` (R4, R5, R8, R10) | Loaded |

Every handler checks `sender == currentSession` and ignores events from a disposed or previous session (INV-REP-31, S8). Opening B while A's session still drains is allowed; A's writes complete on the shared queue. The open generation makes a superseded open's result a no-op: no session is created for it, so nothing registers with `IProjectSettle` and nothing is shown (EDGE-REP-44, IMPROVEMENT; ARCHITECTURE 5.3). Navigating to Settings (06) is NOT `Back`: the session, `ReportEditState` and the open editors' drafts stay alive; only the view-level token's export flatten is canceled (EDGE-REP-41, D-REP-25). Each open starts the report `ScrollViewer` at offset 0 (EDGE-REP-42).

`ProjectDetailViewModel` exposes, as inputs to 06's `NavigationState` (the implementation of 03's `IShellNavigationState`, ARCHITECTURE 4.3 and 5.3): `bool ProjectOpen`, `string? OpenProjectPath`, `string? RawProjectTheme` (from `Current.Theme`, passed through untouched, INV-IPC-14), `double CommittedScale` (`DocScale.Clamp(Current.DisplayScale)`), raising `Changed`. The View, Brand menu (03) edits through `session.Apply(new SetProjectThemeOperation(brand))` (03 7.4.5, ARCHITECTURE 7.5; the brand ids are 10's, R-ARCH-14; it follows 7.5's rules).

Corrected in WP-A17: the gone test also looks through the exceptions a failure wraps (`OpenFailure.IsGone`), because the store reports a missing `project.json` as a `ManifestCorruptException` around the `FileNotFoundException` (01 7.13), where Electron's message carried `ENOENT`. `OpenFailed` carries the exception, not its message, so Home shows it through `INoticeService.ShowError(Exception)` by 11's `UserMessage` rules: a damaged manifest reads as Q-MODEL-11 words it, and an unexpected exception as the generic sentence.

As built in WP-A17: `OpenAsync(path)` returns `Task<bool>`, true when this open is the one shown, and the shell shows the project view only then, so a failed open never leaves Home and "navigate Home" is staying there (06 2.1). `IsLoading` shows `Loading\u2026` while the store reads. A failure while another project is open leaves that project open. `Back` is a `[RelayCommand]` that raises `Closed`, which the shell turns into `CloseProject`; with no project open it does nothing. `Dispose` (the app closing) closes the session without navigating. Each open makes a new `NoticeStackViewModel`, so a project starts with no notices, as the remounted Electron view did. A change that commits a new scale calls `SetDetailView(true, CommittedScale)` again. The log lines, each with the exception: Debug `report: the project to open is gone`, Warning `report: open failed:` and Warning `report: closing the session failed:` (a session disposal that throws). Nothing cancels the image loads centrally: Back clears the report, its figures unload, and each cancels its own load. The `PersistFailed` subscription, `ReportEditState` and `DocScaleEditor` join with WP-C2 and WP-C3, 07's panel with WP-D8, `Adopt` with WP-B9 and WP-C4, and the view-level token with the merge and export flattens (WP-C12, WP-D10).

As built in WP-A18: the project view makes P8's edit. `SetProjectTheme(path, brand)`, which the shell calls with 03's menu choice, builds `SetProjectThemeOperation(brand)` (an id outside the catalog throws there, D-IPC-9) and applies it to the open session only when that session's project is `path` (INV-SHELL-17); otherwise nothing happens. Its `ApplyAsync` observes the task by 7.5's rule: an operation the clone refused is logged at Warning (`report: an edit did not apply: {Operation}`) and shown as the rollback notice, a `StepNotFoundException` from the clone shows nothing, and a write the disk refused is left to `PersistFailed`. The view subscribes to `PersistFailed` on open and unsubscribes on close, ahead of WP-C2: it logs `report: a change could not be saved and was undone: {Operation}` with the exception, at Warning, or at Error when `UserMessage.IsUnexpected` (11 L7), and shows the Save slot's notice. The session's `Changed` after the edit, and after its rollback, raises `RawProjectTheme`, which the shell follows into 06's `NavigationState`, so the theme and the menu show the pin at once and go back with a rollback.

### 7.4 Operations (Core)

Every report edit is a `ReportOperation : ProjectOperation` (`ProjectOperation` is 01's, `ShotAI.Core.Store`, ARCHITECTURE 7.4): `Apply(ProjectManifest m)` is pure, deterministic, performs the edit in place and returns `Changed` or `Unchanged`; ids and all inputs are fixed at construction (S10). The same instance runs twice: once on a clone of `Current` (the optimistic step, S1) and once inside the `IProjectService.MutateAsync` job that the session queues, on the fresh disk read. A step lookup always takes the FIRST step whose `Id` equals the id (`StringComparison.Ordinal`); a missing step throws `StepNotFoundException($"step {id} not found")` (01's message).

```csharp
namespace ShotAI.Core.Report.Operations;

using ShotAI.Core.Store;   // ProjectOperation, MutateResult (01; R-ARCH-20)

public abstract class ReportOperation : ProjectOperation
{
    /// Steps whose card content can change; null means structural (order, count or numbering may change).
    /// Overrides ProjectOperation's virtual AffectedStepIds, which returns null (R-ARCH-20); every
    /// report operation must state its own set, so the override is abstract.
    public abstract override IReadOnlyList<string>? AffectedStepIds { get; }
}
```

| # | Operation (constructor) | `Apply` (exact) | Unchanged when | Electron equivalent |
|---|---|---|---|---|
| R1 | `SetReportZoomOperation(string stepId, double zoom)`; ctor `Zoom = Math.Max(1, Math.Min(4, zoom))` | find step; `StepPatchApplier.ApplyAndInvalidate(step, new StepPatch { ReportZoom = Optional<double>.Of(Zoom) }, hasFreshPng: false)` (04; every `StepPatch` field is an `Optional<T>`, 04 7.5, so each patch below is written the same way) | the step's raw `reportZoom` is a number equal to `Zoom` (IMPROVEMENT, EDGE-REP-14) | `updateStep {reportZoom}` |
| R2 | `SetReportPanOperation(string stepId, double panX, double panY)`; ctor clamps each to `[0, 1]` | patch `ReportPanX`, `ReportPanY` | both raw values are numbers equal to the new ones (IMPROVEMENT, EDGE-REP-12) | `updateStep {reportPanX, reportPanY}` |
| R3 | `MoveStepOperation(string stepId, int destIndex)` | `from` = index of the step; `dest = Math.Clamp(destIndex, 0, count - 1)`; remove at `from`; insert at `dest`; `StepList.Renumber` (01) | `from == dest` | `reorderSteps(ids)` (same final order; IMPROVEMENT: expressed as an intent so a replay after a rollback moves the right step, and 01 D-10 no-drop is moot) |
| R4 | `SetCaptionOperation(string stepId, string caption)` | patch `Caption` (the applier sets `captionEditedByUser = true`) | raw `caption` is a string equal (Ordinal) to `caption` (parity with the UI guard) | `updateStep {caption}` |
| R5 | `SetBodyOperation(string stepId, string body)` | patch `Body` | `(step.Body ?? "") == body` (parity) | `updateStep {body}` |
| R6 and R11 | `DeleteStepOperation(string stepId)` | remove the first match; renumber; files stay on disk | never (a missing step throws) | `deleteStep` |
| R8 | `SetTextOperation(string stepId, string heading, string body)` | patch `Heading`, `Body` | raw `heading` and raw `body` are strings equal to the new ones (IMPROVEMENT, EDGE-REP-21) | `updateStep {heading, body}` |
| R9 | `SetCalloutOperation(string stepId, string? callout)`; ctor requires null or a known kind | patch `Callout` (null REMOVES the key, 01 EDGE-MODEL-42) | `step.CalloutRaw == callout` (Ordinal; raw, so `tip` to null is a change) | `updateStep {callout}` |
| R10 | `SetIntroOperation(string heading, string body)`; ctor `h = JsString.Trim(heading)`, `b = JsString.Trim(body)`, `Intro = (h.Length > 0 \|\| b.Length > 0) ? new SopIntro(h, b) : null` | `m.Intro = Intro`; `m.IntroEditedByUser = Intro is not null` (the key is removed when false) | `Equals(m.Intro, Intro) && m.IntroEditedByUser == (Intro is not null)` (IMPROVEMENT, EDGE-REP-21) | `setIntro` |
| P1 | `AddTextStepOperation(double atIndex, string? callout)`; ctor `NewId = Guid.NewGuid().ToString("D")` (lowercase), callout null or known | `step = TextStepFactory.Create(NewId, Callout)`; `i = JsMath.ClampIndex(atIndex, count)` (01); insert at `i`; renumber | never | `addTextStep` |
| P4 | `SetDisplayScaleOperation(double scale)`; ctor `Scale = DocScale.Clamp(scale)` | `m.DisplayScale = Scale == 1 ? null : Scale` | `(m.DisplayScale ?? 1) == Scale` (01 D-11, INV-REP-12) | `setDisplayScale` |
| P8 | `SetProjectThemeOperation(string? brand)`; ctor requires null or a brand id (`BrandPalette.IsBrandId`, D-IPC-9); added in WP-A18 | `m.Theme = Brand` (null removes the key, a brand pins it, the default included, #77) | `m.Theme` equals `Brand` (Ordinal, raw: an unrecognised pin cleared by null is a change, #107) | `setProjectTheme` |

`TextStepFactory.Create(id, callout)` returns a `ProjectStep` over a new `JsonObject` with keys in exactly this order: `id`, `order: 0`, `kind: "text"`, `screenshot: ""`, `trigger: "hotkey"`, `click: null`, `monitor: null`, `window: null`, `element: {available: false, name: null, controlType: null, bounds: null}`, `caption: ""`, `heading: ""`, `body: ""`, `callout` (only when non-null), `crop: null`, `annotations: []` (`project-store.ts:946-962`). 01's `AddTextStepAsync` must call the same factory so the optimistic and store paths cannot drift (section 10). Landed in WP-A7 with the store: `Create` throws `ArgumentException` for an empty id and for a non-null callout that is not a known kind (V13), so `AddTextStepOperation`'s constructor check and the store's are the same check.

`AffectedStepIds`: R1, R2, R4, R5, R8 return `[stepId]`; R9 returns null (numbering of later steps changes); R3, R6, P1 return null; R10, P4 and P8 return an empty list (no card changes; the overview, the layout or the theme does).

As built in WP-A18: `ReportOperation` and `SetProjectThemeOperation` landed with View, Brand, ahead of the other operations (WP-C1), and 01's `ProjectStore.SetProjectThemeAsync` applies the same operation, so the store path and the optimistic path cannot drift.

### 7.5 Optimistic editing policy (the required improvement)

Rule: every report edit that writes only `project.json` goes through `session.Apply(op)`: the screen changes immediately, the write is queued FIFO behind any earlier write, clicks are never dropped, and a failure rolls back and shows the rollback notice. Edits that write a file besides `project.json` go through `session.ApplyDurable(...)` (its argument is a `Func<IProjectService, Task<ProjectManifest>>`, S5, R-ARCH-5) and show their result only after the store call returns. The editor save path is the named exception of the fixed decision and is durable. This table is the report's part of ARCHITECTURE 7.5.

`ReportViewModel` issues every optimistic edit through one helper that observes the returned task (`VSTHRD110`, ARCHITECTURE 14.4): a `StepNotFoundException` from the clone (S2) means the target is gone and shows nothing; any other exception from the clone is logged at Warning and shown as the rollback notice (the edit never reached the screen or the queue); a persistence failure is shown once, from `PersistFailed` (S4), never a second time from the faulted task.

| # | Path | Mode | What the user sees immediately | On success | On failure |
|---|---|---|---|---|---|
| R1 | zoom in, out, reset | `Apply(SetReportZoomOperation)` | the new zoom, computed from the DISPLAYED zoom so rapid clicks compound (EDGE-REP-15) | nothing further (the persisted echo is value-equal) | rollback to the persisted zoom; rollback notice |
| R2 | pan release | `Apply(SetReportPanOperation)` | the image stays where it was dropped | nothing further | rollback (the image jumps back); notice |
| R3 | up, down, number entry, drop | `Apply(MoveStepOperation(stepId, dest))` with `dest` computed from `Current` at the moment of the gesture | the card moves; numbers update; up and down enable states update | nothing further | rollback to the persisted order plus pending ops; notice |
| R4 | caption commit | `Apply(SetCaptionOperation)` | the new caption (Electron showed the old text until the write returned) | | rollback; notice; the caption editor re-opens seeded with the rejected text when no inline editor is open and the step exists (IMPROVEMENT, draft recovery) |
| R5 | instructions commit | `Apply(SetBodyOperation)` | the new instructions | | as R4 for the instructions editor |
| R6 | delete (after the confirm) | `Apply(DeleteStepOperation)`, with the step id captured when the confirm opened; if the step is already gone from `Current`, do nothing | the card disappears | | the card reappears in its persisted position; notice |
| R7 | merge | durable: `MergeCoordinator` (7.14): flatten off the UI thread, then `ApplyDurable(s => s.MergeStepsAsync(path, keepId, dropId, patch, png))` | `Merging\u2026` on the banner; merge buttons disabled; Delete, Move, caption and instructions editing disabled on the two involved steps (EDGE-REP-28) | the merged card, kept step's new render | alert `Could not merge these steps: {message}` (parity); nothing changed on screen |
| R8 | text editor Save | `Apply(SetTextOperation)`; the editor closes immediately; `fresh` cleared for this step | the new heading and body | | rollback; notice; the text editor re-opens seeded with the rejected heading and body if no text editor is open and the step exists |
| R9 | callout conversion | `Apply(SetCalloutOperation)` | the new card style and renumbered badges | | rollback; notice |
| R10 | overview Save, Remove | `Apply(SetIntroOperation)`; the editor closes immediately | the overview (or its removal) | | rollback; notice; the overview editor re-opens with the draft on a failed Save |
| R11 | Cancel on a fresh blank plain text step | `Apply(DeleteStepOperation)` | the step disappears | | the empty step reappears; notice |
| P1 | insert Text, Note, Caution, Warning, Section | `Apply(AddTextStepOperation)`; then open its editor at once using `op.NewId` and mark it fresh (no round trip) | the new step with its editor open | | the step disappears; its latches are dropped (vanished); notice |
| P2 | insert Image | durable: `ApplyDurable(s => s.ImportStepAsync(path, bytes, atIndex))` (01); `IsImporting` disables Export | nothing until done | the new step | `Import failed: {message}` (parity) |
| P3 | insert Screenshot | durable: `ApplyDurable(_ => capture.CaptureScreenshotAsync(path, target, atIndex))` (02) | nothing until done (the app hides for the grab, 02) | the new step | `Import failed: {message}` (parity, Q-REP-5) |
| P4 | size control commit | `Apply(SetDisplayScaleOperation)` (7.7) | the layout at the new scale; the committed scale changes, which triggers the grow-only window resize | | rollback to the persisted scale; notice (Electron: silent) |
| P5, P6 | export, package export | durable: `IStepFlattener.EnsureFlattenedAsync` (04, the overload that takes this `IProjectSession`, R-ARCH-5), which persists each re-bake through `ApplyDurable`, then 09's `IExportService.ExportWithSaveDialogAsync` or `ExportPackageAsync` (09 7.13, R-ARCH-9), which waits for the project's pending writes through `IProjectSettle` before it reads (INV-EXP-28, R-ARCH-6) | the export button's busy label and progress | the export's own result (09) | `Export failed: {message}` (parity) |
| P7 | editor Save (04) | durable, synchronous: flatten, persist, then report success; the report adopts the result | the editor's saving state (04) | the edited card | the editor's notice (04) |
| P8 | View, Brand (03) | `Apply(SetProjectThemeOperation)` (03 7.4.5; brand ids are 10's, R-ARCH-14) | the brand change | | rollback; notice |

Session behaviors this policy depends on. They were this spec's requests to 01; all five are now part of the consolidated `IProjectSession` contract of ARCHITECTURE 7.4 (R-ARCH-23), which is normative, and `Store/ProjectSessionTests` (AC-ARCH-4) proves them:

1. `Apply` whose optimistic `op.Apply` throws `StepNotFoundException` queues nothing, raises nothing and returns a faulted task; the report treats it as "the target is gone" and shows nothing (S2).
2. `ApplyDurable` completion sets `Current` to the durable result WITH every optimistic operation issued after the durable call started re-applied on top, so an optimistic edit made during a merge or import is not lost from the screen (S5).
3. `Changed` carries 01's `ManifestChangedEventArgs(Kind, AffectedStepIds)`, with `ManifestChangeKind` and the event args in `ShotAI.Core.Store` and `AffectedStepIds` taken from the operation's override of `ProjectOperation.AffectedStepIds` (S1, R-ARCH-20; 7.6).
4. A persisted echo that is value-equal to `Current` (apart from `updatedAt`) raises `Changed` with `Kind = Persisted` and no visible change; one that differs raises `External` (S3).
5. `PersistFailed` carries `PersistFailedEventArgs(Operation, Error)` with the failed `ProjectOperation`, so the report can recover a text draft (R4, R5, R8, R10) (S4).

**Rollback notice** (IMPROVEMENT, answers 01 Q-MODEL-12; ARCHITECTURE 5.5): `Your last change couldn't be saved and was undone. ` followed by `UserMessage.From(exception)` (ARCHITECTURE 8.2: the message of a `ShotAIException`, `IOException` or `UnauthorizedAccessException` verbatim, else the generic sentence). One slot (`SaveError`): a new failure replaces the previous text; dismissed by the user only. Logged at Warning with the operation type and the exception (no step text in the log).

### 7.6 Edit state and reconciliation (Core)

```csharp
namespace ShotAI.Core.Report;

using ShotAI.Core.Store;   // ManifestChangeKind { Local, Persisted, RolledBack, Durable, External } lives here (01, R-ARCH-20)

public sealed class ReportEditState
{
    public string? EditingTextId { get; }
    public string? FreshTextId { get; }
    public string? EditingCaptionId { get; }
    public string? EditingBodyId { get; }
    public string? EditingNumberId { get; }
    public bool EditingIntro { get; }
    public int? InsertMenuAt { get; }
    public string? MergingDropId { get; }
    public string? MergingKeepId { get; }
    public event EventHandler? Changed;

    public void AutoOpen(string newId);                  // P1: EditingTextId = FreshTextId = newId
    public void OpenText(string id);                     // no-op if already open; else FreshTextId = null; EditingTextId = id
    public void CloseTextAfterSave(string id);           // if FreshTextId == id: FreshTextId = null; EditingTextId = null
    public CancelResult CancelText(ProjectStep step);    // returns DeleteFresh when fresh && heading, body, callout all empty (raw: missing, null or "")
    public void OpenCaption(string id); public void OpenBody(string id); public void OpenNumber(string id);
    public void CloseInline();                           // caption, body, number
    public void OpenIntro(); public void CloseIntro();
    public void OpenInsertMenu(int atIndex); public void CloseInsertMenu();
    public void BeginMerge(string dropId, string keepId); public void EndMerge();
    public bool IsLockedByMerge(string stepId);          // stepId is MergingDropId or MergingKeepId
    public bool TextDraftOpen => EditingTextId is not null;

    /// Electron's reconciliation (2.12), applied only for the kinds below.
    public void Reconcile(IReadOnlyList<ProjectStep> steps, ManifestChangeKind kind);
}
```

`CancelText` emptiness: `!step.heading && !step.body && !step.callout` in JS truthiness over the RAW values, so a missing key, null and `""` are all empty.

`Reconcile` by kind (IMPROVEMENT except where marked):

| Kind | Raised for | Drop latches of vanished steps (text, fresh) | Close caption, body, number editors (abandon, no commit) and the insert menu |
|---|---|---|---|
| `Local` | an optimistic `Apply` | yes | no |
| `Persisted` | a queued op reached disk | yes | no |
| `RolledBack` | a persist failed | yes | only an editor whose step vanished |
| `Durable` | merge, import, screenshot, export flatten, editor save completed; an adopt after a recording on the open project (7.3) | yes | yes (parity) |
| `External` | the initial `Sync` of an open or adopt (7.3), a persisted state that carried changes this session did not make (S3) | yes | yes (parity) |

So an instructions editor opened right after a caption commit is no longer torn down by that caption's write (EDGE-REP-24).

Which session path raises which kind is fixed by ARCHITECTURE 7.4 (S1 to S5), not by this spec. Two consequences: an adopt after a recording on the open project goes through `ApplyDurable` and arrives as `Durable` (7.3), which this table treats exactly like `External`; SOP apply and revert are optimistic `session.Apply(ApplySopPlanOperation)` and `session.Apply(RevertSopOperation)` (07 7.9, ARCHITECTURE 7.5), so the session raises `Local` (then `Persisted`, or `External` if the disk differed). To keep Electron's parity of closing the inline editors after an SOP change, `ProjectDetailViewModel` also runs the `External` row when 07's `SopPanelViewModel.SopChanged` event (07 7.10 and section 10; raised on the UI thread after a completed apply or revert) arrives.

### 7.7 Size control (Core `DocScaleEditor`, App `DocScaleControl`)

```csharp
namespace ShotAI.Core.Report;

public sealed class DocScaleEditor
{
    public DocScaleEditor(Func<double> committed, Action<double> commit);   // commit = v => the 7.5 helper over session.Apply(new SetDisplayScaleOperation(v))
    public double? Preview { get; }          // nullable override, never a copy of the committed value
    public string? Draft { get; }
    public double Displayed => Preview ?? committed();
    public int SliderIndex => DocScale.DetentIndex(Displayed);
    public string BoxText => Draft ?? ((int)JsMath.Round(Displayed * 100)).ToString(CultureInfo.InvariantCulture);
    public string ValueText => $"{(int)JsMath.Round(Displayed * 100)} percent";
    public event EventHandler? Changed;

    public void SliderMoved(int index);      // Preview = DocScale.DetentAt(index)
    public void SliderReleased();            // Commit(Displayed)
    public void BoxTextChanged(string raw);  // raw != "" && IsLegal(parse(raw) / 100) ? Commit(value) : Draft = raw
    public void Step(int delta);             // at = SliderIndex; to = Math.Clamp(at + delta, 0, 12); if (to != at) Commit(DetentAt(to))
    public void CommitDraft();               // Enter and focus loss
    public void Abandon();                   // Escape: Draft = null; Preview = null
    public void OnCommittedChanged();        // the committed value changed under the control (rollback, external): Preview = null; Draft kept (parity: applyManifest overwrote the preview)

    private void Commit(double v);           // if (v != committed()) commit(v); then Draft = null; Preview = null (commit first, so the layout never flashes the old value)
}
```

- `CommitDraft`: `held = Draft`; return if null; `Draft = null`; `next = held.Trim().Length > 0 && double.TryParse(held, NumberStyles.None, CultureInfo.InvariantCulture, out pct) ? DocScale.Clamp(pct / 100) : Displayed`; `Commit(next)`.
- Because `Commit` clears `Preview` and the optimistic `Apply` moves the committed value at once, there is no seq counter (ELECTRON-ONLY `seqRef`, replaced by the session's ordering, INV-REP-14) and no "preview the committed value" on failure: the session's rollback moves the committed value back and the preview is already null.
- The editor is created per session, so a commit that arrives while the project is closing still goes to the right project (macOS lesson).
- Escape: `Abandon()` sets a one-shot `ignoreNextFocusLoss` flag in the control, so the focus loss that follows commits nothing even if a keystroke raced it (INV-REP-13). Any text input clears the flag.

`DocScaleControl` (App) layout, left to right with 6.4 DIP gaps: a `Label` `Size` (weight 600, `Target` bound to the slider, so a click or access key focuses the slider like the HTML `label htmlFor`; `Label.Target` exists only on `Label`, not on `TextBlock`); `Slider` width 88, `Minimum 0`, `Maximum 12`, `SmallChange 1`, `LargeChange 1`, `IsSnapToTickEnabled = true`, `TickFrequency = 1`, `IsMoveToPointEnabled = true` (Chromium jumps to the click), `AutomationProperties.Name = "Document size"` with a subclass of `SliderAutomationPeer` that also implements `IValueProvider` (returned from `GetPatternCore(PatternInterface.Value)`, `IsReadOnly = true`, `Value` = `ValueText`), because `SliderAutomationPeer` itself exposes only `RangeValue`, whose value would be the detent index; a percent `TextBox` (width 3.6 em, right-aligned, tabular figures, digits only through `PreviewTextInput` and a paste filter: IMPROVEMENT, Chromium's number box also accepted `.`, `e`, `-`, `+`; no length cap, so `1000` buffers and clamps to 125), `AutomationProperties.Name = "Document size, percent"`; two small `RepeatButton`s (up, down, 12 DIP tall each) standing in for Chromium's spinners, each calling `Step(+1)` or `Step(-1)`; `TextBlock` `%`.

Event wiring: `Slider.ValueChanged` from user input calls `SliderMoved` (WPF raises `ValueChanged` for programmatic changes too, so the control sets a suppression flag while it writes `SliderIndex` into `Value` and ignores the events raised during that write); `Thumb.DragCompleted`, `PreviewMouseLeftButtonUp` on the slider, and `KeyUp` of Left, Right, Up, Down, Home, End, PageUp, PageDown call `SliderReleased` (IMPROVEMENT over "any key", EDGE-REP-32); a thumb drag raises both `DragCompleted` and the tunneling `PreviewMouseLeftButtonUp`, which is harmless because `SliderReleased` is idempotent (after the first commit `Preview` is null and `Displayed == committed`); the TextBox's `TextChanged` (user edits only) calls `BoxTextChanged`; `PreviewKeyDown` Up and Down call `Step` and mark handled, Enter calls `CommitDraft` then moves keyboard focus to the report `ScrollViewer` (parity with Chromium's blur), Escape calls `Abandon` then moves focus the same way; `LostKeyboardFocus` calls `CommitDraft`.

Stepper hold (IMPROVEMENT, answers 03 Q-SHELL-13): while the pointer is over the two `RepeatButton`s, `ProjectDetailViewModel` defers `SetDetailView(true, committed)` and applies the latest deferred value on `MouseLeave` or when the control unloads. The slider and the keyboard are not held.

### 7.8 View models and card diff (App, with the diff in Core)

`ReportViewModel.Sync(manifest, kind, affected)`:

1. `ctx = StepContext.Build(manifest.Steps)`: display numbers (INV-REP-1), `numberedTotal`, `canMergeInto` per index (INV-REP-33), first and last flags.
2. `keys` = for each step, `id + "\u0001" + occurrence` (the occurrence counts earlier steps with the same id, EDGE-REP-36).
3. `plan = CardListDiff.Plan(oldKeys, newKeys)`: a list of `Remove(index)`, `Move(from, to)` and `Insert(index, key)` that transforms the old order into the new one with `ObservableCollection.Move` for survivors (never remove and re-add a surviving card, so its image and focus survive).
4. For each card: `card.Update(step, ctx.For(index))`. `Update` sets each bindable property through `SetProperty` (CommunityToolkit.Mvvm raises `PropertyChanged` only on change). When `affected` is non-null and the kind is `Local` or `Persisted`, only the listed cards and cards whose context (number, index, flags) changed are updated. Otherwise every card is updated, skipping a card whose raw step `JsonNode.DeepEquals` its previous snapshot and whose context is equal.
5. `Intro`, `IsEmpty`, `StepCount`, `HasShots` update the same way.
6. `EditState.Reconcile(manifest.Steps, kind)`.

`StepCardViewModel` (derives from `ViewModelBase : ObservableObject`, ARCHITECTURE 5.1, like every report view model; `[RelayCommand]` for every button) properties: `Id`, `Index`, `Kind` (`Shot`, `PlainText`, `Callout`, `Section`), `CalloutKind` (known kinds only), `RawCallout`, `DisplayNumber` (`int?`), `Glyph`, `IsTextKind`, `Caption`, `Heading`, `Body`, `WindowLine` (`WindowLine.Format(app, title)`: both empty gives null; app only; title only; both `app + " \u2014 " + title`; IMPROVEMENT EDGE-REP-25), `ImageRelPath` and `RenderRev` (the image key), `Zoom` (`Math.Max(1, reportZoom)` when `reportZoom` is a finite number, else 1; no upper cap, EDGE-REP-11, EDGE-REP-47), `PanX`, `PanY` (the raw value clamped to `[0, 1]` when it is a finite number, else 0.5, EDGE-REP-47), `Click` (for the marker), `MarkerColor`, `MarkerBaked`, `CropOrigin`, `ShowMergeSuggestion` (`click.button == "right" && canMergeInto`), `IsMerging`, `IsLockedByMerge`, `CanMoveUp`, `CanMoveDown`, `MenuItems` (7.13), `IsDragSource`, `IsDropTarget`, and the editor flags from `ReportEditState`.

Card XAML never binds to the manifest; it binds only to the card view model, so a caption change re-renders one `TextBlock`.

As built in WP-A17: a step whose `id` is not a string is keyed `"\u0002" + n`, n counting such steps, a key no string id can produce. `StepContext.Build` gives each index a `CardContext(Index, Number, IsFirst, IsLast)`; `canMergeInto` joins with the merge (WP-C12). `Sync` also sets `IsEmpty`, `HasIntro` (steps and a heading or a body, Q-REP-7), `IntroHeading`, `IntroBody` and `Scale` (the committed scale, clamped); the step count is the command bar's, on `ProjectDetailViewModel` (`ReportStrings.StepCount`), and `HasShots` joins with export (WP-D10). The card's properties as built: `Key` (fixed for the card's life), `Id`, `Index`, `Number` (the spec's `DisplayNumber`), `Kind`, `CalloutKind`, `Caption`, `Heading`, `Body`, `WindowLine`, `ImageKey` (a `ReportImageKey`, the spec's `ImageRelPath` and `RenderRev`), `Zoom`, `PanX`, `PanY`, `Marker` (a `ReportMarker` from `ReportPresentation.MarkerFor`, which folds `Click`, `MarkerColor`, `MarkerBaked` and `CropOrigin` into the point and colours drawn), and `AutomationName`, the row's name (7.18). The row name holds the caption, so a caption edit raises `Caption` and `AutomationName` and nothing else. The flags the templates switch on (`IsShot`, `IsPlainText`, `IsCallout`, `IsSection`, `NumberText`, `HasNumber`, `Glyph`, `BadgeTip`, `HasCaption`, `HasHeading`, `HasBody`, `HasHeadingAndBody`, `HasBodyOnly`, `IsBlank`, `HasWindowLine`) are observable properties set in `Update` with the others, so each raises only when its value changes; derived with `NotifyPropertyChangedFor`, each would raise whenever its input did. `RawCallout`, `IsTextKind`, `ShowMergeSuggestion`, `IsMerging`, `IsLockedByMerge`, `CanMoveUp`, `CanMoveDown`, `MenuItems`, `IsDragSource`, `IsDropTarget` and the editor flags join with WP-C2, WP-C3 and WP-C12.

### 7.9 Layout (App)

Native layout values (IMPROVEMENT where they differ from the Electron CSS, EDGE-REP-1; they reproduce the export's chrome so INV-REP-9 holds):

```
Grid (ProjectDetailView)
  Row 0: CommandBar                       Border: Background ground, bottom hairline 1, Padding 32,9.6,32,14.4
           head row (Grid: Back | title * | count), gap 13.6
           actions (WrapPanel, 8 horizontal and 8 vertical gaps): [SopPanel (07)] [DocScaleControl] [Resume] [ExportButton]
  Row 1: ScrollViewer (vertical Auto, horizontal Disabled, CanContentScroll false = pixel scrolling)
           Frame: Border, HorizontalAlignment Center, Width = Math.Max(1, Math.Min(Widths(s).RepFrame, viewportWidth)),
                  Padding 32,20,32,28           (INV-REP-34: width from the space offered, not the content)
             StackPanel (content column, width Frame - 64 = HtmlColumn(s) when there is room)
               Overview | AddOverview
               InsertZone(0)
               ItemsControl (cards; StackPanel panel, no virtualization, Q-REP-11)
                 each item: Row + InsertZone(index + 1)
  Overlay layer (03): NoticeStack pinned to the top of Row 1 (EDGE-REP-31), menus, dialogs, the editor (04)
```

Row: `Grid` with columns `32` (rail) | `14` (gap) | `*` (card), bottom margin 9.6. Rail: badge 32 by 32 with top margin 13.6, then the grip with a 4.8 gap. Card: `Border` `BorderThickness 1`, `CornerRadius` the brand's card radius, `Padding 15,12.6,15,12.6` (border plus padding = 16 horizontally, 13.6 vertically). So at full width the card is `HtmlColumn(s) - 46` and its content `HtmlColumn(s) - 78 = HtmlImageMax(s)`, exactly the export's (INV-REP-9). Section cards: no border, no background, padding `0,6.4,0,0` (parity with `co-section`). Callout cards: the kind's bg, bd and fg brushes on the card border (parity with `co-*`).

Type scale in DIP (rem times 16; weights as in 2.10): caption 16 / 600; text heading 16.8 / 650; text body 16 (inherited from the Chromium default, `body` sets no size) line height 1.6; section heading 18.4 / 700; overview heading 17.6; badge number 15.2 / 650; callout glyph 16.8; instructions 16.8 line height 1.6; window line 12.8; merge banner 13.76; menu items `--fs-body` (14); insert labels `--fs-label` (11.04). Fonts, colors and radii come from the brand spec's tokens (the token names quoted in section 2 map one to one).

`UseLayoutRounding = true` on the view; the figure image sets `RenderOptions.BitmapScalingMode = HighQuality`.

As built in WP-A17: the frame is `ReportFrame`, a `Decorator` whose width is `ReportLayout.FrameWidth(s, viewport)` (a NaN viewport, which WPF would treat as Auto, offers the whole frame) and whose child is arranged centred inside the padding. The numbers of this section are `ReportLayout`'s constants; `FigureChrome` is 142, so a full-width figure is `HtmlImageMax(s)`. The command bar is its head row: Back, the title on one line with an ellipsis and the whole title as its tooltip, and the count. Its actions join with their packages (the SOP panel WP-D8, the size control WP-C3, Resume WP-B9, Export WP-D10). The cards are `ReportList` with `StepCard.Row`; the insert zones, the overview's Add, Edit and Remove, the grip and the number editor join with WP-C2 and WP-C3. The empty state's hint is `ReportStrings.EmptyHint` in ink-2 with a 24.8 DIP line height, at most 468 DIP wide. The notices are 03's `NoticeHost`, over the report's row.

### 7.10 The figure control (App `ReportFigure`)

`ReportFigure : Control` with template: `Grid` "figbox" (`HorizontalAlignment Center`) holding (a) the wrap `Border` (`BorderThickness 1`, hairline brush, `CornerRadius` figure radius, `Background` surface, `Width = fit.WrapW`, `Height = fit.WrapH`) whose child is a `Canvas` with `ClipToBounds = true` and a rounded `Clip` (`RectangleGeometry` of the inner size with the inner corner radius), containing the `Image` at `(offsetX, offsetY)` sized `(baseW * zoom, baseH * zoom)` and the marker; (b) the controls `StackPanel` at top-right with margin 8, outside the clip.

- Measurement: `availW` = the width the card content offers the figure (`ReportFigure`'s arrange constraint width, read in `MeasureOverride` from `availableSize.Width`, infinity treated as null). Recompute `ReportGeometry.Fit(image, availW, Zoom, scale)` when any input changes.
- Pan offset: `rangeX = baseW * zoom - baseW * Math.Min(zoom, 1)` (equals Electron's `scrollWidth - clientWidth`), same for Y; `offsetX = -rangeX * PanX`, `offsetY = -rangeY * PanY`, recomputed whenever the image size, zoom, `availW`, scale or the stored pan changes (EDGE-REP-5).
- Pan gesture (INV-REP-3): `MouseLeftButtonDown` on the wrap: return if `Zoom <= 1`; return if `rangeX <= 0 && rangeY <= 0`; `CaptureMouse()`; remember the pointer and the offset; `MouseMove`: `offset = clamp(start + delta, -range, 0)` per axis (live, no write); `MouseLeftButtonUp` or `LostMouseCapture`: release; `px = rangeX > 0 ? -offsetX / rangeX : 0.5`, same for Y; `Apply(new SetReportPanOperation(id, px, py))`. A `panning` flag makes the first of the two events finish the gesture, because `ReleaseMouseCapture()` inside `MouseLeftButtonUp` raises `LostMouseCapture` synchronously. Only the left button starts a pan (IMPROVEMENT, EDGE-REP-51). Cursor `Cursors.Hand` over a pannable wrap, `Cursors.SizeAll` while panning (WPF has no grab cursor; Q-REP-13). The control never handles `MouseWheel`, so the report scrolls.
- Marker (INV-REP-16, EDGE-REP-13): visible iff `Click != null && !MarkerBaked && image known && fraction in [0, 1]`; `fx = (click.image.x - offX) / natW` with `offX = (flattened non-empty && crop != null) ? crop.x : 0` (`ReportPresentation.MarkerFraction`); an `Ellipse` 22 by 22 (`Stroke` color, `StrokeThickness 2.5`, `Fill` color with alpha `0x2E`) and behind it an `Ellipse` 26 by 26 (`Stroke` `#B3FFFFFF`, `StrokeThickness 2`, no fill), both centered at `(fx * baseW * zoom, fy * baseH * zoom)` in image space, `IsHitTestVisible = false`.
- Controls (INV-REP-28): four 30.4 by 30.4 `Button`s (faces and names of 2.11), gap 4, delete with 6 extra top margin; `Opacity` 0.3 and `IsHitTestVisible = false` unless `figbox.IsMouseOver || controls.IsKeyboardFocusWithin`, then 1 and true (0.12 s opacity animation); buttons stay focusable (Tab reaches them and shows the strip). Zoom in is disabled at `Zoom >= 4` (IMPROVEMENT EDGE-REP-14); zoom out at `Zoom <= 1`; reset at `Zoom == 1`. Commands: `Apply(new SetReportZoomOperation(id, Zoom * 1.25))`, `Zoom / 1.25`, `1`; delete runs 7.13's confirm then R6.
- States: loading (a 120 DIP tall placeholder, width `min(availW, capW)`, with an indeterminate `ProgressBar`, IMPROVEMENT over an empty image), shown only when no earlier image exists for the card; missing (the same box with `Image missing: {relPath}`, IMPROVEMENT, macOS parity); loaded. On a key change the previous image and its natural size stay on screen until the new decode completes, then both swap at once (EDGE-REP-50).

As built in WP-A17: the loader, the project folder and the document scale reach each figure as inherited attached properties (`ReportFigure.Loader`, set on the main window; `ProjectDir` and `DocScale`, set on the frame), so no view model holds an App service (INV-ARCH-3). The template is `ReportFigure.Template` in `StepCardTemplates.xaml`: the `Grid` of this section with the wrap and a placeholder `Border`. The canvas's clip is a `RectangleGeometry` of the inner size, its radius the figure radius less the border, read through a resource reference, so it follows the brand. The pan gesture and the controls join with WP-C3. The ring and its halo come from `Marker` (a `ReportMarker`, 7.8): drawn when its fraction of the natural size is in [0, 1], at that fraction of the image as drawn, `(baseW * zoom, baseH * zoom)` at the pan offset. The ring's brushes are the parsed colours rather than tokens, because they must match the ring baked into a render (EDGE-REP-13); the halo is `FixedColors.MarkerHalo`. The loading placeholder is 120 DIP tall and `min(availW, round(816 s))` wide with an indeterminate progress bar; the missing state is the same box with `Image missing: {relPath}` (`ReportStrings.ImageMissing`). A figure loads once it is within one viewport height of the report's viewport (its top no more than two viewport heights below the viewport's top, and its bottom no more than one viewport height above it), and again when its key, folder or loader changes or when it needs more pixels than it has (7.11). A figure farther away waits for a scroll rather than a queue. The image's accessible name is the caption, as Electron's `alt` was.

### 7.11 Image loading (Platform decoder, App loader)

`ReportImageLoader.LoadAsync(projectDir, relPath, renderRev, decodeWidthPx, ct)`:

1. `abs = ProjectStore.ResolveImage(projectDir, relPath)` (01: `Confine` plus the `.png`, `.jpg`, `.jpeg` allowlist); null gives Missing (INV-REP-18).
2. Read the bytes off the UI thread with `new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)` into a `MemoryStream`, then close the file (INV-REP-32; `FileShare.Delete` lets a concurrent atomic replace of the render succeed).
3. `ReportImageDecoder.Decode(bytes, Func<ImageSize, int> targetWidthFor, ct)` (Platform, synchronous WIC COM, called on the thread pool; the shared `WicFactory` (02 7.1) is created on an MTA thread and is MTA-safe, 02 `WicImageCodec` notes, ARCHITECTURE 6.6): the SAME decode path as 02's `WicImageCodec` and 04's `IRenderCodec.Decode` (04 7.13), so the report, the editor and the flatten can never disagree about orientation or color: first the magic-byte check (PNG `89 50 4E 47`, JPEG `FF D8 FF`; anything else throws, the figure shows the missing-image state and a Debug line is logged); `IWICStream.InitializeFromMemory`; then the decoder is created EXPLICITLY for that container with `IWICImagingFactory::CreateDecoder(GUID_ContainerFormatPng` or `GUID_ContainerFormatJpeg, null, ...)` and `IWICBitmapDecoder::Initialize(stream, WICDecodeMetadataCacheOnDemand)`, never with `CreateDecoderFromStream`, which picks a codec by sniffing and could run any third-party codec installed on the machine inside the privileged process (R-ARCH-21, Q-IPC-16 default, ARCHITECTURE 9.1 item 2); an `Initialize` or frame failure fails closed the same way; `GetFrame(0)`; the EXIF orientation from the frame's metadata reader gives the ORIENTED natural size (width and height swapped for orientations 5 to 8) (EDGE-REP-40); the caller's callback computes the target width from that natural size (the fit needs it, see below); then `IWICBitmapScaler` (`WICBitmapInterpolationModeFant`) to the target size expressed in the UNORIENTED frame's axes, then `IWICBitmapFlipRotator`, then the ICC to sRGB transform and `IWICFormatConverter` to `GUID_WICPixelFormat32bppPBGRA`, then `CopyPixels`. Returns `(naturalW, naturalH, decodedW, decodedH, byte[] pbgra)`. The orientation and color steps are one shared helper with 02's codec, not a copy. The exact CsWin32 member names are UNVERIFIED in the same way as 04 7.13 and are pinned by `ReportImageLoaderTests.JpegExifOrientationIsApplied`. (The previous draft used WinRT `Windows.Graphics.Imaging.BitmapDecoder`; that works unpackaged, but 04 7.13 fixes the codec as WIC COM, and two decoders would be two orientation implementations, Q-REP-19.)
4. App: `BitmapSource.Create(decodedW, decodedH, 96, 96, PixelFormats.Pbgra32, null, pixels, decodedW * 4)`, `Freeze()`, marshal to the UI thread.

Decode size: `target = Math.Min(naturalW, Math.Ceiling(baseW * Zoom * dpiScaleX))` where `dpiScaleX` is `VisualTreeHelper.GetDpi(figure).DpiScaleX`; re-decode when the needed width exceeds the decoded width by more than 1 px (zoom in, scale up, DPI change); never re-decode smaller except when the key changes. The fit and the marker always use the NATURAL size, never the decoded size.

Key: `(relPath, renderRev)`; a key change cancels the previous load and starts a new one (INV-REP-17). Scheduling: cards within the viewport plus one viewport height below load first; others queue; at most `Math.Clamp(Environment.ProcessorCount / 2, 1, 4)` decodes run at once (parity with `loading="lazy"`, IMPROVEMENT in prioritization). Canceled when the view closes.

Corrected in WP-A17: the decode path is, in order:
1. the magic-byte check;
2. `IWICStream.InitializeFromMemory` over the bytes, pinned until the decode ends;
3. `CreateDecoder(container, GUID_VendorMicrosoftBuiltIn)`, then a check that the decoder's vendor (`IWICComponentInfo.GetVendorGUID`) is `GUID_VendorMicrosoft`, since the vendor argument is only a preference (fail closed otherwise); the built-in decoders report `GUID_VendorMicrosoft`, and 02, 04 and 09, which compared with `GUID_VendorMicrosoftBuiltIn`, are corrected with it: that check refused every decode on both Windows runners;
4. `Initialize(stream, WICDecodeMetadataCacheOnDemand)` and `GetFrame(0)`;
5. the orientation, for a JPEG only, from `/app1/ifd/{ushort=274}`; a missing or unreadable tag, or a value outside 1 to 8, is 1;
6. an embedded ICC profile (a `WICColorContextProfile` context of the frame) converted to sRGB by `IWICColorTransform`, straight to 32bppPBGRA, skipped when WIC refuses the transform;
7. `IWICFormatConverter` to 32bppPBGRA;
8. the Fant scaler to the target size in the stored axes;
9. for an orientation other than 1, the scaled pixels buffered with `CreateBitmapFromSource(WICBitmapCacheOnLoad)` and turned upright by `IWICBitmapFlipRotator`, which flips before it rotates (5 is a horizontal flip then 270 degrees, 7 a horizontal flip then 90);
10. `CopyPixels`.

The colour steps come before the scaler, so the scaler filters premultiplied sRGB. The flip-rotator reads a buffer, because it reads a pixel at a time and is quadratic over a scaler or a decoder (Microsoft's guidance). The helpers are Platform's internal `WicDecoding`. A decode's WIC objects are released in reverse order when it ends (`WicScope`), except the decoder info object, which WIC may share between decoders. The read of the first list's step 2 fills an array of the file's length (`ReportImageFile.ReadAllBytesAsync`, Core), with no `MemoryStream`. For the first list's step 4, the `BitmapSource` is made on the UI thread from the pixels the pool thread returns, then frozen, because a `BitmapSource` made on a pool thread gives that thread a `Dispatcher`.

As built in WP-A17: `ReportImageLoader.LoadAsync(projectDir, relativePath, Func<ImageSize, int> targetWidthFor, ct)` returns a `ReportImage(NaturalSize, Bitmap)`, or null for the missing state, with a Debug line saying why (`report: image path refused: {RelativePath}`, or `report: image not shown: {RelativePath}` with the exception). The key and the decode width are the figure's, which passes `targetWidthFor` so the width is computed from the natural size on the pool thread. The gate is a `SemaphoreSlim` of `Math.Clamp(Environment.ProcessorCount / 2, 1, 4)`. The decode width is `ReportFigure.DecodeWidth`: `Math.Max(1, Math.Min(naturalW, Math.Ceiling(fit.BaseW * zoom * dpiScaleX)))`; the decoded height keeps the aspect, rounded half away from zero. `WicImageSizeProbe` reads the size by the same path, metadata only, from a path confined with `ConfineNoLinks` after `ResolveImage`, and turns any failure but cancellation into `ScreenshotLoadException` (04's text, `Could not load a screenshot to flatten ({relPath}).`).

### 7.12 Reorder input (App)

- Up and down: `Apply(new MoveStepOperation(id, index -/+ 1))` using the index in `Current` at click time.
- Number entry: `InlineEditBox` in number mode (digits only; Up and Down change the value by 1 within `[1, numberedTotal]`, parity with Chromium's number input); on commit `StepNumberEntry.ToAbsoluteIndex(steps, raw)` returns null for an unparsable entry (JS `parseInt`: leading digits of the trimmed text, EDGE-REP-29) or the absolute destination per 2.14; then `Apply(new MoveStepOperation(id, dest))`.
- Drag (`DragReorderController`): `PreviewMouseLeftButtonDown` on the grip records the point; `PreviewMouseMove` with the left button down and a move beyond `SystemParameters.MinimumHorizontalDragDistance` or `MinimumVerticalDragDistance` starts `DragDrop.DoDragDrop(grip, new DataObject("ShotAI.StepDrag", payload), DragDropEffects.Move)` where `payload` is the STRING `sessionId + "\n" + stepId` (a custom class in a custom format would need `BinaryFormatter` if OLE ever marshals it, which .NET 9 and later no longer provide; Microsoft Learn, "Windows Forms and WPF BinaryFormatter OLE guidance", recommends a `string` or `byte[]` payload); the drop side reads it with `GetData("ShotAI.StepDrag") as string` and splits on the first `\n`; the source card gets `IsDragSource` (opacity 0.45). Rows handle `DragOver`: if the data lacks the `ShotAI.StepDrag` format or belongs to another session, `Effects = None` (EDGE-REP-17); else `Effects = Move` and `IsDropTarget` on that row (a 2 DIP accent line 8 DIP above the row). `Drop` on row `i`: `from` = current index of `stepId`; `ReorderPlanner.DropDestination(from, i)` = `from < i ? i - 1 : i`, no-op when `from == i`; `Apply(new MoveStepOperation(stepId, dest))`. The trailing insert zone accepts a drop and moves the step to `count - 1` (IMPROVEMENT, EDGE-REP-16). `DoDragDrop` returning clears the drag state.
- Auto-scroll (`DragAutoScroller`, parity with Chromium): while `DoDragDrop` runs, a `DispatcherTimer` at 30 ms (it relies on the OLE modal drag loop dispatching the dispatcher's `WM_TIMER`; UNVERIFIED, so `AutoScrollNearBottomEdge` must hold the pointer still near the edge, and if the timer does not tick inside the loop the same step runs from the rows' `DragOver` events instead, which OLE raises while the pointer moves) reads `CursorPosition.Get()` (CsWin32 `GetCursorPos`), converts with `scrollViewer.PointFromScreen`; within 64 DIP of the viewport top, scroll up by `26 * Math.Min(1, (64 - y) / 64)`; within 64 DIP of the bottom, scroll down symmetrically; only while `x` is inside the viewport. Stopped when the drag ends.

### 7.13 Menus, dialogs and the in-window overlay layer (App)

The report's popovers and modals (the export menu, the step overflow menu, the insert menu, the package dialog, the capture-insert dialog and its target dropdown) are drawn in 03's in-window overlay layer, not as separate `Popup` or `Window` HWNDs (IMPROVEMENT reasoning: parity with the DOM popovers, the same placement arithmetic as Electron, and no extra top-level windows to register for capture exclusion or to steal focus). This is the default of R-ARCH-19 (ARCHITECTURE 5.4): a `ShotAIPopup` is used only where WPF itself needs a popup HWND, which in this view means tooltips (the button `title` hints of 2.3, 2.4 and 2.11) and any context menu or `ComboBox` drop-down; those HWNDs are registered for capture exclusion by 03's `ShotAIPopup` and `PopupExclusion` class handlers (INV-SHELL-1, INV-HOME-43). A new report popover or modal is added to the overlay layer.

`PopoverPlacement` (Core, pure):

```csharp
public static class PopoverPlacement
{
    public const double ExportMenuMinWidth = 230, ExportMenuGutter = 8, Offset = 4;
    /// INV-REP-26: true = open rightward (left edges aligned); false = leftward (right edges aligned).
    public static bool ExportMenuOpensRightward(double triggerRightInWindow) => triggerRightInWindow - ExportMenuMinWidth < ExportMenuGutter;
    /// OverflowMenu: estHeight = itemCount * 34 + 16; open upward iff below < estHeight && triggerTop > below.
    public static bool OverflowOpensUpward(int itemCount, double triggerTop, double triggerBottom, double windowClientHeight);
}
```

- Export menu: `MinWidth = PopoverPlacement.ExportMenuMinWidth` (the XAML style reads the same constant through `x:Static`, which is what the command-bar test's width pin becomes); top = trigger bottom + 4; closes on a mouse down outside the export control, observed with a window-level `PreviewMouseDown` handler that does NOT mark the event handled (no hit layer, so the click still reaches its target, parity EDGE-REP-45), on Escape from anywhere in the window, on a second click of Export, or on an item. Items as in 2.4 (label plus hint `TextBlock`).
- Overflow menu: right-aligned to the trigger, `MinWidth 172`, upward per `OverflowOpensUpward`; a full-window transparent backdrop closes it and consumes that click (parity EDGE-REP-45); Escape closes it; an item closes the menu, then runs. Items per 2.13, built by `StepCardViewModel.MenuItems` from `ReportPresentation.MenuFor(step, index, count)` (Core, so labels and enable states are tested on Linux).
- Insert menu: inline in the zone (the zone grows), parity with 2.17; the zone's `+` is transparent until the zone is hovered or the button has keyboard focus.
- Confirm and alert: through 06's `IConfirmService` (`ConfirmAsync(message, confirmLabel, danger, ct)`, `AlertAsync(message, ct)`, 06 7.11), rendered by 06's `ConfirmHost` in the overlay layer (ARCHITECTURE 5.4). The report supplies only the texts of 2.15 and 2.16: delete is `ConfirmAsync("Delete this step? (its image file stays on disk)", "Delete", danger: true, viewToken)`; a merge failure is `AlertAsync("Could not merge these steps: " + message, viewToken)`. The host gives the 2.15 behavior: the confirm button is focused on open (Enter confirms, parity), Escape cancels, Tab cycles inside, the page behind is not hit-testable and the backdrop does NOT close a confirm (parity: the confirm overlay has no click handler).
- Package dialog: 2.4; the backdrop, Cancel and Escape close it (Escape is IMPROVEMENT, EDGE-REP-46); Tab cycles inside; `IncludeOriginals` resets to false on every open (IMPROVEMENT [SECURITY], EDGE-REP-22).
- Only one confirm or alert is shown at a time, by 06 7.11's rule: a second request while one is open first resolves the open one with `false`, then shows (the Electron hook silently replaced it and left the first promise pending, 2.15). In the report the only reachable case is a merge-failure alert arriving while a delete confirm is open: the confirm resolves `false`, so nothing is deleted, and the alert shows.
- Capture-insert dialog: 7.14; Escape closes it from anywhere in the window (parity), including while its target dropdown is open; the dropdown's backdrop consumes its closing click (parity); Tab cycles inside the dialog (IMPROVEMENT, EDGE-REP-46).

### 7.14 Merge and capture insert (App)

`MergeCoordinator.MergeAsync(dropIndex)`:

1. From `session.Current`: return unless `MergePlanner.CanMergeInto(steps, dropIndex)` and no merge is in flight; `drop = steps[dropIndex]`, `keep = steps[dropIndex + 1]` (snapshots); `EditState.BeginMerge(drop.Id, keep.Id)`.
2. Off the UI thread: `size = await imageSizeProbe.GetOrientedSizeAsync(projectDir, keep.Screenshot, ct)` (Platform decoder metadata only, through the same magic-byte check and explicit decoder as 7.11, R-ARCH-21; the path is confined with `ConfineNoLinks`, the same rule as the flatten's source read in step 3); `plan = MergePlanner.Plan(keep, drop, size)`: the marker per 2.16 step 2 with `AnnotationFactory.Marker(x, y, AnnotationStyle.MarkerColorFor(drop))` (04; no radius, parity with `createMarker`), `annotations = keep.Annotations + marker`, `keepMarker = keep.Click is { } c ? new FlattenMarker(c.Image.X, c.Image.Y, MarkerColorFor(keep), c.Radius) : null` (04 D-EDIT-18 carries the radius), `patch = new StepPatch { Annotations = Optional<JsonArray>.Of(annotations), Caption = Optional<string>.Of(JoinText(drop.Caption, keep.Caption, " \u2192 ")), Body = Optional<string>.Of(JoinText(drop.Body, keep.Body, "\n\n")), MarkerBaked = Optional<bool>.Of(true) }` where `JoinText` trims with `JsString.Trim` and drops empties (EDGE-REP-26). `annotations` is a NEW `JsonArray` of `DeepClone()` copies of keep's annotation nodes plus the new marker, because a `JsonNode` can have only one parent and keep's nodes still belong to the session's `Current`.
3. `png = await stepFlattener.FlattenStepAsync(projectDir, keep, plan.Annotations, keep.Raw["crop"], plan.KeepMarker, ct)` (04; reads keep's RAW screenshot through `ConfineNoLinks`, INV-REP-19).
4. `await session.ApplyDurable(s => s.MergeStepsAsync(path, keep.Id, drop.Id, plan.Patch, png))`.
5. Any exception except cancellation: `IConfirmService.AlertAsync` with `Could not merge these steps: {message}` (7.13; `message` is `UserMessage.From(exception)`, ARCHITECTURE 8.2). Finally `EditState.EndMerge()`.

Capture insert: `CaptureInsertViewModel(variant, atIndex)` wraps Core `CaptureTargetPicker` (the 2.19 logic with injected `Func<CancellationToken, Task<CaptureTargets>> listTargets` (02 `ListTargetsAsync`) and `Func<CancellationToken, Task<Rect?>> selectArea` (03 `IAreaSelectionService.SelectAreaAsync(window, ct)`)). Monitor ids are 02's `uint` (`(uint)HMONITOR`, R-ARCH-22), compared only within one launch. Confirm: `screenshot` runs P3; `capture` checks `EditState.TextDraftOpen` and, when set, shows the info notice `Finish editing the text step before capturing.` without the import prefix (IMPROVEMENT, EDGE-REP-23, Q-REP-5), else raises `CaptureInsertRequested(atIndex, target)` for the App's capture coordinator (06, 02: `StartAsync(path, {Target, InsertAt})`). Resume capturing (disabled while `TextDraftOpen`) reads the Home picker's current target through `ICaptureTargetSelection.BuildTarget()` (an App interface in `ShotAI.App.Home`, implemented by 06's singleton `CaptureModePickerViewModel`, injected into `ProjectDetailViewModel`; R-ARCH-26) at click time and raises `ResumeCaptureRequested(target)`; the auto fallbacks of 06 EDGE-HOME-7 apply (a Window mode with no picked window starts an Auto session).

### 7.15 Export wiring (App `ExportViewModel`)

`ExportViewModel.ExportAsync(format)` and `ExportViewModel.ExportPackageAsync()` (the view model's own commands) follow 2.4 exactly with: a per-run `CancellationTokenSource` linked to the view's token (canceled on Back, EDGE-REP-35); `await stepFlattener.EnsureFlattenedAsync(path, dir, session.Current.Steps, session, ct)` (04's overload that takes the open `IProjectSession`, which persists each re-bake through `session.ApplyDurable`; every caller inside an open project uses it, R-ARCH-5; the four-argument overload writes around the session and is for Home exports only, 04 7.7, Q-EDIT-20); then the entry points of 09 7.13 (R-ARCH-9; 09 Q-EXP-18), never a 05-specific name:

- export: `IExportService.ExportWithSaveDialogAsync(path, format, owner, progress, ct)`, where `owner` is the window that hosts the report (the main window), passed by `ExportButton` as the command parameter (`Window.GetWindow(this)`), so the view model keeps no window reference; `progress` is an `IProgress<ExportProgress>` (a `Progress<T>`) created on the UI thread and guarded by the run token and the view model's disposal (T8);
- package export: `IExportService.ExportPackageAsync(path, includeOriginals, ct)`.

09's `PrepareAsync` waits for the project's pending optimistic writes through `IProjectSettle` before it reads (INV-EXP-28, R-ARCH-6, ARCHITECTURE 7.7), so the report does not settle itself. A result with `ExportResult.Canceled` (the save dialog was dismissed) is not an error and shows nothing. `OperationCanceledException` after a cancel returns silently; any other exception sets `ExportError` (`Export failed: {UserMessage.From(exception)}`). The button's enabled state and labels follow 2.4 with `IsImporting` from P2 and `TextDraftOpen`.

### 7.16 Notices and errors

`NoticeStackViewModel` holds four slots, each one message: `ImportError` (`Import failed: `), `ExportError` (`Export failed: `), `SaveError` (the rollback notice, 7.5), `Info` (no prefix; the capture guard). Rendered by 03's shared notice control pinned to the top of the report viewport under the command bar (EDGE-REP-31). No auto-dismiss (parity). The Electron `Error: ` slot for a failed open moves to Home as `OpenFailed`, which 06 shows through `INoticeService` (EDGE-REP-39, ARCHITECTURE 5.5). Merge failures use the alert (parity). The editor's notices are 04's.

As built in WP-A17: the slots are `ReportNoticeSlot.Import`, `Export`, `Save` and `Info`, stacked in that order; `Show(slot, message)` adds the slot's prefix and replaces the slot's text in place, and `Clear` and the dismiss button take a slot down. The stack has the shape of 06's `NoticeCenter`, so one `NoticeHost` renders either. Nothing shows into it yet: the save, import and export flows join with WP-C2, WP-C4 and WP-D10.

As built in WP-A18: the Save slot's prefix is `ReportStrings.RolledBack`, `Your last change couldn't be saved and was undone. `, and the project view shows into it for a write the disk refused (7.3), View, Brand's the first; the report's own edits join with WP-C2.

### 7.17 Threading, cancellation, disposal

- UI thread: all view models, `ReportEditState`, `DocScaleEditor`, `IProjectSessionFactory.Create`, `session.Apply` (the optimistic step, a manifest clone plus `op.Apply`) and `Changed` and `PersistFailed` handling. The session posts both events to the UI context it captured at `Create` (ARCHITECTURE 6.3, S8), so the report needs no `IUiDispatcher.Post` for them; any singleton event the report subscribes to is marshaled with `IUiDispatcher.Post` only (T6, R-ARCH-11) using the template of ARCHITECTURE 5.6.
- Store queue consumer (01, ARCHITECTURE 6.1): `op.Apply` on the disk read and the write.
- Thread pool: file reads, WIC decodes, the merge size probe; 04's flatten runs where 04 says (`StaRenderThread` for rasterizing).
- One view-level `CancellationTokenSource` per open project: canceled on Back; links image loads, the merge flatten (before `ApplyDurable`), export flatten and the capture modal's target load and area selection. Queued writes are NEVER canceled; the session drains on dispose (01).
- Disposal: the view unsubscribes from the session, stops `DragAutoScroller`, cancels the token, then `DisposeAsync` the session without blocking the UI (S9). Nothing in the report blocks the UI thread on a task (T9); decodes, zips and hashes never run on it (T10).

### 7.18 Accessibility and keyboard

| Element | UIA |
|---|---|
| report list | an `ItemsControl` subclass whose `OnCreateAutomationPeer` returns a peer reporting `AutomationControlType.List` (a plain `ItemsControl` does not promise the List and ListItem control types), `AutomationProperties.Name` = the project title; each row `ListItem` named by its number and caption (for example `Step 3, Click Save`), callouts by kind (`Note callout`), sections by heading |
| badge | `Button` named `Click to set this step's position` plus the number |
| grip | `Button` named `Drag to reorder` |
| image controls | `Button`s named `Zoom in`, `Zoom out`, `Reset zoom`, `Delete step` |
| step menu trigger | `Button` named `More step actions`, `ExpandCollapse` pattern |
| export menu items | `MenuItem` with name = label and `HelpText` = hint |
| dialogs | `Window`-less custom `AutomationPeer` with `ControlType.Window`, named as in 2.4, 2.15, 2.19 |
| size control | slider `Document size` with value text `N percent`; box `Document size, percent` |

Keyboard: every rule of 2.21, implemented by `InlineEditBox` (single-line: `Enter` commits, `Escape` cancels, focus loss commits, `GotKeyboardFocus` selects all; multi-line: `AcceptsReturn = true`, `Ctrl+Enter` commits, `Escape` cancels, focus loss commits; a `done` flag makes the first exit win; `Abandon()` used by reconciliation sets `done` without committing) and `TextStepEditor` (no shortcuts, Save and Cancel only). Focus on open is deferred until the element is in the tree through the App helper `UiDeferral` at input priority or a one-shot `LayoutUpdated` handler, never a raw `Dispatcher.BeginInvoke`, which is banned in the App outside the allowlisted files (R-ARCH-18, ARCHITECTURE 6.3 and 14.9).

As built in WP-A17: the list is `ReportList`, whose peer (`ReportListAutomationPeer`) reports List, and whose item peers (`ReportListItemAutomationPeer`) report ListItem named by the card's `AutomationName`: `Step 3, Click Save` (`Step 3` with no caption), a plain text step by its heading or else its body, `Note callout`, `Caution callout` or `Warning callout`, and a section by its heading, or `Section` with none (`ReportStrings.StepName`, `CalloutName`, `SectionName`; `ReportAutomationTests`). The other rows of the table join with their controls (WP-C2, WP-C3, WP-D10).

### 7.19 Composition and packages

Registrations (ARCHITECTURE 4.1 C7 and 4.3; no report registration goes into `App.OnStartup` directly): `AddShotAIApp` registers the singletons `ReportImageLoader` and the per-open factories for `ReportViewModel` and `DocScaleEditor` (C6), the transients `ProjectDetailViewModel`, `ExportViewModel` and the `CaptureInsertViewModel` factory; `AddShotAIPlatform` registers `ReportImageDecoder` (public, the App calls it directly) and `WicImageSizeProbe` as `IImageSizeProbe` only (INV-ARCH-4). Consumes (constructor injection only, ARCHITECTURE 4.1 C2; view models only catalog interfaces, INV-ARCH-3): `IProjectService` and `IProjectSessionFactory` (01, `ShotAI.Core.Store`; never the concrete `ProjectStore`, R-ARCH-4, R-ARCH-5; the static `ProjectStore.ResolveImage` is a helper call, not a dependency), `IStepFlattener` (04, `ShotAI.Core.Rendering`, R-ARCH-7), 04's `EditorFactory` (the Edit entry point, which receives the view's `IProjectSession`), `ICaptureService` (02), `IAreaSelectionService` and `IMainWindowLayout` (03), `ICaptureTargetSelection` (06, `ShotAI.App.Home`, R-ARCH-26), `IConfirmService` (06), 07's `SopPanelViewModelFactory` (R-ARCH-16; the panel reads `ISettingsService.Current.Sop.Enabled` itself, so the report has no SOP settings dependency), `IExportService` (09, `ShotAI.App.Export`), `IFileDialogs` (11, the image insert picker), `ILogger<T>` (10; the category is the type's namespace, which 10 7.5.3 maps to the `main` label).

NuGet: nothing new. `CommunityToolkit.Mvvm` (Core presenters and App), `Microsoft.Extensions.DependencyInjection` (App) and `Microsoft.Extensions.Logging.Abstractions` (Core, added by the foundation PR, ARCHITECTURE 3.5) are in the inventory of ARCHITECTURE 3.2, pinned in `dotnet/Directory.Packages.props`. No WinRT API is used by this subsystem. CsWin32 `NativeMethods.txt`: `GetCursorPos`, `GUID_ContainerFormatPng`, `GUID_ContainerFormatJpeg` (added once if 02 or 04 has not already); the WIC interfaces (`IWICImagingFactory`, `IWICBitmapDecoder`, `IWICBitmapScaler`, `IWICBitmapFlipRotator`, `IWICFormatConverter`, `IWICColorTransform`, `IWICMetadataQueryReader`) are already requested by 02 and 04.

As built in WP-A17: `AddShotAIApp` registers `ReportImageLoader` and `ReportViewModelFactory` as singletons and `ProjectDetailViewModel` as a transient, which the shell takes; `AddShotAIPlatform` registers `ReportImageDecoder` as itself (with `OwnWindowRegistry`, one of the two public Platform registrations, INV-ARCH-4) and `WicImageSizeProbe` as `IImageSizeProbe`. The main window takes the loader and sets it on itself for every figure. `ProjectDetailViewModel` takes `IProjectService`, `IProjectSessionFactory`, `ReportViewModelFactory`, `IMainWindowLayout` and `ILogger<ProjectDetailViewModel>`. Corrected in WP-A17: 02 and 04 had not yet requested the WIC names, so this work package adds them to `NativeMethods.txt`: `IWICStream`, `IWICBitmapDecoder`, `IWICBitmapDecoderInfo`, `IWICComponentInfo`, `IWICBitmapFrameDecode`, `IWICBitmapScaler`, `IWICBitmapFlipRotator`, `IWICFormatConverter`, `IWICColorContext`, `IWICColorTransform`, `IWICMetadataQueryReader`, `GUID_ContainerFormatPng`, `GUID_ContainerFormatJpeg`, `GUID_VendorMicrosoftBuiltIn`, `GUID_VendorMicrosoft`, `GUID_WICPixelFormat32bppPBGRA`, `GUID_WICPixelFormat32bppBGRA`, the option enums and `PropVariantClear`. CsWin32 projects every one of them by that name.

### 7.20 Divergence register

| ID | Divergence | Class | Justification |
|---|---|---|---|
| D-REP-1 | Edits apply to the screen first and persist in the background through the serialized queue, with rollback and a notice | IMPROVEMENT | fixed decision; the feasibility doc's "Edits wait for the disk" |
| D-REP-2 | `busyRef` click-dropping replaced by FIFO queueing (merge-in-flight locks only its two steps) | IMPROVEMENT | INV-REP-29, EDGE-REP-28 |
| D-REP-3 | Only changed cards re-render; survivors are moved, not re-created | IMPROVEMENT | INV-REP-30 |
| D-REP-4 | Report layout spends the export's chrome; the figure equals the export figure at every scale | IMPROVEMENT (visible) | INV-REP-9, EDGE-REP-1, macOS parity |
| D-REP-5 | Reconciliation runs only on durable and external changes | IMPROVEMENT | EDGE-REP-24 |
| D-REP-6 | Unchanged no-ops for zoom, pan, text and intro | IMPROVEMENT | #77 re-dating class (EDGE-REP-12, EDGE-REP-14, EDGE-REP-21) |
| D-REP-7 | Zoom clicks compound from the displayed value; zoom in disabled at 4 | IMPROVEMENT | EDGE-REP-14, EDGE-REP-15 |
| D-REP-8 | Drop on the trailing zone moves to the end | IMPROVEMENT | EDGE-REP-16 |
| D-REP-9 | Package dialog resets to redacted-only on every open | IMPROVEMENT [SECURITY] | EDGE-REP-22 |
| D-REP-10 | Capture guard shown as an info notice without the `Import failed: ` prefix | IMPROVEMENT | EDGE-REP-23 |
| D-REP-11 | Notices pinned to the viewport | IMPROVEMENT | EDGE-REP-31 |
| D-REP-12 | Failed open reported on Home | IMPROVEMENT | EDGE-REP-39 |
| D-REP-13 | Marker colors parsed as CSS hex of any legal length | IMPROVEMENT | EDGE-REP-13 |
| D-REP-14 | Window line never shows a stray dash | IMPROVEMENT | EDGE-REP-25 |
| D-REP-15 | Scale slider commits on drag completion regardless of pointer position | IMPROVEMENT | EDGE-REP-32 |
| D-REP-16 | Percent box accepts digits only | IMPROVEMENT | decimals and exponents are meaningless for 5% detents |
| D-REP-17 | Stepper hold for the spinner buttons | IMPROVEMENT | macOS lesson, 03 Q-SHELL-13 |
| D-REP-18 | Loading and missing-image states | IMPROVEMENT | macOS parity; Electron shows an empty or broken image |
| D-REP-19 | Draft recovery after a failed text save | IMPROVEMENT | a rollback must not silently discard typing |
| D-REP-20 | Session per project; late results ignored | IMPROVEMENT | EDGE-REP-30 |
| D-REP-21 | `shot://`, `projectId`, `AbortController`, `seqRef`, `busyRef`, `manifestRev`, `selectedStepId`, the append import path | ELECTRON-ONLY | replaced by `ResolveImage`, the session, `CancellationToken`, queue ordering, change kinds; dead code not ported |
| D-REP-22 | Escape closes the package dialog; every modal traps Tab; one confirm or alert at a time (06 7.11's rule: a second request resolves the open one with `false`) | IMPROVEMENT | EDGE-REP-46, 2.15 |
| D-REP-23 | Duplicate-id steps are numbered by position | IMPROVEMENT | EDGE-REP-36, EDGE-REP-48 |
| D-REP-24 | A superseded open is discarded (open generation) | IMPROVEMENT | EDGE-REP-44 |
| D-REP-25 | Settings navigation keeps the session, the edit latches and drafts | IMPROVEMENT | EDGE-REP-41 (Q-REP-20) |
| D-REP-26 | Non-number or out-of-range `reportZoom`, `reportPanX`, `reportPanY` read as the export reads them | IMPROVEMENT | EDGE-REP-47 |
| D-REP-27 | A re-rendered image swaps with its size in one step | IMPROVEMENT | EDGE-REP-50 |
| D-REP-28 | Only the left button pans | IMPROVEMENT | EDGE-REP-51 |
| D-REP-29 | Report images decode through the shared WIC codec path | IMPROVEMENT (engineering) | one orientation and color implementation (EDGE-REP-40, Q-REP-19) |

## 8. Tests

### 8.1 Electron test files

| File | Purpose | Cases (grouped, with citations) | Ports to | Target class |
|---|---|---|---|---|
| `src/renderer/project/report-geometry.test.ts` | pins the no-crop fit (`746b012`) | **No-crop invariant**: slack never negative for 9 shapes (`1920x1200`, `3840x2160`, `1366x768`, `800x1400`, `786x500`, `120x90`, `5000x100`, `100x5000`, `1x1`) at widths 786, 820, 500, 300, 120 (`:16-31`); the wrap fits the measured column for 3 shapes at 786, 820, 640, 300 (`:33-41`). **reportFit**: `1920x1200 @ 786` gives `baseW 784`, `baseH floor(1200 * 784 / 1920)` = 490, below 816 (`:45-53`); `800x1600` height-bounded, `baseH <= 598` (`:55-58`); `300x200` not upscaled (`:60-64`); zoom 3 keeps the zoom-1 wrap and base (`:66-72`); whole pixels for 3 shapes (`:74-81`); null width gives `baseW 814`, and 786 is smaller (`:83-88`); null, `0x0`, `-5x10` give all zeros (`:90-94`) | ShotAI.Core.Tests (Linux) | `Geometry/ReportGeometryTests` (`[Theory]` with `MemberData` for the shape and width grids) |
| `src/shared/doc-scale.test.ts` | pins the scale contract (#70, macOS #83) and the derived widths | **Range**: 13 detents, first 0.65, last 1.25, contains 1 (`:9-14`); no float noise: `round(s * 100) / 100 == s` and `JsNumber.ToJsString(s).Length <= 4` (`:16-23`). **clampScale**: unusable inputs (null for undefined and null, `"big"`, NaN, +Infinity, a `JsonObject`, a `JsonArray`) give 1 (`:27-31`); 3 gives 1.25, 0.1 and -5 give 0.65 (`:33-38`); 0.83, 0.82, 1.13, 1 give 0.85, 0.8, 1.15, 1 (`:40-45`); midpoints 0.825, 0.775, 1.125, 0.824, 0.826 give 0.85, 0.8, 1.15, 0.8, 0.85 (`:47-58`); the 16-row normative table including `1.024, 1.025, 1.026` giving `1, 1, 1.05` and `0` giving 0.65 (`:60-82`); only legal outputs for 9 inputs (`:83-86`); idempotent (`:87-92`). **docWidths**: scale 1 gives 880, 816, 816, 738, 1476 (`:96-103`; the source comment says 820 for `reportCol`, the value is 816); `htmlImgMax == max(120, round(816 s) - 78)` and differs from `round(738 s)` for s other than 1 (`:105-113`); image inside the card (`:115-122`); strictly monotonic (`:124-132`); integers (`:134-138`); embed is exactly twice (`:140-147`); 99 equals the max, NaN equals the default (`:149-152`). **detailWindowWidth**: 1 at 1920 gives 1010, 1.25 at 1920 gives `1084 + 130` = 1214 (`:156-160`); at most 1100 and 1024 on those work areas (`:162-167`); 0.65, 0.8, 0.95 give 1010 (`:169-175`); work areas 0, -1, NaN, +Infinity give an integer of at least 1010 (`:177-183`) | ShotAI.Core.Tests (Linux) | `Geometry/DocScaleTests` |
| `src/shared/report-matches-export.test.ts` | pins #81 (report width equals export) and the fixed-padding frame (#102) | per detent (13 each): `reportCol == htmlCol` (`:13-16`); frame `== htmlCol + 64` (`:27-30`); image ceiling `== max(120, round(816 s) - 78)` (`:47-49`); single: `REPORT_COL_BASE == HTML_COL_BASE` (`:20-22`); 880 and 816 at scale 1 (`:32-36`); 1.25 gives 1084, the old spelling 1100 (`:39-42`) | ShotAI.Core.Tests (Linux) | `Geometry/ReportMatchesExportTests` (plus the native figure clause, 8.2) |
| `src/renderer/project/command-bar.test.ts` | pins the export menu flip and the one-row command bar (`701d4aa`) | source scans of the TSX and CSS: the open handler calls `getBoundingClientRect()` and `setExportMenuLeft(rect.right - EXPORT_MENU_MIN_W < EXPORT_MENU_GUTTER` (`:29-37`); `EXPORT_MENU_MIN_W` equals `.export__menu`'s `min-width` (`:39-50`); `.export__menu--left` sets `left: 0` and `right: auto` (`:52-58`); no `BrandPicker` or `Document brand` in the bar (`:62-69`); `.detail__baractions` has `flex-wrap: wrap` (`:71-75`) | ELECTRON-ONLY as written (it greps TypeScript and CSS source, which do not exist natively); each intent ports: the flip arithmetic to ShotAI.Core.Tests, the width pin, the flipped alignment, the missing brand control and the wrap to ShotAI.App.Tests (Windows) | `Report/PopoverPlacementTests` (Linux): `ExportMenuFlip` with trigger right edges 0, 100, 237 (rightward), 238, 1000 (leftward); `Report/CommandBarTests` (Windows): `ExportMenuMinWidthMatchesConstant`, `FlippedMenuAlignsLeftEdgesAndStaysInWindow` (trigger at x 0 in a 680-wide window), `BarHasNoBrandControl` (no `ComboBox` and no element whose automation name contains `brand`, case-insensitive), `ActionsWrapInsteadOfClipping` (at window width 680 every action's bounds lie inside the window) |

### 8.2 New tests the native code needs

ShotAI.Core.Tests (Linux and Windows):

| Class | Cases |
|---|---|
| `Geometry/DocScaleTests` (additions) | `DetentIndexRoundTripsForEveryDetent` and `FromTypedPercent` (macOS `DocScaleTests.swift:277-292`); `DetentAtClampsIndex`; `ClampAcceptsJsonNumberNodes` (`JsonValue.Create(0.83)` gives 0.85; a JSON string `"0.83"` gives 1); `ClampNeverUsesBankersRounding` (0.825 gives 0.85, where `Math.Round` would give 0.80); `DerivedWidthTable` (the 13-row table of section 3) |
| `Geometry/ReportLayoutTests` | `CardContentEqualsHtmlImageMax` per detent (`HtmlColumn - 46 - 32`); `FullWidthFigureEqualsExport`: with `availW = HtmlImageMax(s)`, `Fit(1920x1200)` gives `wrapW == HtmlImageMax(s)` at every detent, with exact values `s = 0.65: 450x281, wrap 452x283`; `s = 1: 736x460, wrap 738x462`; `s = 1.25: 940x587, wrap 942x589`; `Fit(3840x2160)` at `s = 1` gives `736x414`; portrait `600x2000` at `s = 1` gives `179x598` and at 1.25 gives `224x748` (height cap scales) |
| `Report/ReportPresentationTests` | numbering (INV-REP-1 cases plus two sections and a callout at index 0); `ZoomFloorsLegacyValues` (0.5 gives 1; absent gives 1; 6 stays 6); `DisplayedImagePath` (flattened non-empty wins; empty flattened falls back; text step null); `ImageKeyIsPathAndRenderRev`; `MarkerOverlayRules` (baked hides; crop offset only with flattened; outside the crop hides; raw space when not flattened; zero size hides); `MarkerColorFor` (set, right, left, none); `MenuFor` (shot: two items with enable states at first, middle, last; plain text: `Make note callout`, `Make caution callout`, `Make warning callout`, `Make section divider`, `Delete step`; note: `Change to caution`, `Change to warning`, `Change to section divider`, `Convert to plain text`; section: the three `Change to` kinds and `Convert to plain text`; unknown `tip`: `Change to` labels and `Convert to plain text`); `WindowLineFormats` (both, app only, title only, neither); `FramingReadNormalization` (EDGE-REP-47: `reportPanX` 5 gives 1, -1 gives 0, `"x"` gives 0.5, absent gives 0.5; `reportZoom` `"x"` gives 1, `true` gives 1, 0.5 gives 1, 6 stays 6); `DuplicateIdsNumberByPosition` (`[A, A, B]` gives 1, 2, 3, EDGE-REP-36) |
| `Report/StepNumberEntryTests` | the worked examples of 2.14; `0`, `-3`, `99` clamp; `2.7` gives position 2; `""`, `abc` give null; `3abc` gives 3 (JS `parseInt`) |
| `Report/ReorderPlannerTests` | drop destination `from < i ? i - 1 : i`; `from == i` no-op; adjacent drop below is a no-op; trailing zone gives `count - 1` |
| `Report/MergePlannerTests` | `CanMergeInto` (text on either side, empty screenshot, last index); origin recovery with `imageScale` 1 and 0.5 (keep global (1100, 500), image (100, 50), scale 0.5: origin (900, 400); drop global (1000, 450) maps to (50, 25)); fallback to the drop's image coordinates without a keep click; clamp to `[0, natW - 1]`; no marker without a drop click; `JoinText` (`"  a "`, `""` gives `a`; both empty gives `""`; separator ` \u2192 ` for captions and `\n\n` for bodies); patch always carries `caption`, `body`, `annotations`, `markerBaked: true`; `BakeRequestCarriesKeepAnnotationsCropAndMarkers` |
| `Report/ReportOperationsTests` | each operation of 7.4 on an in-memory manifest: Changed and Unchanged rules; key removal for a null callout and for `introEditedByUser`; `AddTextStepOperation` serializes (with 01's `JsJson`) byte-identically to the Electron literal for a plain and a `note` step; index clamping (`-1`, `2.5`, `len + 5`, NaN); `MoveStepOperation` equals Electron's `reorderTo` for every `(from, to)` pair on a 5-step list; `SetCaptionOperation` sets `captionEditedByUser`; `SetBodyOperation` does not; zoom clamps to `[1, 4]`; pan clamps to `[0, 1]`; missing step throws `StepNotFoundException` with `step {id} not found`; every operation is deterministic (applying a clone twice gives equal results) |
| `Report/OptimisticPolicyTests` (a fake `IProjectService` with controllable completion and failure behind a real session from `IProjectSessionFactory`, created with a test `SynchronizationContext` installed, which the session captures at `Create`; the session contract itself is proven by `Store/ProjectSessionTests`, AC-ARCH-4) | `CaptionShowsBeforeWriteCompletes`; `RapidMovesAllApply`; `FailedPersistRollsBackAndNotifies` (the second of three ops fails: the view shows ops 1 and 3, the rollback notice text is exact); `OutOfOrderCompletionCannotRevertNewerEdit`; `EditorSaveIsDurable` (nothing shown before completion); `DurableResultKeepsLaterOptimisticEdits`; `UnchangedQueuesNothing`; `FailedTextSaveReopensDraft` |
| `Report/ReportEditStateTests` | `AutoOpenIsSingleShot`; `OpenAlwaysSwitches`; `CancelDeletesOnlyFreshBlankPlainText` (plain empty: delete; note empty: keep; section empty: keep; fresh with typed but unsaved text but blank on disk: delete, parity); `ReconcileNeverReasserts`; `ReconcileByKind` (the 7.6 table); `MergeLocksBothSteps` |
| `Report/DocScaleEditorTests` | the 2.5 event table through the Core API: `PreviewNeverChangesCommitted`; `SliderReleaseCommitsOnce`; `NoOpCommitQueuesNothing`; typed sequences `1, 10, 100` (applies at 100), `8, 85` (applies at 85), `83` then Enter (0.85), `200` then Enter (1.25), empty then Enter (reverts, nothing queued); `EscapeThenBlurDoesNotCommit`; `StepFromBoundIsNoOp` (at 1.25, Step(+1) queues nothing and keeps the draft); `RollbackClearsPreview`; `DisplayedValueText` (`100 percent`) |
| `Report/CaptureTargetPickerTests` | modes per variant (capture: 4 in order; screenshot: 3, no auto); initial mode screen and one initial load; `loadTargets` keeps a still-present pick, else the first window, else the primary monitor, else the first; `modeReady` per mode; `buildTarget` per mode, including the auto fallbacks; picker labels (every branch of 2.19); a canceled area selection keeps the previous area; errors surface through the error callback |
| `Report/PopoverPlacementTests` | the export flip (8.1); overflow drop-up: 5 items (estimate 186) with 100 below and 400 above opens up; 100 below and 50 above opens down; exactly 186 below opens down |
| `Report/CardListDiffTests` | identity (no edits); single move up and down; insert at 0, middle, end; remove; duplicate ids keyed by occurrence; `CaptionEditTouchesOneCard` (with a recording `PropertyChanged` sink over 100 cards, exactly one card raises, only `Caption`); `CalloutConversionRenumbersLaterCards` |
| `Report/ExportFlowTests` (fakes for `IStepFlattener` and `IExportService`) | `FlattenFailureStopsExport` (the export service is never called; `Export failed: Step 2 ("x") couldn't be prepared: ...`); `FlattenUsesTheSessionOverload` (R-ARCH-5); `ExportCallsExportWithSaveDialogAsync` (R-ARCH-9: format, owner and progress forwarded); `CanceledDialogIsSilent` (`ExportResult.Canceled` shows no notice); `CancelIsSilent`; `ProgressLabel` (`Exporting HTML\u2026 3/10`, and without progress `Exporting PDF\u2026`); `PackageDialogOpensRedacted` (checked, closed, reopened: unchecked); button enabled rules (no shots, exporting, packaging, text draft, importing) |
| `Report/ProjectDetailStateTests` | `TextDraftBlocksRecordingAndExport` (and not Screenshot or Image); `CaptureGuardShowsInfoWithoutPrefix`; `LateResultFromClosedSessionIgnored`; `OpenFailureGoneIsSilent` and `OpenFailureOtherRaisesOpenFailed`; `StepCountLabel` (`1 step`, `0 steps`, `2 steps`); `SupersededOpenIsDiscarded` (open A slow, open B fast: B shown, A's session disposed, EDGE-REP-44); `SettingsRoundTripKeepsDrafts` (EDGE-REP-41: session not disposed, a text draft survives, an export flatten is canceled); `SessionComesFromTheFactory` (R-ARCH-5: `IProjectSessionFactory.Create` is called once per open on the UI context and never for a superseded open); `AdoptIntoOpenSessionIsDurable` (EDGE-REP-43: the open session raises `Durable`, the inline editors close, no new session); `ResumeUsesCaptureTargetSelection` (R-ARCH-26: `ResumeCaptureRequested` carries `ICaptureTargetSelection.BuildTarget()`); `SopChangeClosesInlineEditors` (7.6) |
| `Report/ReportStringsTests` | every constant equals its 2.22 text by code point (the tests spell `\u2014`, `\u2026`, `\u2192` as escapes, so no source file contains the characters by accident) |

ShotAI.App.Tests (Windows only; an STA test host with a `Dispatcher`, as 04 uses):

| Class | Cases |
|---|---|
| `Report/ReportLayoutTests` | `FigureWidthEqualsExportAtEveryDetent` (a 3840x2160 PNG in a window wide enough for `repFrame(s)`: the wrap's `ActualWidth == HtmlImageMax(s)` at all 13 detents); `EmptyAndSmallProjectsUseTheFullColumn`; `NarrowWindowShrinksTheColumn` (window 680: frame equals the viewport, figure fits, slack non-negative) |
| `Report/StepFigureTests` | `NoPanAtZoomOne`; `RightButtonDoesNotPan` (EDGE-REP-51); `PanFinishesOnce` (mouse up raises one operation although `LostMouseCapture` follows); `RerenderSwapsImageAndSizeTogether` (EDGE-REP-50); `PanPersistsFractionsOnRelease` (from the centered pan 0.5, dragging the image right by `rangeX / 2` gives `panX = 0`; dragging it left by `rangeX` gives `panX = 1`, clamped); `ClickWithoutMoveIsUnchanged`; `IdleControlsAreNotHitTestable`; `ControlsAppearOnKeyboardFocus`; `WheelScrollsTheReport` |
| `Report/ReportImageLoaderTests` | `DoesNotHoldTheFile` (load, then rename over and delete the source); `RefusesPathsOutsideTheProject`; `ReloadsOnRenderRevOnly` (changing zoom does not decode again; bumping `renderRev` does); `JpegExifOrientationIsApplied` (orientation 6 swaps width and height, and the decoded pixels match `IRenderCodec.Decode` of the same file downscaled); `DecodeSizeTracksZoom`; `RefusesBytesThatAreNotPngOrJpeg` (INV-REP-18, R-ARCH-21: GIF and BMP bytes under a `.png` name show the missing-image state and no WIC decoder is created) |
| `Report/DocScaleControlTests` | `EscapeThenClickAwayKeepsCommittedValue`; `ArrowKeysApplyImmediately`; `DragReleasedOutsideCommits`; `StepperHoldDefersResize` |
| `Report/DragReorderTests` | `DropBeforeRow`; `DropOnTrailingZoneMovesToEnd`; `ExternalFileDragIsRefused`; `ForeignSessionPayloadIsRefused` (a `ShotAI.StepDrag` string with another session id); `AutoScrollNearBottomEdge` (pointer held still near the edge) |
| `Report/InlineEditBoxTests` | `EnterCommitsOnce` (Enter then the focus loss it causes commits one operation); `EscapeCancels`; `CtrlEnterCommitsMultiline`; `AbandonDoesNotCommit` |
| `Report/CommandBarTests` | the four command-bar cases of 8.1; `ExportMenuOutsideClickPassesThrough` (with the menu open, one click on a caption closes the menu AND opens the caption editor, EDGE-REP-45); `OverflowBackdropConsumesClick` |
| `Report/DialogKeyboardTests` | `PackageDialogEscapeCloses`; `CaptureDialogEscapeClosesWithDropdownOpen`; `ConfirmEnterConfirmsEscapeCancels`; `TabStaysInsideEachDialog`; `MergeAlertCancelsOpenDeleteConfirm` (D-REP-22: with a delete confirm open, a merge failure resolves it `false`, nothing is deleted, the alert shows; the dialog itself is 06's `ConfirmHost`, tested in 06) |
| `Report/ReportResponsivenessTests` | `CaptionShowsBeforeWriteCompletes` (a store whose write blocks on a gate: the new caption is rendered before the gate opens); `CardsAreNotRecreated` (reference identity of the card containers across a move) |
| `Report/ReportAutomationTests` | the UIA names of 7.18 |

As built in WP-A18: Core `Report/Operations/SetProjectThemeOperationTests` (P8's rules, ahead of `ReportOperationsTests`) and the rollback prefix in `ReportStringsTests`; App `Report/ProjectThemeEditTests` (`TheChoiceRepaintsTheProjectView`, `SettingsOverTheProjectKeepsTheAppBrand`, `AnUnrecognisedPinWearsTheAppBrand`, `AFailedWriteRollsBackWithTheNotice`, `AnUnexpectedFailureShowsTheGenericSentence`, `AChoiceForAnotherProjectIsIgnored`); `NoticeStackViewModelTests` expects the Save slot's prefix. `OptimisticPolicyTests`' P8 row stays with WP-C1.

As built in WP-A17 (the read-only report's tests; the rest join with their packages):
- Core: `Geometry/DocScaleTests`, `ReportGeometryTests`, `ReportMatchesExportTests` and `ReportLayoutTests`, and `Report/ReportPresentationTests` without `MenuFor`, which joins with the step menu (WP-C2); also `Report/StepContextTests`, `OpenFailureTests`, `ReportImageFileTests`, `ReportStringsTests` (the display strings), `Editor/CssColorTests` and `AnnotationStyleTests`.
- `CaptionEditTouchesOneCard` needs the card view models, so it is the App's `Report/ReportViewModelTests.CaptionEditTouchesOneCard`: over 100 cards, after a targeted sync and after a full one, one card raises, for `Caption` and its row name `AutomationName` (7.18) and nothing else. `CalloutConversionRenumbersLaterCards` is in both.
- `Report/ProjectDetailStateTests` is App.Tests', not Core.Tests', since the view model is the App's. Its open cases are here: `OpenShowsTheReport`, `LoadingShowsWhileTheStoreReads`, `OpenFailureGoneIsSilent`, `OpenFailureOtherRaisesOpenFailed`, `StepCountLabel`, `SupersededOpenIsDiscarded`, `SessionComesFromTheFactory`, `LateResultFromClosedSessionIgnored`, `TheOpenSessionsChangesShow`, `BackClosesTheSessionAndShrinksTheWindow`, `OpenSizesTheWindowFromTheCommittedScale`, `EachOpenStartsWithNoNotices` and `DisposeClosesTheSession`. Corrected in WP-A17: in `SupersededOpenIsDiscarded` no session is made for A at all (the rule `SessionComesFromTheFactory` states), so none is disposed; a project already shown is disposed when the next one is shown.
- The decoder's cases are App.Tests' `Report/ReportImageDecoderTests` and `ImageSizeProbeTests`, not Platform's, because the fixtures are made with WPF's encoders. `JpegExifOrientationIsApplied` checks all eight orientations by where each quadrant of a four-colour image lands; its comparison with `IRenderCodec.Decode` joins with 04's codec (WP-C10). `RefusesBytesThatAreNotPngOrJpeg` shows the refusal is the magic-byte check's (`UnsupportedImageException`), before WIC is reached.
- `ReportImageLoaderTests` also has the figure's cases: `ReloadsOnRenderRevOnly`, `ANewKeyKeepsTheOldImageUntilTheNewOneLoads` (EDGE-REP-50 for a new key; `StepFigureTests.RerenderSwapsImageAndSizeTogether` stays WP-C3's), `AMissingImageShowsItsPath`, `LoadsNearTheViewportOnly`, `TheRingSitsOnTheClick` and `PanOffsetFollowsTheStoredPan`.
- `ReportLayoutTests` has its three cases, `NarrowWindowShrinksTheColumn` ahead of WP-C3 since the read-only column already follows the window, and `EachOpenStartsAtTheTop`. The wide cases lay the view out on a canvas 1400 DIP wide, so they need no window wider than the runner's screen.
- New: `Report/StepCardViewModelTests`, `NoticeStackViewModelTests`, `ReportAutomationTests` (the list and row names so far), 06's `Home/HomeViewModelTests.OpenFailedShowsNotice`, `Shell/ShellScrollTests.TheProjectStartsAtTheTop` and `Shell/ShellViewModelTests.HomesOpenShowsTheProjectAndBackReturns` and `.AFailedOpenStaysOnHome`, and Platform `Imaging/WicDecodingTests`.

## 9. Acceptance criteria

**AC-REP-1.** `Geometry/ReportGeometryTests`, `Geometry/DocScaleTests` and `Geometry/ReportMatchesExportTests` contain every case of the three Electron geometry test files (8.1) and pass on Linux and Windows.

**AC-REP-2.** `Report/PopoverPlacementTests` and `App.Tests Report/CommandBarTests` cover every intent of `command-bar.test.ts` and pass.

**AC-REP-3.** `Geometry/ReportLayoutTests.FullWidthFigureEqualsExport` passes, and `App.Tests Report/ReportLayoutTests.FigureWidthEqualsExportAtEveryDetent` measures a full-width figure at exactly `HtmlImageMax(s)` DIP at all 13 detents.

**AC-REP-4.** Manual: the same project exported to HTML from the native app and opened in Edge at 100% zoom, placed beside the native report at the same document scale, shows screenshots of the same width (within 2 DIP, the export's own border difference) at 65%, 100% and 125%.

**AC-REP-5.** `Report/ReportPresentationTests.NumbersSkipKnownCalloutsOnly` passes, and a project containing a step with `callout: "tip"` shows the same numbers in the native report, the Electron report and the macOS report.

**AC-REP-6.** `Report/OptimisticPolicyTests` and `App.Tests Report/ReportResponsivenessTests.CaptionShowsBeforeWriteCompletes` pass.

**AC-REP-7.** Manual: with `project.json` made read-only, editing a caption shows the new caption at once, then within 2 seconds reverts it and shows `Your last change couldn't be saved and was undone. ` followed by the error, and the caption editor re-opens with the typed text.

**AC-REP-8.** Manual: on a 20-step project, clicking "Move down" on step 1 five times as fast as possible moves it to position 6 (no click dropped), and the numbers update on every click before the disk writes finish.

**AC-REP-9.** Every edit path R1 to R11 and P1 to P8 behaves per the 7.5 table: `Report/OptimisticPolicyTests` has one case per row asserting Apply versus ApplyDurable, the immediate state and the failure state.

**AC-REP-10.** Manual: an editor save (04) on a step with a new blur shows the updated render only after the save completes, and the file `export/.render/<id>.png` exists with the blur baked before the report shows it.

**AC-REP-11.** `Report/ReportOperationsTests.AddTextStepSerializesLikeElectron` passes: a native text insert, persisted, produces the same step JSON bytes (apart from `id`) as Electron's `addTextStep` for a plain and a `note` step.

**AC-REP-12.** `Report/CardListDiffTests.CaptionEditTouchesOneCard` passes, and on a 100-step project a caption edit costs under 16 ms of UI-thread time from commit to render (measured with a `Stopwatch` around `Sync` plus one layout pass on the reference machine).

**AC-REP-13.** `Report/DocScaleEditorTests` passes every row of the 2.5 event table and every typed sequence listed in 8.2.

**AC-REP-14.** Manual: dragging the size slider from 125% to 90% previews the layout continuously, the window does not resize during the drag, and on release the project's `displayScale` is `0.9` in `project.json` and the window width is unchanged (grow-only); dragging from 100% to 125% grows a 1010-wide window to `min(1214, work area)` once, on release, and leaves a window already wider than that unchanged.

**AC-REP-15.** Manual: type `83` in the percent box and press Escape, then click elsewhere: the scale is unchanged and `project.json` is not rewritten (`updatedAt` unchanged).

**AC-REP-16.** Manual: clicking the slider thumb without moving it and tabbing through the command bar leave `project.json` byte-identical (INV-REP-12).

**AC-REP-17.** Manual: at zoom 1, dragging on a screenshot does nothing and writes nothing; at zoom 2.44 (four zoom-in clicks), dragging pans the image, and after reopening the project and exporting HTML, the exported image shows the same framing.

**AC-REP-18.** Manual: zoom in is disabled at 4, zoom out and reset are disabled at 1, and two rapid zoom-in clicks from 1 give 1.5625 (`reportZoom` in `project.json`).

**AC-REP-19.** `App.Tests Report/StepFigureTests.IdleControlsAreNotHitTestable` passes; manual: after deleting a shot step above a callout, a click on the callout opens its editor on the first click.

**AC-REP-20.** Manual: a right-click step followed by a menu-selection step shows the merge banner; Merge produces one step whose render shows both rings, whose caption is `<right-click caption> \u2192 <selection caption>`, whose redactions from the kept step are still baked (inspect `export/.render/<id>.png`), and the right-click step is gone; the dropped step's screenshot file remains on disk.

**AC-REP-21.** Manual: while a merge is in flight, Delete and Move on the two involved steps are disabled and other steps can still be edited, moved and deleted.

**AC-REP-22.** Manual: each insert menu entry works at gap 0, a middle gap and the last gap: Text and each callout insert at that position with the editor open; Cancel on a fresh blank Text removes it; Cancel on a fresh Note keeps it; Image inserts the chosen PNG or JPEG there; Screenshot inserts one image with no marker; Capture starts a recording whose steps land at that position (02).

**AC-REP-23.** Manual: with a text step editor open, Resume capturing and Export are disabled with the tooltip `Finish editing the text step first`, "+ Capture" confirm shows the info notice `Finish editing the text step before capturing.` without a prefix, and "+ Screenshot" still works and the draft survives.

**AC-REP-24.** Manual: in a window narrowed to 680 DIP with the command bar wrapped onto two rows, the export menu opens fully inside the window.

**AC-REP-25.** Manual: open the package dialog, check "Include original screenshots", export, reopen the dialog: the checkbox is unchecked and the button reads `Export (redacted)`.

**AC-REP-26.** Manual: every string of 2.22 appears exactly as quoted (spot-check with the UIA tree or Accessibility Insights for Windows); `Report/ReportStringsTests` passes.

**AC-REP-27.** `App.Tests Report/ReportImageLoaderTests` passes; manual: with the report open on a project, archiving it from another path is not blocked by a sharing violation, and re-saving a step in the editor replaces its render without an error.

**AC-REP-28.** Manual: a project copied with a hand-edited `flattened: "../../outside.png"` shows the missing-image state for that step and reads no file outside the project (Process Monitor shows no access).

**AC-REP-29.** Manual: dragging a step near the bottom edge of the report scrolls it; dropping on the trailing gap moves the step to the end; dragging a file from Explorer onto the report shows the not-allowed cursor and changes nothing.

**AC-REP-30.** Manual: going Back while a slow write is pending and opening another project immediately shows the second project's steps unchanged when the first project's write completes, and the first project's `project.json` contains the edit.

**AC-REP-31.** Manual: a failed persist while the report is scrolled to the bottom shows the rollback notice at the top of the visible report area.

**AC-REP-32.** Manual: a project whose `project.json` is damaged (invalid JSON) shows a Home notice when opened (06 wording) instead of silently staying on Home.

**AC-REP-33.** Manual: an imported JPEG with EXIF orientation 6 displays upright in the report with its marker-free figure fitted to the rotated size.

**AC-REP-34.** `Report/CaptureTargetPickerTests` passes, and manually the capture-insert modal for each variant shows exactly the modes of 2.19, loads the monitor list on open, and Escape and the backdrop close it.

**AC-REP-35.** Manual: with a text draft open, open Settings from the SOP panel and return: the project is still open, the draft is still in its editor (D-REP-25), and an export started before leaving did not continue.

**AC-REP-36.** Manual: with the export menu open, one click on a step caption closes the menu and opens the caption editor; with a step's overflow menu open, one click on another step's Edit only closes the menu (EDGE-REP-45).

**AC-REP-37.** `Report/ReportPresentationTests.FramingReadNormalization` passes, and a hand-edited `reportPanX: 5` on a zoomed step shows the right edge of the image in the report and in the HTML export.

**AC-REP-38.** Manual: re-saving a step with a new crop replaces the report image and its box size in one frame, with no placeholder and no image drawn at the old aspect (EDGE-REP-50).

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides |
|---|---|---|
| 01 Model and store | `IProjectService` (`OpenProjectAsync`, `MutateAsync` as queued by the session, `ImportStepAsync`, `MergeStepsAsync`; never the concrete `ProjectStore`, R-ARCH-4) and the static helper `ProjectStore.ResolveImage`; `IProjectSessionFactory.Create(OpenedProject)` on the UI thread (never `new ProjectSession`, R-ARCH-5) and the `IProjectSession` contract of ARCHITECTURE 7.4 (`Apply`, `ApplyDurable(Func<IProjectService, Task<ProjectManifest>>)`, `Current`, `Changed`, `PersistFailed`, `WhenIdleAsync`, `DisposeAsync`; S1 to S10, R-ARCH-23); `ProjectOperation` with its virtual `AffectedStepIds`, `MutateResult`, `ManifestChangeKind`, `ManifestChangedEventArgs`, `PersistFailedEventArgs` (all `ShotAI.Core.Store`, R-ARCH-20); `OpenedProject`, `ProjectManifest` and `ProjectStep` views, `StepList.Renumber`, `StepNumbering`, `CalloutKinds`, `CalloutGlyphs`, `JsMath` (`ShotAI.Core.Json`, R-ARCH-1), `JsString`, `JsJson`, `StepNotFoundException` | the optimistic policy (7.5, answering 01 7.10's "decided by 05"; ARCHITECTURE 7.5); the rollback notice text (Q-MODEL-12); `DocScale.Clamp` (shared); `TextStepFactory` (01's `AddTextStepAsync` must use it); `ReportOperation`, which overrides `AffectedStepIds`; the former requests (a) to (d) are now S1 to S5 of ARCHITECTURE 7.4 (7.5); image-loading rule EDGE-MODEL-39 implemented in 7.11 with the explicit decoders of R-ARCH-21 |
| 02 Capture | `ICaptureService.ListTargetsAsync`, `CaptureScreenshotAsync(path, target, atIndex)`, `StartAsync(path, {Target, InsertAt})` via the App coordinator; `CaptureTarget`, `CaptureTargets`, `WindowInfo`, `MonitorInfo` (monitor ids are `uint`, R-ARCH-22) | `CaptureInsertRequested(atIndex, target)`, `ResumeCaptureRequested`; `Adopt(dir, manifest)` after a recording |
| 03 Windows and shell | `IMainWindowLayout.SetDetailView(open, scale)`; `IAreaSelectionService.SelectAreaAsync(window, ct)`; the overlay layer (the default home of every report popover and modal, R-ARCH-19); `ShotAIPopup` and `PopupExclusion` for the tooltips; the shared notice control; `SetProjectThemeOperation` (03 7.4.5) | the inputs of 06's `NavigationState`, which implements `IShellNavigationState` (`ProjectOpen`, `OpenProjectPath`, `RawProjectTheme`, `CommittedScale`, `Changed`); the stepper hold (Q-SHELL-13) |
| 04 Editor | `EditorOverlayView` (Edit entry point `OpenEditor(step)`, which passes the view's `IProjectSession` to `EditorFactory.Create`, R-ARCH-5), `IStepFlattener.FlattenStepAsync` and the `EnsureFlattenedAsync` overload that takes an `IProjectSession` (`ShotAI.Core.Rendering`, R-ARCH-5, R-ARCH-7), `StepPatch`, `StepPatchApplier`, `AnnotationStyle.MarkerColorFor`, `AnnotationFactory.Marker`, `FlattenMarker`, `CssColor.TryParse`, the orientation-respecting decode rule and the explicit-decoder rule (R-ARCH-21) | the merge flow and its captions; the editor save adoption (durable) |
| 06 Home and settings UI | navigation into and out of the detail view; `ICaptureTargetSelection.BuildTarget()` for Resume (an App interface in `ShotAI.App.Home`, implemented by `CaptureModePickerViewModel`, R-ARCH-26); `IConfirmService` and `ConfirmHost` for the delete confirm and the merge alert (06 7.11); `OpenFailed` display through `INoticeService` | `OpenAsync(path)`, `Adopt(dir, manifest)`, `Back`, `OpenFailed(message)`, `ResumeCaptureRequested(target)`, `CaptureInsertRequested(atIndex, target)`; the inputs of `NavigationState`; the Home list refresh after SOP changes is 06's (App effect `App.tsx:330-338`) |
| 07 SOP | `SopPanelView` and its view model from `SopPanelViewModelFactory.Create(IProjectSession)`, given this view's `IProjectSession` (dependencies per R-ARCH-16; the panel reads `ISettingsService.Current.Sop.Enabled` itself); SOP apply and revert go through `session.Apply` (07 7.9, ARCHITECTURE 7.5) and raise `Local`, then `Persisted` or `External`; the `SopPanelViewModel.SopChanged` event after apply or revert (7.6) | the command bar slot; the open `IProjectSession` for the panel's factory; forwarding `SopChanged` to 06's Home refresh |
| 09 Exports | the entry points of 09 7.13 (R-ARCH-9): `IExportService.ExportWithSaveDialogAsync(path, format, owner, progress, ct)` and `ExportPackageAsync(path, includeOriginals, ct)`; `ExportProgress`, `ExportResult.Canceled`, `PackageResult`; the settle before reading (INV-EXP-28, through `IProjectSettle`, R-ARCH-6); the export crop of `reportZoom`/`reportPan` (`zoomCropRect`) | `DocScale.Widths` (the shared derivation 09's CSS and geometry use); the export menu, package dialog and flatten-first rule |
| 10 Brand, settings and infra | `ILogger<T>` (the category is the type's namespace; `ShotAI.App.Report`, `ShotAI.Core.Report` and `ShotAI.Core.Geometry` map to the `main` label, 10 7.5.3); brand tokens (colors, radii, fonts); the brand functions `BrandPalette.PinnedBrand`, `PinIsUnrecognised`, `IsBrandId` (R-ARCH-14) | log lines: Warning on persist failure (operation type, exception), Debug on image decode failure |
| Brand spec (that is 10, R-ARCH-14) | theme tokens; `SetProjectThemeOperation` (03 7.4.5) | `RawProjectTheme` from the session |
| 11 Service boundary | the catalog interfaces of 11 7.3 as consolidated in ARCHITECTURE 4.4 (including `IFileDialogs` for the image insert picker), the threading rules T1 to T12 (ARCHITECTURE 6.2), `UserMessage` (ARCHITECTURE 8.2), `UiDeferral` (R-ARCH-18) | the deletion of the report's IPC channels (`projects:update-step` for report fields, `projects:reorder-steps`, `projects:delete-step`, `projects:merge-steps`, `projects:add-text-step`, `projects:set-intro`, `projects:set-display-scale`, `projects:import-step`, `projects:export`, `projects:export-package`, the `projects:export-progress` event, `capture:screenshot`, `capture:list-targets`, `view:set-detail`; `src/shared/ipc.ts:187-262`), replaced by direct calls |
| 12 Packaging and CI | the Linux job for ShotAI.Core.Tests; the Windows job for ShotAI.App.Tests with the STA host | the new test classes of 8.2 |

## 11. Open questions and risks

**Q-REP-1.** Rollback notice wording. Recommended default: `Your last change couldn't be saved and was undone. ` plus the exception message (01 Q-MODEL-12), one slot, no auto-dismiss.

**Q-REP-2.** The native report is visibly narrower than Electron's (cards 770 instead of 820 at 100%) because it reproduces the export's chrome (EDGE-REP-1). Recommended default: accept; it is what #81 intended, what the tests pin, and what macOS ships. Optionally fix Electron's CSS (`.rep` and `.rep__bodywrap` from the derived widths) before cutover so pilot users see the same report in both Windows builds.

**Q-REP-3.** Title editing and the macOS meta line (`Created`, `Updated`, numbered-step count) in the detail header. Recommended default: not in 2.0.0 (no new features); rename stays on Home (06). Decided in WP-A17: the default; the command bar is Back, the title and the step count.

**Q-REP-4.** Step deletion keeps image files on Windows and deletes them on macOS. Recommended default: keep Electron's behavior (files stay) for 2.0.0; revisit together with an undo feature.

**Q-REP-5.** Notice prefixes for non-import failures (text insert and screenshot use `Import failed: `). Recommended default: fix only the capture guard (EDGE-REP-23, info notice without prefix); keep the others for parity.

**Q-REP-6.** Drop on the trailing gap (EDGE-REP-16). Recommended default: add it (IMPROVEMENT, macOS parity); it changes no existing gesture.

**Q-REP-7.** Show an existing overview in the empty state (EDGE-REP-19). Recommended default: parity (hidden) for 2.0.0; low risk to change later. Decided in WP-A17: parity; the overview shows only with steps (`ReportViewModelTests.OverviewShowsOnlyWithSteps`).

**Q-REP-8.** The delete confirmation mentions an image for text steps (EDGE-REP-18). Recommended default: parity string.

**Q-REP-9.** A stored `reportZoom` above 4 (EDGE-REP-11). Recommended default: parity (no read cap) so the report and the export agree; if a cap is wanted, apply it in both the report and 09's crop.

**Q-REP-10.** The overview editor does not count as a draft (EDGE-REP-20). Recommended default: parity; include it in `TextDraftOpen` only if pilot users lose overview text to a recording.

**Q-REP-11.** UI virtualization of the card list. Recommended default: none (a `StackPanel`); cards re-render only on change and images decode at display size, which is enough for the tens-of-steps projects in use. Measure a 300-step project in Phase A; if layout exceeds 100 ms, switch to `VirtualizingStackPanel` with `ScrollUnit = Pixel`, keeping all editor state in the view models so a recycled container cannot lose a draft. As built in WP-A17: no virtualization (`ReportList` over a `StackPanel`); the 300-step measurement is M-A's (WP-A20).

**Q-REP-12.** Tab between report fields (macOS). Recommended default: not in 2.0.0; standard WPF Tab order applies.

**Q-REP-13.** Grab cursors. WPF has no open-hand or closed-hand cursor. Recommended default: `Cursors.Hand` and `Cursors.SizeAll`; ship `.cur` resources only if users report confusion.

**Q-REP-14.** Resolved by R-ARCH-23 and R-ARCH-20: the change kinds, the durable re-apply rule, the throwing-clone rule and the failed operation on `PersistFailed` are part of the consolidated `IProjectSession` contract of ARCHITECTURE 7.4 (S1 to S10), with `ManifestChangeKind` and the event args in `ShotAI.Core.Store` and `AffectedStepIds` a virtual member of `ProjectOperation`; `Store/ProjectSessionTests` proves them (AC-ARCH-4) in WP-A9. Original text: session change kinds and the durable re-apply rule are requests to 01's `ProjectSession` (section 10). Risk: without them the optimistic layer either closes editors on every write or loses edits made during a merge. Recommended default: land them in the same PR as the first report operation; `Report/OptimisticPolicyTests.DurableResultKeepsLaterOptimisticEdits` guards it.

**Q-REP-15.** Optimistic rollback can move a card the user is looking at (a failed reorder snaps back). Recommended default: accept; the notice explains it. Failures are rare (read-only folders, sync conflicts).

**Q-REP-16.** Draft recovery (re-opening an editor with rejected text) could surprise a user who has moved on. Recommended default: re-open only when no editor of the same kind is open, and never steal focus from another text field.

**Q-REP-17.** Pixel equality with Chromium's text layout is not a goal: card heights will differ slightly because WPF and Chromium lay out text differently. Recommended default: accept; widths (the invariant) are exact, heights are not. Decided in WP-A17: accept; the layout tests pin widths, never heights.

**Q-REP-18.** `IsMoveToPointEnabled` on the slider jumps to the click like Chromium, but WPF raises `ValueChanged` on mouse down, so the preview happens on down and the commit on up. Recommended default: accept (parity with Chromium's range, which also moves on down).

**Q-REP-19.** Resolved by R-ARCH-21 (and ARCHITECTURE 3.5): the report decodes on the one shared WIC COM path, after the magic-byte check and with the decoder created explicitly for PNG or JPEG, never by `CreateDecoderFromStream`; WinRT `BitmapDecoder` is not used. Original text: report image decoding. Recommended default: reuse 02's WIC COM codec path with an added scaler step (7.11), so one implementation applies EXIF orientation and ICC conversion for the report, the editor, the flatten and OCR. WinRT `Windows.Graphics.Imaging.BitmapDecoder` would also work unpackaged, but it is a second orientation implementation that could disagree with the flatten on a rotated JPEG (the marker would then sit in a different place in the report than in the baked render). Built in WP-A17: Platform's `WicDecoding` (7.11), which 04's codec is to call as well (WP-C10).

**Q-REP-20.** Draft survival across the Settings round trip (EDGE-REP-41). Recommended default: keep the session and the drafts (D-REP-25), because losing typed text to a Settings visit is a defect, not a behavior anyone relies on; cancel an in-flight export flatten on leaving (parity), since an export dialog that pops up over Settings would be confusing.
