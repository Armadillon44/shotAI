import { describe, it, expect } from 'vitest';
import path from 'node:path';
import { confinePath } from './path-confine';

// Use a platform-native base dir so path.resolve/relative behave as in prod.
const DIR = process.platform === 'win32' ? 'C:\\projects\\abc' : '/projects/abc';

describe('confinePath', () => {
  it('accepts an in-folder relative path and returns it resolved', () => {
    const abs = confinePath(DIR, 'shots/step-0001.png');
    expect(abs).toBe(path.resolve(DIR, 'shots/step-0001.png'));
  });

  it('accepts a nested in-folder path', () => {
    expect(confinePath(DIR, 'export/.render/x.png')).toBe(
      path.resolve(DIR, 'export/.render/x.png'),
    );
  });

  it.each([
    ['parent escape (posix)', '../evil.png'],
    ['deep parent escape', '../../../../etc/passwd'],
    ['dot-dot mid-path', 'shots/../../evil.png'],
    ['the folder root itself', '.'],
  ])('rejects %s', (_label, rel) => {
    expect(confinePath(DIR, rel)).toBeNull();
  });

  it('rejects an absolute path', () => {
    const abs = process.platform === 'win32' ? 'C:\\Windows\\system32\\x.png' : '/etc/passwd';
    expect(confinePath(DIR, abs)).toBeNull();
  });

  it('rejects a traversal id used as a render filename (S5 vector)', () => {
    // mergeSteps/updateStep build `export/.render/<id>.png`; a hand-edited
    // manifest id with traversal segments must not escape the project folder.
    const stepId = '../../../evil';
    const rel = path.posix.join('export', '.render', `${stepId}.png`);
    expect(confinePath(DIR, rel)).toBeNull();
  });
});
