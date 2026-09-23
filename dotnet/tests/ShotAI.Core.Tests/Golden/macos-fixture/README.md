# macOS fixture project

`b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c/` is a byte-identical copy of the folder
`Fixtures/b7e2c4d1-9f3a-4e8b-a2c5-6d1f8e9a0b3c/` in `Armadillon44/shotAI_MacOS` at commit
`f445bca` (`f445bca2b9ccb5bf5f8c8d867939bd55e1e43b57`). It is a project the macOS app wrote,
kept here because that repository is read-only to this one (spec 01 Q-MODEL-14). It is not
in `contract/`, which must stay byte-identical across the two repositories.

Everything in it is synthetic: "Acme ERP", its customers and their `.example` addresses are
made up.

| File | Bytes | SHA-256 |
|---|---|---|
| `export/.render/9a1b3c5d-7e2f-4b8a-9c1d-2e4f6a8b0c3e.png` | 107675 | `b71facae45c96ac6a495e8759c7e2f9097de6107e86976a98eb0d85646b6d668` |
| `project.json` | 5978 | `815fe274e76dd574d79a4a80521b5bf841c031efba5997b7ee5d9d57f824a3d8` |
| `shots/step-0001.png` | 213677 | `b87efc3ea77c12a097fc780cf74577cf0e4b71235d6662f714132e386d11f8f3` |
| `shots/step-0002.png` | 232809 | `b77d4ff82fff945063ef0b67e25e3bb18dbb5ebcd321274f5a8a2e53ac3334a2` |
| `shots/step-0003.png` | 213677 | `b87efc3ea77c12a097fc780cf74577cf0e4b71235d6662f714132e386d11f8f3` |

Used by `Codec/ElectronGoldenTests`: the native codec's output for `project.json` must equal
Electron's, `../codec/expected/macos-fixture.json` (AC-MODEL-3), and must keep every step's
`note` and every annotation (AC-MODEL-6).

Do not edit these files. To refresh them, copy the folder again from the macOS repository,
record the new commit and hashes here, and regenerate the Electron outputs
(`../codec/README.md`).
