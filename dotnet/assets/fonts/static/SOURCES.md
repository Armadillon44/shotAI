# Archivo static instances

The WPF app renders the LFI brand's text from these files (docs/native/spec/06-home-settings-ui.md
Q-HOME-2, docs/native/spec/10-brand-settings-infra.md 7.9 and Q-INFRA-19). WPF does not drive a
variable font's `wght` and `wdth` axes from `FontWeight` and `FontStretch`, so the variable
`Archivo.ttf` that Electron and the PDF print page use would render its default instance,
SemiBold, everywhere.

## Source

- Repository: https://github.com/Omnibus-Type/Archivo (SIL Open Font License 1.1, no Reserved Font Name).
- Commit: `555fa4a86de865637a2e5c35123ddb8972333467` (2021-01-28, "Ready for GF (#18)"), folder `fonts/ttf/`.
  The repository has no release tags. This is the commit whose `fonts/variable/Archivo[wdth,wght].ttf`
  is byte-identical to the bundled `src/renderer/fonts/Archivo.ttf` (sha256 `0e094a7d...b05053`),
  so the static and variable faces are one build, `head.fontRevision` 2.001.
- The files are unmodified. The later upstream commit `b5d6398` rebuilt them with only
  `head.modified` and the checksum changed; these are the earlier bytes, from the same commit as
  the variable face.
- `OFL.txt` is the repository's licence at that commit, byte-identical to `src/renderer/fonts/OFL.txt`.
  It stays in this folder and beside every installed copy (INV-INFRA-31).

## Files

| File | Weight (`usWeightClass`) | Width (`usWidthClass`) | Family (name ID 16, else 1) | sha256 |
|---|---|---|---|---|
| `Archivo-Regular.ttf` | 400 | 5 (normal) | Archivo | `78cba454bbad357e24040ce832a237c9b0c0024d84030735363689c17e259e32` |
| `Archivo-Medium.ttf` | 500 | 5 | Archivo | `0aabf6e660fdc2d7b803dafba63c00a5921bedc391d91d5f6eeceb1dcb33ad40` |
| `Archivo-SemiBold.ttf` | 600 | 5 | Archivo | `f21316d36f259d620a6ad06261cc7b5459fd33d49af518c89321007b43489783` |
| `Archivo-Bold.ttf` | 700 | 5 | Archivo | `bea0a163d32a5e7f740d53de3d41b7f843bdc01d404ee8bd563f7e5bc408b92a` |
| `Archivo-ExtraBold.ttf` | 800 | 5 | Archivo | `454e0954bba5c1c50031d45ded53c08d60caebe61f88a72ae4e4f2c2b81fdb49` |
| `ArchivoExtraCondensed-SemiBold.ttf` | 600 | 2 (extra-condensed, `wdth` 62) | Archivo ExtraCondensed | `215679ec838f401705ce5b90d9b7705cb2d4b5ffb692c810fd7b04101dff1f54` |
| `ArchivoExtraCondensed-Bold.ttf` | 680 | 2 | Archivo ExtraCondensed | `f298078929564a4bbfafb213bdd3cfbcd9c1b49559a98fac0821ed926d454488` |
| `OFL.txt` | | | | `108b4e57c9c796d3d38d0428ca7ee39de47ad93187302718d9b2d8864b9b716b` |

Two facts of the upstream files that the app's font stack accounts for:

- The `wdth` 62 files name their own family, `Archivo ExtraCondensed`, so the label stack names that
  family before `Archivo` (06 7.5, `Font.label-stack`).
- `ArchivoExtraCondensed-Bold.ttf` declares weight 680, not 700; a Bold request still selects it,
  the nearest of the two condensed weights.

`FontPackagingTests` checks these hashes against the files and the build output.
