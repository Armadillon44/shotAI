import { defineConfig } from 'vitest/config';

// Unit tests for pure logic (path confinement, redaction detection, the
// render-gate, capture geometry, validators, marker color). Tests live next to
// the code as *.test.ts. Modules under test should stay import-clean (no
// electron / native deps) so they run under plain node; when a test must touch
// an electron-importing module, add an alias stub here.
export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
