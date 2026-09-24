# Settings goldens

`expected/<name>.json` is what Electron writes for one settings file: the bytes on disk after
`src/main/settings.ts` runs one write over it, `setLastUpdateCheckAt(1790000000000)` (the
startup update check's stamp), through its real `load`, `mutate` and `save` chain.
`Settings/SettingsGoldenTests` checks that `SettingsCodec` writes the same bytes for the same
file (spec 10 7.4.2, INV-INFRA-14, INV-INFRA-15).

The inputs:

| Golden | Input |
|---|---|
| `fresh` | no `settings.json` at all: the first write on a new machine |
| `<name>` | `inputs/<name>.json`, hand-written edge cases, all synthetic |

The generator fixes the home folder at `/shotai-home`, so every default projects folder is
`/shotai-home/shotAI Projects`, and the tests pass that string as the default. No input holds
a string `projectsDir`, whose native rule differs by design (Q-INFRA-3). Every input is a case
where the two apps agree; the IMPROVEMENTs (a BOM, unrecognised enum strings, unknown keys
inside `sop`) are tested in `Settings/SettingsCodecTests` instead.

## Regenerate

From the repository root, after `npm ci --ignore-scripts` (with `ELECTRON_SKIP_BINARY_DOWNLOAD=1`):

```
SHOTAI_SETTINGS_GOLDENS=1 npx vitest run src/main/settings-golden.test.ts
```

The generator, `src/main/settings-golden.test.ts`, writes only `expected/`; without the
variable it is skipped and writes nothing. Commit the outputs together with the input change.
A native failure after regenerating is a codec divergence to fix, never a golden to edit by
hand.
