# 04 Annotation editor, flatten, redaction guarantee and OCR auto-redact

> Spec for the native rewrite. Sources read: `src/renderer/editor/Editor.tsx` (1227 lines), `src/renderer/editor/annotations.ts` (192), `src/renderer/editor/flatten.ts` (293), `src/renderer/editor/BlurRegion.tsx` (96), `src/renderer/editor/editor-geometry.ts` (41), `src/renderer/editor/editor.css` (262), `src/main/ocr.ts` (89), `src/shared/redact-detect.ts` (138), `src/main/render-gate.ts` (49), `src/main/step-render.ts` (60). Tests: `src/renderer/editor/editor-geometry.test.ts` (32), `src/shared/redact-detect.test.ts` (37), `src/main/render-gate.test.ts` (60), `src/main/step-render.test.ts` (90). Supporting reads: `src/renderer/project/sop-prepare.ts` (74), `src/renderer/project/merge.ts` (106), `src/shared/project.ts` (554, annotation, click and patch types), `src/main/ipc.ts` (981, `parseClick`, `parseStepPatch`, `updateStep`, `mergeSteps`, `redactScan`), `src/main/project-store.ts` (1099, `updateStep`, `mergeSteps`, `getProjectForRead`), `src/renderer/project/Report.tsx` (1193, render cache-bust, marker overlay, Edit button), `src/renderer/project/ProjectDetail.tsx` (701, editor mount, egress preparation), `src/renderer/project/SopPanel.tsx` and `src/renderer/project/ProjectList.tsx` (egress preparation call sites), `src/main/claude-service.ts`, `src/main/export.ts`, `src/main/export-package.ts` (render-gate call sites), `src/renderer/Notice.tsx` (39), `src/renderer/notice.css` (95), `docs/HARDENING-PLAN.md` (296: S3, T1a, S6, S7, T2b, T3a, E1 to E7, B3, T1, T2), `docs/PHASE-3-PLAN.md` (125), `docs/PLAN.md` (280), `docs/NATIVE-WINDOWS-FEASIBILITY.md` (274), `docs/native/spec/01-model-store.md`, `docs/native/spec/02-capture.md`. Konva 10.3 library defaults the editor relies on: `lib/shapes/Transformer.js` (anchors, keep-ratio and Shift/Alt behavior, lines 525-560 and 1036-1059), `lib/shapes/Text.js` (default font, lines 484-491), `lib/shapes/Arrow.js` (self rect and pointer defaults, lines 83-99), `lib/Global.js` (`dragDistance`, `dblClickWindow`). Commits read: `9da70df` (history root; the repo was imported, so earlier editor fixes survive only as code comments and plan entries), `207eddd` (#62 gap 3, `captionEditedByUser`), `8f3941a` (#98, #82 symlink refusal on the render write). macOS (read-only, `/home/user/armadillon44/shotai_macos`): `Packages/EditorKit/Sources/EditorKit/{Flatten.swift (361), RedactDetect.swift (134), VisionOCR.swift (76), AnnotationStyle.swift (85)}`, `Packages/EditorKit/Tests/EditorKitTests/{FlattenTests.swift (200), RedactDetectTests.swift (58)}`, `shotAI/Editor/EditorModel.swift` (625), `shotAI/Editor/EditorOverlay.swift` (772), `Packages/ShotModel/Sources/ShotModel/{RenderGate.swift (76), Annotations.swift (257), StepPatch.swift (76)}`, `Packages/ShotModel/Tests/ShotModelTests/RenderFreshnessTests.swift` (256), `PARITY.md`; macOS commits `5b6684e` (#109, #117), `65091a7`, `26bb2b2`, `9b0b179`, `e4e3746`. Verification also read: every source and test file above end to end, `src/main/project-store.ts:55-100` (`normalizeSteps`, which keeps annotation arrays verbatim, junk elements included), `src/shared/project.ts:60-86` (`parseRect`, `parsePoint`), `node_modules/konva/lib/shapes/Transformer.js:520-700,780-830,1030-1062` (Konva 10.3.0), `docs/native/spec/01-model-store.md` (EDGE-MODEL-51, Q-MODEL-22, `PathConfine`, `ProjectSession`, `AtomicFile`), `docs/native/spec/02-capture.md` (`WicImageCodec`), the cross-references to this spec in `05`, `06`, `07`, `09` and `11`, and Microsoft Learn for `Windows.Media.Ocr.OcrEngine` (agile, `ThreadingModel.Both`) and WPF `RenderTargetBitmap`. Status: extracted and verified. Consolidated with ARCHITECTURE.md R-ARCH-1 to R-ARCH-26 on 2026-09-23.

**Notation used in this document.**

- Several Electron strings contain U+2014 (EM DASH), U+2026 (HORIZONTAL ELLIPSIS), U+2212 (MINUS SIGN) or other non-ASCII characters. Inside a quoted string this document writes every non-ASCII character as its escape (`\u2014`, `\u2026`, `\u2212`, ...). The C# literal must contain that exact character; C# accepts the same escape in a string literal, so copying the quoted text is exact.
- `round(x)` means **JavaScript `Math.round`**, ported as `JsMath.Round` from spec 01 (`var f = Math.Floor(x); return x - f >= 0.5 ? f + 1 : f;`, NaN and infinities unchanged). Never C# `Math.Round`. `clamp(v, lo, hi)` means `max(lo, min(v, hi))` evaluated in doubles, exactly as `flatten.ts:19-21` (so `clamp(NaN, ...)` is NaN). `min`, `max`, `abs`, `hypot`, `floor` are the ordinary functions; `min`/`max` propagate NaN like `Math.min`/`Math.max`.
- `finite(v)` means `flatten.ts:23-25`: `v` when it is a finite JSON number, else `0`. It does NOT coerce strings (`Number.isFinite("10")` is false, so `"10"` becomes 0).
- "image px" means pixels of the stored original screenshot (`shots/*.png` or `.jpg`), the coordinate space of every annotation, the crop and `click.image`. "DIP" means a WPF device-independent pixel, which is the same unit as an Electron CSS px (1/96 inch). "screen px" in Electron statements means CSS px.
- Classification: **REQUIRED** (parity), **IMPROVEMENT** (a deliberate native change, justified where it is stated), **ELECTRON-ONLY** (disappears natively; the replacement of its intent is named).
- JS truthiness (`JsTruthy`): false for `undefined` (absent), `null`, `false`, `0`, `-0`, `NaN`, `""`; true for everything else, including `{}`, `[]` and `"0"`.

---

## 1. Scope

**Owns**

| Area | Electron location |
|---|---|
| The inline screenshot editor: every tool, pointer gesture, selection and transform handle, property control, keyboard shortcut, the text inline editor, the crop box, the movable click marker, view fit, zoom and pan, the notice area, the hint line | `src/renderer/editor/Editor.tsx:1-1227`, `src/renderer/editor/editor.css:1-262`, `src/renderer/editor/BlurRegion.tsx:1-96` |
| Annotation semantics (the six types, factories, key order, style defaults, radius and size formulas, `markerColorFor`) | `src/renderer/editor/annotations.ts:1-192`, `src/shared/project.ts:126-208` |
| Editor geometry (`clampRectToImage`, `computeCropView`) | `src/renderer/editor/editor-geometry.ts:1-41` |
| The flatten: crop, destructive redaction bake, vector overlay, marker bake, PNG | `src/renderer/editor/flatten.ts:1-293` |
| The render cache and FRESHNESS rules: `applyPatchAndInvalidate`, `writeStepRender`, `renderRev`, `markerBaked`, and the `StepPatch` validation rules | `src/main/step-render.ts:1-60`, `src/main/ipc.ts:139-226`, `src/shared/project.ts:293-340` |
| Pre-egress render preparation (`ensureFlattened`) | `src/renderer/project/sop-prepare.ts:1-74` |
| The fail-closed render gate shared by Claude send and every file export | `src/main/render-gate.ts:1-49` |
| OCR auto-redact: the engine, the scan handler, the sensitive-text detectors, the suggestion UI | `src/main/ocr.ts:1-89`, `src/shared/redact-detect.ts:1-138`, `src/main/ipc.ts:525-543`, `Editor.tsx:538-563` |

**Does not own**

| Concern | Owner |
|---|---|
| The `project.json` codec, `ProjectStep` views, the write queue, `AtomicFile`, `PathConfine` and `IPathProbe`, `IProjectService`, `IProjectSession` (made by `IProjectSessionFactory.Create` on the UI thread, R-ARCH-5) and its `ApplyDurable`, `IProjectSettle`, the transaction around `UpdateStepAsync` and `MergeStepsAsync` | 01 |
| How the click was captured: `click.image`, `click.imageScale`, `click.button`, downscale | 02 |
| The project window that hosts the editor overlay, the shared notice control, window chrome, the global Delete-step shortcuts outside the editor | 03 |
| The report: the "Edit" button that opens the editor, the report image loader and its cache, the CSS click-marker overlay drawn when `markerBaked` is falsy, the merge UI and the origin-recovery math in `merge.ts:57-82` | 05 |
| Home row export and bulk export flows that call the egress preparation | 06 |
| SOP generation, review-before-send, the Claude request that reads renders through the gate | 07 |
| Every export format, the export zoom crop (`reportZoom`), the "render missing from disk" check, the safe package collapse | 09 |
| Logging sinks and categories (`ocr`), the theme tokens the editor chrome uses | 10 |
| The IPC channels `projects:update-step`, `projects:merge-steps`, `projects:redact-scan` (deleted natively) | 11 |
| Packaging of the OCR language dependency, CI runners | 12 |

---

## 2. Reference behavior (Electron)

### 2.1 Annotation types as stored

Every annotation is a JSON object inside `step.annotations`, in image px, never coerced by the codec (spec 01 INV-MODEL-5). Types (`src/shared/project.ts:126-208`):

| `type` | Fields (the factory's key order, which is the on-disk order for new annotations) | Meaning | Factory |
|---|---|---|---|
| `rect` | `id, type, x, y, width, height, cornerRadius, stroke, strokeWidth, fill` | rounded-rectangle outline; `fill` null = unfilled | `annotations.ts:57-77` |
| `arrow` | `id, type, points, stroke, strokeWidth` | `points = [x1, y1, x2, y2]`, tail to tip | `annotations.ts:79-94` |
| `blur` | `id, type, x, y, width, height, mode, blockSize` | redaction; `mode` `'pixelate'` (labelled "Blur") or `'solid'` (labelled "Black box"); `blockSize` is the mosaic factor, ignored for solid | `annotations.ts:96-114` |
| `stamp` | `id, type, x, y, n, radius, fill, textColor` | numbered circle, `(x, y)` = center | `annotations.ts:121-138` |
| `text` | `id, type, x, y, text, fontSize, fill` | single-line label, `(x, y)` = top-left | `annotations.ts:150-166` |
| `marker` | `id, type, x, y, color` then optional `radius` (appended on the first resize) | click-register ring, `(x, y)` = center; `radius` absent = derived from the image | `annotations.ts:175-177`, `Editor.tsx:415-423` |

`id` is `crypto.randomUUID()` (lowercase v4), falling back to `` `a-${Date.now().toString(36)}-${Math.round(Math.random() * 1e9).toString(36)}` `` if that throws (`annotations.ts:49-55`). REQUIRED (natively: `Guid.NewGuid().ToString("D")`, which is lowercase; the fallback is ELECTRON-ONLY).

Updates use object spread `{ ...a, ...patch }` (`Editor.tsx:246-249`): an existing key keeps its position, a new key is appended, and unknown keys on a known annotation survive. REQUIRED.

Default values at creation (`annotations.ts:35-38`, factories):

| Field | Default |
|---|---|
| any color (new shapes) | `ACCENT = '#e11d48'` until the user picks a color |
| `rect.cornerRadius` | `10` |
| `rect.fill` | `null` |
| `rect.strokeWidth`, `arrow.strokeWidth` | the editor's current stroke width (2.3) |
| `blur.mode` | the editor's current redact mode, initially `'pixelate'`; auto-redact always `'solid'` |
| `blur.blockSize` | the editor's current block size, initially `DEFAULT_BLOCK_SIZE = 14` |
| `stamp.n` | count of existing `stamp` annotations + 1 (`Editor.tsx:272`) |
| `stamp.radius` | `defaultStampRadius(natW, natH)` |
| `stamp.fill` | current color; `stamp.textColor = '#ffffff'` |
| `text.text` | `''` at creation; `text.fontSize = defaultFontSize(natW, natH)`; `text.fill` = current color |
| `marker.color` | current color; no `radius` |

Size formulas (all in image px, `natW`/`natH` = decoded image size, `m = min(natW, natH)`):

```
clickMarkerRadius(w, h) = max(14, min(60, round(min(w, h) * 0.02)))      annotations.ts:117-119
defaultStrokeWidth(w, h) = max(4, min(50, round(min(w, h) * 0.008)))     annotations.ts:141-143
defaultStampRadius(w, h) = max(16, min(72, round(min(w, h) * 0.022)))    annotations.ts:146-148
defaultFontSize(w, h)    = max(16, min(96, round(min(w, h) * 0.022)))    annotations.ts:169-171
dragRect(x1, y1, x2, y2) = { x: min(x1, x2), y: min(y1, y2), width: |x2 - x1|, height: |y2 - y1| }   annotations.ts:180-192
markerColorFor(step)     = step.markerColor ?? (step.click?.button === 'right' ? '#2563eb' : '#e11d48')   annotations.ts:45-47
```

All REQUIRED. `markerColorFor` is used by the report overlay, the merge marker and `ensureFlattened`, but NOT by the editor (2.3, EDGE-EDIT-23).

### 2.2 Opening and closing

| Behavior | Detail | Citation | Class |
|---|---|---|---|
| Entry point | The report's per-step "Edit" button (`title="Edit"`) calls `onEditStep(step)`; text steps never open the editor (`if (step.kind === 'text') return`). | `Report.tsx:921-929`, `ProjectDetail.tsx:358-361` | REQUIRED (05 owns the button) |
| Presentation | An in-window overlay `div.ed__overlay` with `role="dialog"` and `aria-label="Edit screenshot"`, `position: fixed; inset: 0; z-index: 50; padding: 1.5rem; background: rgba(15, 23, 42, 0.55)`, centering the editor panel which fills it (`width/height: 100%`, `padding: 0.9rem`, panel radius and shadow `0 18px 48px rgba(0, 0, 0, 0.35)`). Not a separate window; no backdrop click handler. | `ProjectDetail.tsx:685-697`, `editor.css:6-30` | REQUIRED |
| The step passed in | The report's in-memory step object at click time. Editor state is seeded from it once (2.3); later changes to the step do not reach an open editor. | `Editor.tsx:88-134` | REQUIRED |
| Image load | Loads the ORIGINAL `step.screenshot` (never `flattened`) through `shot://` with `crossOrigin = 'anonymous'` so the canvas stays untainted. On load: store the image, then `strokeWidth = defaultStrokeWidth(naturalWidth, naturalHeight)`. On error: notice `{kind: 'error', text: 'Could not load the screenshot.'}`; the canvas keeps showing `Loading screenshot\u2026` and Save stays disabled. | `Editor.tsx:152-174`, `:941-942`, `:746` | REQUIRED; the `shot://` and CORS mechanics are ELECTRON-ONLY (native reads the file through `PathConfine`, 7.10) |
| Cancel | Button "Cancel" (disabled while saving) calls `onClose()`: the overlay unmounts and every unsaved change is discarded. No confirmation, no dirty tracking. | `Editor.tsx:739-741`, `ProjectDetail.tsx:691` | REQUIRED |
| Escape | Does NOT close the editor (2.12). | `Editor.tsx:451-454` | REQUIRED |
| Save success | `onSaved(manifest)` (the project view applies the returned manifest and clears `editing`), then `onClose()`. | `Editor.tsx:606-607`, `ProjectDetail.tsx:692-695` | REQUIRED |
| Undo / redo | None. No history, no Ctrl+Z. | whole file | REQUIRED (Q-EDIT-9) |

### 2.3 Editor state

| State | Initial value | Citation |
|---|---|---|
| `img` | null until decoded | `Editor.tsx:95` |
| `annotations` | `step.annotations ?? []` (the raw array, verbatim) | `:96-98` |
| `crop` | `step.crop ?? null` (raw) | `:99` |
| `viewCropped` | `!!step.crop` (open in the cropped view when a crop exists) | `:100-105` |
| `editorZoom` | `1` | `:108` |
| `clickImage` | `step.click ? { ...step.click.image } : null` | `:109-111` |
| `clickRadius` | `step.click?.radius ?? null` (null = derived) | `:112-115` |
| `tool` | `'select'` | `:116` |
| `strokeWidth` | `DEFAULT_STROKE_WIDTH = 4`, replaced by `defaultStrokeWidth(...)` on image load | `:117`, `:163` |
| `blockSize` | `DEFAULT_BLOCK_SIZE = 14` | `:118` |
| `redactMode` | `'pixelate'` | `:119` |
| `color` | `ACCENT` (default for new shapes) | `:120` |
| `markerColor` | `step.markerColor ?? ACCENT` (NOT `markerColorFor`, EDGE-EDIT-23) | `:121` |
| `selectedId` | null; pseudo ids `'__click__'` (click marker) and `'__crop__'` (crop box) | `:50-51`, `:122` |
| `draft` / `arrowDraft` | null (drag previews) | `:123-126` |
| `editingTextId` | null | `:130` |
| `selBox` | null (dashed outline for a selected text) | `:131` |
| `saving`, `scanning` | false | `:132-133` |
| `notice` | null (`{kind: 'error' or 'info', text}`) | `:134` |
| `viewport` | `{ w: VIEW_W = 940, h: VIEW_H = 540 }` until measured | `:53-54`, `:138` |

### 2.4 Layout and chrome

Left to right: a tool rail, then the main column (top bar, properties bar, canvas area, hint line). REQUIRED layout; exact CSS values are the Electron reference, the native theme maps them (10).

**Tool rail** (`Editor.tsx:642-698`, `editor.css:34-60,102-137,227-243`): `role="toolbar"`, `aria-label="Editor tools"`, width 104 px, vertical, scrolls vertically only. Three labelled groups (labels uppercase, letter-spaced 0.08em):

| Group label | Tool | Button label | Glyph | Tooltip (`title`) |
|---|---|---|---|---|
| `Draw` | select | `Select` | `\u2B1A` | `Select / move / resize (V)` |
| `Draw` | rect | `Box` | `\u25A2` | `Rounded rectangle` |
| `Draw` | arrow | `Arrow` | `\u2197` | `Arrow` |
| `Draw` | blur | `Redact` | `\u2591` | `Redact \u2014 hide sensitive data (blur or black box), permanently applied to the exported image` |
| `Mark` | stamp | `Number` | `\u2460` | `Numbered step stamp (1, 2, 3\u2026)` |
| `Mark` | marker | `Marker` | `\u25CE` | `Ring to circle a button or link \u2014 click to place` |
| `Mark` | text | `Text` | `T` | `Text label` |
| `Crop` | crop | `Crop` | `\u26F6` | `Crop the screenshot` |

Citations: `annotations.ts:23-32`, `Editor.tsx:59-73`. The active tool button has `aria-pressed="true"` and the `ed__tool--on` style (accent tint, accent border, weight 600). Clicking a tool button (`Editor.tsx:655-662`): (1) if a text edit is open, finish it (2.9); (2) `tool = t`; (3) if `t !== 'select'`, clear the selection; (4) if `t === 'crop'`, `viewCropped = false` while KEEPING the current zoom (a reset to 100% here was a reported jarring jump, EDGE-EDIT-4).

Zoom cluster pinned at the rail base (`Editor.tsx:674-697`), group tooltip `Zoom the canvas for precise editing`: button `\u2212` (`editorZoom = max(0.5, z / 1.25)`, no own `title`, so the browser shows the inherited group tooltip), a middle button with tooltip `Fit` whose label is `` `${Math.round(editorZoom * 100)}%` `` (`JsMath.Round`: the steps give labels such as `80%`, `64%`, `51%`, `50%`, `63%`) and which sets `editorZoom = 1`, and button `+` (`editorZoom = min(8, z * 1.25)`, no own `title`, inherited group tooltip). The percentage is relative to the fit scale, not an absolute scale.

**Top bar** (`Editor.tsx:703-750`): left, only when `crop` is set: a toggle `Apply crop` / `Show full` (tooltip `Work on just the cropped region (non-destructive)`; flips `viewCropped` and sets `editorZoom = 1`) and `Reset crop` (sets `crop = null`, `viewCropped = false`). Then `Auto-redact` (label `Scanning\u2026` while scanning; disabled when `scanning || saving || !img`; tooltip `Scan this screenshot for SSNs, credit cards, and API keys, and add redaction boxes to review`). A flexible spacer, then `Cancel` (disabled while saving) and the primary `Save` (label `Saving\u2026` while saving; disabled when `saving || !img`). Cancel and Save are pinned right so they never reflow.

**Properties bar** (`Editor.tsx:756-879`, `editor.css:80-99`): always present with a reserved minimum height of 2.5rem so selecting or deselecting never resizes the canvas. With no selection it shows the hint `Select an element to change its color or size, edit its text, or delete it.` With a selection it shows, in order:

| Control | Shown when | Range / values | Effect | Citation |
|---|---|---|---|---|
| `Color` (native color input, `title="Color"`) | the selection is the click marker, or any annotation except `blur` | `#rrggbb` | 2.8 `changeColor` | `:628`, `:759-768` |
| `Size` (range, `title="Text size"`) | selected `text` | min 10, max 160, step 1 | sets `fontSize` of that text | `:769-782` |
| `Width` (range, `title="Line width"`) | selected `rect` or `arrow` | min 1, max 80, step 1 | 2.8 `changeStroke` | `:783-794` |
| `How to hide` segmented radio group (`role="radiogroup"`, `aria-label="How to hide this region"`, group tooltip `Blur softens the pixels; Black box covers them \u2014 both are permanently applied to the exported image`), buttons `Blur` (`'pixelate'`) and `Black box` (`'solid'`) | selected `blur` | | 2.8 `changeMode` | `:795-822` |
| `Strength` (range, `title="Blur strength \u2014 higher is stronger; the minimum keeps text unreadable"`) | selected `blur` in `'pixelate'` mode | min `MIN_REDACT_BLOCK` = 8, max 60, step 1 | 2.8 `changeBlock` | `:823-837` |
| `Edit text` button (`title="Edit this text (or double-click it)"`) | selected `text` | | opens the inline editor (2.9) | `:840-849` |
| spacer, then the danger button | always with a selection | label `Remove marker` (click marker), `Remove crop` (crop box), else `Delete element` | removes it (2.12 same effects as Delete) | `:850-872` |

**Canvas area** (`Editor.tsx:881-1209`, `editor.css:161-197`): a non-scrolling wrapper (`position: relative`) that hosts the notice stack (hovering top-center, dismissable, 2.4 below) and the inline text input, and inside it the scroll container `.ed__canvas` (`overflow: auto`, `scrollbar-gutter: stable both-edges`, horizontally `safe center`, vertically `safe flex-start`, 1 px hairline border, a 20 px checkerboard background) holding the stage.

**Notice** (`Editor.tsx:882-888`, `Notice.tsx:18-39`, `notice.css:4-40`): at most one; hovers top-center of the canvas wrapper (`top: 0.6rem`, max width `min(92%, 680px)`), translucent (error: `rgba(185, 28, 28, 0.6)`), white text, a `\u00D7` dismiss button (`aria-label="Dismiss"`, `title="Dismiss"`), `role="status"`. REQUIRED (the shared control is 03's; this spec fixes the kinds and texts).

**Hint line** (`Editor.tsx:1212-1223`): per tool, followed by one space and the fixed sentence `Redactions are permanently applied to the exported image on save.`:

| Tool | Hint |
|---|---|
| select | `Click to select; drag to move; handles to resize. The ring is the click point \u2014 drag it, or select it and use Remove marker to delete it.` |
| crop | `Drag to set the crop region. Reset crop to clear.` |
| stamp | `Click to place a numbered stamp.` |
| text | `Click where the text should go and type \u2014 it previews live; press Enter, switch tools, or Save to place it.` |
| rect, arrow, blur, marker | `Drag to draw.` (marker is placed by a click, but shows this hint) |

### 2.5 View geometry, zoom and pan

| Quantity | Formula | Citation |
|---|---|---|
| viewport `w` | `floor(.ed__canvas.clientWidth)` (stable because of the reserved scrollbar gutter) | `Editor.tsx:182-196` |
| viewport `h` | `floor(.ed__canvaswrap.clientHeight)` (the NON-scrolling wrapper, so a horizontal scrollbar cannot feed back; EDGE-EDIT-1) | `:186-189` |
| update rule | only when both are > 0; re-measured by a `ResizeObserver` on both elements | `:189-194` |
| fit scale (full view) | `scale = natW && natH ? viewport.w / natW : 1` (fit to WIDTH, not contain; small images are scaled UP to the width; EDGE-EDIT-2) | `:200-203` |
| region | `viewCropped && crop ? crop : {x: 0, y: 0, width: natW, height: natH}` | `:208-209` |
| base scale | `viewCropped && crop ? min(viewport.w / crop.width, viewport.h / crop.height, 8) : scale` (crop view is contain-fit, capped at 8) | `:210-213` |
| stage scale | `stageScale = baseScale * editorZoom`, `editorZoom` in `[0.5, 8]` | `:214`, `:678`, `:693` |
| stage transform | `computeCropView(region, stageScale)` = `{ scale: s, x: -region.x * s \|\| 0, y: -region.y * s \|\| 0, w: max(1, round(region.width * s)), h: max(1, round(region.height * s)) }` (the `\|\| 0` turns `-0` into `+0`, and also turns `NaN` into `0`) | `editor-geometry.ts:31-41`, `Editor.tsx:215` |
| pointer to image px | Konva `stage.getRelativePointerPosition()`: `(pointer - stage.position) / s`; during a drag, window events are mapped with the library's own math (`setPointersPositions` then `getRelativePointerPosition`) so the moving end matches the drag start exactly (EDGE-EDIT-8) | `:257-260`, `:312-317` |
| stage placement | the stage (size `w` by `h` CSS px) is centered horizontally when narrower than the viewport and top-anchored; when larger, the container scrolls | `editor.css:177-181` |
| crop view clipping | only the region is visible (stage size equals region size times scale; content outside the stage canvas is not drawn) | `:944-953` |
| pan | no pan tool: panning is the scroll container (scrollbars, mouse wheel vertical, Shift+wheel and touchpad horizontal, Chromium defaults). No click-drag pan. | `editor.css:171-197` |
| marker default radius | `markerR = natW && natH ? clickMarkerRadius(natW, natH) : 20` (from the FULL image, not the crop) | `Editor.tsx:204` |

All REQUIRED. "Apply crop" and "Show full" never change the saved crop; the crop always persists on Save whatever the view.

### 2.6 Tools and pointer gestures

The stage `onMouseDown` handles every button (left, middle and right alike; EDGE-EDIT-36). `p` = pointer in image px; `onEmpty` = the event target is the stage itself (the image layer does not listen, so clicking the bare screenshot counts as empty) (`Editor.tsx:262-302`).

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| Idle, tool `select` | mouse down | `onEmpty` | `selectedId = null` | Idle |
| Idle, tool `select` | mouse down | on a shape | nothing here (the shape's click, 2.7, selects on release if it was not dragged) | Idle |
| Idle, tool `stamp` | mouse down | | append `createStamp(p.x, p.y, count(stamp)+1, defaultStampRadius(natW, natH), color)`; select it; `tool = 'select'` | Idle, select |
| Idle, tool `marker` | mouse down | | append `createMarker(p.x, p.y, color)`; select it; `tool = 'select'` | Idle, select |
| Idle, tool `text` | mouse down | | if a text edit is open, finish it (2.9); append `createText(p.x, p.y, '', defaultFontSize(natW, natH), color)`; select it; `editingTextId = id`; `tool = 'select'` | Idle, select, TextEditing |
| Idle, tool `rect`, `blur`, `crop` | mouse down | | `dragStart = p`; `draft = {x: p.x, y: p.y, width: 0, height: 0}` | Drafting |
| Idle, tool `arrow` | mouse down | | `dragStart = p`; `arrowDraft = [p.x, p.y, p.x, p.y]` | Drafting |
| Drafting | window mouse move (anywhere, even outside the stage) | | `q` = event mapped to image px; arrow: `arrowDraft = [start.x, start.y, q.x, q.y]`; else `draft = dragRect(start.x, start.y, q.x, q.y)` | Drafting |
| Drafting (arrow) | window mouse up | `hypot(dx, dy) >= MIN_DRAG` (6 image px) on the LAST draft | append `createArrow(...draft, strokeWidth, color)`; select it; `tool = 'select'` | Idle, select |
| Drafting (arrow) | window mouse up | shorter | discard the draft; tool unchanged | Idle |
| Drafting (rect/blur/crop) | window mouse up | `draft.width < 6 \|\| draft.height < 6` | discard; tool unchanged (a misclick stays in the tool) | Idle |
| Drafting (crop) | window mouse up | both >= 6 | `crop = clampRectToImage(draft, natW, natH)`; `tool = 'select'`; the crop is NOT selected; `viewCropped` stays false | Idle, select |
| Drafting (rect) | window mouse up | both >= 6 | append `createRect(d.x, d.y, d.width, d.height, strokeWidth, color)`; select; `tool = 'select'` | Idle, select |
| Drafting (blur) | window mouse up | both >= 6 | append `createBlur(d.x, d.y, d.width, d.height, redactMode, blockSize)`; select; `tool = 'select'` | Idle, select |

Citations: `Editor.tsx:262-302` (down), `:321-354` (finish from the last drafted geometry, NOT a fresh pointer read, so a mouse-up outside the stage still completes), `:358-373` (window listeners). Drafts are never clamped while drawing (overshooting the image is fine); only the crop is clamped on commit. REQUIRED.

Draft previews (`Editor.tsx:1116-1149`): rect draft: stroke `ACCENT` (not the current color), `strokeWidth` = current, `cornerRadius 10`; crop draft: stroke `#2563eb`, width 3, dash `[10, 6]`, radius 0; blur draft: fill `rgba(15,23,42,0.55)`; arrow draft: stroke and fill `ACCENT`, current width, head `max(12, strokeWidth * 3)`. All not hit-testable. REQUIRED except the color (IMPROVEMENT D-EDIT-16).

### 2.7 Selection, dragging and the transform handles

**Hit testing** is Konva's: a shape is hit on its fill and its stroke. Consequences (REQUIRED unless noted): an unfilled Box is hit only on its outline, whose hit width equals its `strokeWidth` in image px (EDGE-EDIT-35); arrows on the shaft stroke and the filled head; stamps and markers anywhere in the disc (the marker has a translucent fill); text on its whole text box; blur regions anywhere (filled); the crop box anywhere inside when it has its faint fill (below). Z-order bottom to top: screenshot, annotations in array order, the click marker, drafts, the crop box, the text selection outline, the transformer (`Editor.tsx:955-1206`). Because the crop box sits above the annotations and is filled when `!viewCropped && tool === 'select'`, clicks inside a crop in the full view hit the crop, not the annotations under it (EDGE-EDIT-33).

**Select** (`Editor.tsx:976-985`, `:970`, `:1107-1108`, `:1166-1167`): a click (press and release without a drag) on a shape selects it, only when `tool === 'select'`. Konva does not fire click after a drag, so dragging an unselected shape moves it without selecting it (EDGE-EDIT-34).

**Drag** (Konva `draggable`, only when `tool === 'select'`; starts after the pointer moves `dragDistance = 3` CSS px; no clamping). Commit on drag end:

| Target | On drag end | Citation |
|---|---|---|
| rect, blur, stamp, marker, text | `x, y` = node position (image px) | `:971`, `:998`, `:1044`, `:1075`, `:1091` |
| arrow | `dx, dy` = node offset; node reset to (0,0); `points = [p0+dx, p1+dy, p2+dx, p3+dy]` | `:1014-1033` |
| click marker | `clickImage = {x, y}` (unclamped, may leave the image) | `:1110` |
| crop box (draggable only when `!viewCropped && tool === 'select'`) | `crop = clampRectToImage({x, y, width: crop.width, height: crop.height}, natW, natH)` (so dragging past the right or bottom edge SHRINKS it, EDGE-EDIT-29) | `:1165-1177` |

Dragging hides the text selection outline (`onDragStart: setSelBox(null)`), recomputed afterwards.

**Transformer** (`Editor.tsx:219-244`, `:1197-1205`; Konva defaults from `Transformer.js:1036-1059`): attached only when `tool === 'select'` and the selection is a `rect`, `blur`, `arrow`, `stamp` or `marker` annotation, the click marker or the crop box. Text is never handle-resized (Size slider instead). Settings: rotation off, flip off, `ignoreStroke` (the box is the shape's geometry without its stroke), eight anchors (four corners, four edge midpoints) of 10 by 10 CSS px, white fill, 1 px stroke `rgb(0, 161, 255)`, border 1 px `rgb(0, 161, 255)`. `keepRatio` is true only for circular things (stamp, marker, click ring), so they stay round; for the others the keep-ratio of corner anchors is toggled ON by holding Shift (Konva `shiftBehavior: 'default'`: `keepProportion = keepRatio || shiftKey`); Alt scales about the center. Keep-ratio affects corner anchors only; an edge anchor always scales one axis. `boundBoxFunc`: a proposed box narrower or shorter than 8 CSS px (absolute, screen space) is rejected and the old box kept.

Konva scales the node during the drag; on transform end (`Editor.tsx:375-434`), with `sx = |scaleX|`, `sy = |scaleY|` read and the node scale reset to 1:

| Target | Committed geometry |
|---|---|
| crop box | `crop = clampRectToImage({x: node.x, y: node.y, width: max(8, node.width * sx), height: max(8, node.height * sy)}, natW, natH)` |
| click marker | `s = max(sx, sy)`; `clickRadius = max(6, round((clickRadius ?? markerR) * s))`; `clickImage = node position` |
| rect, blur | `x = node.x, y = node.y, width = max(4, node.width * sx), height = max(4, node.height * sy)` |
| stamp, marker | `s = max(sx, sy)`; `baseR = stamp ? radius : (radius ?? markerR)`; `x, y` = node position; `radius = max(6, round(baseR * s))` (a marker gains its `radius` key here) |
| arrow | `ox, oy` = node position; `points = [ox + p0*sx, oy + p1*sy, ox + p2*sx, oy + p3*sy]`; node reset to (0,0) |

Node position after a transform is the node origin moved by the scaling about the fixed anchor: for a node whose self rect relative to its origin is `(rx, ry, rw, rh)` and whose new box is `(bx, by, bw, bh)`, `sx = bw / rw`, `sy = bh / rh`, `origin' = (bx - rx * sx, by - ry * sy)`. Self rects: rect and blur `(0, 0, w, h)`; stamp and circles `(-r, -r, 2r, 2r)`; arrow: the bounding box of its points widened vertically only by `pointerWidth / 2` on each side (`Arrow.js:83-92`). A perfectly vertical arrow has a zero-width box, so every proposed box fails the 8 px test and it cannot be resized (EDGE-EDIT-32). REQUIRED (the arrow case is IMPROVEMENT D-EDIT-15).

**Text selection outline** (`Editor.tsx:478-494`, `:1184-1195`): only a selected `text` shows it: the text node's client rect (image px) padded by 6 on each side, stroke `#4f46e5`, width 2, dash `[6, 4]`, not hit-testable. Other selections show the transformer instead.

### 2.8 Property changes

```
changeStroke(v): strokeWidth = v; if selected is rect or arrow: selected.strokeWidth = v        Editor.tsx:511-516
changeBlock(v):  blockSize = v;   if selected is blur: selected.blockSize = v                  :517-520
changeMode(m):   redactMode = m;  if selected is blur: selected.mode = m                       :521-524
changeColor(c):  color = c;                                                                    :525-536
                 if selectedId == '__click__': markerColor = c
                 else if selected is rect or arrow: selected.stroke = c
                 else if selected is text or stamp: selected.fill = c
                 else if selected is marker: selected.color = c
Size slider:     selected text.fontSize = Number(value)                                        :776-779
```

Each control also becomes the default for the next shape of that kind. Displayed values (`:618-637`): Width shows the selected shape's `strokeWidth`; Strength shows the selected blur's `blockSize`; the mode shows the selected blur's `mode`; Color shows `markerColor` for the click marker, else the selection's `fill` (text, stamp), `stroke` (rect, arrow), `color` (marker). The color input accepts only `#rrggbb`; any other stored form displays as `#000000` (EDGE-EDIT-42). REQUIRED.

### 2.9 Inline text editing

| State | Event | Guard | Action | Next state |
|---|---|---|---|---|
| NotEditing | Text tool click | | create the text (2.6), `editingTextId = id` | Opening |
| NotEditing | double-click a text | `tool === 'select'` | `editingTextId = id` | Opening |
| NotEditing | `Edit text` button | selected text | `editingTextId = selected.id` | Opening |
| Opening | effect runs | | `opening = true`; focus the input and select all its text; start a 300 ms timer that sets `opening = false` | Editing |
| Editing | input change | | `text = value` (live; the canvas text is hidden while editing, the input shows it) | Editing |
| Editing | key `Enter` or `Escape` | | `preventDefault`; finish | NotEditing |
| Editing | any key | | `stopPropagation` (so Delete and Backspace never reach the editor's window handler) | Editing |
| Editing | blur | `opening` | `opening = false`; refocus on the next animation frame (the creating click's mouse-up stole focus; E7) | Editing |
| Editing | blur | `!opening` | finish | NotEditing |
| Editing | a tool button, or a Text tool click | | finish first, then the button's own action | NotEditing |
| Editing | Save | | `editingTextId = null`; empty texts are filtered by Save itself (2.15) | NotEditing |

`finish` (`Editor.tsx:503-509`): `editingTextId = null`; if the annotation is a text whose `text.trim()` is empty, remove it (which also clears the selection if it was selected). Escape COMMITS like Enter; it does not revert (the plan text in `HARDENING-PLAN.md:151` E7 said "Escape cancels"; the shipped code commits). REQUIRED as shipped.

The input (`Editor.tsx:889-939`, `editor.css:249-262`): `<input type="text">` (single line), `placeholder="Type\u2026"`, `spellCheck=false`, `size = max(text.length, 4)` characters, absolutely positioned in the canvas wrapper at `left = stageRect.left - wrapRect.left + (a.x - region.x) * s`, `top = stageRect.top - wrapRect.top + (a.y - region.y) * s`, `font-size = a.fontSize * s` px, `color = a.fill`, `font-family: Arial, sans-serif` (Konva's default text font), `line-height: 1`, `padding: 0 1px`, no border, `outline: 1px dashed` accent, background `rgba(255, 255, 255, 0.14)`, caret accent, `white-space: pre`, z-index 45. The position is computed at render time only; scrolling the canvas during an edit leaves the input behind until the next render (EDGE-EDIT-52). REQUIRED except the font (IMPROVEMENT D-EDIT-8) and the scroll tracking.

### 2.10 The click marker and marker annotations

The step's own click (`step.click`) is not an annotation. The editor shows it as a movable, resizable ring with the pseudo id `'__click__'` (`Editor.tsx:1096-1113`): center `clickImage`, radius `clickRadius ?? markerR`, stroke `markerColor`, stroke width `max(2, round(radius * 0.22))`, fill `markerColor + '2e'` (hex alpha 0x2e = 46/255, about 0.18; EDGE-EDIT-43). Select it by click; drag moves it; the transformer resizes it with keep-ratio; the Color control sets `markerColor`; `Remove marker` or Delete/Backspace sets `clickImage = null` (so Save persists `click: null`). A step without a click has no ring. REQUIRED.

A `marker` annotation (created by the Marker tool, or brought in by a merge) draws the same ring (`Editor.tsx:1063-1078`) with `radius ?? markerR` and color `a.color`, is an ordinary selectable annotation, and is resized like a stamp. REQUIRED.

### 2.11 The crop box

`crop` is image px, full-image coordinates. Drawn (`Editor.tsx:1151-1180`) as a rect with stroke `#2563eb`, width 3 image px, dash `[10, 6]`, fill `rgba(37,99,235,0.06)` only when `!viewCropped && tool === 'select'` (so the interior is grab-able to move it); listening and draggable under the same condition, so in the cropped view it is inert. Pseudo id `'__crop__'`. Created by the Crop tool (2.6), moved and resized with the rules in 2.7, removed by `Remove crop`, Delete/Backspace or `Reset crop`. Annotations stay in full-image coordinates whatever the crop. REQUIRED.

`clampRectToImage(r, w, h)` (`editor-geometry.ts:7-16`):

```
x = max(0, min(r.x, w));  y = max(0, min(r.y, h))
width  = max(1, min(r.width,  w - x))
height = max(1, min(r.height, h - y))
```

Never zero-sized: a rect fully outside becomes 1 by 1 at the far edge (`editor-geometry.test.ts:14-16`). REQUIRED.

### 2.12 Keyboard

A `keydown` listener on the window (`Editor.tsx:436-458`), ignored when the event target is an `INPUT` or `TEXTAREA`. That guard covers every `<input>`, not only the text editor: while the Width, Size or Strength range slider or the color input has focus (it keeps focus after being dragged or clicked), Delete, Backspace and Escape do nothing until the user clicks elsewhere (EDGE-EDIT-65):

| Key | Guard | Action |
|---|---|---|
| `Delete` or `Backspace` | a selection | `preventDefault`; click marker: `clickImage = null`, deselect; crop: `crop = null`, deselect; else remove the annotation |
| `Escape` | | deselect; `tool = 'select'` (does not close the editor). Pressed during a drag draft, the draft is cancelled: the window mouse-up then runs `finishDrag` with `tool === 'select'`, which creates nothing (EDGE-EDIT-66) |

Nothing else: no `V` despite the Select tooltip (EDGE-EDIT-37), no arrow-key nudge, no Ctrl+Z, no Ctrl+S, no zoom keys. REQUIRED (with the `V` IMPROVEMENT D-EDIT-17).

### 2.13 Live rendering in the editor

Konva draws (`Editor.tsx:955-1094`), all in image px under the stage scale:

| Type | Preview |
|---|---|
| rect | `KRect` with `cornerRadius`, `stroke`, `strokeWidth`, `fill ?? undefined` |
| arrow | `KArrow` points, stroke and fill `stroke`, `strokeWidth`, `pointerLength = pointerWidth = max(12, strokeWidth * 3)`, `lineCap: 'butt'`, pointer at the end only |
| stamp | a group at `(x, y)`: circle `radius` filled `fill`; text `String(n)`, `fontSize = round(radius * 1.15)`, bold, `fill = textColor`, box `2r` by `2r` offset by `r`, centered both ways (Konva default font Arial) |
| marker | ring as in 2.10 |
| text | `KText` at `(x, y)`, `fontSize`, `fill`, Konva default font Arial, line height 1; hidden while its inline editor is open |
| blur | `BlurRegion` (below) |
| unknown type | falls through to the text branch: an invisible, draggable `KText` (EDGE-EDIT-38) |

`BlurRegion` (`BlurRegion.tsx:32-95`): `solid` draws an opaque `#000000` rect. `pixelate` builds a preview canvas: `w = max(1, round(a.width))`, `h = max(1, round(a.height))`, `block = max(MIN_REDACT_BLOCK, round(a.blockSize))` (no `|| 12` fallback, EDGE-EDIT-56), `sw = max(1, round(w / block))`, `sh = max(1, round(h / block))`; draw the RAW image's source rect `(a.x, a.y, a.width, a.height)` into `sw` by `sh` with smoothing; draw that back to `w` by `h` with smoothing; draw it again with `filter = blur(max(1, round(block * 0.6))px)`; show it as an image at `(a.x, a.y)` sized `(a.width, a.height)`. If the canvas context is unavailable or the draw throws, fall back to a rect filled `rgba(15,23,42,0.55)`. The preview is recomputed when x, y, width, height, blockSize or mode change. The preview samples the raw image, not the composite, so overlapping blurs preview differently from the bake (EDGE-EDIT-16). The preview canvas is built only when `a.mode === 'pixelate'` (`BlurRegion.tsx:33`): a blur whose `mode` is neither `'pixelate'` nor `'solid'` (absent, or a future value) previews as the translucent `rgba(15,23,42,0.55)` fallback rect, while the bake pixelates it (EDGE-EDIT-63). A `blockSize` that is absent or non-numeric makes `block` NaN, so the tile canvas is 0 by 0, every `drawImage` returns early on its NaN arguments, and the preview is a fully TRANSPARENT image: the raw pixels show through in the editor (EDGE-EDIT-63). REQUIRED behavior except the preview anomalies, which native replaces with the bake's rules (D-EDIT-8).

### 2.14 Auto-redact (OCR pre-scan) from the editor

`autoRedact` (`Editor.tsx:542-563`):

| State | Event | Guard | Action | Next |
|---|---|---|---|---|
| Idle | `Auto-redact` click | `img && !scanning` (the button is also disabled while saving) | `scanning = true`; `notice = null`; call `projects.redactScan(projectPath, step.id)` | Scanning |
| Scanning | result `[]` | | notice `{kind: 'info', text: 'No sensitive data detected (best-effort \u2014 redact manually if needed).'}` | Idle |
| Scanning | result `rects` (n > 0) | | append `rects.map(r => createBlur(r.x, r.y, r.width, r.height, 'solid'))` (block size 14, default); select the LAST added; tool unchanged | Idle |
| Scanning | rejection | | notice `{kind: 'error', text: message}` | Idle |

Suggestions are ordinary blur annotations: reviewed, moved, resized, re-moded or deleted like any other, and baked only when the user clicks Save; Cancel discards them. Nothing is scanned or suggested automatically. Save is not disabled while scanning; if the editor closes first, the late result is dropped (EDGE-EDIT-39). REQUIRED.

The scan handler (`ipc.ts:525-543`): `getProjectForRead(projectPath)` (the known-project gate; does not unarchive; errors propagate to the notice); find the step by id; if missing or `screenshot` is empty return `[]`; `abs = confinePath(dir, step.screenshot)` (lexical); if null return `[]`; return `scanForSensitiveRects(abs)`. It OCRs the ORIGINAL file on disk, ignoring the crop and any unsaved edits. REQUIRED (the read source is IMPROVEMENT D-EDIT-11).

### 2.15 The save pipeline

`onSave` (`Editor.tsx:565-613`):

1. Guard `img` loaded. `saving = true`, `notice = null`.
2. If a text edit is open, `editingTextId = null`.
3. `anns = annotations.filter(a => !(a.type === 'text' && !a.text.trim()))`; if anything was dropped, the state is updated too.
4. `click = step.click && clickImage ? { ...step.click, image: clickImage, ...(clickRadius != null ? { radius: clickRadius } : {}) } : null`.
5. `blob = await flattenToPng(img, anns, crop, click ? { x: click.image.x, y: click.image.y, color: markerColor, radius: clickRadius ?? undefined } : null)` (2.16).
6. `manifest = await projects.updateStep(projectPath, step.id, { annotations: anns, crop, click, markerColor, markerBaked: true }, bytes)`. The patch never contains `caption` (so an editor save never sets `captionEditedByUser`); `markerBaked` is true even when there is no click; `markerColor` is always sent, even without a click (EDGE-EDIT-24, EDGE-EDIT-25), but main keeps it only when it matches the 2.18 regex: a non-hex value seeded from the step (for example a hand-edited `"red"`) is silently not written, so the stored value stays. Main also drops every annotation without a string `type` and a string `id` AFTER the bake (EDGE-EDIT-54).
7. Success: `onSaved(manifest)`, `onClose()`.
8. Any throw from 5 or 6: notice `{kind: 'error', text: message}`; the editor stays open with its state.
9. Finally `saving = false`.

Steps 2 and 3 run BEFORE the `try` (`Editor.tsx:571-573`): an annotation that is `null`, or a `text` annotation whose `text` is not a string, throws a `TypeError` there, the rejection is unhandled (`void onSave()`), `saving` is never reset, and Save and Cancel stay disabled for good (Escape does not close the editor), so the only way out is closing the project window (EDGE-EDIT-67). ELECTRON-ONLY defect; natively `PrepareSave` never throws (7.3).

Main side (`ipc.ts:411-433`, `project-store.ts:765-790`): the patch is validated (2.18), then ONE write-queue job: resolve the known project; read the manifest; find the step (else throw `` `step ${stepId} not found` ``); `applyPatchAndInvalidate(step, patch, true)`; `writeStepRender(resolved, step, stepId, png)`; `updatedAt = now`; write the manifest; return it. REQUIRED. The whole sequence is synchronous from the user's point of view: success is reported only after both files are written (the fixed decision keeps this path non-optimistic, INV-EDIT-10).

While saving, only Save, Cancel and Auto-redact are disabled; the canvas stays interactive and any edit made during the save is lost when the editor closes (EDGE-EDIT-40).

### 2.16 The flatten algorithm (exact, in order)

`flattenToPng(image, annotations, crop, marker = null)` (`flatten.ts:42-111`). `nw, nh` = the image's natural size.

**Step 1: crop to integers** (`:50-53`):

```
cx = crop ? clamp(round(crop.x), 0, max(0, nw - 1)) : 0
cy = crop ? clamp(round(crop.y), 0, max(0, nh - 1)) : 0
cw = crop ? clamp(round(crop.width), 1, nw - cx) : nw
ch = crop ? clamp(round(crop.height), 1, nh - cy) : nh
```

**Step 2: the canvas** (`:55-62`): a `cw` by `ch` canvas (transparent). Missing 2D context throws `Error('2d canvas context unavailable')`. Draw the source rect `(cx, cy, cw, ch)` at `(0, 0, cw, ch)` (an exact 1:1 copy).

**Step 3: bake every redaction, destructively, BEFORE any overlay** (`:63-82`). For each annotation `a` in array order with `a.type === 'blur'`:

```
bx = finite(a.x); by = finite(a.y); bw = finite(a.width); bh = finite(a.height)
ow = min(bx + bw, cx + cw) - max(bx, cx)
oh = min(by + bh, cy + ch) - max(by, cy)
if ow <= 0 || oh <= 0: continue                          // entirely outside the export
if !bakeRedaction(...): throw Error(E_UNBAKEABLE)         // fail closed
```

`E_UNBAKEABLE = 'A redaction region could not be applied (too small or off-image). Adjust or remove it, then save again.'`

`bakeRedaction` (`:118-173`), in canvas coordinates:

```
x  = round(a.x - cx);  y = round(a.y - cy)
x0 = clamp(x, 0, cw);  y0 = clamp(y, 0, ch)
x1 = clamp(x + round(a.width), 0, cw);  y1 = clamp(y + round(a.height), 0, ch)
w = x1 - x0;  h = y1 - y0
if w <= 0 || h <= 0: return false
if a.mode === 'solid': fill (x0, y0, w, h) with '#000000'; return true
block = max(MIN_REDACT_BLOCK, round(a.blockSize || 12))            // MIN_REDACT_BLOCK = 8
sw = max(1, round(w / block));  sh = max(1, round(h / block))
tmp = sw x sh canvas; if its context is unavailable: fill (x0, y0, w, h) black; return true   // fail closed
tmp <- draw canvas region (x0, y0, w, h) scaled to (sw, sh), smoothing on        // "average" downsample
canvas region <- draw tmp scaled to (w, h), smoothing on                        // opaque destroyed base
clip to (x0, y0, w, h); filter = blur(max(1, round(block * 0.6))px)
canvas region <- draw tmp scaled to (w, h) again                                 // soft Gaussian over the base
restore (resets clip, filter, smoothing)
return true
```

Because the integer region is computed relative to an integer crop origin, `round(a.x - cx) = round(a.x) - cx`: the baked rect in image px is `[round(a.x), round(a.x) + round(a.width))` intersected with the crop. The downsample reads the CURRENT canvas, so a later blur that overlaps an earlier one samples the earlier bake (EDGE-EDIT-16). Any `mode` other than `'solid'` takes the pixelate path. Chromium's `drawImage` with the default `imageSmoothingQuality: 'low'` is a bilinear sample, not a true area average (EDGE-EDIT-60). REQUIRED behavior; the downsample kernel is IMPROVEMENT D-EDIT-1.

**Step 4: vector overlay** (`:83-87`, `drawVector` `:211-293`): translate by `(-cx, -cy)`, then for each annotation in array order:

| Type | Drawing |
|---|---|
| `rect` | path `roundRect(x, y, width, height, rr)` with `rr = max(0, min(cornerRadius, abs(width) / 2, abs(height) / 2))`; if `fill` is truthy, fill it with `fill`; stroke with `stroke`, `lineWidth = strokeWidth` (canvas defaults: butt cap, miter join, miter limit 10, stroke centered on the path). A missing or non-numeric `cornerRadius` makes `rr` NaN, and per the HTML standard `roundRect` then returns without adding a subpath, so the rect is INVISIBLE in the bake while Konva previews it with radius 0 (EDGE-EDIT-69) |
| `arrow` | `L = W = max(12, strokeWidth * 3)`; `len = hypot(x2 - x1, y2 - y1)`; `u = len > 0 ? (x2 - x1, y2 - y1) / len : (0, 0)`; `b = (x2, y2) - u * L`; `n = (-u.y, u.x)`; save; stroke and fill = `stroke`; `lineWidth = strokeWidth`; `lineCap = 'butt'`; `lineJoin = 'miter'`; if `len > 0`: stroke the segment (x1, y1) to (x2, y2) (the shaft runs to the tip); head: path (x2, y2), `b + n * W/2`, `b - n * W/2`, close; FILL then STROKE (the miter tip juts about 2 x strokeWidth past the geometric tip, exactly like Konva); restore |
| `stamp` | circle at (x, y) radius `radius`, filled `fill`; then text `String(n)` in `` `bold ${round(radius * 1.15)}px "Segoe UI", sans-serif` ``, `textAlign = 'center'`, `textBaseline = 'middle'`, at (x, y), filled `textColor` |
| `text` | `fillStyle = fill`; font `` `${fontSize}px "Segoe UI", sans-serif` ``; `textAlign = 'left'`; `textBaseline = 'top'`; `fillText(text, x, y)` (canvas replaces ASCII whitespace, including newlines, with spaces) |
| `blur` | nothing (baked in step 3) |
| `marker` | nothing (step 5) |
| anything else | nothing |

Only the arrow saves and restores state ("so `lineCap`/`lineJoin` don't leak onto later shapes", `:243-265`). An invalid color string assigned to `fillStyle` or `strokeStyle` is ignored by the canvas, which keeps the previous style (EDGE-EDIT-42). The same "ignore and keep the previous value" rule applies to `lineWidth` (zero, negative, infinite and NaN are ignored, so a `strokeWidth` of 0 strokes with the previous shape's width) and to `font` (an unparseable font string such as `NaNpx "Segoe UI", sans-serif` keeps the previous font, possibly a stamp's bold one) (EDGE-EDIT-69). Conversely the canvas ACCEPTS every CSS color syntax (named colors such as `red`, `rgb()`, `hsl()`, `transparent`), not only hex (EDGE-EDIT-42).

**Step 5: click markers, on top of everything** (`:88-103`, `drawClickMarker` `:176-196`): `R = clickMarkerRadius(nw, nh)` (the FULL image). If `marker`: ring at `(finite(marker.x) - cx, finite(marker.y) - cy)` with radius `marker.radius ?? R`. Then for each `marker` annotation in array order: ring at `(finite(a.x) - cx, finite(a.y) - cy)`, radius `a.radius ?? R`, color `a.color`. A ring: `arc(x, y, r, 0, 2*PI)`; fill with the color at `globalAlpha = 0.18`; then `globalAlpha = 1`, `lineWidth = max(2, round(r * 0.22))`, stroke with the color. Off-canvas rings draw harmlessly clipped.

**Step 6: encode** (`:105-110`): `canvas.toBlob(cb, 'image/png')`; a null blob rejects `Error('canvas.toBlob returned null')`. The result is always PNG, `cw` by `ch` (the PNG color type Chromium writes is UNVERIFIED; consumers decode, so only the dimensions are a contract).

Callers of the flatten: the editor save (2.15), the merge (`merge.ts:84-104`: the kept step's annotations plus the new marker, the kept crop, the kept click as the marker param with `markerColorFor(keep)` and no radius), and `ensureFlattened` (2.19). Exports and the Claude pass never flatten in the main process; they read what these wrote (2.20).

### 2.17 The render cache and freshness

`applyPatchAndInvalidate(step, patch, hasFreshPng)` (`step-render.ts:17-37`):

```
Object.assign(step, patch)                                 // patch keys in their insertion order (2.18)
if typeof patch.caption === 'string': step.captionEditedByUser = true      // before the early return (#62 gap 3)
if hasFreshPng: return                                     // the caller writes the render
if 'annotations' in patch || 'crop' in patch:              // own key present, whatever its value
    step.flattened = null
    step.markerBaked = false                               // the dropped render carried the baked marker
```

`hasFreshPng = !!(flattenedPng && flattenedPng.length)` (`project-store.ts:776`, `:820`). Redaction is enforced by render FRESHNESS, not mere existence: a new or changed blur or crop with no re-bake drops the stale render so the next egress is forced to re-flatten (the "Phase-3b review" finding). It is shared by `updateStep` and `mergeSteps`; `mergeSteps` previously lacked the invalidation (security fix S3, `HARDENING-PLAN.md:45-47`). REQUIRED.

`writeStepRender(resolved, step, id, png)` (`step-render.ts:44-60`):

```
rel = path.posix.join('export', '.render', id + '.png')   // a JOIN: dot segments INSIDE the id are normalized (EDGE-EDIT-62)
abs = confinePathNoSymlinks(resolved, rel)                 // #82 / #98: a symlinked component is refused
if !abs: throw Error(`refusing to write render for step "${id}" \u2014 path escapes the project folder`)
mkdir -p dirname(abs)
writeFile(abs, png)                                        // plain write, NOT atomic (EDGE-EDIT-20)
step.flattened = rel
step.renderRev = (step.renderRev ?? 0) + 1
```

In `updateStep` and `mergeSteps` the render is written BEFORE the manifest, inside the same queue job (`project-store.ts:776-783`, `:820-829`). REQUIRED ordering. The stored `flattened` is the joined (normalized) string, for example id `../../x` gives `x.png` (inside the project, accepted, written, stored as `flattened: "x.png"`), id `a/b` gives `export/.render/a/b.png` (a subfolder is created), id `../../../evil` gives `../evil.png` (refused). For every id shotAI generates (UUIDs, `step-NNNN` style ids) the join equals the concatenation `export/.render/<id>.png`. Natively the id rule is D-EDIT-22.

Consumers of the fields (REQUIRED semantics other specs rely on):

| Field | Meaning | Consumers |
|---|---|---|
| `flattened` | project-relative path of the current render; null or absent = no current render | render gate (2.20); report and SOP panel prefer it over the raw shot (`Report.tsx:67-71`, `SopPanel.tsx:17`); `ensureFlattened` skip rule |
| `renderRev` | bumped on every render write, never otherwise | cache-busts the report and SOP images: `` `${shotUrl(projectId, step.flattened)}?v=${step.renderRev ?? 0}` `` (`Report.tsx:67-71`), so a re-save reloads the image and a display-only change (zoom, pan) does not |
| `markerBaked` | truthy = the current render has the click ring in its pixels | the report draws its CSS ring overlay only when `step.click && !step.markerBaked` (`Report.tsx:96-102`); `ensureFlattened` re-bakes when falsy |
| `captionEditedByUser` | a human edited the caption | 07 (`captionForPrompt`); cleared by SOP apply |

### 2.18 StepPatch validation (the IPC boundary)

`parseStepPatch(value)` (`ipc.ts:194-226`) builds the patch in THIS key order, which is also the order `Object.assign` appends new keys to the step:

| # | Key | Rule |
|---|---|---|
| | (input) | not a truthy object: throw `Error('patch must be an object')` (an array passes and yields an empty patch) |
| 1 | `caption` | kept when a string |
| 2 | `heading` | kept when a string |
| 3 | `body` | kept when a string |
| 4 | `kind` | kept when `'shot'` or `'text'` |
| 5 | `callout` | when the key is present: a valid callout kind, else `undefined` (clears; the key is dropped on write) |
| 6 | `crop` | when present: `null` stays null; else `parseRect` (four finite numbers, else null) |
| 7 | `click` | when present: `null` stays null; else `parseClick` (below), which may return null |
| 8 | `markerColor` | kept when a string matching `/^#[0-9a-fA-F]{3,8}$/`, else silently ignored |
| 9 | `markerBaked` | kept when a boolean |
| 10 | `reportZoom` | finite number: `max(1, min(6, v))` |
| 11 | `reportPanX` | finite number: `max(0, min(1, v))` |
| 12 | `reportPanY` | finite number: `max(0, min(1, v))` |
| 13 | `annotations` | when an array: keep only elements that are truthy objects with a string `type` and a string `id` (EDGE-EDIT-54) |

`parseClick(value)` (`ipc.ts:147-174`): not a truthy object: null. `global` and `image` through `parsePoint` (finite x and y); either invalid: null (so an editor save REMOVES a click whose points are damaged, EDGE-EDIT-53). `button` kept if one of `'left'`, `'right'`, `'middle'`, `'other'`, else `'left'`. `radius` kept only when a finite number > 0. `imageScale` kept only when a finite number > 0 (T2: preserves the capture downscale across editor saves). Output key order: `global, image, button`, then `radius` and `imageScale` only when kept. Every other key of the click is dropped. REQUIRED (the native service boundary, 11, applies the same rules; 7.5).

### 2.19 Pre-egress preparation: `ensureFlattened`

`ensureFlattened(projectId, projectPath, steps, signal?)` (`sop-prepare.ts:29-74`) runs before every SOP estimate and generation (`SopPanel.tsx:131`), every in-project export and package export (`ProjectDetail.tsx:321`, `:345`) and every Home row or bulk export (`ProjectList.tsx:117`, `:301`):

```
latest = null; shotNo = 0
for step in steps (the caller's in-memory list, in order):
    if step.kind === 'text': continue
    shotNo++
    if step.flattened && step.markerBaked: continue          // current, marker-baked render exists
    if !step.screenshot: continue
    signal?.throwIfAborted()
    try:
        img = load ORIGINAL step.screenshot                  // error text: `Could not load a screenshot to flatten (${url}).`
        marker = step.click ? { x: click.image.x, y: click.image.y, color: markerColorFor(step) } : null   // no radius (EDGE-EDIT-26)
        png = flattenToPng(img, step.annotations, step.crop, marker)
        latest = projects.updateStep(projectPath, step.id, { markerBaked: true }, png)
    catch err:
        if err is an AbortError: rethrow
        caption = (step.caption ?? '').trim() || 'untitled'
        throw Error(`Step ${shotNo} ("${caption}") couldn't be prepared: ${err.message}`)
return latest                                                // null when nothing changed
```

Steps run one at a time. Any failure aborts the egress: the caller shows the error and does not generate or export (this is the renderer half of the "two-layer" guarantee; the gate is the main half). The patch `{ markerBaked: true }` has no `annotations` or `crop` key, and a fresh PNG is always co-written, so nothing is invalidated. REQUIRED.

### 2.20 The render gate

`resolveSendableRender(dir, step, stepLabel, verb)` (`render-gate.ts:27-49`), called for every shot step by the Claude request builder (`claude-service.ts:394`, verb `'send'`, label numbered over the AI-filtered list), the file exporter (`export.ts:388`, verb `'export'`, label over all steps) and the safe package export (`export-package.ts:88`, verb `'export'`):

```
hasBlur = (step.annotations ?? []).some(a => a.type === 'blur')
rel = step.flattened ?? null
if !rel && (hasBlur || step.crop):                           // JS truthiness of the RAW crop value
    throw Error(`${stepLabel} has a redaction or crop that hasn't been baked into a render yet \u2014 ` +
                `refusing to ${verb} the raw screenshot. Open it in the editor and save, then retry.`)
relToRead = rel ?? step.screenshot
abs = relToRead ? confinePath(dir, relToRead) : null         // lexical confinement
if !abs: throw Error(`${stepLabel} has no readable screenshot.`)
ext = path.extname(relToRead).toLowerCase()
mediaType = ext === '.jpg' || ext === '.jpeg' ? 'image/jpeg' : 'image/png'
return { abs, mediaType, ext: ext || '.png' }
```

Only a step with neither a blur nor a crop may read the original shot. The gate is implemented once so the Claude and export paths cannot drift (T1a). REQUIRED; hardened natively (D-EDIT-3, D-EDIT-4, D-EDIT-6).

### 2.21 OCR engine and detectors

**Engine** (`ocr.ts:1-89`): Tesseract.js 7 (WASM) in the main process, created lazily and reused: `createWorker('eng', 1, { cachePath: <userData>/tessdata, langPath, gzip: true })` where `oem = 1` is LSTM only and `langPath` is `<resources>/tessdata` when packaged or `<appPath>/vendor/tessdata` in development, holding the vendored `eng.traineddata.gz` (the LSTM `best_int` model, 2.82 MB, S7), so OCR is fully offline and never fetches from a CDN. An init failure clears the memo so a later scan retries (`:56-58`). A scan: `worker.recognize(imagePath, {}, { blocks: true })`; walk blocks, paragraphs, lines; for each line map words to `{ text, bbox: {x0, y0, x1, y1} }` (image px, x0/y0 top-left, x1/y1 right/bottom); keep non-empty lines; `detectSensitiveRects(lines)`. Any throw logs a warning and returns `[]` (best-effort; the manual gate is the guarantee). Default page segmentation. ELECTRON-ONLY engine; replaced by Windows.Media.Ocr (fixed decision, 7.9).

**Detectors** (`redact-detect.ts:1-138`), JavaScript regex semantics (no `u` flag: `\d` is `[0-9]`, `\b` is the ASCII word boundary over `[A-Za-z0-9_]`), all global:

| Name | Pattern (exact) | Validation |
|---|---|---|
| `SSN` | `/\b\d{3}-\d{2}-\d{4}\b/g` | none |
| `API_PATTERNS[0]` (OpenAI / Anthropic-style secret keys) | `/\bsk-[A-Za-z0-9_-]{16,}\b/g` | none |
| `API_PATTERNS[1]` (AWS access key id; the range tolerates OCR drift around the canonical 16) | `/\bAKIA[0-9A-Z]{12,20}\b/g` | none |
| `API_PATTERNS[2]` (GitHub tokens) | `/\bgh[posru]_[A-Za-z0-9]{20,}\b/g` | none |
| `API_PATTERNS[3]` (Slack tokens) | `/\bxox[baprs]-[A-Za-z0-9-]{10,}\b/g` | none |
| `API_PATTERNS[4]` (long hex: hashes, hex secrets) | `/\b[0-9a-fA-F]{40,}\b/g` | none |
| `API_PATTERNS[5]` (long base64-ish blobs) | `/\b[A-Za-z0-9+/]{40,}={0,2}\b/g` | none |
| `CARD` (13 to 19 digits with optional single space or dash separators) | `/\b(?:\d[ -]?){13,19}\b/g` | `digits = match.replace(/\D/g, '')`; accept iff `13 <= digits.length <= 19` and `luhn(digits)` |

There is NO issuer-prefix check (Visa 4, Mastercard 51-55 and so on): any Luhn-valid 13 to 19 digit run matches. Email and phone numbers are deliberately excluded as too noisy (`:5-7`). REQUIRED (Q-EDIT-12).

`luhn(digits)` (`:35-49`): from the last character backwards, `d = charCode - 48`, any `d` outside 0..9 returns false; every second digit (starting with the second from the right) is doubled and reduced by 9 when > 9; valid iff the sum is divisible by 10.

`detectSensitiveRects(lines, pad = 4)` (`:90-138`):

```
rects = []
for line in lines:
    if no words: continue
    text = words joined with single spaces; spans[i] = [start, end) of word i in text (UTF-16 indices)
    addMatch(mStart, mEnd):
        covered = words whose span satisfies start < mEnd && end > mStart
        if none: return
        x0 = min bbox.x0; y0 = min bbox.y0; x1 = max bbox.x1; y1 = max bbox.y1   over covered
        rects.push({ x: x0 - pad, y: y0 - pad, width: x1 - x0 + 2*pad, height: y1 - y0 + 2*pad })
    run SSN; then each API pattern in table order; then CARD with its validation
    (each run: every non-overlapping match left to right, lastIndex reset first)
return mergeRects(rects)
```

`mergeRects` (`:70-82`): `out = []`; for each `r` in order: `merged = r`; for `i` from `out.length - 1` down to 0: if `overlaps(out[i], merged)` (strict: `a.x < b.x + b.w && a.x + a.w > b.x && a.y < b.y + b.h && a.y + a.h > b.y`, so touching rects do not merge), `merged = union(out.splice(i, 1), merged)`; then push `merged`. It is order dependent and can leave overlapping rects when a union grows into an entry already passed (EDGE-EDIT-48). Rects are not clamped to the image (EDGE-EDIT-47). REQUIRED, ported 1:1.

### 2.22 User-visible strings

| String | Where | Citation |
|---|---|---|
| `Edit screenshot` | overlay `aria-label` | `ProjectDetail.tsx:686` |
| `Editor tools` | rail `aria-label` | `Editor.tsx:642` |
| `Draw`, `Mark`, `Crop` | rail group labels | `:69-73` |
| tool labels and tooltips | 2.4 table | `annotations.ts:23-32` |
| `Zoom the canvas for precise editing`, `Fit`, `\u2212`, `+`, `` `${Math.round(editorZoom * 100)}%` `` | zoom cluster | `Editor.tsx:674-697` |
| `Apply crop`, `Show full`, `Work on just the cropped region (non-destructive)`, `Reset crop` | top bar | `:704-728` |
| `Auto-redact`, `Scanning\u2026`, `Scan this screenshot for SSNs, credit cards, and API keys, and add redaction boxes to review` | top bar | `:729-737` |
| `Cancel`, `Save`, `Saving\u2026` | top bar | `:739-749` |
| `Color`, `Size`, `Text size`, `Width`, `Line width`, `How to hide`, `How to hide this region`, `Blur`, `Black box`, `Blur softens the pixels; Black box covers them \u2014 both are permanently applied to the exported image`, `Strength`, `Blur strength \u2014 higher is stronger; the minimum keeps text unreadable`, `Edit text`, `Edit this text (or double-click it)`, `Remove marker`, `Remove crop`, `Delete element` | properties bar | `:756-872` |
| `Select an element to change its color or size, edit its text, or delete it.` | properties bar hint | `:875-877` |
| `Type\u2026` | text input placeholder | `:909` |
| `Loading screenshot\u2026` | canvas before load | `:942` |
| hint line texts and `Redactions are permanently applied to the exported image on save.` | 2.4 | `:1212-1223` |
| `Could not load the screenshot.` | error notice | `:166` |
| `No sensitive data detected (best-effort \u2014 redact manually if needed).` | info notice | `:548-552` |
| `A redaction region could not be applied (too small or off-image). Adjust or remove it, then save again.` | save error | `flatten.ts:78-80` |
| `2d canvas context unavailable`, `canvas.toBlob returned null` | save error (practically unreachable) | `flatten.ts:59`, `:107` |
| `` `refusing to write render for step "${id}" \u2014 path escapes the project folder` `` | save error | `step-render.ts:55` |
| `` `step ${stepId} not found` `` | save error | `project-store.ts:775` |
| `` `${stepLabel} has a redaction or crop that hasn't been baked into a render yet \u2014 refusing to ${verb} the raw screenshot. Open it in the editor and save, then retry.` `` | egress refusal (07, 09 show it) | `render-gate.ts:37-40` |
| `` `${stepLabel} has no readable screenshot.` `` | egress refusal | `render-gate.ts:44` |
| `` `Step ${shotNo} ("${caption}") couldn't be prepared: ${message}` ``, `untitled` | egress preparation failure | `sop-prepare.ts:66-69` |
| `` `Could not load a screenshot to flatten (${url}).` `` | preparation load failure | `sop-prepare.ts:17` |
| `Dismiss`, `\u00D7` | notice dismiss button | `Notice.tsx:28-35` |

Errors thrown in the MAIN process (the store's `step ... not found`, the render-write refusal, the known-project gate of the scan handler) reach the renderer through `ipcMain.handle`, and Electron prefixes their message, so the editor notice actually reads `` Error invoking remote method 'projects:update-step': Error: step abc not found `` (and the same prefix with `projects:redact-scan`). The prefix is ELECTRON-ONLY (D-IPC-2): natively these are direct calls and the notice shows `UserMessage.From(exception)` (ARCHITECTURE 8.2), which is the exception message alone for every `ShotAIException`. Errors raised in the renderer (the flatten's `E_UNBAKEABLE`, the load failure, the `ensureFlattened` wrapper) never carried the prefix, although the `ensureFlattened` wrapper can embed a prefixed inner message from `updateStep`.

### 2.23 Log lines

| Level | Text | Citation |
|---|---|---|
| info (`ocr`) | `` `initializing OCR worker (vendored lang: ${langPath}, cache: ${cachePath})` `` | `ocr.ts:52` |
| info (`ocr`) | `` `auto-redact: ${lines.length} line(s), ${rects.length} sensitive region(s) in ${path.basename(imagePath)}` `` | `ocr.ts:81-83` |
| warn (`ocr`) | `'auto-redact: OCR scan failed (best-effort, skipping):'` followed by the error | `ocr.ts:86` |
| dev | `ipc: projects:update-step`, `ipc: projects:merge-steps`, `ipc: projects:redact-scan` | `ipc.ts:419`, `:495`, `:528` |

The recognized text is never logged. REQUIRED (the dev IPC lines are ELECTRON-ONLY).

---

## 3. Constants

| Name | Value | Unit | Meaning | Citation |
|---|---|---|---|---|
| `CLICK_ID` | `'__click__'` | id | pseudo selection id of the click marker | `Editor.tsx:50` |
| `CROP_ID` | `'__crop__'` | id | pseudo selection id of the crop box | `Editor.tsx:51` |
| `VIEW_W` | 940 | CSS px | viewport width before the first measurement | `Editor.tsx:53` |
| `VIEW_H` | 540 | CSS px | viewport height before the first measurement | `Editor.tsx:54` |
| `MIN_DRAG` | 6 | image px | minimum draft width and height (rect, blur, crop) and arrow length | `Editor.tsx:55` |
| zoom min / max / step | 0.5 / 8 / x1.25 | factor | editor zoom on top of the fit | `Editor.tsx:678`, `:693` |
| crop-view fit cap | 8 | factor | max base scale in the cropped view | `Editor.tsx:212` |
| text opening guard | 300 | ms | window in which one spurious blur is absorbed | `Editor.tsx:470-472` |
| `ACCENT` | `'#e11d48'` | color | default shape and marker color (rose-600) | `annotations.ts:35` |
| `RIGHT_CLICK_COLOR` | `'#2563eb'` | color | right-click marker default (blue-600), via `markerColorFor` only | `annotations.ts:36` |
| `DEFAULT_STROKE_WIDTH` | 4 | image px | stroke width before the image loads | `annotations.ts:37` |
| `DEFAULT_BLOCK_SIZE` | 14 | image px | default mosaic factor | `annotations.ts:38` |
| `MIN_REDACT_BLOCK` | 8 | image px | floor of the mosaic factor in bake, preview and slider | `flatten.ts:17` |
| blockSize fallback | 12 | image px | used when `blockSize` is falsy (bake only) | `flatten.ts:149` |
| Gaussian sigma | `max(1, round(block * 0.6))` | image px | soft pass blur radius (CSS `blur()` standard deviation) | `flatten.ts:169`, `BlurRegion.tsx:58` |
| rect `cornerRadius` | 10 | image px | new boxes and the rect draft | `annotations.ts:72`, `Editor.tsx:1122` |
| stamp default radius (factory) | 22 | image px | factory default (the editor always passes the formula) | `annotations.ts:125` |
| text default size (factory) | 28 | px | factory default (the editor always passes the formula) | `annotations.ts:154` |
| stamp `textColor` | `'#ffffff'` | color | digits | `annotations.ts:136` |
| stamp font | `round(radius * 1.15)`, bold | px | digit size | `Editor.tsx:1050`, `flatten.ts:274` |
| click marker radius | `max(14, min(60, round(min(w,h) * 0.02)))` | image px | derived ring radius | `annotations.ts:117-119` |
| default stroke width | `max(4, min(50, round(min(w,h) * 0.008)))` | image px | seeded on image load | `annotations.ts:141-143` |
| default stamp radius | `max(16, min(72, round(min(w,h) * 0.022)))` | image px | new stamps | `annotations.ts:146-148` |
| default font size | `max(16, min(96, round(min(w,h) * 0.022)))` | px | new text | `annotations.ts:169-171` |
| marker radius when no image | 20 | image px | `markerR` fallback | `Editor.tsx:204` |
| ring stroke width | `max(2, round(r * 0.22))` | image px | click marker and marker annotations | `Editor.tsx:1073`, `flatten.ts:192` |
| ring fill alpha | 0.18 (bake), hex suffix `2e` = 46/255 (preview) | alpha | translucent ring fill | `flatten.ts:188`, `Editor.tsx:1074` |
| arrow head | `max(12, strokeWidth * 3)` | image px | pointer length and width | `Editor.tsx:1011-1012`, `flatten.ts:234-235` |
| resize minimum (rect, blur) | 4 | image px | width and height floor after a transform | `Editor.tsx:412-413` |
| resize minimum (crop) | 8 | image px | width and height floor after a transform | `Editor.tsx:390-391` |
| resize minimum (radius) | 6 | image px | stamp, marker, click ring | `Editor.tsx:402`, `:422` |
| transformer minimum box | 8 | CSS px | proposed boxes smaller than this are rejected | `Editor.tsx:1202-1204` |
| transformer anchor | 10 by 10, fill `white`, stroke `rgb(0, 161, 255)` 1 px; border `rgb(0, 161, 255)` 1 px | CSS px | handle look | Konva `Transformer.js:1039-1053` |
| Konva drag distance | 3 | CSS px | pointer travel before a drag starts | Konva `Global.js:40` |
| Konva double-click window | 400 | ms | double-click detection | Konva `Global.js:19` |
| crop box stroke | `#2563eb`, 3, dash `[10, 6]` | color, image px | crop box and crop draft | `Editor.tsx:1123-1125`, `:1158-1160` |
| crop box fill | `rgba(37,99,235,0.06)` | color | grab area in the full view | `Editor.tsx:1163` |
| blur draft and preview fallback fill | `rgba(15,23,42,0.55)` | color | | `Editor.tsx:1135`, `BlurRegion.tsx:72` |
| text selection outline | pad 6, stroke `#4f46e5`, 2, dash `[6, 4]` | image px | | `Editor.tsx:1186-1192` |
| Size slider | 10 to 160, step 1 | px | text size | `Editor.tsx:774-775` |
| Width slider | 1 to 80, step 1 | image px | stroke width | `Editor.tsx:788-789` |
| Strength slider | 8 to 60, step 1 | image px | mosaic factor | `Editor.tsx:831-832` |
| rail width | 104 | CSS px | tool rail | `editor.css:39` |
| properties bar min height | 2.5 | rem | reserved height | `editor.css:87` |
| overlay padding / backdrop | 1.5 rem / `rgba(15, 23, 42, 0.55)` | | editor overlay | `editor.css:13-14` |
| text input background | `rgba(255, 255, 255, 0.14)` | color | inline editor | `editor.css:256` |
| render path | `path.posix.join('export', '.render', id + '.png')` (equals `export/.render/<id>.png` for a single-segment id; natively that is the only accepted form, D-EDIT-22) | rel path | render location | `step-render.ts:51` |
| markerColor regex | `^#[0-9a-fA-F]{3,8}$` | regex | patch validation | `ipc.ts:209` |
| reportZoom clamp | `[1, 6]` | factor | patch validation | `ipc.ts:216` |
| reportPan clamp | `[0, 1]` | fraction | patch validation | `ipc.ts:217-218` |
| click buttons | `'left'`, `'right'`, `'middle'`, `'other'` (default `'left'`) | enum | patch validation | `ipc.ts:139-156` |
| OCR pad | 4 | image px | padding on every detected rect | `redact-detect.ts:90` |
| card digit count | 13 to 19 | digits | card validation | `redact-detect.ts:133` |
| Tesseract language / OEM | `'eng'` / 1 (LSTM only) | | ELECTRON-ONLY engine config | `ocr.ts:53` |
| vendored model | `eng.traineddata.gz` (2.82 MB `best_int`) | file | ELECTRON-ONLY | `ocr.ts:7-10`, `HARDENING-PLAN.md` S7 |
| native: OCR language preference | `en-US`, then the user profile languages | BCP-47 | 7.9 | IMPROVEMENT D-EDIT-11 |
| native: OCR max dimension | `OcrEngine.MaxImageDimension` (read at runtime) | px | downscale above it | 7.9 |

---

## 4. Invariants

**INV-EDIT-1 [SECURITY]. Every redaction that overlaps the exported region is baked into the pixels before any overlay, or the flatten throws.** Never emit a PNG with an unobscured area the user believes is redacted. Why: the redaction guarantee is the product's primary security asset (`HARDENING-PLAN.md:5`, `PLAN.md:132`). Citation: `flatten.ts:63-82`. Test: `FlattenerTests.SubPixelRedactionFailsClosed`, `FlattenerTests.RedactionOverlappingCropEdgeByUnderHalfPixelFailsClosed`, `FlattenerTests.BlurIsBakedBeforeOverlays` (a rect stroke over a solid region stays visible, the region under it is black).

**INV-EDIT-2 [SECURITY]. Inside a baked pixelate region, every output pixel is a function of the downsampled tile only.** No original pixel value can reach the output except through the per-cell average. Why: averaged text cannot be reconstructed, raw pixels can. Citation: `flatten.ts:141-172` (the design intent); natively strengthened to a true box average (D-EDIT-1). Test: `RedactionBakerTests.PermutingPixelsWithinACellDoesNotChangeTheOutput`, `RedactionBakerTests.EqualCellAveragesGiveIdenticalOutput`.

**INV-EDIT-3 [SECURITY]. A solid redaction is exactly opaque black over the whole integer region.** Citation: `flatten.ts:135-139`. Test: `RedactionBakerTests.SolidIsExactlyOpaqueBlack` (every pixel B=G=R=0, A=255), ported `FlattenTests.solidRedactionIsOpaqueBlack` (`macOS:Packages/EditorKit/Tests/EditorKitTests/FlattenTests.swift:47-56`).

**INV-EDIT-4 [SECURITY]. The bake's mosaic factor is never below `MIN_REDACT_BLOCK` = 8, whatever the stored `blockSize`.** A falsy `blockSize` means 12. Why: below 8 averaged text can stay legible; a hand-edited manifest must not be able to blur text back into legibility. Citation: `flatten.ts:15-17`, `:147-149`. Test: `RedactionBakerTests.BlockSizeIsFlooredAtEight` (`blockSize` 1, 0, -5, 1e300 give 8, 12, 8 and a 1 by 1 tile).

**INV-EDIT-5 [SECURITY]. Every flatten starts from the ORIGINAL screenshot, never from an existing render.** Why: re-flattening a render would stack bakes and could not remove a redaction the user deleted; conversely the raw shot is the only source that makes the new annotation list authoritative. Citation: `Editor.tsx:152-168`, `sop-prepare.ts:46`, `merge.ts:52`; `macOS:shotAI/Editor/EditorModel.swift:9-13`. Test: `StepFlattenerTests.LoadsScreenshotNotFlattened`, `EditorSaveTests.FlattensFromTheOriginal`.

**INV-EDIT-6 [SECURITY]. Egress reads images only through the render gate: a shot step with a (possible) redaction or a crop and no current render is refused, never downgraded to the raw screenshot.** Egress = the Claude request (07), every file export and the safe package (09). Citation: `render-gate.ts:33-41`, call sites `claude-service.ts:394`, `export.ts:388`, `export-package.ts:88`. Test: `RenderGateTests` (8.1) and `RenderGateMayRedactTests` (8.2).

**INV-EDIT-7 [SECURITY]. Freshness: a patch that has an own `annotations` or `crop` key and no co-written PNG clears `flattened` and sets `markerBaked = false`.** Why: redaction is enforced by render freshness, not existence (Phase-3b review). Citation: `step-render.ts:31-36`. Test: `StepPatchApplierTests` (8.1).

**INV-EDIT-8 [SECURITY]. `UpdateStepAsync` and `MergeStepsAsync` use the same applier.** Why: `mergeSteps` once lacked the invalidation, so an unbaked redaction could ride a stale render to egress (S3). Citation: `step-render.ts:9-16`, `project-store.ts:776`, `:820`, `HARDENING-PLAN.md:45-47`. Test: `StoreRenderTests.MergeWithoutPngInvalidates`, `StoreRenderTests.UpdateWithoutPngInvalidates`.

**INV-EDIT-9 [SECURITY]. The render write path is `export/.render/<id>.png` for a single-segment id, confined with link refusal; a refused path or a non-segment id (D-EDIT-22) throws and writes nothing.** Why: a symlinked or junctioned `export/` would redirect the write out of the project (#82, #98), and a dotted id would redirect it inside the project (EDGE-EDIT-62). Citation: `step-render.ts:50-55`. Test: `StepRenderWriterTests.RefusesNonSegmentIds`, `StepRenderWriterTests.RefusesSymlinkedExportFolder`, `Platform.Tests/Rendering/RenderWriteJunctionTests.RefusesJunctionedExportFolder`.

**INV-EDIT-10. The editor save path is synchronous and durable: flatten, write the render, write the manifest, and only then report success and close.** No optimistic update, no background retry. Why: the redaction bake must be on disk before anything can read it (fixed decision). Citation: `Editor.tsx:586-607`, `project-store.ts:765-790`. Test: `EditorSaveTests.ReportsSuccessOnlyAfterBothFilesAreWritten` (a fake store that delays completion; the editor stays open and Saving until it completes), `EditorSaveTests.FailureKeepsEditorOpenWithState`.

**INV-EDIT-11. `renderRev` increases by exactly 1 per render write and never otherwise; image caches key on `(flattened, renderRev)`.** Why: a re-save must refresh the report image, a zoom change must not reload it. Citation: `step-render.ts:59`, `Report.tsx:67-71`. Test: `StepRenderWriterTests.BumpsRenderRevFromAbsentAndFromN`.

**INV-EDIT-12. `markerBaked` is true only for a render produced by the marker-aware flatten.** Set by the editor save, the merge and `ensureFlattened`; cleared by invalidation; the report draws its overlay ring iff `click && !markerBaked`. Why: without it the ring would be doubled or missing, and Claude would not see where the user clicked. Citation: `project.ts:301-308`, `Editor.tsx:603`, `merge.ts:102`, `sop-prepare.ts:61`, `Report.tsx:96`. Test: `StepPatchApplierTests.DropsStaleRenderWhenBlurAddedWithoutPng` asserts the clear; `StepFlattenerTests.RebakesWhenMarkerBakedFalsy`.

**INV-EDIT-13. `captionEditedByUser` is set iff the patch carries a string `caption` (including `""`), and it is set before the fresh-PNG early return.** Why: a regenerate rewrites from a human correction (#62 gap 3); flagging editor saves would defeat the pre-AI-original rule. Citation: `step-render.ts:23-30`. Test: `StepPatchApplierTests` caption group (8.1).

**INV-EDIT-14. All annotation, crop and click geometry is in image px of the stored original, independent of the view scale, zoom, scroll, crop view and screen DPI.** Citation: `Editor.tsx:1-4`, `project.ts:126-132`. Test: `EditorPointerMappingTests` (App.Tests): a click at a known DIP position under zoom 2 in the crop view lands at the expected image px.

**INV-EDIT-15. Crop is non-destructive in the model.** Annotations keep full-image coordinates; the crop is applied only by the flatten; `Apply crop` / `Show full` change only the view. Citation: `Editor.tsx:100-105`, `:208-215`, `:704-716`. Test: `EditorDocumentTests.ApplyCropDoesNotChangeTheModel`.

**INV-EDIT-16. Every `Math.round` in this subsystem is ported as `JsMath.Round`, and every clamp is evaluated in doubles before any integer conversion.** Why: banker's rounding moves crops and redaction edges by a pixel at halves; converting before clamping overflows on crafted values (`macOS:Packages/EditorKit/Sources/EditorKit/Flatten.swift:349-361` needed `satInt` for the same reason). Citation: `flatten.ts:50-53`, `:125-151`. Test: `FlattenerTests.HalfPixelCropRoundsLikeJavaScript` (crop x 2.5 gives cx 3, x -0.5 gives 0), `FlattenerTests.ExtremeGeometryDoesNotThrowOverflow`.

**INV-EDIT-17. Auto-redact is an assist: suggestions are ordinary solid blur annotations that exist only in the editor until the user saves; an OCR failure never blocks editing or saving.** Citation: `Editor.tsx:538-563`, `ocr.ts:1-5`, `:85-87`, `PHASE-3-PLAN.md:114-124`. Test: `EditorDocumentTests.AutoRedactAppendsSolidBlursAndSelectsLast`, `EditorDocumentTests.CancelDiscardsSuggestions`, `WindowsOcrEngineTests.FailureReturnsStatusNotThrow`.

**INV-EDIT-18 [SECURITY]. OCR runs locally and offline, and the recognized text is never logged, persisted or sent.** Citation: `ocr.ts:7-10`, `:81-83`. Test: `OcrLoggingTests.LogsCountsOnly` (a capturing logger sees no word text), code review of 7.9.

**INV-EDIT-19. The detector set is exactly: SSN, card (Luhn, 13 to 19 digits, no issuer prefixes), and the six API patterns, in that run order; email and phone are excluded.** Citation: `redact-detect.ts:21-31`, `:129-134`. Test: `SensitiveTextDetectorTests` (8.1, 8.2).

**INV-EDIT-20. Detector regexes have ECMAScript semantics: `\d` is ASCII `[0-9]`, `\b` is the ASCII word boundary, indices are UTF-16 code units.** Why: .NET's default `\d` and `\b` are Unicode-aware, which would match Arabic-Indic or full-width digits Electron ignores (the macOS port diverged here: `macOS:Packages/EditorKit/Sources/EditorKit/RedactDetect.swift:128` filters with `isNumber`). Citation: `redact-detect.ts:21-31`. Test: `SensitiveTextDetectorTests.UnicodeDigitsDoNotMatch`, `SensitiveTextDetectorTests.Base64PaddingBoundaryMatchesJavaScript`.

**INV-EDIT-21. An empty text annotation (`JsString.Trim(text) == ""`) is never persisted or baked.** Citation: `Editor.tsx:501-509`, `:569-573`. Test: `EditorDocumentTests.EmptyTextIsDroppedOnFinishAndOnSave`.

**INV-EDIT-22. Removing the click marker persists `click: null`; keeping it persists the edited `image` and `radius` merged into the original click.** Citation: `Editor.tsx:575-584`. Test: `EditorSaveTests.RemovedMarkerPersistsNullClick`, `EditorSaveTests.MovedMarkerKeepsGlobalButtonAndImageScale`.

**INV-EDIT-23. The editor save patch is exactly `{annotations, crop, click, markerColor, markerBaked: true}`, never `caption`.** Citation: `Editor.tsx:603`, `step-render.test.ts:58-69`. Test: `EditorSaveTests.PatchHasExactlyTheFiveKeys`.

**INV-EDIT-24. The flatten output is a PNG of exactly `cw` by `ch` pixels.** Citation: `flatten.ts:50-57`. Test: `FlattenerTests.CropProducesExpectedDimensions` (port of `macOS:.../FlattenTests.swift:166-173`), `FlattenerTests.PlainFlattenPreservesTheImage`.

**INV-EDIT-25. Paint order is: source, blur bakes (array order), vectors (array order), the step's click marker, marker annotations (array order).** Citation: `flatten.ts:61-103`. Test: `FlattenerTests.PaintOrder` (a recording rasterizer sees vectors before markers; a text drawn over a solid region is visible).

**INV-EDIT-26. Before any egress, every shot step lacking `flattened` or `markerBaked` is re-baked from its original, one at a time, and any failure aborts the egress with a message naming the step.** Citation: `sop-prepare.ts:29-74`. Test: `StepFlattenerTests` (8.2).

**INV-EDIT-27 [SECURITY]. Natively, a render write and its manifest write succeed or fail together from any reader's point of view.** IMPROVEMENT D-EDIT-5: the render is written atomically, and if the manifest write then fails the previous render bytes are restored (or the new render deleted). Why: Electron can leave a new render (for example without a blur the user removed) behind a manifest that still describes the old state, after the user saw "save failed" (EDGE-EDIT-20). Citation: `project-store.ts:776-783`, `step-render.ts:57`. Test: `StoreRenderTests.ManifestFailureRestoresPreviousRender`, `StoreRenderTests.ManifestFailureDeletesRenderWhenNoneExisted`.

**INV-EDIT-28. Annotation arrays pass through the editor verbatim: unknown annotation types and unknown keys survive an editor save when the element has a string `type` and a string `id`.** Citation: `Editor.tsx:96-98`, `:246-249`, `ipc.ts:219-225`. Test: `EditorSaveTests.UnknownAnnotationSurvivesSave`, `StepPatchValidatorTests.AnnotationWithoutStringIdIsDropped`.

**INV-EDIT-29. Natively, the editor preview and the bake use one painter.** IMPROVEMENT D-EDIT-8: rect, arrow, stamp, text and ring drawing is a single routine used by the canvas and by the overlay rasterizer, with the same font. Why: Electron's Konva preview used Arial and a different text baseline from the canvas bake's Segoe UI (EDGE-EDIT-55). Test: `PainterParityTests.PreviewAndBakeProduceTheSamePixels` (App.Tests).

**INV-EDIT-30 [SECURITY]. Natively, every read that produces an egress-able render or feeds egress refuses reparse points (symlinks, junctions) in the path.** The gate, `ensureFlattened`, the editor source load and the OCR source load use `PathConfine.ConfineNoLinks`. IMPROVEMENT D-EDIT-4, from `macOS:Packages/ShotModel/Sources/ShotModel/RenderGate.swift:37-41` and macOS `26bb2b2`. Why: a shared project can plant `shots/leak.png` as a link to any file; a following read would put that file's bytes into a Claude request or an export. Test: `RenderGateTests.RefusesLinkedRender` (fake probe), `Platform.Tests/Redaction/RenderGateJunctionTests`.

**INV-EDIT-31 [SECURITY]. The gate treats as a possible redaction any annotation element that is not a JSON object whose `type` is one of `rect`, `arrow`, `stamp`, `text`, `marker`.** IMPROVEMENT D-EDIT-3 (macOS #109). Test: `RenderGateMayRedactTests`.

**INV-EDIT-32 [SECURITY]. Natively, the annotation list the editor save bakes is exactly the list it persists.** `PrepareSave` applies the `parseStepPatch` element filter (a JSON object with a string `type` and a string `id`) BEFORE the flatten, so no element is baked into the render and then dropped from the manifest, or the reverse. IMPROVEMENT D-EDIT-25. Why: Electron bakes `anns` and main filters them afterwards (`Editor.tsx:586-605`, `ipc.ts:219-225`), so a blur without a string `id` is in the render but not in the manifest, and the next re-bake after any invalidation silently loses that redaction (EDGE-EDIT-54). Test: `EditorSaveTests.BakedListEqualsPersistedList`.

---

## 5. Edge cases and hard-won fixes

**EDGE-EDIT-1.** The image pulsed endlessly on zoom (B3): a vertical scrollbar appearing on `.ed__canvas` changed its `clientWidth`, which changed the width-fit scale, which changed the stage height, which toggled the scrollbar. Required: the width used for the fit must not depend on scrollbar visibility, and the height must come from a non-scrolling ancestor. Electron: `scrollbar-gutter: stable both-edges` and height from `.ed__canvaswrap`. Origin: `HARDENING-PLAN.md` B3. Citation: `Editor.tsx:176-196`, `editor.css:182-185`. Natively: 7.10.3.

**EDGE-EDIT-2.** Contain-fit shrank tall screenshots to fit the height (T1). Required: fit to WIDTH (`viewport.w / natW`), top-anchored, scroll down; small images scale up to the width. Origin: `HARDENING-PLAN.md` T1. Citation: `Editor.tsx:200-203`, `editor.css:177-180`.

**EDGE-EDIT-3.** A reopened step with a saved crop used to open on the full capture with a stray crop box (the crop was always persisted, only the view was wrong). Required: open in the cropped view when `step.crop` is set. Citation: `Editor.tsx:100-105`.

**EDGE-EDIT-4.** Picking the Crop tool used to reset the zoom to 100%, a jarring jump (reported bug). Required: the Crop tool switches to the full view and keeps the current zoom. `Apply crop` / `Show full` DO reset the zoom to 1. Citation: `Editor.tsx:659-661`, `:708-711`; the macOS port kept this (`macOS:shotAI/Editor/EditorOverlay.swift` tool button comment).

**EDGE-EDIT-5.** The creating click's mouse-up stole focus from the just-opened text input, whose blur then finished and removed the empty text (E7). Required: absorb exactly one blur within 300 ms of opening and refocus on the next frame. Citation: `Editor.tsx:147-150`, `:461-474`, `:927-936`; `HARDENING-PLAN.md:142`.

**EDGE-EDIT-6.** Existing text could not be reliably re-edited (E1). Required: both double-click (select tool) and an explicit `Edit text` button reopen the inline editor in place. Citation: `Editor.tsx:840-849`, `:1090`; `HARDENING-PLAN.md` E1, E7.

**EDGE-EDIT-7.** Drawing had to stop exactly at the image edge. Required: the drag is tracked on the window, the cursor may roam outside the stage, and the shape is finalized from the last drafted geometry, not a fresh pointer read, so a mouse-up outside the stage still completes it. Citation: `Editor.tsx:319-373`.

**EDGE-EDIT-8.** A hand-rolled inverse of the stage transform (container rect plus scale) diverged from the library's content-rect normalization (DPI, a container border, sub-pixel rounding), so an arrow tip landed off from where it was drawn. Required: the drag start and the moving end go through the same pointer-to-image mapping. Citation: `Editor.tsx:304-317`. Natively: one `ToImage(Point)` function on the canvas used for every event (7.10.3).

**EDGE-EDIT-9.** The baked arrow looked shorter than the editor's after saving because flatten only filled the head while Konva fills and strokes it with a miter join, so Konva's tip juts about 2 x strokeWidth further. Required: shaft stroked to the tip with a butt cap; head filled AND stroked with a miter join. A round cap draws a half disc past the tip (macOS found the same). Citation: `flatten.ts:226-266`; `macOS:Packages/EditorKit/Sources/EditorKit/Flatten.swift:229-235`.

**EDGE-EDIT-10.** Line cap and join set for the arrow leaked onto later shapes. Required: arrow drawing restores state; natively each shape gets explicit pens (no shared state). Citation: `flatten.ts:243-265`.

**EDGE-EDIT-11 [SECURITY].** A blur inside the export whose rounded size is 0 (for example width 0.4) overlaps the region (0.4 > 0) but bakes nothing. Required: fail closed with `E_UNBAKEABLE`. Citation: `flatten.ts:73-81`, `:131-133`; ported test `macOS:.../FlattenTests.swift:143-150`.

**EDGE-EDIT-12.** A blur that overlaps the crop edge by less than half a pixel (for example crop width 100, blur x 99.7) passes the overlap test but rounds to an empty region, so the save fails with `E_UNBAKEABLE` even though the blur is almost entirely outside. Required: parity (the user moves or removes it). Citation: `flatten.ts:73-81`, `:125-133`.

**EDGE-EDIT-13.** A blur entirely outside the crop is skipped, not an error, and the render gate still treats the step as redacted (the render exists). Required. Citation: `flatten.ts:75`; ported test `macOS:.../FlattenTests.swift:152-162`.

**EDGE-EDIT-14 [SECURITY].** `blockSize` from a hand-edited or foreign manifest: `0`, `null`, `""`, `false`, absent give 12; `true` gives 8 (`round(true)` is 1); `"20"` gives 20; `[]` gives 8. A non-numeric string such as `"abc"` or an object gives `NaN`, which makes the tile canvas 0 by 0 and every draw a no-op: Electron leaves the ORIGINAL pixels under a pixelate region and reports success. Required natively: JS `ToNumber` coercion for every value Electron handles, and a NaN result treated as 12 (IMPROVEMENT D-EDIT-2, fail closed). Citation: `flatten.ts:149-171`; macOS decodes a missing or non-number `blockSize` as 12 (`macOS:Packages/ShotModel/Sources/ShotModel/Annotations.swift:100-110`).

**EDGE-EDIT-15 [SECURITY].** Blur geometry that is not a finite JSON number (`"50"`, `null`, `NaN` cannot occur in JSON but a string can) becomes 0 through `finite()`, and a negative width or height makes the overlap non-positive: in both cases Electron SKIPS the blur silently, the render is written, and the gate passes (the render exists), while Konva's preview of a negative-size rect draws toward the negative side. Required natively: a `blur` element whose `x`, `y`, `width` or `height` is not a finite JSON number, or whose `width` or `height` is negative, fails the flatten with `E_UNBAKEABLE` (IMPROVEMENT D-EDIT-2). A finite zero size keeps parity (skipped). Citation: `flatten.ts:66-76`. No UI path produces these values (`dragRect` normalizes, transforms floor at 4).

**EDGE-EDIT-16.** Overlapping blurs: the bake's downsample reads the current canvas, so a pixelate region over an earlier solid one samples black, while the editor preview samples the raw image. macOS samples the raw SOURCE in the bake (`macOS:.../Flatten.swift:147-155`), which re-exposes an average of original pixels over an area an earlier solid box blacked out. Required natively: sample the composited buffer (Electron parity; strictly more destructive). The preview keeps sampling the raw image (parity).

**EDGE-EDIT-17 [SECURITY].** An annotation of an unknown type (a future `redact2`, a hand edit) is invisible to Electron's gate (`hasBlur` checks only `type === 'blur'`), so the raw screenshot is cleared for egress. macOS #109 (`5b6684e`) made `.unknown` count. A `null` element makes Electron's `a.type` throw a `TypeError` inside the gate, which refuses the egress by accident with that raw message instead of the refusal text. Required natively: INV-EDIT-31 (a `null` element counts as a possible redaction and gets the normal refusal message). Citation: `render-gate.ts:33`; `macOS:Packages/ShotModel/Sources/ShotModel/RenderGate.swift:45-63`.

**EDGE-EDIT-18 [SECURITY].** A malformed blur (`{"type":"blur"}` without geometry): Electron's gate counts it (type check only) and its flatten skips it (geometry 0), so after any save the render passes the gate unredacted. macOS decodes a malformed blur tolerantly as a blur where it can, else as `.unknown`, and its flatten ignores `.unknown`. Required natively: the gate counts it (type is `blur`) and the flatten fails closed on it (EDGE-EDIT-15). Citation: `render-gate.ts:33`, `flatten.ts:66-76`; `macOS:.../Annotations.swift:93-110`, `macOS:Packages/ShotModel/Tests/ShotModelTests/RenderFreshnessTests.swift:216-227`.

**EDGE-EDIT-19 [SECURITY].** Electron's gate reads with lexical `confinePath`, and `fs.readFile` follows links: a synced or shared project can plant `shots/x.png` (or `export/.render/<id>.png`) as a link to any file, set `flattened` and `markerBaked` so nothing re-flattens, and have that file base64'd into the Claude request or embedded in exports. macOS fixed this in `26bb2b2`. Required natively: INV-EDIT-30. Citation: `render-gate.ts:43`.

**EDGE-EDIT-20 [SECURITY].** The render is written with a plain `writeFile` before the manifest: a crash mid-write truncates a render the manifest still trusts (spec 01 EDGE-MODEL-47), and a manifest write failure after the render write leaves the new render in place under the old manifest (for example a render without a blur that the old manifest still lists, after the user saw the save fail and cancelled). Required natively: INV-EDIT-27. Citation: `step-render.ts:56-57`, `project-store.ts:776-785`.

**EDGE-EDIT-21 [SECURITY].** `mergeSteps` lacked the invalidation branch (S3); every UI merge sends a PNG, so the branch was reachable only by direct call, and had to be tested by direct call. Required: one shared applier (INV-EDIT-8) and a direct test. Citation: `step-render.ts:14-16`, `HARDENING-PLAN.md:45-47`.

**EDGE-EDIT-22.** The merge patch carries `caption` (`joinText(drop.caption, keep.caption, ' \u2192 ')`), so merging sets `captionEditedByUser = true` on the kept step, although `207eddd` states that only the report's inline editor sends a caption. Required: parity until 07 decides (Q-EDIT-10). Citation: `merge.ts:98-103`, `step-render.ts:23-30`.

**EDGE-EDIT-23.** The editor seeds `markerColor` with `step.markerColor ?? ACCENT`, not `markerColorFor(step)`: an unset right-click step shows a blue ring in the report, a rose ring in the editor, and after any editor save `markerColor = '#e11d48'` is persisted, so the report ring turns rose. The hardening plan deliberately left this (`HARDENING-PLAN.md:59`, T3a in P4: "Do NOT touch `Editor.tsx`"); macOS uses `markerColor(for:)` in the editor (`macOS:shotAI/Editor/EditorModel.swift:95`). Required: Q-EDIT-1 (recommended IMPROVEMENT: use `markerColorFor`). Citation: `Editor.tsx:121`.

**EDGE-EDIT-24.** An editor save always writes `markerColor`, even for a step with no click. Required: parity. Citation: `Editor.tsx:603`.

**EDGE-EDIT-25.** An editor save writes `markerBaked: true` even when there is no marker (macOS writes `marker != nil`, `macOS:shotAI/Editor/EditorModel.swift:597`). Required: Electron parity (true): the flag means "this render came from the marker-aware flatten", which is true. Citation: `Editor.tsx:603`.

**EDGE-EDIT-26.** `ensureFlattened` passes no `radius` for the step's click marker, so a re-bake after an invalidation reverts a custom ring size to the derived one. The merge does the same for the KEPT step's click (`merge.ts:88-90`), so merging a step whose ring the user resized bakes the derived size while `click.radius` stays in the manifest (the editor then shows the custom size again). Required natively: pass `click.radius` in both (IMPROVEMENT D-EDIT-18; matches what the editor last baked). Citation: `sop-prepare.ts:49-55`, `merge.ts:88-90`.

**EDGE-EDIT-27.** `flattened: ""`: Electron's gate treats it as no render, then `relToRead = ""` (`"" ?? x` is `""`) fails with `has no readable screenshot`, even for a plain step. macOS treats empty as absent and reads the screenshot. Required natively: empty or non-string `flattened` is absent (IMPROVEMENT D-EDIT-6). Citation: `render-gate.ts:34-44`; `macOS:Packages/ShotModel/Sources/ShotModel/RenderGate.swift:64`.

**EDGE-EDIT-28.** A truthy but unparseable raw `crop` (for example `{}` or `{"x":"a"}`): the gate refuses (truthiness), and the Electron flatten computes NaN sizes, a 0 by 0 canvas and fails with `canvas.toBlob returned null`. Required natively: the gate uses raw JS truthiness (parity); the editor opens such a step with no crop (the codec view is null) and saving writes `crop: null`; `ensureFlattened` and any flatten given an unparseable crop fail closed with `E_CROP_UNAPPLIED` (IMPROVEMENT D-EDIT-7, 7.4). Citation: `render-gate.ts:36`, `flatten.ts:50-56`.

**EDGE-EDIT-29.** Dragging the crop box past the right or bottom edge shrinks it (`clampRectToImage` keeps the origin and cuts the width). Required: parity. Citation: `Editor.tsx:1169-1177`, `editor-geometry.ts:13-14`.

**EDGE-EDIT-30.** A crop fully outside the image clamps to a 1 by 1 rect at the far edge, and the flatten of any crop yields at least 1 by 1. Required. Citation: `editor-geometry.ts:13-14`, `flatten.ts:52-53`, `editor-geometry.test.ts:14-16`.

**EDGE-EDIT-31.** Stamp numbers are `count(stamp) + 1`, so deleting stamp 1 of two and adding one yields a second "2"; numbers are free labels, never renumbered. Required (macOS matches, `macOS:shotAI/Editor/EditorModel.swift:159-171`). Citation: `Editor.tsx:272`.

**EDGE-EDIT-32.** A perfectly vertical arrow has a zero-width transformer box and a perfectly horizontal one only the head's height, so the 8 px minimum rejects every resize of a vertical arrow. Natively: arrows get endpoint handles (IMPROVEMENT D-EDIT-15). Citation: `Editor.tsx:1202-1204`, Konva `Arrow.js:83-92`.

**EDGE-EDIT-33.** In the full view with a crop set, the filled crop box sits above the annotations, so clicks inside the crop select and drag the crop instead of the annotations under it; users switch to `Apply crop` to edit inside. Natively: hit-test annotations first and the crop interior last (IMPROVEMENT D-EDIT-14). Citation: `Editor.tsx:1151-1180`.

**EDGE-EDIT-34.** Dragging an unselected shape moves it without selecting it (Konva fires no click after a drag). Natively: press selects, then the drag moves (IMPROVEMENT D-EDIT-13; macOS does the same, `macOS:shotAI/Editor/EditorOverlay.swift` `beginDrag` select branch). Citation: `Editor.tsx:976-985`.

**EDGE-EDIT-35.** An unfilled Box is hit only on its outline, whose hit width is `strokeWidth` image px (4 at default, a few screen px at fit). Required natively: outline-only hit (parity), with a minimum hit band of 8 DIP (IMPROVEMENT D-EDIT-20). Citation: Konva hit rules, `Editor.tsx:986-1001`.

**EDGE-EDIT-36.** Right and middle buttons start drafts and place stamps, markers and text like the left button. Natively: only the left button draws or places; right and middle do nothing (IMPROVEMENT D-EDIT-13, Windows convention). Citation: `Editor.tsx:262-302`.

**EDGE-EDIT-37.** The Select tooltip advertises `(V)` but no handler exists. Natively: `V` selects the Select tool when focus is not in a text box (IMPROVEMENT D-EDIT-17; the string stays). Citation: `annotations.ts:24`, `Editor.tsx:436-458`.

**EDGE-EDIT-38.** An unknown annotation type falls through the render switch to the text branch and becomes a draggable Konva `Text` built from whatever `x`, `y`, `text`, `fontSize` and `fill` keys it happens to have: usually nothing visible and a zero-width (practically unhittable) node, but an element that carries a string `text` draws it and can be dragged, which writes `x`/`y` into the unknown element. Natively: unknown annotations are not drawn, not hit-testable and preserved verbatim (IMPROVEMENT D-EDIT-10). Citation: `Editor.tsx:1080-1093`.

**EDGE-EDIT-39.** Save is allowed while a scan runs; if the editor closes first the late OCR result is dropped. Natively: parity, and the scan is cancelled when the editor closes (IMPROVEMENT D-EDIT-12). Citation: `Editor.tsx:733`, `:746`.

**EDGE-EDIT-40.** During a save only Save, Cancel and Auto-redact are disabled; edits made while the flatten runs are silently lost when the editor closes on success. Natively: the canvas and controls are read-only while saving (IMPROVEMENT D-EDIT-12). Citation: `Editor.tsx:565-613`.

**EDGE-EDIT-41.** A negative `radius` on a stamp or marker (hand edit) makes `ctx.arc` throw `IndexSizeError`, so the save fails with a DOM exception message. A NaN radius draws nothing. Natively: a non-finite or non-positive radius draws nothing and never throws (IMPROVEMENT D-EDIT-10). Citation: `flatten.ts:186-187`, `:270`.

**EDGE-EDIT-42.** Invalid color strings: the canvas ignores the assignment and keeps the previous style (possibly another annotation's color, or the default black); `<input type="color">` shows `#000000` for anything that is not `#rrggbb`. The canvas also accepts every CSS color syntax (named colors, `rgb()`, `hsl()`), which no UI path writes but a hand edit or a foreign manifest can. Natively: colors parse deterministically (`#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, case-insensitive); anything else, CSS named and functional colors included, draws in `ACCENT` (IMPROVEMENT D-EDIT-9, macOS does the same, `macOS:Packages/EditorKit/Sources/EditorKit/Flatten.swift:342`); the picker shows the parsed color. Citation: `flatten.ts:211-287`, `Editor.tsx:760-767`.

**EDGE-EDIT-43.** The preview ring fill appends `2e` to the color string, which is invalid for `#rgb`, `#rgba` and `#rrggbbaa` colors (the `markerColor` regex admits 3 to 8 digits), so the canvas ignores that `fillStyle` and fills with whatever style is current (UNVERIFIED which one; Konva's per-shape save and restore suggests the context default, opaque black), while the bake (alpha 0.18) fills them translucently. Natively: one painter with alpha 0.18 (D-EDIT-8). Citation: `Editor.tsx:1074`, `:1105`, `flatten.ts:188-190`.

**EDGE-EDIT-44.** A screenshot that fails to load: error notice, the canvas shows `Loading screenshot\u2026` forever, Save and Auto-redact stay disabled, Cancel works. Required. Citation: `Editor.tsx:165-167`, `:941-942`.

**EDGE-EDIT-45.** Tesseract init failure clears the memo so the next scan retries; any scan failure returns `[]`, which the editor reports with the same "No sensitive data detected" notice as a clean scan. Natively: a failed or unavailable engine shows its own notice (IMPROVEMENT D-EDIT-11, Q-EDIT-5). Citation: `ocr.ts:55-58`, `:85-88`.

**EDGE-EDIT-46.** The scan reads the original file on disk: it ignores the crop (detections outside the crop are added, kept as annotations, and skipped by the bake) and cannot see unsaved edits. Required: detections outside the crop are still added (parity). Citation: `ipc.ts:535-541`.

**EDGE-EDIT-47.** Detected rects are padded by 4 and not clamped, so a match at the image edge yields a rect with negative `x` or `y`; the bake clamps it. Required. Citation: `redact-detect.ts:112-117`.

**EDGE-EDIT-48.** `mergeRects` is order dependent and single pass: a union that grows into an entry already skipped in the backwards scan stays separate, so the output can contain overlapping rects. Required: port exactly (the output feeds the annotation order and the selection of the last one). Citation: `redact-detect.ts:70-82`.

**EDGE-EDIT-49.** In the base64 pattern `={0,2}\b`, a boundary after `=` needs a following word character, so padding is usually excluded from the match (the covered-word mapping still covers the whole word). Required: ECMAScript semantics (INV-EDIT-20). Citation: `redact-detect.ts:30`.

**EDGE-EDIT-50.** Native only: `Windows.Media.Ocr` refuses images larger than `OcrEngine.MaxImageDimension` on either side (a multi-monitor capture can exceed it). Required: downscale uniformly to fit, OCR, and divide every word box by the scale before detection. Citation: 7.9.

**EDGE-EDIT-51.** The text editor is single-line; pasted newlines are removed by the input, and the canvas replaces ASCII whitespace with spaces when drawing. Required natively: `TextBox.AcceptsReturn = false`, and the painter replaces U+0009, U+000A, U+000C, U+000D with U+0020 before drawing. Citation: `Editor.tsx:903-906`, `flatten.ts:285`.

**EDGE-EDIT-52.** The inline text input is positioned at render time, so scrolling the canvas during an edit leaves it behind. Natively: the text box lives in the canvas coordinate space and moves with it (IMPROVEMENT, part of D-EDIT-8). Citation: `Editor.tsx:898-901`.

**EDGE-EDIT-53.** `parseClick` returns null when `global` or `image` is invalid, so an editor save of such a step deletes its click; an invalid `radius` or `imageScale` is dropped; any extra click key is dropped. Required. Citation: `ipc.ts:147-174`.

**EDGE-EDIT-54.** `parseStepPatch` keeps only annotation elements that are objects with a string `type` and a string `id`, so an editor save drops an annotation (even a blur) that lacks a string id. The flatten runs on the unfiltered list first, so such a blur IS baked into the render that is written, while the manifest no longer lists it: the render and the manifest disagree, and the next re-bake (after any invalidation, or the next editor save) loses the redaction silently. Required natively: parity for the filter, and because a dropped blur can reveal pixels, the editor surfaces such elements before saving: an element with `type: "blur"` and no string `id` gets a fresh id when the editor opens (IMPROVEMENT D-EDIT-2, keeps the redaction instead of silently dropping it); the filter runs before the bake (INV-EDIT-32, D-EDIT-25). Citation: `ipc.ts:219-225`, `Editor.tsx:586-605`.

**EDGE-EDIT-55.** Preview and bake fonts differ: Konva text and the inline input use Arial with Konva's line layout, the bake uses `"Segoe UI"` with a top baseline, so baked text shifts and changes width after saving. Natively: one font and one layout for preview, text box and bake (D-EDIT-8). Citation: `editor.css:258-259`, Konva `Text.js:484`, `flatten.ts:274`, `:282-285`.

**EDGE-EDIT-56.** The blur preview computes `block = max(8, round(blockSize))` without the bake's `|| 12`, so `blockSize: 0` (and `null`, since `round(null)` is 0) previews at 8 and bakes at 12, and an absent or non-numeric `blockSize` previews as a transparent image (EDGE-EDIT-63). Natively: preview and bake share `RedactionBaker.EffectiveBlock` (D-EDIT-8). Citation: `BlurRegion.tsx:36`, `flatten.ts:149`.

**EDGE-EDIT-57.** Imported JPEGs may carry an EXIF orientation. Chromium applies it to the `<img>` and to `drawImage`, so annotation coordinates are in the ORIENTED space. Required natively: decode with EXIF orientation respected, everywhere this subsystem decodes (editor, flatten, OCR). Citation: `Editor.tsx:155-168` (image element), `flatten.ts:48-62`.

**EDGE-EDIT-58.** A moved blur is not clamped (it can be dragged partly off-image; the part outside hides nothing, which the preview shows). macOS clamps a moved blur's origin to keep it fully on-image (`macOS:shotAI/Editor/EditorModel.swift:524-533`). Required natively: Electron parity (no clamp). Citation: `Editor.tsx:971`.

**EDGE-EDIT-59.** The transformer's 8 px minimum is in screen px, so at high zoom a shape can be resized much smaller in image px than at fit; the committed floor is then 4 (rect, blur), 8 (crop) or 6 (radius) image px. Required. Citation: `Editor.tsx:1202-1204`, `:390-422`.

**EDGE-EDIT-60 [SECURITY].** Chromium's canvas downscale with smoothing is bilinear without mipmaps: each tile pixel is derived from about four source pixels near the cell center, so most of the cell is discarded rather than averaged, and a thin high-contrast glyph stroke can survive into the tile at full contrast if it crosses a sample point. The result is still destructive, but the comment's "average-downsample" is not what runs. Natively: a true box average over each cell (IMPROVEMENT D-EDIT-1), which makes INV-EDIT-2 provable. Citation: `flatten.ts:141-162`.

**EDGE-EDIT-61.** The bake's integer region does not depend on the crop, because the crop origin is integral (`round(a.x - cx) = round(a.x) - cx`), so in image px it is always `[round(a.x), round(a.x) + round(a.width))`. The Electron preview does NOT use that region: it samples the fractional source rect `(a.x, a.y, a.width, a.height)` and draws the result at the fractional position, so preview and bake edges can differ by up to half a pixel. Natively: `RedactionBaker.Region` is computed once in image px and intersected with the crop, and the preview uses the same `Region` (with no crop), so the preview shows exactly the pixels the bake covers (IMPROVEMENT, part of D-EDIT-8). Citation: `flatten.ts:125-133`, `BlurRegion.tsx:34-46`.

**EDGE-EDIT-62 [SECURITY].** The render path is `path.posix.join('export', '.render', id + '.png')`, not a concatenation, so dot segments inside a hand-edited step id are normalized BEFORE confinement: id `../../shots/abc` gives `shots/abc.png`, which is inside the project, passes `confinePathNoSymlinks`, and OVERWRITES the original screenshot `shots/abc.png` (of this or any other step) with a render (the original that every later flatten starts from, INV-EDIT-5); id `a/b` creates `export/.render/a/`. Only an id that escapes the folder is refused. Spec 01 left the decision to this spec (EDGE-MODEL-51, Q-MODEL-22). Required natively: IMPROVEMENT [SECURITY] D-EDIT-22. Citation: `step-render.ts:51-55`.

**EDGE-EDIT-63.** Blur preview anomalies (`BlurRegion.tsx:32-64`): a `mode` other than `'pixelate'` or `'solid'` previews as the translucent `rgba(15,23,42,0.55)` rect, which shows the pixels underneath at 45% while the bake pixelates them; an absent or non-numeric `blockSize` (NaN `block`) yields a transparent preview through which the original pixels are fully visible, while the bake uses 12 (absent) or does nothing at all (non-numeric, EDGE-EDIT-14). No UI path produces either value. Natively: the preview uses `RedactionBaker` exactly as the bake does, so any `mode` other than `'solid'` previews and bakes as pixelate (D-EDIT-8).

**EDGE-EDIT-64.** `Reset crop`, `Apply crop` and `Show full` do not touch the selection. After `Reset crop` with the crop box selected, `selectedId` stays `'__crop__'`, so the properties bar keeps showing `Remove crop` for a crop that no longer exists (clicking it only deselects). After `Apply crop` with the crop box selected, the transformer stays attached to the (now inert, non-listening) crop box, so its handles remain usable in the cropped view. Natively: `ResetCrop` and `ToggleCropView` clear a `'__crop__'` selection (IMPROVEMENT D-EDIT-23). Citation: `Editor.tsx:704-728`, `:232-242`, `:1151-1180`.

**EDGE-EDIT-65.** The window key handler ignores events whose target is any `INPUT` (`Editor.tsx:439`), so a focused range slider or color input swallows Delete, Backspace and Escape (the slider keeps focus after the user drags it). Natively: only the inline `TextBox` blocks the editor keys; a focused `Slider`, swatch button or tool button does not (IMPROVEMENT D-EDIT-24; WPF sliders use the arrow keys, never Delete).

**EDGE-EDIT-66.** Escape during a drag draft switches to the Select tool, so the next window mouse-up runs `finishDrag` with `tool === 'select'` and creates nothing. For rect, blur and crop drafts the draft is cleared; for an ARROW draft `finishDrag` takes the non-arrow branch and never calls `setArrowDraft(null)`, so a ghost arrow preview (not hit-testable, never saved) stays on the canvas until the next arrow drag starts. Natively: Escape cancels any draft and clears both previews (the ghost arrow is ELECTRON-ONLY). Citation: `Editor.tsx:321-354`, `:451-454`, `:1139-1149`.

**EDGE-EDIT-67.** Junk in `step.annotations` (the codec keeps the array verbatim, `project-store.ts:88-91`): a `null` element crashes the editor's render (`a.type` in `annotations.map`) and, with no React error boundary in the app, the project window's React tree unmounts; a `text` element whose `text` is not a string throws in `onSave` before the `try`, leaving Save and Cancel disabled forever (2.15); non-object junk such as `42` is kept, drawn as nothing, skipped by the bake and dropped by `parseStepPatch` on save. Natively: views never throw; junk elements are neither drawn nor hit-testable; `PrepareSave` drops them through the patch filter (INV-EDIT-32, parity with the Electron drop); a `text` element with a non-string `text` is kept verbatim and draws nothing (D-EDIT-10). Citation: `Editor.tsx:217`, `:572`, `:959-1094`.

**EDGE-EDIT-68.** A step whose `click` exists but has no valid `image` point: the Electron editor seeds `clickImage = {...undefined}` (an empty object), draws a ring at undefined coordinates, and the save's `parseClick` returns null, so the save REMOVES the click (EDGE-EDIT-53); `ensureFlattened` reads `step.click.image.x` and throws a `TypeError`, which aborts the egress with `Step N ("...") couldn't be prepared: Cannot read properties of undefined (reading 'x')`. Natively: `ProjectStep.Click` (spec 01) is a lenient parse that is null for such a click, so the editor shows no ring and saves `click: null` (parity with the Electron result), and `EnsureFlattenedAsync` bakes no ring instead of failing (IMPROVEMENT, part of D-EDIT-10). Citation: `Editor.tsx:109-111`, `sop-prepare.ts:49-55`, `ipc.ts:147-152`.

**EDGE-EDIT-69.** Canvas setters that ignore invalid values in the bake: a rect whose `cornerRadius` is missing or not a number is not drawn at all (`roundRect` with a NaN radius adds no subpath; Konva previews it with radius 0); `lineWidth` ignores 0, negatives, NaN and infinities, so such a `strokeWidth` strokes with the previous shape's width (Konva draws no stroke for 0); `font` ignores an unparseable string (a NaN or negative `fontSize`, or a non-number `radius` for a stamp label), so the previous shape's font is used. Natively: a non-finite `cornerRadius` draws with radius 0 (preview parity); a `strokeWidth` that is not a finite number > 0 draws no stroke; a text or stamp label whose font size is not a finite number > 0 draws no text (D-EDIT-10). Citation: `flatten.ts:198-209`, `:211-293`.

---

## 6. macOS port notes

| Topic | macOS implementation | Divergence from Electron and lesson for Windows |
|---|---|---|
| Flatten placement | Pure `Flatten.toPNG(image:annotations:crop:marker:)` in the `EditorKit` package, CoreGraphics only, unit-tested headless (`macOS:Packages/EditorKit/Sources/EditorKit/Flatten.swift:54-117`, tests `FlattenTests.swift:47-199`). | Proves the feasibility doc's claim that a native editor can keep the fail-closed bake. Lesson: keep the pixel work out of the UI layer; port these tests first. Windows goes further: the redaction bake is pure byte math in ShotAI.Core (Linux-testable), and only vector rasterization needs WPF. |
| Orientation | Three commits (`4d1e214`, `9156ae0`, `df52e2f`) fixed images and the mosaic rendering upside down in a flipped CGContext; `mosaicOrientationMatchesPlainPath` and `plainFlattenDoesNotFlip` pin it (`FlattenTests.swift:93-139`). | Lesson: pin orientation with tests that compare two paths, not a harness convention. Windows buffers are top-down BGRA throughout, so the risk is lower, but the tests port as-is. |
| Mosaic | Crops the SOURCE image region, downsamples with `interpolationQuality = .high`, draws back clipped, NO Gaussian second pass (`Flatten.swift:147-187`). | Two divergences: (1) no soft pass, so macOS blur looks blockier ("blur softness" is a known minor gap in `macOS:PARITY.md:142`); (2) sampling the source, not the composite, re-exposes averaged originals over an earlier solid box (EDGE-EDIT-16). Windows: composite sampling and the soft pass. |
| Untrusted numbers | `satInt` saturates Double to Int (NaN to 0, beyond 2^40 clamps) because `Int(Double)` traps on `1e300` (`Flatten.swift:349-361`, `65091a7`); test `extremeGeometryDoesNotTrap`. | .NET does not trap on double-to-int conversion (it saturates since .NET 9) but the lesson stands: do the clamp in doubles first, convert last (INV-EDIT-16), and test 1e300 and infinities. |
| Blur decode | Tolerant: unknown or missing `mode` is pixelate, missing or non-number `blockSize` is 12; a blur whose geometry fails to decode becomes `.unknown` (`Annotations.swift:93-110`, `188-210`). | Lesson: a decoder that demotes a malformed redaction to "unknown" silently changes its security meaning. Windows keeps annotations as raw JSON (spec 01) and interprets them at use; the gate counts by `type`, and the flatten fails closed on a malformed blur (D-EDIT-2). |
| Gate | `mayRedact` counts `.blur` and `.unknown` (#109, `5b6684e`); empty `flattened` is absent; reads through `confinePathNoSymlinks` (`26bb2b2`) (`macOS:Packages/ShotModel/Sources/ShotModel/RenderGate.swift:42-75`). Tests: `RenderFreshnessTests.swift:81-160`, `216-256`. | Adopted natively (D-EDIT-3, D-EDIT-4, D-EDIT-6). Windows is type-based rather than decode-based: a malformed ARROW is not blocked natively (macOS blocks it because it decodes as `.unknown`); both are safe since an arrow never redacts. |
| Render write | Atomic via `writeFileAtomic`, link-refusing confinement (`macOS:Packages/ShotModel/Sources/ShotModel/ProjectStore.swift:106-118`). | Adopted and extended with rollback on manifest failure (D-EDIT-5). |
| Freshness | `applyPatchAndInvalidate` ported with the same early return and `annotations`/`crop` rule (`StepPatch.swift:54-76`); tri-state `PatchField` for `crop`, `click`, `callout`. | Windows uses the same tri-state (`Optional<T>`, 7.5). |
| Editor model | `@Observable EditorModel` holds tools, selection, text editing, click marker and save; the SwiftUI `EditorOverlay` is a view with gesture code (`EditorModel.swift:16-617`, `EditorOverlay.swift:1-736`). | Lesson: state machines in a model object. Windows puts the editor document and its state machines in ShotAI.Core (`EditorDocument`, 7.3) so they run on Linux; the WPF view only maps input and draws. |
| Editor differences | Select on press then move; hit-test by bounds (a Box's interior is hit); one bottom-right resize handle with a 14 pt grab tolerance after users could not grab the corner (`9b0b179`); crop corner handles with a 10 pt tolerance; blur clamped to the image on create, move and resize; minimum draw size 5 (not 6); stroke slider 1 to 40 and the image default capped at 40; zoom is absolute (`.fit` or `.absolute(z)`, 0.1 to 8) with 100% meaning 1 image px to 1 device px; the editor uses `markerColor(for:)`; `markerBaked = marker != nil`; Cancel and Save in the window toolbar; errors in an alert titled `Couldn't save this step`. | Windows keeps Electron's behavior (relative zoom, 8 handles, Electron minimums) except where section 7.14 lists an improvement. The resize grab lesson applies: native handles are 10 DIP visuals with a 16 DIP hit area. |
| Live preview | A real mosaic tile preview cached per region and block (`MosaicCache`, bounded to 80 entries during drags; `e4e3746`). | Adopt the bounded cache idea for the WPF preview (7.10.4). |
| Text | Helvetica for bake, preview and the inline field so the field sits exactly where the text bakes; the model owns `editingText` so Save can flush an open edit (`EditorModel.swift:197-217`, `565-575`). | Same lesson on Windows: one font everywhere (Segoe UI) and the document owns the edit buffer. |
| OCR | `VisionOCR` with `recognitionLevel = .accurate` and `usesLanguageCorrection = false` (raw tokens must not be "corrected" into words), word boxes from `boundingBox(for:)` on whitespace-split ranges, normalized bottom-left boxes converted to top-left image px (`macOS:Packages/EditorKit/Sources/EditorKit/VisionOCR.swift:22-75`). The Auto-redact button is deliberately NOT surfaced (dormant code, `macOS:shotAI/Editor/EditorModel.swift:544-561`, `macOS:PARITY.md:142`, `:159-160`). | Windows keeps the button (Electron parity, Q-EDIT-13). Windows OCR has no language-correction switch; it returns words directly. The coordinate conversion lesson applies: Windows boxes are already top-left image px of the bitmap passed in, so only the downscale factor needs undoing (EDGE-EDIT-50). |
| Detectors | Ported 1:1 with NSRegularExpression (ICU) and UTF-16 spans (`macOS:Packages/EditorKit/Sources/EditorKit/RedactDetect.swift:34-134`); tests `macOS:Packages/EditorKit/Tests/EditorKitTests/RedactDetectTests.swift:15-57` (SSN, card across words, non-Luhn 16 digits, three API keys, ordinary text, email and phone excluded, merge of overlapping padded boxes). | ICU `\d` and `isNumber` are Unicode-aware, a silent divergence. Windows uses `RegexOptions.ECMAScript` (INV-EDIT-20) and ports the macOS tests too. |
| Undo | None. | Parity. |

---

## 7. Native design (C#)

### 7.1 Placement

| Namespace | Project | Types |
|---|---|---|
| `ShotAI.Core.Editor` | ShotAI.Core | `AnnotationKind`, `AnnotationView` and its six typed views plus `UnknownAnnotationView`, `AnnotationFactory`, `AnnotationEdits`, `AnnotationStyle` (constants and formulas), `EditorGeometry` (`ClampRectToImage`, `ComputeCropView`, `CropView`), `EditorDocument` (the editor state and its state machines), `EditorTool`, `EditorSelection`, `TransformMath`, `HitTesting`, `CssColor` |
| `ShotAI.Core.Rendering` | ShotAI.Core | `PremultipliedImage`, `Flattener`, `FlattenRequest`, `FlattenMarker`, `FlattenException`, `RedactionBaker`, `OverlayScene` and its primitives, `IOverlayRasterizer`, `IRenderCodec`, `Compositor`, `StepPatch`, `Optional<T>`, `StepPatchValidator`, `StepPatchApplier`, `IStepRenderWriter`, `StepRenderWriter`, `RenderWriteReceipt`, `RefusedRenderPathException`, `StepFlattener`, `IStepFlattener` |
| `ShotAI.Core.Redaction` | ShotAI.Core | `RenderGate` (static), `IRenderGate` and `RenderGateService` (the DI wrapper holding the `IPathProbe`), `SendableRender`, `EgressVerb`, `RenderGateException`, `SensitiveTextDetector`, `OcrTextLine`, `OcrTextWord`, `IOcrEngine`, `OcrScanResult`, `OcrScanStatus`, `ISensitiveRegionScanner`, `SensitiveRegionScanner` |
| `ShotAI.Core.Json` (spec 01's namespace; added by this spec) | ShotAI.Core | `JsValue.Truthy(JsonNode?)` (the notation's `JsTruthy`), `JsValue.ToNumber(JsonNode?)` (ECMAScript `ToNumber`, used by `EffectiveBlock`), `JsPath.ExtName(string)` (7.6) |
| `ShotAI.Platform.Capture` | ShotAI.Platform | `WicImageCodec` (spec 02's class, 02 7.1; it stays in 02's namespace) also implements `IRenderCodec` |
| `ShotAI.Platform.Ocr` | ShotAI.Platform | `WindowsOcrEngine : IOcrEngine` |
| `ShotAI.Platform.Dialogs` | ShotAI.Platform | `Win32ColorDialog` (ChooseColor) |
| `ShotAI.App.Editor` | ShotAI.App | `EditorOverlayView` (XAML), `EditorViewModel`, `EditorFactory` (the C6 factory of ARCHITECTURE 4.1), `EditorCanvas`, `AnnotationPainter`, `SelectionAdorner`, `InlineTextBox`, `BlurPreviewCache`, `WpfOverlayRasterizer`, `StaRenderThread`, `IColorPicker` and `Win32ColorPicker` (the App wrapper over the Platform `Win32ColorDialog`, 7.10.1), `EditorRegistration` (DI) |
| `ShotAI.App.Threading` | ShotAI.App | `UiDeferral`, the one App helper for focus and layout deferrals at a named `DispatcherPriority`, beside `WpfUiDispatcher` (R-ARCH-18; its file `src/ShotAI.App/Threading/UiDeferral.cs` is the allowlisted file of ARCHITECTURE 14.9; added by PLAN WP-C9) |

Model types (`ProjectStep`, `StepClick`, `Rect`, `Point`, `ProjectManifest`, `StepGeometry`) and `JsMath`, `JsString`, `JsNumber`, `PathConfine`, `IPathProbe`, `AtomicFile`, `IProjectService`, `IProjectSession`, `IProjectSessionFactory`, `IProjectSettle`, `StepNotFoundException` come from spec 01. Every consumer in this spec depends on `IProjectService`, never on the concrete `ProjectStore` (R-ARCH-4); the open project's session is an `IProjectSession` created by `IProjectSessionFactory.Create` on the UI thread, never `new ProjectSession` (R-ARCH-5). `IStepFlattener` lives in `ShotAI.Core.Rendering`, owned by this spec (R-ARCH-7). Every user-facing exception of this spec (`FlattenException`, `RenderGateException`, `RefusedRenderPathException`) derives from `ShotAI.Core.Errors.ShotAIException` (ARCHITECTURE 8.1), so `UserMessage.From` shows its `Message` verbatim. Core references no Windows API; everything Windows-specific is behind `IOverlayRasterizer`, `IRenderCodec` and `IOcrEngine`. Log categories follow 10 7.5.3: `ShotAI.Core.Redaction` and `ShotAI.Platform.Ocr` log as `ocr`; `WicImageCodec`'s `IRenderCodec` members log under its namespace `ShotAI.Platform.Capture` as `capture`; `ShotAI.Core.Editor`, `ShotAI.Core.Rendering` and `ShotAI.App.Editor` fall to `main` (no `editor` or `render` label exists).

### 7.2 Annotation model (Core)

Annotations stay the step's raw `JsonArray` of `JsonNode` (spec 01 `ProjectStep.Annotations`). The editor works on a deep clone. Typed views read with JS semantics and never throw:

```csharp
namespace ShotAI.Core.Editor;

public enum AnnotationKind { Rect, Arrow, Blur, Stamp, Text, Marker, Unknown }

public abstract class AnnotationView
{
    public JsonNode? Node { get; }                       // the element itself (object or junk)
    public JsonObject? Raw => Node as JsonObject;
    public string? Id { get; }                           // null unless a JSON string
    public abstract AnnotationKind Kind { get; }
    public static AnnotationView From(JsonNode? node);   // dispatch on Raw["type"] (Ordinal); else Unknown
    public bool MayRedact => Kind is AnnotationKind.Blur or AnnotationKind.Unknown;   // INV-EDIT-31
}

public sealed class BlurAnnotationView : AnnotationView
{
    public double? X, Y, Width, Height;                  // null unless a finite JSON number (strict)
    public double FiniteX => X ?? 0;                     // flatten.ts `finite`
    public bool IsSolid => Raw?["mode"] is JsonValue v && v.TryGetValue(out string? s) && s == "solid";
    public JsonNode? BlockSizeRaw;                       // interpreted by RedactionBaker.EffectiveBlock
    public bool HasUsableGeometry => X is not null && Y is not null && Width is >= 0 && Height is >= 0;
}
// RectAnnotationView (X, Y, Width, Height, CornerRadius, Stroke, StrokeWidth, Fill),
// ArrowAnnotationView (Points: double[4]? null unless an array of four finite numbers, Stroke, StrokeWidth),
// StampAnnotationView (X, Y, N raw, Radius, Fill, TextColor), TextAnnotationView (X, Y, Text, FontSize, Fill),
// MarkerAnnotationView (X, Y, Color, Radius?), UnknownAnnotationView.
```

`AnnotationFactory` creates `JsonObject`s in the exact key order of 2.1 with `Guid.NewGuid().ToString("D")` ids:

```csharp
public static class AnnotationFactory
{
    public static JsonObject Rect(double x, double y, double w, double h, double strokeWidth, string color);  // cornerRadius 10, fill null
    public static JsonObject Arrow(double x1, double y1, double x2, double y2, double strokeWidth, string color);
    public static JsonObject Blur(double x, double y, double w, double h, string mode = "pixelate", double blockSize = 14);
    public static JsonObject Stamp(double x, double y, double n, double radius, string color);           // textColor "#ffffff"
    public static JsonObject Text(double x, double y, string text, double fontSize, string color);
    public static JsonObject Marker(double x, double y, string color);                                   // no radius key
}
```

`AnnotationEdits.Set(JsonObject o, string key, JsonNode? value)` implements the spread rule: an existing key is replaced in place, a new key is appended (the same `JsonObject` indexer behavior spec 01 pins in AC-MODEL-10). Every editor mutation goes through it on a clone of the element, then replaces the element in the array at its index.

`AnnotationStyle`: the constants and formulas of section 3, computed with `JsMath.Round`, plus `MarkerColorFor(ProjectStep)`. `CssColor.TryParse(string?, out Rgba)` accepts `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa` (hex, case-insensitive, surrounding whitespace trimmed) and `CssColor.OrAccent(string?)` returns the parsed color or `ACCENT` (D-EDIT-9). `CssColor.ToInputHex(Rgba)` returns lowercase `#rrggbb` (the format the Electron color input writes). As built in WP-A17, ahead of the editor, for the report's ring (05 7.10): `AnnotationStyle.Accent`, `RightClickColor` and `MarkerColorFor`, and `CssColor.TryParse` with `Rgba` (and its `WithAlpha`); the whitespace trimmed is CSS's (space, tab, line feed, form feed, carriage return). The other constants and formulas, `OrAccent` and `ToInputHex` join with the editor document (WP-C8).

### 7.3 The editor document and its state machines (Core)

`EditorDocument` is the platform-neutral editor. The WPF canvas feeds it input already mapped to image px and renders its state. Every table in 2.6 to 2.14 is implemented here, so `EditorDocumentTests` run on Linux.

```csharp
namespace ShotAI.Core.Editor;

public enum EditorTool { Select, Rect, Arrow, Blur, Stamp, Marker, Text, Crop }
public enum PointerButton { Left, Middle, Right }
public readonly record struct Hit(string Id, HitPart Part);        // Part: Body, Handle(n), Endpoint(0/1)
[Flags] public enum EditorModifiers { None = 0, Shift = 1, Alt = 2, Control = 4 }   // Core has no WPF ModifierKeys

public sealed class EditorDocument
{
    public EditorDocument(ProjectStep step, int naturalWidth, int naturalHeight);   // seeds 2.3 (+ Q-EDIT-1 color rule)
    public int NatW { get; } public int NatH { get; }
    public IReadOnlyList<JsonNode?> Annotations { get; }
    public Rect? Crop { get; }                          // StepGeometry.ParseRect of the raw crop (EDGE-EDIT-28)
    public bool ViewCropped { get; } public double Zoom { get; }
    public Point? ClickImage { get; } public double? ClickRadius { get; } public string MarkerColor { get; }
    public EditorTool Tool { get; } public string? SelectedId { get; }   // includes "__click__", "__crop__"
    public double StrokeWidth { get; } public double BlockSize { get; } public string RedactMode { get; } public string Color { get; }
    public string? EditingTextId { get; }
    public Rect? Draft { get; } public (double, double, double, double)? ArrowDraft { get; }
    public bool IsSaving { get; } public bool IsScanning { get; }
    public event EventHandler? Changed;

    // Input (image px). Returns what the view must do (capture the mouse, focus the text box, ...).
    public PointerResult PointerDown(Point p, PointerButton button, Hit? hit, EditorModifiers mods);
    public void PointerMove(Point p, EditorModifiers mods);
    public void PointerUp(Point p);
    public void DoubleClick(Hit hit);
    public bool KeyDown(EditorKey key);                 // Delete, Backspace, Escape, V; false = not handled
    public void SelectTool(EditorTool tool);
    public void CommitTransform(string id, TransformResult r);           // TransformMath output (7.3.2)
    public void ChangeColor(string hex); public void ChangeStroke(double v); public void ChangeBlock(double v);
    public void ChangeMode(string mode); public void ChangeFontSize(double v); public void DeleteSelected();
    public void BeginTextEdit(string id); public void UpdateEditingText(string text); public void FinishTextEdit();
    public void ToggleCropView(); public void ResetCrop(); public void ZoomIn(); public void ZoomOut(); public void ZoomFit();
    public void AddAutoRedactions(IReadOnlyList<Rect> rects);            // 2.14
    public EditorSaveSnapshot PrepareSave();            // 2.15 steps 2-4 plus the patch element filter (INV-EDIT-32); sets IsSaving; never throws
    public void SaveFailed();                           // clears IsSaving (also called when the save is cancelled)
}

public sealed record EditorSaveSnapshot(JsonArray Annotations, Rect? Crop, StepClick? Click,
                                        string MarkerColor, FlattenMarker? Marker);
```

`PrepareSave` in order: close an open text edit; drop every `text` element whose `text` is a JSON string with `JsString.Trim(text) == ""` (a non-string `text` is kept, EDGE-EDIT-67); drop every element that is not a JSON object with a string `type` and a string `id` (the `parseStepPatch` filter, applied here so the bake and the manifest see the same list, D-EDIT-25); build the click as 2.15 step 4 from `step.Click` (spec 01's lenient parse, null for an unusable click, EDGE-EDIT-68) and `ClickImage`/`ClickRadius`; build `FlattenMarker(click.Image.X, click.Image.Y, MarkerColor, ClickRadius)` when the click is kept. The same pruned list is written back into the document (parity with `setAnnotations(anns)`).

#### 7.3.1 Hit testing (`HitTesting`)

Order, top first: the transform handles of the current selection (select tool only), the crop box border and handles (select tool, full view only), the click marker, annotations in reverse array order, then the crop interior (select tool, full view only; D-EDIT-14), else empty. Per type (image px, `k = 1 / stageScale` converts DIP to image px):

| Type | Hit when |
|---|---|
| rect with `fill` truthy | inside the rounded rect expanded by `strokeWidth / 2` |
| rect without fill | distance to the rounded-rect outline <= `max(strokeWidth, 8k) / 2` (D-EDIT-20) |
| arrow | distance to the shaft segment <= `max(strokeWidth, 8k) / 2`, or inside the head triangle |
| stamp | distance to center <= `radius` |
| marker, click marker | distance to center <= `radius + ringWidth / 2` |
| text | inside the measured text box |
| blur | inside `(x, y, width, height)` normalized |
| unknown | never |

#### 7.3.2 Transform math (`TransformMath`)

Handles: rect, blur and crop get eight handles (corners and edge midpoints); stamp, marker and click marker get four corner handles plus four edge handles with keep-ratio (circles stay round, edges scale one axis, then `max(sx, sy)` applies); arrows get two endpoint handles (D-EDIT-15). Text has none. While dragging handle `h` with the opposite anchor `F` fixed (or the center when Alt is held):

```
free:         the moved edges follow the pointer on the handle's axes
proportional: (circles on corners, or Shift on a rect/blur/crop corner)
              s = |pointer - F| / |oldCorner - F|;  box = oldBox scaled by s about F
reject:       a proposed box narrower or shorter than 8 DIP keeps the previous box
no flip:      a box never inverts (the reject rule stops it first)
```

On release, `sx = newBox.Width / selfRect.Width`, `sy = newBox.Height / selfRect.Height`, `origin' = (newBox.X - selfRect.X * sx, newBox.Y - selfRect.Y * sy)`, and the committed geometry is exactly the 2.7 table (with `JsMath.Round` for radii). Arrow endpoint drag: the dragged endpoint follows the pointer; commit when released if the arrow length stays >= `MIN_DRAG`, else revert.

#### 7.3.3 Pointer gestures

The 2.6 table, with these native rules: only `PointerButton.Left` starts a draft, places a shape or selects (D-EDIT-13; other buttons do nothing); in the select tool a press on an annotation selects it immediately and a move beyond 3 DIP starts a drag of the (now selected) shape (D-EDIT-13); a press on empty clears the selection. Drag previews use the current `Color` (D-EDIT-16). Drag commits happen on release (`PointerUp`), live positions are view-only until then.

#### 7.3.4 Keyboard

`KeyDown` handles `Delete`, `Backspace` (2.12), `Escape` (deselect, Select tool, and cancel any draft, clearing both `Draft` and `ArrowDraft`, EDGE-EDIT-66) and `V` (Select tool; D-EDIT-17). The view calls it only when keyboard focus is not in a `TextBox` (the INPUT/TEXTAREA guard narrowed to text entry: a focused `Slider` or swatch no longer swallows the keys, D-EDIT-24, EDGE-EDIT-65). `ResetCrop` and `ToggleCropView` clear a `"__crop__"` selection (D-EDIT-23, EDGE-EDIT-64). Returns true when handled so the view marks the event handled.

### 7.4 Flatten (Core, pure pixels plus one rasterizer seam)

```csharp
namespace ShotAI.Core.Rendering;

public sealed class PremultipliedImage            // top-down BGRA, premultiplied alpha, stride = Width * 4
{
    public int Width { get; } public int Height { get; } public byte[] Pixels { get; }
    public PremultipliedImage(int width, int height, byte[]? pixels = null);   // throws if Width*Height*4 > Array.MaxLength
    public PremultipliedImage CropCopy(int x, int y, int w, int h);
}

public interface IRenderCodec
{
    PremultipliedImage Decode(ReadOnlySpan<byte> encoded);   // PNG or JPEG only, chosen by magic bytes and decoded by the explicit built-in decoder, never by sniffing (R-ARCH-21, 7.13);
                                                             // anything else throws (fail closed); EXIF orientation respected; sRGB; premultiplied BGRA; synchronous (WIC COM), call off the UI thread
    byte[] EncodePng(PremultipliedImage image);              // 8-bit RGBA PNG, straight alpha on disk (Core un-premultiplies first, 7.13)
}

public abstract record OverlayItem;                          // image px of the OUTPUT canvas (already translated by -cx, -cy)
public sealed record OverlayRoundRect(double X, double Y, double W, double H, double Radius, Rgba? Fill, Rgba Stroke, double StrokeWidth) : OverlayItem;
public sealed record OverlayArrow(double X1, double Y1, double X2, double Y2, Rgba Color, double StrokeWidth) : OverlayItem;
public sealed record OverlayStamp(double X, double Y, double Radius, string Label, Rgba Fill, Rgba TextColor) : OverlayItem;
public sealed record OverlayText(double X, double Y, string Text, double FontSize, Rgba Fill) : OverlayItem;
public sealed record OverlayRing(double X, double Y, double Radius, Rgba Color) : OverlayItem;
public sealed record OverlayScene(int Width, int Height, IReadOnlyList<OverlayItem> Items);

public interface IOverlayRasterizer
{
    /// Transparent premultiplied BGRA of scene.Width x scene.Height with the items painted in order.
    Task<PremultipliedImage> RasterizeAsync(OverlayScene scene, CancellationToken ct);
}

public sealed record FlattenMarker(double X, double Y, string Color, double? Radius);
public sealed record FlattenRequest(PremultipliedImage Source, IReadOnlyList<JsonNode?> Annotations,
                                    JsonNode? RawCrop, FlattenMarker? Marker);

public sealed class FlattenException : ShotAIException     // ShotAI.Core.Errors (ARCHITECTURE 8.1)
{
    public FlattenFailure Failure { get; }      // UnbakeableRedaction, CropUnapplied, UnknownAnnotation, EncodeFailed
}

public sealed class Flattener(IOverlayRasterizer rasterizer, IRenderCodec codec)
{
    public Task<byte[]> FlattenToPngAsync(FlattenRequest request, CancellationToken ct);
}
```

`FlattenToPngAsync` (the 2.16 algorithm, numbered to match):

0. **Pre-checks (native, fail closed).** `crop`: absent or JS-falsy: no crop; `StepGeometry.ParseRect` (spec 01 `ShotAI.Core.Model.StepGeometry`) succeeds: that rect; truthy and unparseable: throw `CropUnapplied` (D-EDIT-7). For each element with `type == "blur"`: if `!HasUsableGeometry`, throw `UnbakeableRedaction` (D-EDIT-2). For each element that is not an object with a string `type` among the six known types (junk such as `null` or `42` included): throw `UnknownAnnotation` (Q-EDIT-2 recommended default). The editor save never reaches this with junk, because `PrepareSave` already dropped every element without a string `type` and a string `id` (INV-EDIT-32); `EnsureFlattenedAsync` can, and then fails closed with the step named (Electron: a `null` element throws a `TypeError` there, a `42` is silently skipped).
1. Crop integers exactly as 2.16 step 1, in doubles with `JsMath.Round`, converted to `int` after the clamp.
2. `canvas = Source.CropCopy(cx, cy, cw, ch)`.
3. For each blur in array order: the overlap test and `RedactionBaker.Region` (2.16), in doubles. `ow <= 0 || oh <= 0`: skip. Empty integer region: throw `UnbakeableRedaction`. `IsSolid`: `RedactionBaker.FillSolid`. Else `RedactionBaker.Pixelate(canvas, region, EffectiveBlock(blockSizeRaw))`. Any exception thrown inside `Pixelate` (allocation included): `FillSolid` over the region and continue (parity with the missing-context fail-closed branch).
4. Build the `OverlayScene` of `cw` by `ch`: for each element in array order, rect, arrow, stamp and text become items translated by `(-cx, -cy)`. A malformed non-redacting element (non-finite geometry, arrow points not four finite numbers, non-positive or non-finite radius or font size, a `text` whose `text` is not a string) contributes no item (D-EDIT-10); a rect whose `cornerRadius` is not a finite number is drawn with radius 0 and a shape whose `strokeWidth` is not a finite number > 0 is drawn without a stroke (EDGE-EDIT-69); a stamp whose label size `JsMath.Round(radius * 1.15)` is not > 0 draws its circle without the label. Colors through `CssColor.OrAccent` (D-EDIT-9). Stamp label `JsNumber.ToJsString(n)` when `n` is a number, else `JsString.ToJsString(n)` of the raw value. Text: replace U+0009, U+000A, U+000C, U+000D with U+0020 (EDGE-EDIT-51); empty text contributes nothing.
5. Rings: `R = AnnotationStyle.ClickMarkerRadius(Source.Width, Source.Height)`; the marker param first, then marker annotations in array order, each `OverlayRing(finite(x) - cx, finite(y) - cy, radius ?? R, color)`; a non-finite or non-positive radius contributes nothing (EDGE-EDIT-41).
6. `overlay = await rasterizer.RasterizeAsync(scene, ct)`; `Compositor.SourceOver(canvas, overlay)` (per channel `d = s + round(d * (255 - sa) / 255)`, integer, premultiplied).
7. `return codec.EncodePng(canvas)`; a codec failure throws `EncodeFailed`.

The rasterizer is called only when `Items.Count > 0`; an empty scene skips step 6's rasterize and composite. Threading: steps 0 to 3 and 6 to 7 run on the thread pool; step 6's rasterization runs on the STA render thread (7.10.6). Because the editor save calls `FlattenToPngAsync` from the UI thread (7.10.7), the method moves its CPU-bound work off the caller's thread itself: steps 0 to 5 run inside `Task.Run` (CPU-bound work, the one use ARCHITECTURE 14.4 allows in a service), and every await uses `ConfigureAwait(false)` (T2), so steps 6 and 7 continue on the pool; no bake, composite or encode ever runs on the UI thread (T10).

`RedactionBaker` (pure, deterministic, Linux-tested):

```csharp
public static class RedactionBaker
{
    public const int MinRedactBlock = 8;
    /// JS `a.blockSize || 12` then Math.round, floored at 8; NaN (non-numeric string, object) -> 12 (D-EDIT-2).
    public static double EffectiveBlock(JsonNode? blockSizeRaw);
    /// 2.16 bakeRedaction region, in output-canvas integers; Empty when w <= 0 || h <= 0.
    public static IntRect Region(double ax, double ay, double aw, double ah, int cx, int cy, int cw, int ch);
    public static void FillSolid(PremultipliedImage img, IntRect r);                    // B=G=R=0, A=255
    public static void Pixelate(PremultipliedImage img, IntRect r, double block);
    /// max(1, round(block * 0.6)), capped at max(w, h) so an absurd block (1e300, or Infinity from `1e400`) cannot size the layer (D-EDIT-26).
    public static int Sigma(double block, int w, int h) => (int)Math.Min(Math.Max(1, JsMath.Round(block * 0.6)), Math.Max(w, h));
}
```

`EffectiveBlock(v)`: if `JsTruthy(v)` is false, `n = 12`; else `n = JsToNumber(v)` (number: itself; string: ECMAScript `StringToNumber`, where `""` cannot occur because it is falsy; `true`: 1; array: `ToNumber(ToString(array))`, so `[]` is 0 and `[5]` is 5; object: NaN); if `n` is NaN, `n = 12` (IMPROVEMENT); return `max(8, JsMath.Round(n))` (may be `+Infinity`).

`Pixelate(img, r, block)` with `w = r.W`, `h = r.H`:

1. `sw = max(1, JsRound(w / block))`, `sh = max(1, JsRound(h / block))` (block infinite gives 1).
2. **Tile, box average (D-EDIT-1).** For tile pixel `(i, j)`: source columns `[r.X + floor(i * w / sw), r.X + floor((i + 1) * w / sw))`, rows likewise with `h, sh`; every cell is non-empty because `sw <= w` and `sh <= h`. Each channel of the premultiplied BGRA = `(sum + n / 2) / n` in integer arithmetic (`long` sums). The source is the CURRENT canvas (EDGE-EDIT-16).
3. **Base, bilinear upscale.** For destination `(dx, dy)` in the region: `u = (dx + 0.5) * sw / w - 0.5`, `v = (dy + 0.5) * sh / h - 0.5`, each clamped to `[0, sw - 1]` and `[0, sh - 1]`; interpolate the four neighbors (`floor(u)`, `min(floor(u) + 1, sw - 1)`, weights `u - floor(u)`), per channel, round half up; write into the region.
4. **Soft pass.** `sigma = Sigma(block, w, h)`, `m = 3 * sigma`. The cap changes no output: whenever `block > max(w, h) / 1.5` the tile is a single cell (`sw = sh = 1`), a uniform color that any Gaussian leaves unchanged, and below that bound `0.6 * block` is already under `max(w, h)`. Layer `L` of `(w + 2m)` by `(h + 2m)` transparent, with the base image of step 3 copied at `(m, m)`. Separable Gaussian on `L` with kernel radius `m`, weights `exp(-k^2 / (2 * sigma^2))` normalized to sum 1, horizontal then vertical, intermediate kept in `float`, final channels rounded half up and clamped to `[0, 255]`, edges treated as transparent (zero). Crop `L` back to `(m, m, w, h)` and composite it source-over onto the base in the region (clipped to the region). This is the CSS `blur()` semantics of the canvas filter pass: the blurred layer fades at its edges, where the opaque base shows through. Performance: a direct kernel costs `2 * (2m + 1)` taps per channel and pixel (sigma 36 gives 434), which threatens AC-EDIT-28 for large regions; the implementation MAY instead use the three successive box blurs of box size `d = floor(sigma * 3 * sqrt(2 * PI) / 4 + 0.5)` that the Filter Effects specification gives as the `feGaussianBlur` approximation (what Skia does for large sigmas), as long as it is integer-deterministic (`GaussianIsDeterministic`) and still reads only the tile-derived base (INV-EDIT-2).

Every value written in the region derives from the tile alone (INV-EDIT-2). The output is visually equivalent to Electron's (soft, detail destroyed), not byte-identical (Q-EDIT-3).

### 7.5 StepPatch, validation, the applier and the render writer (Core)

```csharp
public readonly record struct Optional<T>(bool IsSet, T Value)
{
    public static Optional<T> Unset => default;
    public static Optional<T> Of(T value) => new(true, value);
}

public sealed class StepPatch                       // field order = 2.18 order = Object.assign order
{
    public Optional<string> Caption, Heading, Body, Kind;
    public Optional<string?> Callout;               // null = remove the key
    public Optional<Rect?> Crop;                    // null = write JSON null
    public Optional<StepClick?> Click;              // null = write JSON null
    public Optional<string> MarkerColor;
    public Optional<bool> MarkerBaked;
    public Optional<double> ReportZoom, ReportPanX, ReportPanY;
    public Optional<JsonArray> Annotations;
    public bool TouchesRedactionOrCrop => Annotations.IsSet || Crop.IsSet;
}

public static class StepPatchValidator
{
    /// parseStepPatch (2.18) over an untrusted JSON value; throws ArgumentException("patch must be an object").
    public static StepPatch Parse(JsonNode? value);
    /// parseClick (2.18); returns null for an unusable click.
    public static StepClick? ParseClick(JsonNode? value);
    /// Builds the editor-save patch through the same rules (markerColor regex, click normalization, annotation filter).
    public static StepPatch ForEditorSave(EditorSaveSnapshot s);
    /// Builds { markerBaked: true } for StepFlattener.
    public static StepPatch MarkerBakedOnly();
}

public static class StepPatchApplier
{
    /// step-render.ts:17-37 exactly: apply in field order (existing keys in place, new keys appended),
    /// caption flag before the early return, then the invalidation.
    public static void ApplyAndInvalidate(ProjectStep step, StepPatch patch, bool hasFreshPng);
}

public interface IStepRenderWriter
{
    /// Refuse a stepId that is not one safe segment (D-EDIT-22); rel = "export/.render/" + stepId + ".png"
    /// (equal to Electron's posix join for every accepted id); ConfineNoLinks; Directory.CreateDirectory;
    /// snapshot the previous bytes (if any); AtomicFile write; step.Flattened = rel; step.RenderRev = (RenderRev ?? 0) + 1.
    Task<RenderWriteReceipt> WriteAsync(string resolvedProjectDir, ProjectStep step, string stepId,
                                        ReadOnlyMemory<byte> png, CancellationToken ct = default);
}

public sealed class RenderWriteReceipt
{
    /// Restores the previous bytes atomically, or deletes the new file when none existed.
    /// If restoring fails, deletes the render (fail closed) and logs Error. Never throws.
    public Task RollbackAsync();
}
```

`StepPatchValidator.Parse` never trusts a `double` without `double.IsFinite`, applies the clamps with `Math.Max(lo, Math.Min(hi, v))`, and uses `Regex(@"^#[0-9a-fA-F]{3,8}$", RegexOptions.CultureInvariant)` for `markerColor` (hex digits are ASCII; `$` must not accept a trailing newline, so use `\z`: `^#[0-9a-fA-F]{3,8}\z`, which is the JS behavior since JS `$` without the `m` flag does not match before a final newline).

The store job (spec 01 7.8 `UpdateStepAsync` and `MergeStepsAsync`, reached by every caller of this spec through `IProjectService`, R-ARCH-4), natively:

```
resolved = gate; manifest = read; step = find (else throw StepNotFoundException: "step {stepId} not found", spec 01 7.13)
hasFreshPng = png.Length > 0
StepPatchApplier.ApplyAndInvalidate(step, patch, hasFreshPng)
receipt = hasFreshPng ? await renderWriter.WriteAsync(resolved, step, stepId, png) : null
(merge only: remove the dropped step; Renumber)
manifest.UpdatedAt = now
try { await atomic.WriteAsync(project.json, Serialize(manifest)) }
catch { if (receipt != null) await receipt.RollbackAsync(); throw; }     // INV-EDIT-27
return manifest
```

`RefusedRenderPathException` (derives from `ShotAIException`, ARCHITECTURE 8.1) message: `$"refusing to write render for step \"{stepId}\" \u2014 path escapes the project folder"`. It is thrown, before anything is written, when `ConfineNoLinks` returns null OR when the id fails the D-EDIT-22 rule: empty, `.` or `..`, or containing `/`, `\`, `:`, a control character (U+0000 to U+001F, U+007F), or any character `PathConfine.HasHostileSegment` rejects (the rule spec 01 Q-MODEL-22 recommends). For every id shotAI or the macOS app generates this is a no-op; a step whose id was hand-edited into a path (EDGE-EDIT-62) is refused on its next native save with this message instead of writing elsewhere in the project.

### 7.6 The render gate (Core)

```csharp
public enum EgressVerb { Send, Export }                   // message words "send", "export"
public sealed record SendableRender(string Abs, string MediaType, string Ext);
public sealed class RenderGateException : ShotAIException    // R-ARCH-8; ShotAI.Core.Errors (ARCHITECTURE 8.1)
{
    public RenderGateException(string message);
    public RenderGateException(string message, Exception inner);  // 07 D-SOP-22 wraps with it
}

public static class RenderGate
{
    public static SendableRender ResolveSendableRender(string dir, ProjectStep step, string stepLabel,
                                                       EgressVerb verb, IPathProbe probe);
}

public interface IRenderGate                               // DI singleton; spec 09 injects this
{
    SendableRender Resolve(string dir, ProjectStep step, string stepLabel, EgressVerb verb);
}
public sealed class RenderGateService(IPathProbe probe) : IRenderGate
{
    public SendableRender Resolve(string dir, ProjectStep step, string stepLabel, EgressVerb verb)
        => RenderGate.ResolveSendableRender(dir, step, stepLabel, verb, probe);
}
```

Spec 07 calls the static form with its probe, spec 09 the injected `IRenderGate`; both run the one algorithm below and both catch `RenderGateException`, the one name (R-ARCH-8; 09's earlier `RenderRefusedException` is superseded).

Algorithm:

```
mayRedact = step.Annotations.Any(e => AnnotationView.From(e).MayRedact)                  // D-EDIT-3
rel = step.Raw["flattened"] is a non-empty JSON string ? that string : null              // D-EDIT-6
hasCrop = JsTruthy(step.Raw["crop"])                                                     // raw truthiness, REQUIRED
if rel is null && (mayRedact || hasCrop):
    throw RenderGateException($"{stepLabel} has a redaction or crop that hasn't been baked into a render yet \u2014 " +
                              $"refusing to {verbWord} the raw screenshot. Open it in the editor and save, then retry.")
relToRead = rel ?? step.Screenshot
abs = relToRead != "" ? PathConfine.ConfineNoLinks(dir, relToRead, probe) : null         // D-EDIT-4
if abs is null: throw RenderGateException($"{stepLabel} has no readable screenshot.")
ext = JsPath.ExtName(relToRead).ToLowerInvariant()
mediaType = ext is ".jpg" or ".jpeg" ? "image/jpeg" : "image/png"
return new(abs, mediaType, ext == "" ? ".png" : ext)
```

`JsPath.ExtName` implements Node `path.extname` on the last `/`- or `\`-separated segment: the substring from the last `.` when that `.` is not the segment's first character, else `""` (so `.hidden` gives `""`, `a.` gives `"."`). Corrected in WP-A6, which landed it (01's `ResolveImage` uses it): it is a port of Node 22's `path.win32.extname`, so trailing separators are skipped (`a.png/` gives `".png"`), a drive prefix is not part of the segment (`C:.png` gives `""`), and `..` gives `""`; `Json/JsPathTests` pins 26 values printed by Node. The gate is the ONLY function 07 and 09 may use to pick an image file for egress.

### 7.7 Pre-egress preparation (Core)

```csharp
public interface IStepFlattener                     // ShotAI.Core.Rendering (R-ARCH-7)
{
    /// ensureFlattened (2.19). Returns the last manifest written, or null when nothing changed.
    /// Without a session (Home exports, 06): each re-bake calls the injected IProjectService.UpdateStepAsync directly.
    Task<ProjectManifest?> EnsureFlattenedAsync(string projectPath, string projectDir,
                                                IReadOnlyList<ProjectStep> steps, CancellationToken ct);
    /// With the open project's IProjectSession (05, 07, 09 inside a project): each re-bake persists through
    /// session.ApplyDurable, so the session's Current adopts every written manifest (Changed(Durable)).
    /// Every caller inside an open project uses this overload (R-ARCH-5).
    Task<ProjectManifest?> EnsureFlattenedAsync(string projectPath, string projectDir,
                                                IReadOnlyList<ProjectStep> steps, IProjectSession session,
                                                CancellationToken ct);
    /// The merge entry point (the editor calls Flattener directly with its decoded source, 7.10.7):
    /// decode the ORIGINAL, flatten, return PNG bytes.
    Task<byte[]> FlattenStepAsync(string projectDir, ProjectStep step, IReadOnlyList<JsonNode?> annotations,
                                  JsonNode? rawCrop, FlattenMarker? marker, CancellationToken ct);
}
```

`StepFlattener` implements 2.19 exactly (skip rules, `shotNo` counting, one step at a time, `ct.ThrowIfCancellationRequested()` before each step, `OperationCanceledException` rethrown unwrapped), with: the source read via `PathConfine.ConfineNoLinks(projectDir, step.Screenshot, probe)` (D-EDIT-4) and `File.ReadAllBytesAsync`, then `IRenderCodec.Decode` with its magic-byte check and explicit decoder (R-ARCH-21) (a null path, any IO failure, bytes that are neither PNG nor JPEG, or any decode failure becomes `$"Could not load a screenshot to flatten ({step.Screenshot})."`, ELECTRON-ONLY URL replaced by the relative path); the marker `new FlattenMarker(click.Image.X, click.Image.Y, AnnotationStyle.MarkerColorFor(step), click.Radius)` (D-EDIT-18); persistence through `session.ApplyDurable(s => s.UpdateStepAsync(projectPath, step.Id, StepPatchValidator.MarkerBakedOnly(), png))` in the session overload (`s` is `IProjectService`, ARCHITECTURE 7.4 S5), else the injected `IProjectService.UpdateStepAsync` directly (Home export, 06); a click that spec 01's lenient `ProjectStep.Click` cannot parse gives no marker instead of Electron's `TypeError` (EDGE-EDIT-68); the source decode and the flatten run on the thread pool, one step at a time; and the error wrap `$"Step {shotNo} (\"{caption}\") couldn't be prepared: {ex.Message}"` with `caption = JsString.Trim(step.Caption)` or `"untitled"` when empty. As built in WP-A17: the load failure's text is Core's `ScreenshotLoadException` (`ShotAI.Core.Report`, `MessageFor(relativePath)`), which 05's size probe already throws.

`StepFlattener` is a Core singleton (ARCHITECTURE 4.3) constructed with `IProjectService`, `Flattener`, `IRenderCodec` and `IPathProbe`. Flattening is only the first of the three egress steps of ARCHITECTURE 7.7: after `EnsureFlattenedAsync` succeeds, the caller (05, 06, 07, 09) awaits `IProjectSettle.WhenSettledAsync(projectPath, ct)` (R-ARCH-6; inside an open project it awaits that session's `WhenIdleAsync`, with none it completes at once), then reads with `GetProjectForReadAsync` and obtains every image through the render gate (7.6). The flattener itself does not settle or read for egress.

### 7.8 Sensitive-text detection (Core)

```csharp
namespace ShotAI.Core.Redaction;

public sealed record OcrTextWord(string Text, double X0, double Y0, double X1, double Y1);
public sealed record OcrTextLine(IReadOnlyList<OcrTextWord> Words);

public static class SensitiveTextDetector
{
    public static IReadOnlyList<Rect> Detect(IReadOnlyList<OcrTextLine> lines, double pad = 4);
    internal static bool Luhn(string digits);
    internal static IReadOnlyList<Rect> MergeRects(IReadOnlyList<Rect> rects);
}
```

Regexes are `static readonly Regex` built with `RegexOptions.ECMAScript | RegexOptions.Compiled` (the ECMAScript option cannot be combined with `CultureInvariant` or `NonBacktracking`, and it makes `\d`, `\w`, `\s` and `\b` ASCII; INV-EDIT-20), patterns copied verbatim from 2.21 without the JS slashes and flags. Iteration: `regex.Matches(text)` (non-overlapping, left to right; none of the patterns can match empty). Card validation: `digits` = the match's characters in `'0'..'9'` only (JS `/\D/g` without `u`), then the length and `Luhn` checks. Spans and match indices are UTF-16 indices (both platforms). `MergeRects` ports the backward scan with `List.RemoveAt` exactly (EDGE-EDIT-48).

```csharp
public enum OcrScanStatus { Ok, Unavailable, Failed }
public sealed record OcrScanResult(OcrScanStatus Status, IReadOnlyList<OcrTextLine> Lines);
public interface IOcrEngine { Task<OcrScanResult> RecognizeAsync(PremultipliedImage image, CancellationToken ct); }  // never throws except on cancel

public interface ISensitiveRegionScanner                 // spec 11 references this interface
{
    Task<(OcrScanStatus Status, IReadOnlyList<Rect> Rects)> ScanAsync(PremultipliedImage image, string fileName, CancellationToken ct);
}
public sealed class SensitiveRegionScanner(IOcrEngine engine, ILogger<SensitiveRegionScanner> log) : ISensitiveRegionScanner
{
    public async Task<(OcrScanStatus Status, IReadOnlyList<Rect> Rects)> ScanAsync(PremultipliedImage image, string fileName, CancellationToken ct);
}
```

`ScanAsync`: `r = await engine.RecognizeAsync(image, ct)`; on `Ok`, `rects = SensitiveTextDetector.Detect(r.Lines)`, log Information `$"auto-redact: {r.Lines.Count} line(s), {rects.Count} sensitive region(s) in {fileName}"`; on `Unavailable` or `Failed`, return `(status, [])` (the engine already logged). `OperationCanceledException` propagates. Category `ocr` (10).

### 7.9 Windows OCR (Platform)

`WindowsOcrEngine : IOcrEngine`, using `Windows.Media.Ocr` and `Windows.Graphics.Imaging` from the `net10.0-windows10.0.19041.0` TFM (no NuGet):

| Concern | Rule |
|---|---|
| Engine creation | Lazily, once, under a `SemaphoreSlim(1)`: `OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))` when `OcrEngine.IsLanguageSupported` for it; else `OcrEngine.TryCreateFromUserProfileLanguages()`. Null: status `Unavailable`, not memoized (a language pack installed later is picked up on the next scan; parity with the Tesseract memo reset). Log Information `$"initializing OCR engine (language: {engine.RecognizerLanguage.LanguageTag})"` once (ELECTRON-ONLY vendored-path wording replaced). |
| Offline | Windows OCR is on-device; nothing is vendored or downloaded by the app. The en-US recognizer ships with en-US Windows as a Feature on Demand (`Language.OCR~~~en-US~0.0.1.0`); on images without it, IT deploys it (12 documents this; Q-EDIT-5). |
| Input | The editor's decoded source (`PremultipliedImage`, the same pixels the user sees; D-EDIT-11) converted with `SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied)` (`AsBuffer` is `System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions`, part of the Windows SDK projection the TFM brings); the `SoftwareBitmap` is disposed after recognition. |
| Size cap | `max = OcrEngine.MaxImageDimension`; if `w > max \|\| h > max`: `f = max / (double)Math.Max(w, h)`, box-downscale the `PremultipliedImage` in Core to `floor(w * f)` by `floor(h * f)` before the conversion; after recognition divide every word box coordinate by `f` (EDGE-EDIT-50). |
| Recognition | `await engine.RecognizeAsync(bitmap)`; for each `OcrLine` in `result.Lines`, each `OcrWord`: `OcrTextWord(word.Text, R.X, R.Y, R.X + R.Width, R.Y + R.Height)` with `R = word.BoundingRect`; lines with no words are skipped. `result.TextAngle` is ignored (parity: Tesseract's default page segmentation also returned axis-aligned boxes). |
| Threading | `OcrEngine` is agile (`MarshalingBehavior(Agile)`, `ThreadingModel.Both`, verified on Microsoft Learn); the call runs on the thread pool, one recognition at a time (semaphore). |
| Tokenization | Windows OCR decides the word boundaries; whether it keeps `123-45-6789`, `sk-...` or `4111-1111-1111-1111` as one word or splits at the hyphens is UNVERIFIED. The detector joins a line's words with single spaces (2.21), so a split token such as `123 - 45 - 6789` no longer matches `SSN`. The fixture test measures this (Q-EDIT-19). |
| Cancellation | `ct` is checked before and after recognition; a cancelled scan's result is discarded (the WinRT operation itself is not interruptible; D-EDIT-12). |
| Failure | Any exception other than cancellation: log Warning `"auto-redact: OCR scan failed (best-effort, skipping)"` with the exception type and HResult only (never the recognized text), return `Failed`. |

The editor's scan entry point (App) replaces `projects:redact-scan` (ELECTRON-ONLY IPC): `EditorViewModel.AutoRedactAsync()` calls the injected `ISensitiveRegionScanner.ScanAsync(document source image, Path.GetFileName(step.Screenshot), editorCts.Token)` and then `EditorDocument.AddAutoRedactions(rects)`. Notices:

| Result | Notice |
|---|---|
| `Ok`, no rects | info `No sensitive data detected (best-effort \u2014 redact manually if needed).` (REQUIRED) |
| `Ok`, rects | none; suggestions added, last one selected (REQUIRED) |
| `Unavailable` | info `Auto-redact is not available on this PC because Windows text recognition is not installed. Redact manually.` (IMPROVEMENT D-EDIT-11, text per Q-EDIT-5) |
| `Failed` | error `Auto-redact could not scan this screenshot. Redact manually.` (IMPROVEMENT D-EDIT-11, text per Q-EDIT-5) |

### 7.10 The editor UI (App)

#### 7.10.1 Hosting and composition

`EditorOverlayView` is a `UserControl` placed in the project window's top overlay layer (03 provides the layer), with the 2.2 backdrop (`#8C0F172A`, a 24 DIP margin) and a rounded panel; `AutomationProperties.Name="Edit screenshot"`, and the panel is the keyboard focus scope while open. `EditorViewModel` (derives from `ViewModelBase : ObservableObject`, ARCHITECTURE 5.1; `[RelayCommand]` for every button, async commands with concurrent execution disallowed, T11) wraps one `EditorDocument` and exposes its state; it is created by `EditorFactory`, a singleton factory (ARCHITECTURE 4.1 C6) constructed by DI with `Flattener`, `IStepFlattener`, `ISensitiveRegionScanner`, `IRenderCodec`, `IPathProbe`, `IColorPicker`, `ILogger<EditorViewModel>` (all Core or App types; none is on the INV-ARCH-3 forbidden list, and no Platform type is injected). The open project's session is not a DI service: it is the `IProjectSession` that 05 obtained from `IProjectSessionFactory.Create` on the UI thread (R-ARCH-5), and 05 passes it to `EditorFactory.Create(IProjectSession session, string projectPath, string projectDir, ProjectStep step)`. Rail, top bar, properties bar and hint line are XAML bound to the view model with the exact strings of 2.22 (non-ASCII characters as in the Electron source). The tool buttons are `RadioButton`s styled as rail buttons (`AutomationProperties.Name` = the label, `ToolTip` = the tooltip); the redaction mode is a two-button segmented `RadioButton` group named `How to hide this region`. Sliders: WPF `Slider` with `Minimum`, `Maximum`, `SmallChange = 1`, `IsSnapToTickEnabled = true`, `TickFrequency = 1`. The Color control is a swatch button whose command calls `IColorPicker.Pick(string currentHex)` (App interface, UI thread; returns the chosen `#rrggbb` or null) and applies a non-null result via `ChangeColor` (parity with Chromium's Windows color input, which opens the same system dialog). `Win32ColorPicker : IColorPicker` is a thin App wrapper over the public Platform helper `Win32ColorDialog` (7.13; a Platform helper the App calls directly, INV-ARCH-4) with the project window as owner, so the view model never references a Platform type (INV-ARCH-3). Whether the system dialog's HWND is excluded from capture is Q-EDIT-21.

#### 7.10.2 Loading

On open: `abs = PathConfine.ConfineNoLinks(projectDir, step.Screenshot, probe)` (D-EDIT-4); read the bytes on the thread pool; `IRenderCodec.Decode` (thread pool; the magic-byte check and the explicit built-in PNG or JPEG decoder of R-ARCH-21, so a file that is neither is refused, never handed to a sniffed codec) into the view model's `source` (`PremultipliedImage`, kept for the preview tiles, OCR and the save's flatten); construct the `EditorDocument` (which assigns a fresh id to any `blur` element without a string `id`, D-EDIT-2); create a `WriteableBitmap` (`PixelFormats.Pbgra32`, 96 DPI) from the pixels for display. Any failure: notice error `Could not load the screenshot.`, the canvas shows `Loading screenshot\u2026`, Save and Auto-redact disabled (EDGE-EDIT-44). The file is not kept open (spec 01 EDGE-MODEL-39).

#### 7.10.3 Canvas, view geometry and pan

`EditorCanvas : FrameworkElement` renders the screenshot, annotations, previews, the click marker, the crop box and selection chrome in `OnRender` through `AnnotationPainter` under `PushTransform(new MatrixTransform(s, 0, 0, s, -region.X * s, -region.Y * s))`, clipped to its bounds; `MeasureOverride` returns `(cropView.W, cropView.H)` from `EditorGeometry.ComputeCropView` (2.5). It sits in a `ScrollViewer` (`HorizontalAlignment="Center"` for the canvas, `VerticalAlignment="Top"`), with `VerticalScrollBarVisibility="Visible"` so the viewport width never toggles, `HorizontalScrollBarVisibility="Auto"`. The fit width is `ScrollViewer.ViewportWidth` and the height is the `ActualHeight` of the non-scrolling `Grid` that wraps the `ScrollViewer` (EDGE-EDIT-1). Viewport changes update the document through `SizeChanged` only when both values are > 0, floored like Electron. `RenderOptions.BitmapScalingMode="Linear"` for the screenshot (parity with Konva's smoothed image), `EdgeMode` unspecified (antialiased shapes).

Pointer mapping, one function for every event (EDGE-EDIT-8): `ToImage(Point p) = new(region.X + p.X / s, region.Y + p.Y / s)` with `p = e.GetPosition(canvas)`. A press calls `CaptureMouse()`, so moves and the release outside the canvas still arrive (EDGE-EDIT-7); `LostMouseCapture` ends a gesture from the last draft. Double-click: `MouseLeftButtonDown` with `e.ClickCount == 2` on a text (select tool). Cursor: `Cursors.Arrow` in the select tool, `Cursors.Cross` otherwise.

Pan: the `ScrollViewer` (wheel vertical). Shift+wheel scrolls horizontally, and `WM_MOUSEHWHEEL` (0x020E) from an `HwndSource` hook scrolls horizontally, both REQUIRED to match Chromium's defaults (WPF has neither by default). Ctrl+wheel zooms the editor by x1.25 or /1.25 within `[0.5, 8]` (IMPROVEMENT D-EDIT-17; replaces Chromium's page zoom that the Electron editor did not intercept).

#### 7.10.4 Painter, previews and the blur preview cache

`AnnotationPainter` (static, WPF `DrawingContext`) draws each `OverlayItem` and is used by BOTH `EditorCanvas` and `WpfOverlayRasterizer` (INV-EDIT-29):

| Item | WPF drawing |
|---|---|
| round rect | `DrawRoundedRectangle(fill, new Pen(stroke, w) { LineJoin = Miter, MiterLimit = 10, StartLineCap = Flat, EndLineCap = Flat }, rect, r, r)` with `r` as 2.16; negative `W`/`H` normalized for drawing (canvas `roundRect` draws them mirrored) |
| arrow | shaft `DrawLine` with a flat-capped pen of width `w` when `len > 0`; head `StreamGeometry` triangle (2.16 formula), `DrawGeometry(brush, new Pen(brush, w) { LineJoin = Miter, MiterLimit = 10 }, head)` |
| stamp | `DrawEllipse(fill, null, center, r, r)`; label `FormattedText(label, InvariantCulture, LeftToRight, new Typeface(SegoeUI, Normal, Bold, Normal), JsRound(r * 1.15), textBrush, 1.0)` centered: origin `(x - ft.WidthIncludingTrailingWhitespace / 2, y - ft.Height / 2)` |
| text | `FormattedText(text, ..., new Typeface(SegoeUI, Normal, Normal, Normal), fontSize, fill, 1.0)` at origin `(x, y)` (top of the line box) |
| ring | `DrawEllipse` fill = color with alpha `0.18 * a`, then `DrawEllipse(null, new Pen(color, max(2, JsRound(r * 0.22))), ...)` |

`FormattedText` throws `ArgumentOutOfRangeException` for an em size that is not > 0, and also above an upper bound (UNVERIFIED); the scene builder already drops non-positive sizes (7.4 step 4), and the painter additionally catches `ArgumentException` per item, skips that item and logs Warning (category `main`, 10 7.5.3; the item type only, never its text), so one absurd hand-edited `fontSize` can never fail a save.

`TextFormattingMode.Ideal`, `TextRenderingMode.Grayscale` (deterministic, no ClearType fringes in the PNG), pixels per DIP 1.0 (the bake is in image px). Font family `new FontFamily("Segoe UI")` with WPF's default fallback for glyphs Segoe UI lacks. The editor draws text through the same routine at the stage scale, so the preview is exact.

Blur preview: solid regions (`mode === "solid"`) are black rectangles; every other region, whatever its `mode`, shows `RedactionBaker.Pixelate` applied to a copy of the RAW source region `Region(a, 0, 0, NatW, NatH)` (the same integer region the bake uses, EDGE-EDIT-61; the raw, not the composite, EDGE-EDIT-16), converted to a frozen `BitmapSource` and drawn at that integer rect; a region whose integer `Region` is empty (a sub-pixel blur that the save will refuse, EDGE-EDIT-11) previews as the `#8C0F172A` rect at its fractional geometry so it stays visible and selectable. `BlurPreviewCache` keys on `(id, region, EffectiveBlock)` and holds at most 80 entries (cleared when exceeded; the macOS bound, `macOS:shotAI/Editor/EditorOverlay.swift:738-745`); computation runs on the thread pool with the latest request winning, and until it lands the region shows `#8C0F172A` (the Electron fallback color). Drafts: rect and crop outline and blur fill per 2.6, in the current color for rect and arrow (D-EDIT-16). The crop box, text selection outline and dashes per 2.7 and 2.11 (dash arrays converted from image px: `DashStyle` in units of the pen width, so `[10, 6]` at width 3 is `{10/3, 6/3}`).

#### 7.10.5 Selection handles and the inline text box

`SelectionAdorner` (an `Adorner` on the canvas) draws the transformer: a 1 DIP `#00A1FF` border around the self rect and eight (or two, for arrows) 10 by 10 DIP white squares with a 1 DIP `#00A1FF` stroke; each handle's hit area is 16 by 16 DIP (the macOS grab lesson). Handle drags run `TransformMath` live and call `CommitTransform` on release.

`InlineTextBox`: one `TextBox` hosted in a `Canvas` layer above `EditorCanvas`, inside the scroll content, so it scrolls with the image (EDGE-EDIT-52); `Canvas.Left = (a.X - region.X) * s`, `Canvas.Top = (a.Y - region.Y) * s`, `FontFamily` Segoe UI, `FontSize = a.FontSize * s`, `Foreground = a.Fill`, `Background = #24FFFFFF`, a 1 DIP dashed accent outline (`Border`), `Padding = 1,0`, `BorderThickness = 0`, `AcceptsReturn = false`, `SpellCheck.IsEnabled = false`, `MinWidth` = 4 average character widths, width growing with content, placeholder `Type\u2026` as a watermark (WPF `TextBox` has no placeholder property: a hit-test-invisible `TextBlock` over the box, visible while `Text` is empty). `TextChanged` calls `UpdateEditingText`; `PreviewKeyDown` Enter or Escape calls `FinishTextEdit` and marks handled; the key events are marked handled so the editor's key handler never sees them; `LostKeyboardFocus` calls `FinishTextEdit` except within the 300 ms opening guard, where it refocuses through `UiDeferral` at `DispatcherPriority.Input` (EDGE-EDIT-5). On open: `Focus()` then `SelectAll()`, deferred through `UiDeferral` at `DispatcherPriority.Input`. `Dispatcher.BeginInvoke` and `InvokeAsync` are banned in the App outside `WpfUiDispatcher.cs`, `StaRenderThread.cs` and `UiDeferral.cs` (R-ARCH-18, ARCHITECTURE 14.9), so the text box never calls them; these deferrals are view focus plumbing, not service-event marshaling, which stays `IUiDispatcher.Post` only (ARCHITECTURE 6.3).

#### 7.10.6 Overlay rasterizer and the STA render thread

`WpfOverlayRasterizer : IOverlayRasterizer` runs on `StaRenderThread`: one background `Thread` with `ApartmentState.STA`, `IsBackground = true`, name `shotAI render`, running `Dispatcher.Run()`, started lazily on first use. `StaRenderThread.cs` is the allowlisted file that may call `Dispatcher` APIs on its own dispatcher (R-ARCH-18, ARCHITECTURE 14.9); it never touches the UI thread's dispatcher. `StaRenderThread : IDisposable` (ARCHITECTURE 4.1 C5): `Dispose` calls `Dispatcher.InvokeShutdown()` on its own dispatcher and joins the thread with a bounded wait, never needing the UI thread (DL4); the container disposes it at `provider.Dispose()` (ARCHITECTURE 4.5 step 5), so no separate app-exit hook exists. `RasterizeAsync` posts to that dispatcher: build a `DrawingVisual`, `using (var dc = visual.RenderOpen())` paint every item with `AnnotationPainter`, render into `new RenderTargetBitmap(scene.Width, scene.Height, 96, 96, PixelFormats.Pbgra32)`, `CopyPixels` into a new `PremultipliedImage`, and complete the task. `RenderTargetBitmap` always renders in software (Microsoft Learn, "Graphics Rendering Tiers"); its maximum size is not documented (UNVERIFIED), and one bitmap of a 7680 by 4320 scene is 132 MB. So a scene larger than 4096 px on either side is rasterized in tiles of at most 4096 by 4096 px (each tile a `TranslateTransform(-tileX, -tileY)` pushed before painting, copied into the full `PremultipliedImage` at its offset), and only the tiles that intersect an item's bounds are rendered (the rest stay transparent). Nothing WPF crosses threads: the scene is plain records and the result is a byte array.

#### 7.10.7 Save

```
EditorViewModel.SaveAsync():                                     // [RelayCommand], CanExecute: image loaded && !IsSaving
    snapshot = document.PrepareSave()                            // closes text edit, drops empty texts, builds click; IsSaving = true
    notice = null
    try:
        png = await flattener.FlattenToPngAsync(new FlattenRequest(this.source, snapshot.Annotations,   
                                                snapshot.Crop as JSON, snapshot.Marker), ct)   // the PremultipliedImage decoded at load (7.10.2), no second read
        patch = StepPatchValidator.ForEditorSave(snapshot)
        manifest = await session.ApplyDurable(s => s.UpdateStepAsync(projectPath, step.Id, patch, png))   // s: IProjectService
        Close(saved: manifest)                                    // success only now (INV-EDIT-10); the session already adopted it, Changed(Durable)
    catch OperationCanceledException: document.SaveFailed()      // only when the app is shutting down (7.11)
    catch ex: notice = error(UserMessage.From(ex)); document.SaveFailed()   // ARCHITECTURE 8.2
```

`session.ApplyDurable` sets `Current` to the written manifest and raises `Changed(Durable)` (ARCHITECTURE 7.4 S5), so `Close(saved: manifest)` only closes the overlay; 05 does not apply the manifest a second time (the native form of `onSaved`, 2.2). The notice text is `UserMessage.From(ex)` (ARCHITECTURE 8.2): every expected failure here is a `ShotAIException` (the four `FlattenException` messages below, `RefusedRenderPathException`, spec 01's `StepNotFoundException` and `ManifestCorruptException`) or an `IOException` / `UnauthorizedAccessException` and shows its `Message` verbatim, as Electron did; anything else shows the generic sentence and is logged at Error. The editor calls `Flattener` directly with its already decoded source; `IStepFlattener.FlattenStepAsync` (which reads and decodes the original) is the merge's entry point (05). While `IsSaving`, the canvas ignores input and every control is disabled, Cancel included (parity for Save, Cancel and Auto-redact; IMPROVEMENT D-EDIT-12 for the canvas and the property controls). The flatten's `FlattenException` messages: `UnbakeableRedaction` = `A redaction region could not be applied (too small or off-image). Adjust or remove it, then save again.` (REQUIRED); `CropUnapplied` = `The crop on this step could not be applied. Reset the crop, then save again.` (IMPROVEMENT D-EDIT-7); `UnknownAnnotation` = `This screenshot has an annotation this version of shotAI doesn't recognize, so it can't be saved safely. Update shotAI, then try again.` (Q-EDIT-2); `EncodeFailed` = `The edited screenshot could not be encoded. Try saving again.` (replaces `canvas.toBlob returned null`, ELECTRON-ONLY).

### 7.11 Threading, cancellation and disposal

| Work | Thread | Cancellation |
|---|---|---|
| `EditorDocument` mutations, view model, canvas input and rendering | UI (WPF dispatcher) | n/a |
| Pre-checks, redaction bake and scene build of the flatten (7.4 steps 0 to 5) | thread pool, inside `Flattener`'s own `Task.Run` | the editor token, checked between steps |
| Source read and decode, blur preview tiles, redaction bake, composite, PNG encode | thread pool | the editor's `CancellationTokenSource`, cancelled on Cancel or close; a save in flight is NOT cancelled by closing the window: the window cannot close while `IsSaving` (the durable path completes or fails first) |
| Overlay rasterization | `StaRenderThread` | checked before posting and after completion |
| OCR | thread pool, serialized by the engine semaphore | the editor token; result discarded when cancelled |
| Store job (render and manifest writes) | the store's queue (spec 01) | not cancellable once started |

`EditorViewModel : IDisposable` cancels its token, drops its references to the `WriteableBitmap` and the preview cache (neither has a `Dispose`), and detaches document events. `StaRenderThread` is a DI singleton that implements `IDisposable` and is disposed by `provider.Dispose()` at exit (7.10.6, ARCHITECTURE 4.5). `WindowsOcrEngine` is a singleton; the engine object has no disposal.

### 7.12 Persistence and error handling

| Operation | Path | Error surface |
|---|---|---|
| Editor save | `IProjectSession.ApplyDurable` over `IProjectService.UpdateStepAsync` (render then manifest in one job, rollback on manifest failure) | editor notice error with `UserMessage.From(ex)`; editor stays open |
| Merge (05) | `StepFlattener.FlattenStepAsync` for the kept step (annotations plus the mapped marker, kept crop, kept click with `markerColorFor(keep)` and its `radius`, D-EDIT-18), then `ApplyDurable(s => s.MergeStepsAsync(...))` | 05's notice |
| Egress preparation (06, 07, 09) | `IStepFlattener.EnsureFlattenedAsync` (session overload inside an open project, direct-store overload from Home), then `IProjectSettle.WhenSettledAsync` (R-ARCH-6), then `GetProjectForReadAsync` and the gate inside 07 or 09 (ARCHITECTURE 7.7) | the caller's notice with the 2.19 or 2.20 message |
| Auto-redact | in memory only | notice per 7.9 |

No editor state is persisted outside the save (no drafts, no remembered tool or zoom), parity.

### 7.13 NuGet, WinRT and Win32 APIs

- No new NuGet packages (ARCHITECTURE 3.2). Core: the BCL (`System.Text.Json.Nodes`, `System.Text.RegularExpressions`) plus `Microsoft.Extensions.Logging.Abstractions` for `ILogger<T>` (on the Core allowlist, added by the foundation PR, ARCHITECTURE 3.5). `CommunityToolkit.Mvvm` and `Microsoft.Extensions.DependencyInjection` in App come from the fixed stack (their versions live in `Directory.Packages.props`).
- WinRT (TFM): `Windows.Media.Ocr.OcrEngine`, `OcrResult`, `OcrLine`, `OcrWord`; `Windows.Globalization.Language`; `Windows.Graphics.Imaging.SoftwareBitmap`, `BitmapPixelFormat`, `BitmapAlphaMode`, for OCR input only. The image codec is NOT WinRT: spec 02 defines `WicImageCodec` on WIC COM through CsWin32 (`IWICImagingFactory`, MTA-safe, synchronous), and `IRenderCodec` extends that class instead of adding a second codec. Decode (R-ARCH-21, ARCHITECTURE 9.1; never `CreateDecoderFromStream` or `CreateDecoderFromFilename`, which pick a codec by sniffing and can run any third-party codec installed on the machine): first the magic-byte check (PNG: length >= 8 and bytes 0..3 `89 50 4E 47`; JPEG: length >= 3 and `FF D8 FF`, the spec 01 `detectImage` rule), anything else throws before WIC is touched; then `IWICStream.InitializeFromMemory`, `IWICImagingFactory.CreateDecoder(GUID_ContainerFormatPng or GUID_ContainerFormatJpeg, &GUID_VendorMicrosoftBuiltIn)` (the vendor argument is documented only as a preference, so the codec also reads `IWICBitmapDecoder.GetDecoderInfo` and `IWICComponentInfo.GetVendorGUID` and refuses a decoder whose vendor is not `GUID_VendorMicrosoft`; corrected in WP-A17: the built-in decoders report `GUID_VendorMicrosoft` as their vendor, and `GUID_VendorMicrosoftBuiltIn` is only the preference `CreateDecoder` takes; a check against it refused every decode on both Windows runners), `IWICBitmapDecoder.Initialize(stream, WICDecodeMetadataCacheOnDemand)`, `GetFrame(0)`; a failing `CreateDecoder`, vendor check or `Initialize` throws (fail closed: the editor shows `Could not load the screenshot.`, `EnsureFlattenedAsync` fails with the step named); EXIF orientation read from the frame's `IWICMetadataQueryReader` (`/app1/ifd/{ushort=274}` for JPEG; PNG has none) and applied with `IWICBitmapFlipRotator` (WIC COM does not apply orientation itself); an embedded ICC profile converted to sRGB with `IWICColorTransform` (profile from `IWICBitmapFrameDecode.GetColorContexts`, target `IWICColorContext.InitializeFromExifColorSpace(1)`); then `IWICFormatConverter` to `GUID_WICPixelFormat32bppPBGRA`. Encode: Core un-premultiplies to straight BGRA (`c = a == 0 ? 0 : min(255, (c * 255 + a / 2) / a)`), then the PNG encoder with frame format `GUID_WICPixelFormat32bppBGRA`, exactly spec 02's encoder. The orientation query path and the color-context calls are UNVERIFIED names to confirm against the CsWin32 metadata when the codec is written; `RenderCodecTests` pins the behavior. NativeMethods.txt additions: `IWICBitmapDecoder`, `IWICBitmapDecoderInfo`, `IWICComponentInfo`, `GUID_ContainerFormatJpeg`, `GUID_VendorMicrosoftBuiltIn`, `GUID_VendorMicrosoft` (added in WP-A17, for the vendor check), `IWICBitmapFlipRotator`, `IWICColorTransform`, `IWICColorContext`, `IWICMetadataQueryReader`, `IWICFormatConverter` (each only if 02 has not listed it; whether CsWin32 0.3.335 projects the `GUID_*` constants by these names is UNVERIFIED, and if not they are declared as `Guid` constants with their SDK values). As built in WP-A17: the decode steps are Platform's internal `WicDecoding`, landed with 05's report decoder, with the colour and pixel-format steps before the flip-rotator, which reads a buffered bitmap (05 7.11). Every `NativeMethods.txt` name listed here landed with it, and CsWin32 projects the `GUID_*` constants by these names.
- WPF (App): `DrawingVisual`, `DrawingContext`, `RenderTargetBitmap`, `WriteableBitmap`, `FormattedText`, `Typeface`, `StreamGeometry`, `Adorner`, `ScrollViewer`, `HwndSource`.
- Win32 through CsWin32, added to `src/ShotAI.Platform/NativeMethods.txt`: `ChooseColor`, `CHOOSECOLORW`, `CHOOSECOLOR_FLAGS` (the dialog is opened with `CC_RGBINIT | CC_FULLOPEN`, the owner HWND of the project window, and 16 custom colors kept for the session), `WM_MOUSEHWHEEL` is only a message id (0x020E) read in App's `HwndSource` hook; it needs no P/Invoke, and `GET_WHEEL_DELTA_WPARAM` is the high word of `wParam`.

### 7.14 Divergence register

| ID | Change | Class | Justification |
|---|---|---|---|
| D-EDIT-1 | The pixelate tile is a true box average of each cell (Electron: Chromium bilinear sampling) | IMPROVEMENT [SECURITY] | Every source pixel contributes and no glyph stroke can survive a sample point at full contrast; makes INV-EDIT-2 provable; the visual result is the same soft blur (EDGE-EDIT-60). |
| D-EDIT-2 | A `blur` with non-number or negative geometry fails the flatten; a NaN `blockSize` bakes at 12; a blur without a string id gets one on open | IMPROVEMENT [SECURITY] | Electron silently skips or no-ops these, leaving original pixels in a render the gate trusts (EDGE-EDIT-14, 15, 18, 54). No UI path produces them, so normal users see no change. |
| D-EDIT-3 | The gate counts unknown annotation types as possible redactions | IMPROVEMENT [SECURITY] | macOS #109: an annotation this build cannot read might be a redaction; fail closed (EDGE-EDIT-17). |
| D-EDIT-4 | Gate reads and every source read that produces a render (editor, `ensureFlattened`, OCR) use `ConfineNoLinks` | IMPROVEMENT [SECURITY] | Blocks link-based exfiltration through shared project folders (EDGE-EDIT-19, macOS `26bb2b2`). |
| D-EDIT-5 | Atomic render write, rollback of the render when the manifest write fails | IMPROVEMENT [SECURITY] | Keeps render and manifest consistent; a torn or orphaned render can no longer carry fewer redactions than the manifest describes (EDGE-EDIT-20, spec 01 EDGE-MODEL-47). |
| D-EDIT-6 | Empty or non-string `flattened` is absent | IMPROVEMENT | macOS parity; avoids a spurious "no readable screenshot" on a plain step (EDGE-EDIT-27). |
| D-EDIT-7 | A truthy unparseable crop fails the flatten with its own message; the editor opens it as no crop | IMPROVEMENT | Electron fails with an opaque `canvas.toBlob returned null` (EDGE-EDIT-28). |
| D-EDIT-8 | One painter and one font (Segoe UI) for preview, inline text box and bake; preview blur uses the bake's integer region, block and mode rules | IMPROVEMENT | WYSIWYG; Electron's text moved and resized after saving, and blur previews differed for `blockSize: 0`, showed the raw pixels for a missing or non-numeric `blockSize` and a translucent box for an unknown `mode` (EDGE-EDIT-43, 52, 55, 56, 61, 63). |
| D-EDIT-9 | Deterministic color parsing with an ACCENT fallback | IMPROVEMENT | Canvas "keep previous style" makes a bad color depend on the previous annotation (EDGE-EDIT-42). |
| D-EDIT-10 | Malformed non-redacting annotations are skipped, never thrown; a non-finite `cornerRadius` draws as 0 and a non-positive `strokeWidth` draws no stroke; unknown and junk annotations are not drawn or selectable; an unparseable click bakes no ring | IMPROVEMENT | A hand-edited radius could make saving impossible; an invisible draggable node could corrupt unknown data; the canvas's ignore-invalid setters made one shape's style leak into the next (EDGE-EDIT-38, 41, 67, 68, 69). |
| D-EDIT-11 | Windows.Media.Ocr on the in-memory decoded image, en-US preferred, max-dimension scaling, distinct Unavailable and Failed notices | IMPROVEMENT | Fixed decision (engine); scanning the pixels the user sees removes a second file read; users are told when OCR could not run instead of "No sensitive data detected" (EDGE-EDIT-45, 50). |
| D-EDIT-12 | Canvas read-only while saving; scan cancelled on close | IMPROVEMENT | Edits during a save were silently lost (EDGE-EDIT-39, 40). |
| D-EDIT-13 | Left button only; press selects, then drag | IMPROVEMENT | Windows convention and macOS parity; the dragged shape shows its handles (EDGE-EDIT-34, 36). |
| D-EDIT-14 | The crop interior is hit-tested after annotations | IMPROVEMENT | Annotations inside a crop become selectable in the full view (EDGE-EDIT-33). |
| D-EDIT-15 | Arrows resize by two endpoint handles | IMPROVEMENT | Vertical arrows could not be resized; endpoints are the natural control (EDGE-EDIT-32). |
| D-EDIT-16 | Rect and arrow drafts preview in the current color | IMPROVEMENT | The draft matches the shape it creates. |
| D-EDIT-17 | `V` selects the Select tool; Ctrl+wheel zooms the editor | IMPROVEMENT | The tooltip already advertises `V`; Ctrl+wheel replaces Chromium's page zoom with the expected editor zoom (EDGE-EDIT-37). |
| D-EDIT-18 | `ensureFlattened` and the merge's re-bake of the kept step bake the click's custom `radius` | IMPROVEMENT | A re-bake keeps the ring the user sized (EDGE-EDIT-26). |
| D-EDIT-19 | `shot://`, CORS, `crossOrigin`, `?v=renderRev` URLs, the three IPC channels, the Tesseract worker and vendored model | ELECTRON-ONLY | Replaced by direct file reads through `PathConfine`, the `(flattened, renderRev)` cache key in 05's loader, direct service calls, and Windows OCR. |
| D-EDIT-20 | A minimum 8 DIP hit band for outlines and arrow shafts | IMPROVEMENT | Thin outlines were hard to grab at fit (EDGE-EDIT-35). |
| D-EDIT-21 | Viewport measured with the vertical scrollbar always shown | REQUIRED behavior, different mechanism | WPF has no `scrollbar-gutter`; the fit width differs from Electron's by one scrollbar width at most. |
| D-EDIT-22 | A step id that is not one safe path segment is refused by the render writer with the existing refusal message (decides spec 01 Q-MODEL-22) | IMPROVEMENT [SECURITY] | Electron's `path.posix.join` lets a hand-edited id redirect the render anywhere inside the project, including over an original screenshot (EDGE-EDIT-62, EDGE-MODEL-51). Every generated id is unaffected. |
| D-EDIT-23 | `Reset crop`, `Apply crop` and `Show full` clear a crop-box selection | IMPROVEMENT | Electron left a `Remove crop` button for a removed crop and live handles on an inert crop box (EDGE-EDIT-64). |
| D-EDIT-24 | Delete, Backspace, Escape and `V` reach the editor unless the inline text box has focus | IMPROVEMENT | Electron ignored them while a slider or the color input had focus, which reads as a broken Delete key (EDGE-EDIT-65). |
| D-EDIT-25 | The editor save bakes the annotation list after the `parseStepPatch` element filter, so the render and the manifest describe the same list | IMPROVEMENT [SECURITY] | Electron bakes first and filters in main, so a blur without a string id is in the render but not the manifest and is lost at the next re-bake; junk elements that crashed or froze the Electron editor are dropped as Electron's save would have dropped them (INV-EDIT-32, EDGE-EDIT-54, 67). |
| D-EDIT-26 | The Gaussian sigma of the soft pass is capped at `max(w, h)` of the region | IMPROVEMENT (bounded, no visible change) | A crafted `blockSize` (1e300, or Infinity from `1e400`) would otherwise size the blur layer beyond memory; the cap only applies when the tile is a single uniform cell (7.4). |
| D-EDIT-27 | Every decode in this subsystem (editor load, `ensureFlattened`, merge, OCR input) accepts only PNG or JPEG by magic bytes and uses the explicit built-in WIC decoder; any other bytes fail closed | IMPROVEMENT [SECURITY] | Electron decoded in the sandboxed renderer with every format Chromium supports; natively decoding runs in the privileged process, so a sniffing factory could invoke any third-party codec installed on the machine (R-ARCH-21, D-ARCH-3, Q-IPC-16). No UI path writes another format under `shots/`. |

---

## 8. Tests

### 8.1 Electron test files

| File | Purpose | Cases (grouped) | Ports to | Target class |
|---|---|---|---|---|
| `src/renderer/editor/editor-geometry.test.ts` | pins the crop clamp and the stage transform (redaction-adjacent: they decide which pixels are shown and baked) | `clampRectToImage`: contained rect passes through (`:5-7`); right/bottom overflow clamps width and height to 20 (`:8-10`); negative origin clamps to 0 and keeps width 50 (`:11-13`); fully outside gives 1 by 1 at (100, 100) (`:14-16`). `computeCropView`: identity at scale 1 (`:20-22`); uniform 0.5 downscale (`:23-25`); region (100, 50) at scale 2 gives x -200, y -100, 800 by 600 (`:26-28`); a 0.2 by 0.2 region gives a 1 by 1 stage (`:29-31`) | ShotAI.Core.Tests (Linux) | `Editor/EditorGeometryTests` (plus an assertion that `X` and `Y` are never negative zero: `double.IsNegative(view.X)` is false for region x 0) |
| `src/shared/redact-detect.test.ts` | pins the detectors on synthetic OCR lines (`line()` helper: word i has bbox x0 = running x, width 8 per char, y 0 to 16, 8 px gaps, `:6-15`) | SSN `123-45-6789` gives 1 rect (`:18-20`); Luhn-valid card split in four words `4111 1111 1111 1111` gives 1 (`:22-24`); non-Luhn `1111 1111 1111 1111` gives 0 (`:26-28`); `sk-abcdEFGH1234567890wxyz` gives 1 (`:30-32`); `John Smith` gives 0 (`:34-36`) | ShotAI.Core.Tests (Linux) | `Redaction/SensitiveTextDetectorTests` (same helper as a private method) |
| `src/main/render-gate.test.ts` | pins the fail-closed gate (`DIR` is `C:\proj\p` on Windows, `/proj/p` elsewhere, `:6`; the `step()` helper, `:9-23`, builds a shot step `s1` with `screenshot: 'shots/s.png'`) | refuses a blur without a render, verb send, message matches `/redaction or crop/` (`:26-30`); refuses a crop without a render, verb export (`:32-36`); blur with render reads `export/.render/s1.png`, `image/png` (`:38-47`); plain shot reads `shots/s.jpg`, `image/jpeg` (`:49-53`); `../../evil.png` throws `/no readable screenshot/` (`:55-59`) | ShotAI.Core.Tests (Linux) with a fake `IPathProbe` that reports every component `Missing` (the paths do not exist) | `Redaction/RenderGateTests` |
| `src/main/step-render.test.ts` | pins freshness (S3) and the caption flag (#62 gap 3); the fixture step has `flattened` and `markerBaked: true` (`:5-17`) | S3 group: blur added without PNG clears `flattened` and `markerBaked` (`:21-26`); crop changed without PNG clears `flattened` (`:28-32`); fresh PNG keeps `flattened` (`:34-38`); `reportZoom` patch keeps both (`:40-45`). Caption group: caption flags and is applied (`:49-56`); annotation save `{annotations, crop: null, markerBaked: true}` with PNG does not flag (`:58-69`); heading and body do not flag (`:71-75`); empty caption flags (`:77-81`); caption with fresh PNG still flags (`:83-89`) | ShotAI.Core.Tests (Linux) | `Rendering/StepPatchApplierTests` |

`flatten.ts`, `BlurRegion.tsx`, `Editor.tsx`, `annotations.ts` and `ocr.ts` have NO Electron tests. The native port adds them (8.2), starting from the macOS flatten tests, as the feasibility doc directs.

### 8.2 New tests the native code needs

**ShotAI.Core.Tests (Linux and Windows):**

| Class | Cases |
|---|---|
| `Rendering/FlattenerTests` | ports of `macOS:Packages/EditorKit/Tests/EditorKitTests/FlattenTests.swift`: `SolidRedactionIsOpaqueBlack` (`:47-56`), `ExtremeGeometryDoesNotThrowOverflow` (`:60-67`: crop 1e300, blur 1e300 and -1e300, block 1e300 must not throw; the macOS infinity case arrives natively as JSON `1e400`, which spec 01's reader turns into Infinity: an infinite CROP must not throw either, while an infinite BLUR geometry now fails closed with `E_UNBAKEABLE` under D-EDIT-2, and a `blockSize` of `1e400` bakes with the D-EDIT-26 cap), `MosaicDestroysHighContrastStripes` (`:69-91`: adjacent-row luminance difference < 60 and mid-tone), `PlainFlattenDoesNotFlip` (`:96-106`), `SolidRedactionLandsAtTop` (`:110-118`), `MosaicOrientationMatchesPlainPath` (`:124-139`), `SubPixelRedactionFailsClosed` (`:143-150`, message equals `E_UNBAKEABLE`), `RedactionEntirelyOutsideCropIsIgnored` (`:152-162`), `CropProducesExpectedDimensions` (`:166-173`), `ClickMarkerIsBaked` (`:175-189`, with a recording rasterizer asserting one `OverlayRing(50, 50, 20, #e11d48)`), `PlainFlattenPreservesTheImage` (`:191-199`). New: `RedactionOverlappingCropEdgeByUnderHalfPixelFailsClosed`; `HalfPixelCropRoundsLikeJavaScript`; `BlurIsBakedBeforeOverlays`; `PaintOrder`; `OverlappingBlurSamplesComposite` (pixelate over solid stays black); `MalformedBlurGeometryFailsClosed` (`x: "10"`, `width: -5`, missing `height`, `x: 1e400`); `MissingCornerRadiusDrawsWithRadiusZero`; `ZeroStrokeWidthDrawsNoStroke`; `HugeFontSizeSkipsTheItem`; `JunkElementFailsClosedInEnsureFlattened` (`null` and `42` give `UnknownAnnotation`); `ZeroSizeBlurIsSkipped`; `UnparseableCropFailsClosed`; `UnknownAnnotationTypeFailsClosed` (per Q-EDIT-2); `MarkerRadiusDefaultsFromFullImage`; `MarkerParamBeforeMarkerAnnotations`; `NonPositiveRadiusDrawsNothing`; `InvalidColorUsesAccent`; `TextNewlinesBecomeSpaces`; `EncodeFailureMessage` |
| `Rendering/RedactionBakerTests` | `SolidIsExactlyOpaqueBlack`; `PermutingPixelsWithinACellDoesNotChangeTheOutput`; `EqualCellAveragesGiveIdenticalOutput`; `BlockSizeIsFlooredAtEight`; `EffectiveBlockMatchesJavaScript` (`0`, `null`, `""`, `false`, absent: 12; `true`: 8; `"20"`: 20; `[]`: 8; `[25]`: 25; `"abc"`: 12 (IMPROVEMENT); `{}`: 12 (IMPROVEMENT); `7.5`: 8; `14.5`: 15; `" 20 "`: 20 (ECMAScript `StringToNumber` trims); `"0x10"`: 16; `1e400` (Infinity): Infinity, and `Pixelate` then uses a 1 by 1 tile); `SigmaTable` (block 8: 5, 14: 8, 60: 36 on a 100 by 100 region; block 1e300 on a 30 by 20 region: 30); `TileCellBoundaries` (w 17, sw 2 gives cells of 8 and 9 columns); `GaussianIsDeterministic` (two runs byte-equal); `RegionMatchesFlattenRounding` (x 10.5 width 20.4 gives [11, 31)); `PixelateNeverWritesOutsideRegion` |
| `Rendering/StepPatchApplierTests` | 8.1 cases plus `KeysAppendInPatchOrder` (a step without `markerColor` and `markerBaked` gains them in the 2.18 order after the existing keys); `CalloutNullRemovesKey`; `ExistingKeyKeepsPosition` |
| `Rendering/StepPatchValidatorTests` | `NonObjectThrows` (`42`, `null`, `"x"`: `patch must be an object`), `ArrayYieldsEmptyPatch`, each row of 2.18: kind whitelist, callout present and invalid clears, crop null and invalid gives null, click null and invalid gives null, markerColor `#abc` kept, `#abcdefgh1` ignored, `#abc\n` ignored (the `\z` rule), `red` ignored, markerBaked non-boolean ignored, reportZoom 0.5 gives 1, 9 gives 6, `1e400` (Infinity after spec 01's reader) ignored, `"2"` ignored, pan -1 gives 0, annotations keep only objects with string type and id (`AnnotationWithoutStringIdIsDropped`); `ParseClick`: button `'side'` gives `'left'`, radius 0 dropped, imageScale -1 dropped, extra key dropped, key order `global, image, button, radius, imageScale` |
| `Rendering/StepRenderWriterTests` (temp dir, `ManagedPathProbe`) | `WritesAtRenderPathAndSetsFlattened`; `BumpsRenderRevFromAbsentAndFromN` (absent gives 1, 4 gives 5); `RefusesNonSegmentIds` (D-EDIT-22: `../x`, `../../shots/abc`, `a/b`, `a\b`, `..`, `.`, `""`, `a:b` each throw the exact message and write nothing; note that Electron ACCEPTS `../x`, `../../shots/abc` and `a/b` after its join normalization, EDGE-EDIT-62, and refuses only `../../../evil`, `path-confine.test.ts:34-40`); `RefusesSymlinkedExportFolder` (Linux symlink); `RollbackRestoresPreviousBytes`; `RollbackDeletesWhenNoneExisted` |
| `Store/StoreRenderTests` | `UpdateWithoutPngInvalidates`; `MergeWithoutPngInvalidates`; `RenderWrittenBeforeManifest` (a failing fake manifest write sees the render already written, then the rollback restores it); `ManifestFailureRestoresPreviousRender`; `ManifestFailureDeletesRenderWhenNoneExisted`; `StepNotFoundMessage` |
| `Redaction/RenderGateTests` | 8.1 cases plus `RefusesLinkedRender` and `RefusesLinkedScreenshot` (fake probe reports `Link` on `export` or `shots`), `EmptyFlattenedIsAbsent`, `NonStringFlattenedIsAbsent`, `MalformedCropIsTruthy` (`crop: {}` refuses), `ZeroCropIsFalsy` (`crop: 0` allows the raw shot, JS parity), `ExtNameMatchesNode` (`shots/s` gives `.png` and `image/png`; `shots/S.JPEG` gives `image/jpeg`; `.hidden` gives `.png`), `VerbWordsAndExactMessages` |
| `Redaction/RenderGateMayRedactTests` | ports of `macOS:Packages/ShotModel/Tests/ShotModelTests/RenderFreshnessTests.swift:216-256`: `{"type":"blur"}` without geometry refuses; `{"type":"futurekind","x":1}` and `{"type":"redact","w":"wide"}` refuse; a baked render with the malformed blur is allowed; control: a well-formed arrow reads the raw shot. New: a junk element `42` refuses; `{"type":5}` refuses; a malformed arrow is allowed (native type-based rule) |
| `Rendering/StepFlattenerTests` (fake `IProjectService`, fake `IProjectSession`, fake codec, recording rasterizer) | `SkipsTextSteps`; `SkipsFlattenedAndMarkerBaked`; `RebakesWhenMarkerBakedFalsy`; `SkipsEmptyScreenshot`; `ShotNoCountsSkippedShots` (error names step 3 when the third shot fails after two skips); `ErrorWrapsWithCaptionOrUntitled`; `CancellationRethrownUnwrapped`; `LoadsScreenshotNotFlattened`; `MarkerUsesMarkerColorForAndRadius`; `PatchIsMarkerBakedOnly`; `ReturnsNullWhenNothingChanged`; `RefusesLinkedScreenshot` |
| `Redaction/SensitiveTextDetectorTests` | 8.1 cases plus ports of `macOS:Packages/EditorKit/Tests/EditorKitTests/RedactDetectTests.swift:15-57` (`SSN:` prefix word, `Card` prefix, `Order 4111111111111112` ignored, `sk-abcdefghijklmnop0123`, `AKIAIOSFODNN7EXAMPLE`, `ghp_0123456789abcdefghijklmnopqrstuvwx`, ordinary text, email and phone ignored, overlapping padded SSNs merge with pad 8). New: `SlackToken`, `LongHex40`, `LongBase64`, `Base64PaddingBoundaryMatchesJavaScript` (the match excludes `==` when followed by a space), `UnicodeDigitsDoNotMatch` (Arabic-Indic and full-width SSN), `CardWithDashes` (`4111-1111-1111-1111`), `CardTwelveDigitsIgnored`, `CardTwentyDigitsHandled` (JS regex behavior on a 20-digit run, asserted against a value computed once in Electron and recorded in the test), `PadAppliedAndNotClamped` (x0 2 gives x -2), `RunOrderSsnThenApiThenCard` (output order), `MergeIsOrderDependent` (a case where the result keeps two overlapping rects, values recorded from Electron), `TouchingRectsDoNotMerge`, `MatchCoversAllTouchedWords`, `EmptyLinesSkipped` |
| `Editor/AnnotationFactoryTests` | exact key order of each factory (serialize with spec 01's `JsJson` and compare strings); lowercase UUID ids; defaults |
| `Editor/AnnotationStyleTests` | the four formulas at 0, 700, 1000, 3000, 10000 px (for example `clickMarkerRadius(1920, 1080)` = 22, `defaultStrokeWidth(1920, 1080)` = 9, `defaultStampRadius(1920, 1080)` = 24, `defaultFontSize(1920, 1080)` = 24; clamps at the bounds); `MarkerColorFor` (set, right, left, no click) |
| `Editor/AnnotationEditsTests` | set existing key in place; new key appended; unknown keys preserved; a marker's first resize appends `radius` last |
| `Editor/TransformMathTests` | every row of the 2.7 commit table (rect, blur, crop, click, stamp, marker), the origin formula for a circle resized from the bottom-right corner, the proportional corner rule, the 8 DIP rejection, floors 4, 8, 6, arrow endpoint commit and revert below 6 |
| `Editor/EditorDocumentTests` | every row of the 2.6, 2.9 and 2.14 state tables and the 2.12 key table; `ApplyCropDoesNotChangeTheModel`; `CropToolKeepsZoom`; `ApplyCropResetsZoom`; `ReopenWithCropStartsCropped`; `StampNumberIsCountPlusOne`; `EmptyTextIsDroppedOnFinishAndOnSave`; `EscapeCommitsText`; `OpeningGuardAbsorbsOneBlur`; `AutoRedactAppendsSolidBlursAndSelectsLast`; `CancelDiscardsSuggestions`; `ReadOnlyWhileSaving`; `RightButtonDoesNothing`; `PressSelectsThenDrags`; `CropInteriorHitLast`; `VSelectsSelectTool`; `ZoomBoundsAndSteps` (0.5, 8, x1.25; labels `80%`, `64%`, `51%`, `50%`, then `63%` with `JsMath.Round`); `EscapeCancelsRectAndArrowDrafts` (both previews cleared, EDGE-EDIT-66); `ResetCropAndApplyCropClearCropSelection` (D-EDIT-23); `JunkAnnotationsAreNotHitOrDrawn` |
| `Editor/EditorSaveTests` (fake flattener and a fake `IProjectSession`) | `BakedListEqualsPersistedList` (a blur without an id is rekeyed on open; `null`, `42` and `{"type":5}` are absent from both the flatten request and the patch); `PrepareSaveNeverThrows` (a text with numeric `text`, a `null` element); `CancellationClearsSaving`; `NonHexMarkerColorNotWritten` (seeded `"red"` stays on disk); `PatchHasExactlyTheFiveKeys`; `RemovedMarkerPersistsNullClick`; `MovedMarkerKeepsGlobalButtonAndImageScale`; `ReportsSuccessOnlyAfterBothFilesAreWritten`; `FailureKeepsEditorOpenWithState`; `FlattensFromTheOriginal`; `UnknownAnnotationSurvivesSave`; `MarkerBakedTrueWithoutClick`; `MarkerColorAlwaysWritten`; `FailureNoticeUsesUserMessage` (a `FlattenException` shows its message verbatim, an unexpected exception shows `Something went wrong. See the log for details.`, ARCHITECTURE 8.2) |
| `Redaction/OcrLoggingTests` | `LogsCountsOnly` (capturing `ILogger`: no word text in any message) |
| `Redaction/SensitiveRegionScannerTests` (fake engine) | Ok with words gives rects; Unavailable and Failed give no rects and the status; cancellation propagates |

**ShotAI.Platform.Tests (Windows only):**

| Class | Cases |
|---|---|
| `Ocr/WindowsOcrEngineTests` | a checked-in fixture PNG (rendered text, no real data: `SSN 123-45-6789`, `Card 4111 1111 1111 1111`, `key sk-abcdEFGH1234567890wxyz`, and a benign line) yields at least one rect covering each sensitive string and none over the benign line; `ScalesDownAboveMaxDimension` (an image of `MaxImageDimension + 1000` wide still returns boxes in original coordinates, checked against the same text at native size within 4 px); `FailureReturnsStatusNotThrow` (a 0-byte-content bitmap); skipped with `Assert.Skip` when `TryCreateFromUserProfileLanguages` returns null on the runner |
| `Capture/RenderCodecTests` (tests `WicImageCodec`, so it mirrors `ShotAI.Platform.Capture`, ARCHITECTURE 2.4) | PNG round trip is lossless (premultiplied opaque pixels byte-equal); EXIF orientation 6 JPEG decodes rotated (width and height swapped); a transparent PNG keeps alpha through decode and encode; `RejectsNonPngJpegMagic` (a BMP, a GIF, a TIFF and a 7-byte PNG signature throw before any WIC decoder is created, R-ARCH-21); `DecodesByMagicNotExtension` (JPEG bytes in a `.png` file decode as JPEG); `UsesBuiltInDecoder` (the decoder created for PNG and for JPEG reports vendor `GUID_VendorMicrosoft`; corrected in WP-A17: the built-in decoders report `GUID_VendorMicrosoft` as their vendor, and `GUID_VendorMicrosoftBuiltIn` is only the preference `CreateDecoder` takes; a check against it refused every decode on both Windows runners) |
| `Rendering/RenderWriteJunctionTests` | `RefusesJunctionedExportFolder` (`mklink /J export <outside>`), nothing written outside |
| `Redaction/RenderGateJunctionTests` | a junctioned `shots` folder and a junctioned `export\.render` are refused by the gate with `has no readable screenshot` |

**ShotAI.App.Tests (Windows only, STA):**

| Class | Cases |
|---|---|
| `Editor/WpfOverlayRasterizerTests` | a ring of radius 20 at (50, 50) in `#e11d48` paints reddish pixels at (30, 50) and (70, 50) and a translucent interior; an arrow's tip pixel lies at most `2 * strokeWidth + 1` px past the geometric tip along the shaft (Konva miter parity); a rounded rect of radius 10 leaves the corner pixel unpainted; tiling for a 20000 by 5000 px scene joins without seams at tile edges in both axes (a diagonal line crossing four tiles is continuous); text renders with grayscale antialiasing (no pixel whose R, G and B differ by more than 2 for gray text, pinning the UNVERIFIED `RenderTargetBitmap` text mode) |
| `Editor/EditorKeyboardFocusTests` | with a focused Width slider, Delete removes the selected rect (D-EDIT-24); with the inline text box focused, Delete edits the text and the rect stays |
| `Editor/PainterParityTests` | `PreviewAndBakeProduceTheSamePixels`: the canvas rendered at scale 1 into a `RenderTargetBitmap` equals the overlay raster for the same annotations (text, stamp, rect, arrow, ring) |
| `Editor/EndToEndRedactionTests` | the phase C exit test: a synthetic screenshot whose redacted region holds a unique high-entropy pattern; after save, export to HTML and Markdown via 09 and a fake Claude request via 07: no image in any output contains a 4 by 4 block of original pixels from the region (exhaustive search), and every region pixel of a solid redaction is opaque black |
| `Editor/EditorPointerMappingTests` | clicks at known DIP positions at zoom 1, 2 and 0.5, in the full and cropped views, with the canvas scrolled, map to the expected image px (INV-EDIT-14) |
| `Editor/ViewportStabilityTests` | growing the image past the viewport height at width fit does not change `ViewportWidth` and the layout settles within two passes (EDGE-EDIT-1) |
| `Editor/InlineTextBoxTests` | the text box sits at the text's position and moves with scroll; Enter and Escape commit; the opening guard absorbs one focus loss |

---

## 9. Acceptance criteria

**AC-EDIT-1.** `Editor/EditorGeometryTests`, `Redaction/SensitiveTextDetectorTests`, `Redaction/RenderGateTests` and `Rendering/StepPatchApplierTests` contain every case of the four Electron test files (8.1) and pass on Linux and Windows.

**AC-EDIT-2.** Every class in 8.2's ShotAI.Core.Tests table passes on the Linux CI job.

**AC-EDIT-3.** `Rendering/FlattenerTests` contains all eleven ported macOS flatten cases and passes.

**AC-EDIT-4.** `RedactionBakerTests.PermutingPixelsWithinACellDoesNotChangeTheOutput` and `EqualCellAveragesGiveIdenticalOutput` pass for block sizes 8, 14, 60 and region sizes 1 by 1, 7 by 3, 100 by 37 and 1000 by 1000.

**AC-EDIT-5.** `EndToEndRedactionTests` passes on the Windows runner: no exported or sent image contains original pixels from a redacted region (the phase C exit test of the feasibility doc).

**AC-EDIT-6.** A step whose blur is added and then the render invalidated by a direct `UpdateStepAsync(patch with annotations, no PNG)` call is refused by both `RenderGate` verbs with the exact 2.20 message, and `EnsureFlattenedAsync` re-bakes it before egress (manual variant: hand-edit `flattened` to null in `project.json` with a blur present, export HTML: refused with the message, no file written).

**AC-EDIT-7.** Opening, editing and saving a step natively writes `export\.render\<id>.png`, sets `flattened` to `export/.render/<id>.png` (forward slashes) and increments `renderRev` by exactly one; the report shows the new render without restarting.

**AC-EDIT-8.** A project saved by the native editor opens in Electron v1.3.0 and in the macOS app with the same annotations, crop, click and marker color, and re-saving it in Electron succeeds (cross-app manual check).

**AC-EDIT-9.** Annotations created natively serialize with the exact key order of 2.1 (`AnnotationFactoryTests`), and a Box, Arrow, Redact, Number, Marker and Text created in both apps on the same screenshot at the same points produce byte-identical annotation JSON apart from `id`.

**AC-EDIT-10.** Manual visual parity: the same project flattened by Electron and by native, compared side by side at 100%, shows the same crop, the same redaction regions (edges within 1 px), rings of the same size and color, arrows whose tips coincide within 2 px, and text of the same size at a vertical offset of at most `0.15 * fontSize`.

**AC-EDIT-11.** Every string in 2.22 marked REQUIRED appears verbatim in the native UI or error path (a string-table test compares the XAML resources and exception messages with a checked-in list; non-ASCII escapes decoded).

**AC-EDIT-12.** Tool behavior: for each row of the 2.6 table, a manual run (and `EditorDocumentTests`) produces the stated result; a drag shorter than 6 image px creates nothing and keeps the tool; every successful creation drops to Select with the new shape selected.

**AC-EDIT-13.** Transform behavior matches the 2.7 table in `TransformMathTests`; manually, a stamp resized from a corner stays round and a vertical arrow can be lengthened by its endpoint.

**AC-EDIT-14.** Keyboard: Delete and Backspace remove the selection (marker, crop or annotation); Escape deselects and selects the Select tool without closing the editor; `V` selects the Select tool; none of these fire while typing in the text box.

**AC-EDIT-15.** Text: placing a text and typing shows the text live at the click point; Enter, Escape, clicking elsewhere, switching tools and Save all commit; an empty text disappears; double-click and `Edit text` reopen it; the text box stays aligned while scrolling.

**AC-EDIT-16.** View: a 400 px wide screenshot fills the canvas width at 100%; a tall screenshot is width-fit and scrolls vertically; the image never pulses while zooming (`ViewportStabilityTests` and a manual zoom sweep from 50% to 800%).

**AC-EDIT-17.** A reopened step with a crop opens in the cropped view; `Apply crop` / `Show full` toggle and reset the zoom to 100%; choosing the Crop tool shows the full image and keeps the zoom.

**AC-EDIT-18.** Save with a 0.4 px wide blur inside the image fails with `A redaction region could not be applied (too small or off-image). Adjust or remove it, then save again.` and the editor stays open with the blur present.

**AC-EDIT-19.** Save completes (editor closes, report updated) only after `project.json` and the render are both on disk; killing the process during the render write leaves either the previous render or the new one intact (never a truncated PNG), and a simulated manifest write failure leaves the previous render bytes in place (`StoreRenderTests`).

**AC-EDIT-20.** A step with a junctioned `shots` folder: the editor refuses to open it (`Could not load the screenshot.`), `EnsureFlattenedAsync` fails with the step named, and the gate refuses it (`RenderGateJunctionTests`).

**AC-EDIT-21.** Auto-redact on the Windows OCR fixture adds one solid redaction over each of the SSN, card and API key, selects the last, and a clean screenshot shows `No sensitive data detected (best-effort \u2014 redact manually if needed).`; nothing is saved until Save.

**AC-EDIT-22.** Auto-redact on a machine without an OCR recognizer shows the Unavailable notice of 7.9 and does not throw; editing and saving still work.

**AC-EDIT-23.** OCR logs contain only counts and the file name (`OcrLoggingTests`); a manual review of the `ocr` log after scanning the fixture shows no recognized text.

**AC-EDIT-24.** The detector output for 50 recorded OCR line sets (words and boxes captured from Electron's Tesseract on real-looking synthetic screenshots, checked in as JSON) equals Electron's `detectSensitiveRects` output exactly (a golden test; the recorder is a small Electron-side script run once, Q-EDIT-15).

**AC-EDIT-25.** `EnsureFlattenedAsync` runs before every SOP estimate and generation, every in-project export, every package export and every Home export (06, 07, 09 call it; an integration test per entry point with a fake flattener asserts the call precedes the egress call).

**AC-EDIT-26.** A merge (05) of a right-click step into the next step writes one render with both rings baked, `markerBaked: true`, and the dropped step removed, in one queue job; a direct `MergeStepsAsync` with annotations and no PNG invalidates the kept step.

**AC-EDIT-27.** `markerBaked` and the report overlay: after a native save of a step with a click, the report shows exactly one ring (baked); after invalidation, the report shows the overlay ring until the next bake.

**AC-EDIT-28.** Saving and flattening a 7680 by 2160 screenshot with ten pixelate redactions completes in under 2 seconds on the reference x64 machine and under 4 seconds on the ARM64 dev machine (measured in `FlattenerPerfTests`, not a CI gate).

**AC-EDIT-29.** The editor is fully keyboard-reachable (Tab through rail, bars and buttons) and every tool button exposes its label as its UI Automation name (Accessibility Insights check).

**AC-EDIT-30.** A hand-edited blur with `blockSize: "abc"` bakes as a block-12 mosaic, and one with `width: "50"` fails the save with the unbakeable message (D-EDIT-2), while Electron's behavior for the same file is recorded in the test comment.

**AC-EDIT-31.** Unknown annotation types in a project (`{"type":"futurekind","id":"f1"}`) survive a native editor save verbatim if Q-EDIT-2 resolves to "skip"; with the recommended default the save is refused with the `UnknownAnnotation` message and the file is untouched. Either way the gate refuses the raw screenshot when no render exists.

**AC-EDIT-32.** A step whose id is hand-edited to `../../shots/abc` (or `a/b`) is refused by a native editor save with the exact `refusing to write render for step ...` message, and no file under the project folder changes (`StepRenderWriterTests.RefusesNonSegmentIds`, D-EDIT-22).

**AC-EDIT-33.** A step whose annotations hold a blur without an `id`, a `null` and a `42` opens in the native editor without error; after Save, the render and `project.json` contain the same annotation list (the blur with its new id, the junk gone), and the blur's region is baked (`EditorSaveTests.BakedListEqualsPersistedList`, INV-EDIT-32).

**AC-EDIT-34.** Pressing Escape during a rect drag and during an arrow drag leaves no shape and no preview on the canvas (EDGE-EDIT-66).

**AC-EDIT-35.** A step whose `shots/` file holds bytes that are neither PNG nor JPEG by their magic bytes (a BMP renamed `.png`) is refused everywhere this subsystem decodes: the editor shows `Could not load the screenshot.` with Save and Auto-redact disabled, `EnsureFlattenedAsync` fails with the step named, and no WIC decoder is created for it; PNG and JPEG decode only through the built-in decoders (`RenderCodecTests.RejectsNonPngJpegMagic`, `UsesBuiltInDecoder`; R-ARCH-21, D-EDIT-27).

---

## 10. Interfaces with other subsystems

| Spec | This subsystem consumes | This subsystem provides |
|---|---|---|
| 01 Model and store | `ProjectStep` views (`Raw`, `Annotations`, `Crop`, `Click`, `Flattened`, `RenderRev`, `MarkerBaked`, `MarkerColor`), `StepClick`, `Rect`, `Point`, `StepGeometry.ParseRect`, `JsMath`, `JsNumber`, `JsString`, `JsJson`, `PathConfine.Confine`/`ConfineNoLinks`, `IPathProbe`, `AtomicFile`, `IProjectService.UpdateStepAsync`/`MergeStepsAsync`/`GetProjectForReadAsync` (never the concrete `ProjectStore`, R-ARCH-4), `IProjectSession.ApplyDurable` (sessions from `IProjectSessionFactory.Create`, R-ARCH-5), `IProjectSettle` (for the egress callers, R-ARCH-6), `StepNotFoundException`, the magic-byte rule of `detectImage` | `StepPatch`, `Optional<T>`, `StepPatchValidator`, `StepPatchApplier.ApplyAndInvalidate`, `IStepRenderWriter` and `RenderWriteReceipt` (the rollback hook the store job calls), the render path constant |
| 02 Capture | `click.image` in stored-PNG px (after downscale), `click.imageScale`, `click.button`; `WicImageCodec` | `IRenderCodec` requirements (magic-byte check and explicit built-in decoder per R-ARCH-21, orientation, premultiplied, sRGB) added to `WicImageCodec` |
| 03 Windows and shell | the project window's overlay layer (R-ARCH-19), focus scope, the shared notice control, `HwndSource` access for `WM_MOUSEHWHEEL`, the owner HWND for the color dialog, `OwnWindowRegistry` for the color dialog's HWND (Q-EDIT-21); `StaRenderThread` needs no exit hook (the container disposes it, ARCHITECTURE 4.5 step 5) | `EditorOverlayView`; the rule that the editor cannot be closed while saving |
| 05 Report | the Edit entry point (`OpenEditor(step)`, which passes the view's `IProjectSession` to `EditorFactory.Create`, R-ARCH-5), the merge flow (origin recovery, captions) | `IStepFlattener.FlattenStepAsync` for merges, `AnnotationStyle.MarkerColorFor`, `AnnotationFactory.Marker`, the `markerBaked` overlay rule, the image cache key `(flattened, renderRev)` |
| 06 Home | row and bulk export flows | `IStepFlattener.EnsureFlattenedAsync` (without a session: direct store) |
| 07 SOP | the review-before-send flow and cancel token | `EnsureFlattenedAsync` (session overload, `IProjectSession`) before estimate and send; `RenderGate.ResolveSendableRender(..., EgressVerb.Send, probe)` as the only image source; `RenderGateException` with its `(string, Exception)` constructor (R-ARCH-8, D-SOP-22); the refusal messages |
| 09 Export | export flows, package safe mode | `EnsureFlattenedAsync` (session overload inside an open project, direct-store overload from Home), after which 09 awaits `IProjectSettle.WhenSettledAsync` (R-ARCH-6); `IRenderGate.Resolve(..., EgressVerb.Export)` (the DI wrapper over `RenderGate`); `RenderGateException`, the one name (R-ARCH-8; `RenderRefusedException` is superseded); the rule that exports never flatten themselves |
| 10 Settings and infra | `ILogger` categories by namespace (10 7.5.3): `ocr` for `ShotAI.Core.Redaction` and `ShotAI.Platform.Ocr`, `main` for the editor, rendering and imaging namespaces; theme tokens for the editor chrome | log lines of 2.23 and 7.8 |
| 11 Service boundary | `ShotAIException` and `UserMessage.From` (`ShotAI.Core.Errors`); the banned-symbol allowlist entries `StaRenderThread.cs` and `UiDeferral.cs` (R-ARCH-18) | the deletion of `projects:update-step`, `projects:merge-steps`, `projects:redact-scan` (replaced by direct calls); `StepPatchValidator.Parse` as the validation any remaining untrusted-patch entry point must use; `ISensitiveRegionScanner` (`ShotAI.Core.Redaction`) and `IStepFlattener` (`ShotAI.Core.Rendering`, R-ARCH-7) |
| 12 Packaging and CI | Linux job for ShotAI.Core.Tests; Windows job with an OCR recognizer installed and junction creation allowed; the WPF STA test host | test projects `ShotAI.Platform.Tests`, `ShotAI.App.Tests` cases; the OCR Feature on Demand deployment note; removal of `vendor/tessdata` and `tesseract.js` |

---

## 11. Open questions and risks

**Q-EDIT-1.** Editor marker color default: Electron seeds `step.markerColor ?? ACCENT`, so saving a right-click step flips its report ring from blue to rose (EDGE-EDIT-23); macOS uses `markerColorFor`. Recommended default: IMPROVEMENT, seed with `AnnotationStyle.MarkerColorFor(step)` so the editor, report and bake agree; note it in the release notes.

**Q-EDIT-2.** Flatten on an unknown annotation type: fail closed (refuse to save) or skip it and write a render. Failing closed matches the #109 reasoning but blocks saving a step that a newer build annotated. Recommended default: fail closed with the `UnknownAnnotation` message; revisit when a new annotation type is actually introduced (a new type should ship on every platform at once).

**Q-EDIT-3.** Pixel parity of the mosaic, Gaussian and text with Electron is visual, not byte-exact (different samplers, different text engines). Recommended default: accept; AC-EDIT-10 is the bar, and the security tests (INV-EDIT-2, INV-EDIT-3) are exact.

**Q-EDIT-4.** Text vertical metrics: WPF places `FormattedText` by the top of its line box, Chromium's `textBaseline: 'top'` by the em box, so baked text may sit a few px lower than in Electron. Recommended default: accept the WPF line-box placement (preview equals bake natively); if pilot users notice text shifting on projects edited in both apps, compute the em-box offset from `GlyphTypeface` ascent and descent and add it.

**Q-EDIT-5.** OCR availability and notice wording. The en-US recognizer is present on en-US installs; other SKUs may lack it. Recommended default: prefer en-US, then profile languages; show the two new notices of 7.9; 12 documents the Feature on Demand name for IT deployment. Final wording owned by 05's notice conventions.

**Q-EDIT-6.** Windows OCR accuracy on small UI text differs from Tesseract; small screenshots (downscaled at capture by `captureScale`) may miss matches Tesseract caught or vice versa. Recommended default: no upscaling in 2.0; measure detection recall on a synthetic corpus during phase C and upscale by 2 when the image's short side is under 800 px if recall is lower than Electron's.

**Q-EDIT-7.** Outward rounding of the redaction region (`floor` of the start, `ceil` of the end) would cover sub-pixel slivers the preview shows as covered, but changes the fail-closed behavior shared with Electron and macOS (a 0.4 px blur would bake instead of failing). Recommended default: keep parity rounding in 2.0; propose outward rounding to both other apps together.

**Q-EDIT-8.** Versioned render file names (`export/.render/<id>-<rev>.png`) would make the render and manifest writes a single commit without rollback logic, but create orphans for Electron and macOS, which always write `<id>.png`. Recommended default: keep `<id>.png` with rollback (D-EDIT-5); revisit after cutover.

**Q-EDIT-9.** Undo and redo. Electron has none. Recommended default: none in 2.0 (parity); `EditorDocument` keeps every mutation behind methods, so a later command stack is additive.

**Q-EDIT-10.** Merging sets `captionEditedByUser` on the kept step (EDGE-EDIT-22), contrary to the intent recorded in `207eddd`. Recommended default: parity in 2.0; raise with 07 whether a merged caption counts as a human edit, and fix on all platforms together.

**Q-EDIT-11.** Hit-test order change for the crop interior (D-EDIT-14) changes muscle memory for users who drag the crop by its middle while annotations sit under it. Recommended default: adopt it; a crop can still be moved by its border and handles, and by its interior wherever no annotation is.

**Q-EDIT-12.** The brief for this spec, and the original design note `PHASE-3-PLAN.md:122` ("13\u201319 digits passing the **Luhn** check + issuer prefixes", which also lists SSN "spacing variants"), mention issuer prefixes for card detection; the shipped Electron code has neither (any Luhn-valid 13 to 19 digit run; SSN only as `ddd-dd-dddd`). Recommended default: parity (no prefixes), because adding them would miss cards Electron catches.

**Q-EDIT-13.** macOS hides the Auto-redact button by design; Windows shows it. Recommended default: keep it visible natively (Windows parity); the decision is per platform.

**Q-EDIT-14.** Arrow endpoint handles (D-EDIT-15) remove the Shift and Alt box scaling for arrows. Recommended default: adopt; box scaling of an arrow was rarely meaningful and broke for vertical arrows.

**Q-EDIT-15.** The golden detector corpus (AC-EDIT-24) and the arrow and text parity values need a one-time recording in Electron. Recommended default: add a small Electron-side vitest in the same PR as the native detector that writes the JSON goldens into `dotnet/tests/ShotAI.Core.Tests/Golden/redact/` only when an environment variable is set (the pattern spec 01 Q-MODEL-18 uses), with synthetic, non-real data only (this repo is public).

**Q-EDIT-16.** `RenderTargetBitmap` rasterization on the ARM64 dev VM runs in WPF software rendering; very large scenes may be slow. Risk: AC-EDIT-28. Recommended default: band large scenes (7.10.6) and measure in phase C; if too slow, rasterize only the bounding boxes of overlay items and composite them individually.

**Q-EDIT-17.** The fixed decision for OCR is Windows.Media.Ocr; its `RecognizeAsync` cannot be cancelled mid-operation. Risk: a scan on a huge image keeps a thread busy after the editor closes. Recommended default: accept (one scan at a time, result discarded); the engine semaphore prevents pile-up.

**Q-EDIT-18.** Konva-specific interaction details (8 anchors, Shift and Alt semantics, 3 px drag threshold, 400 ms double-click) are ported as described; WPF's system double-click time may differ from 400 ms. Recommended default: use the system double-click time (`GetDoubleClickTime`, via `e.ClickCount`), a Windows convention, and keep the other values.

**Q-EDIT-19.** Windows OCR word segmentation versus Tesseract's: if `Windows.Media.Ocr` splits hyphenated or underscored tokens (`123-45-6789`, `sk-...`, `ghp_...`, `4111-1111-1111-1111`) into several `OcrWord`s, the space-joined line text no longer matches `SSN` or the API patterns and those secrets are missed (UNVERIFIED; the detector is a best-effort assist, the manual gate is the guarantee). Recommended default: measure it with the `WindowsOcrEngineTests` fixture in phase C; if splitting occurs, join adjacent words of one `OcrLine` WITHOUT a space when the gap between their boxes is under 0.25 of the line height before calling `SensitiveTextDetector.Detect` (a native IMPROVEMENT that keeps the detector itself a 1:1 port), and record the rule here.

**Q-EDIT-20.** Cross-spec names to align (this spec is the owner): spec 09 injected `IRenderGate` and caught `RenderRefusedException`, spec 07 called `RenderGate.ResolveSendableRender` and caught `RenderGateException`; spec 11 placed `IStepFlattener` in `ShotAI.Core.Editor`; specs 05 and 07 called a four-argument `EnsureFlattenedAsync` while persisting through the session; spec 05 EDGE-REP-40 cited the codec as `ExifOrientationMode.RespectExifOrientation` (WinRT). Resolved by R-ARCH-7 and R-ARCH-8 (with R-ARCH-5 and R-ARCH-21): `IStepFlattener` lives in `ShotAI.Core.Rendering` (R-ARCH-7); the one exception is `RenderGateException`, deriving from `ShotAIException` with a `(string, Exception)` constructor, 09 injects `IRenderGate` and 07 calls the static `RenderGate` with its probe (R-ARCH-8); every caller inside an open project uses the session overload of `EnsureFlattenedAsync`, which takes `IProjectSession` (R-ARCH-5); every decode goes through spec 02's WIC COM codec with the explicit built-in decoder after the magic-byte check, never a WinRT or sniffing decoder (R-ARCH-21, 7.13). `ISensitiveRegionScanner` keeps its name in `ShotAI.Core.Redaction`. The texts of 05, 07, 09 and 11 are aligned in PLAN WP-C7.

**Q-EDIT-21.** The Color control opens the system `ChooseColor` dialog (7.10.1), an HWND of shotAI's process that no `ShotAIWindow`, `ShotAIPopup` or `PopupExclusion` handler registers, so while the editor is open during a screen share with remote visibility off it could appear in a remote viewer's capture (INV-CAP-7, ARCHITECTURE 9.2 S6). Recommended default: open the dialog with `CC_ENABLEHOOK` and a hook procedure that registers its HWND with 03's `OwnWindowRegistry` on `WM_INITDIALOG` (before it is first shown), and pin it with a case in 03's `AllWindowsRegisteredTests`; the same question applies to the file dialogs behind `IFileDialogs` and `IExportDialogs` (11, 09). PLAN assigns the color dialog to WP-C9 and the file dialogs to the WPs that add them: WP-B10 (`IFileDialogs`, 11 7.3.5, the projects folder picker) and WP-D10 (`IExportDialogs`, 09, the save dialogs), each giving its dialog HWND the equivalent `OwnWindowRegistry` registration before first show and a matching case in `AllWindowsRegisteredTests` (PLAN Q-EDIT-21 row). Specs 11 and 09 own the statement of that registration for their dialogs. Updated in WP-A13: 03's `PopupExclusion` installs a show hook that registers every top-level window the UI thread shows before it is visible (03 7.4.7, Q-SHELL-3), so the dialog is registered without a registration of its own; the `CC_ENABLEHOOK` registration may stay as a second layer, and the `AllWindowsRegisteredTests` case still pins the dialog.
