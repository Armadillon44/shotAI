# Codec goldens

`expected/<name>.json` is what Electron writes for one input: the bytes of
`JSON.stringify(coerceManifest(JSON.parse(text), name), null, 2)`, the `readManifest` then
`writeManifest` path of `src/main/project-store.ts`. The fallback title is the golden's name.
`Codec/ElectronGoldenTests` checks that `ManifestCodec` writes the same bytes for the same
input (AC-MODEL-3, spec 01 8.2).

The inputs:

| Golden | Input |
|---|---|
| `<name>` | `inputs/<name>.json`, hand-written edge cases, all synthetic |
| `macos-fixture` | `../macos-fixture/b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c/project.json` (see its README) |
| `conformance-<case>` | the `input` of `contract/conformance/manifest/<case>.json`, read in place, never copied |

`inputs/invalid-utf8.json` holds invalid UTF-8 bytes on purpose; both apps decode them to
U+FFFD before parsing.

## Regenerate

From the repository root, after `npm ci --ignore-scripts` (with `ELECTRON_SKIP_BINARY_DOWNLOAD=1`):

```
SHOTAI_CODEC_GOLDENS=1 npx vitest run src/main/codec-golden.test.ts
```

The generator, `src/main/codec-golden.test.ts`, writes only `expected/`; without the
variable it is skipped and writes nothing. Commit the outputs together with the input
change. A native failure after regenerating is a codec divergence to fix, never a golden
to edit by hand.
