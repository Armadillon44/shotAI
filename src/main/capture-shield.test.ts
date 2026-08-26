import { describe, it, expect, vi, beforeEach } from 'vitest';
import fs from 'node:fs';

// Two separate concerns, both about the same leak.
//
// 1. The ROUTING invariant. With remoteVisible on, shotAI's windows are
//    deliberately capturable so a remote viewer can see them, and the recording
//    pill is on screen for the whole recording. One unshielded grab therefore puts
//    the pill into that screenshot. There are seven grab sites and nothing in the
//    type system says a new one must be shielded, so it is asserted from source,
//    the same way admx-contract and federation-cache-wiring are.
//
// 2. The RESTORE value, which is the part that is easy to get subtly wrong: after
//    a grab, protection must go back to what the SETTING says, not to a constant.
//    Restoring to "on" unconditionally would silently disable the whole feature
//    after the first capture; restoring to "off" unconditionally would leave a
//    protection-on user exposed.

const SRC = fs.readFileSync('src/main/CaptureController.ts', 'utf8');

/** Index ranges of the two sanctioned helper bodies. */
function helperRanges(): Array<[number, number]> {
  const out: Array<[number, number]> = [];
  for (const sig of [
    'async function grabMonitor(mon: NsMonitor): Promise<NsImage> {',
    'function grabMonitorSync(mon: NsMonitor): NsImage {',
  ]) {
    const start = SRC.indexOf(sig);
    expect(start, `helper missing: ${sig}`).toBeGreaterThan(-1);
    const end = SRC.indexOf('\n}', start);
    expect(end).toBeGreaterThan(start);
    out.push([start, end]);
  }
  return out;
}

describe('every screen grab is shielded', () => {
  it('has both sanctioned helpers, and both take the shield', () => {
    for (const [start, end] of helperRanges()) {
      expect(SRC.slice(start, end)).toContain('shieldOwnWindows()');
      // try/finally, not a bare call: a throwing grab must still restore, or the
      // app is left permanently invisible to the remote viewer.
      expect(SRC.slice(start, end)).toContain('finally');
      expect(SRC.slice(start, end)).toContain('release()');
    }
  });

  it('calls captureImage/captureImageSync ONLY inside those helpers', () => {
    const ranges = helperRanges();
    const offenders: string[] = [];
    for (const m of SRC.matchAll(/\.captureImage(?:Sync)?\(\)/g)) {
      const at = m.index ?? -1;
      const inside = ranges.some(([s, e]) => at > s && at < e);
      if (!inside) {
        const line = SRC.slice(0, at).split('\n').length;
        offenders.push(`line ${line}: ${SRC.slice(at - 40, at + 20).trim()}`);
      }
    }
    expect(offenders, `unshielded grab(s) found:\n${offenders.join('\n')}`).toEqual([]);
  });

  it('routes the menu paths through the helpers too', () => {
    // The menu poller grabs on a TIMER, outside any capture step, and its frame
    // becomes the step image. Missing it would leak the pill into exactly the
    // right-click steps and nowhere else, which is a miserable bug to find.
    expect(SRC).toContain('grabMonitor(m)');
    expect(SRC).toContain('grabMonitorSync(mon)');
  });
});

// --- the restore value -------------------------------------------------------
const state = vi.hoisted(() => ({ visible: false, wins: [] as Array<{ p: boolean[]; dead: boolean }> }));

vi.mock('electron', () => ({
  BrowserWindow: {
    getAllWindows: () =>
      state.wins.map((w) => ({
        isDestroyed: () => w.dead,
        setContentProtection: (on: boolean) => w.p.push(on),
      })),
  },
}));
vi.mock('./settings', () => ({ remoteVisibleNow: () => state.visible }));

import {
  shieldOwnWindows,
  applyRemoteVisibility,
  __shieldDepthForTest,
} from './remote-visibility';

const win = () => ({ p: [] as boolean[], dead: false });

describe('shieldOwnWindows', () => {
  beforeEach(() => {
    state.visible = false;
    state.wins = [win(), win()];
  });

  it('protects during the grab and restores to VISIBLE when the setting is on', () => {
    state.visible = true;
    const release = shieldOwnWindows();
    expect(state.wins.map((w) => w.p)).toEqual([[true], [true]]);
    release();
    // false = capturable again, so the remote viewer sees the app between grabs.
    // If this were `true`, the feature would work exactly once.
    expect(state.wins.map((w) => w.p)).toEqual([[true, false], [true, false]]);
  });

  it('is a no-op pair when the setting is off, never leaving a window exposed', () => {
    state.visible = false;
    shieldOwnWindows()();
    for (const w of state.wins) expect(w.p).toEqual([true, true]);
    expect(state.wins.every((w) => w.p.every((v) => v === true))).toBe(true);
  });

  it('skips destroyed windows rather than throwing mid-capture', () => {
    state.wins[0].dead = true;
    const release = shieldOwnWindows();
    release();
    expect(state.wins[0].p).toEqual([]);
    expect(state.wins[1].p.length).toBe(2);
  });
});

describe('shieldOwnWindows is re-entrant (overlapping grabs)', () => {
  beforeEach(() => {
    state.visible = true;
    state.wins = [win()];
  });

  it('does NOT un-protect when an inner shield releases while an outer one is held', () => {
    // THE BUG THIS FIXES, found by review rather than testing: grabs DO overlap.
    // The menu poller runs on a timer, outside the capture queue, so its grab can be
    // in flight when a click capture starts. Without a refcount the first release
    // un-protected while the second grab was still capturing, and the recording pill
    // landed in a saved screenshot. Nothing failed and nothing was logged; the only
    // evidence would be a wrong image in a finished SOP.
    const outer = shieldOwnWindows();
    const inner = shieldOwnWindows();
    expect(state.wins[0].p).toEqual([true]); // protected once, not twice
    inner();
    expect(state.wins[0].p).toEqual([true]); // still protected: outer holds it
    outer();
    expect(state.wins[0].p).toEqual([true, false]); // only now restored
  });

  it('restores exactly once no matter how deep the nesting went', () => {
    const rs = [shieldOwnWindows(), shieldOwnWindows(), shieldOwnWindows()];
    rs.forEach((r) => r());
    expect(state.wins[0].p).toEqual([true, false]);
    expect(__shieldDepthForTest()).toBe(0);
  });

  it('treats a double release as idempotent, so the count cannot underflow', () => {
    // A retry path that runs its finally twice would otherwise drop the depth below
    // zero and un-protect while a later grab still holds the shield.
    const outer = shieldOwnWindows();
    const inner = shieldOwnWindows();
    inner();
    inner();
    inner();
    expect(__shieldDepthForTest()).toBe(1);
    expect(state.wins[0].p).toEqual([true]); // outer still holds it
    outer();
    expect(state.wins[0].p).toEqual([true, false]);
  });

  it('leaves depth at zero after a balanced pair, so state does not leak between grabs', () => {
    shieldOwnWindows()();
    expect(__shieldDepthForTest()).toBe(0);
  });
});

describe('toggling the setting during a grab', () => {
  beforeEach(() => {
    state.visible = true;
    state.wins = [win()];
  });

  it('does not un-protect mid-capture, and applies on release instead', () => {
    // Flipping the setting on while a grab is in flight must not expose the window
    // for the remainder of that capture.
    const release = shieldOwnWindows();
    expect(state.wins[0].p).toEqual([true]);
    applyRemoteVisibility(true);
    expect(state.wins[0].p).toEqual([true]); // unchanged while the shield is held
    release();
    expect(state.wins[0].p).toEqual([true, false]); // the new value lands here
  });

  it('honors a mid-grab switch to NOT visible by restoring protection on', () => {
    state.visible = true;
    const release = shieldOwnWindows();
    applyRemoteVisibility(false);
    release();
    // Restores to protected, per the setting as it now stands.
    expect(state.wins[0].p).toEqual([true, true]);
  });
});

describe('applyRemoteVisibility', () => {
  beforeEach(() => { state.wins = [win()]; });

  it('inverts: visible means protection OFF', () => {
    applyRemoteVisibility(true);
    expect(state.wins[0].p).toEqual([false]);
  });

  it('inverts: not visible means protection ON', () => {
    applyRemoteVisibility(false);
    expect(state.wins[0].p).toEqual([true]);
  });
});
