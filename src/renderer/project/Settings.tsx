import React from 'react';
import {
  SOP_MODELS,
  SOP_TONES,
  SOP_EFFORTS,
  SOP_CUSTOM_INSTRUCTIONS_MAX,
  type SopSettings,
} from '../../shared/sop';
import type { ApiKeyStatus, AppInfo, AuthStatus } from '../../shared/ipc';
import {
  CAPTURE_SCALE_MIN,
  CAPTURE_SCALE_MAX,
  CAPTURE_SCALE_DEFAULT,
  type ThemePref,
} from '../../shared/project';

const THEME_OPTIONS: { id: ThemePref; label: string; blurb: string }[] = [
  { id: 'system', label: 'System', blurb: 'Match your Windows light/dark setting.' },
  { id: 'light', label: 'Light', blurb: 'Always use the light theme.' },
  { id: 'dark', label: 'Dark', blurb: 'Always use the dark theme.' },
];

// Settings is grouped into tabs (D2) so the panel stays manageable as controls
// grow. Order matters: it's the tab-bar order and the arrow-key cycle order.
const SETTINGS_TABS = [
  { id: 'ai', label: 'AI' },
  { id: 'capture', label: 'Capture' },
  { id: 'appearance', label: 'Appearance' },
  { id: 'storage', label: 'Storage' },
  { id: 'about', label: 'About' },
] as const;
type SettingsTab = (typeof SETTINGS_TABS)[number]['id'];

/**
 * Settings panel — the master AI on/off toggle, the (encrypted) Anthropic API
 * key, and the SOP model + tone + custom-instructions controls, grouped into
 * tabs (AI / Capture / Storage / About). Self-contained: loads its own state via
 * IPC and persists each change immediately. The key value is never read back from
 * main (only a status), so the input is write-only.
 */
export function Settings({
  onBack,
  onProjectsDirChanged,
  onReplayTour,
  onThemeChanged,
}: {
  onBack: () => void;
  /** Called when the projects folder actually changes, so Home re-lists. */
  onProjectsDirChanged?: () => void;
  /** Replay the first-run coach-mark tour (returns to Home and starts it). */
  onReplayTour?: () => void;
  /** Called when the theme preference changes, so App re-applies it (F10). */
  onThemeChanged?: (theme: ThemePref) => void;
}): React.JSX.Element {
  const [sop, setSop] = React.useState<SopSettings | null>(null);
  const [keyStatus, setKeyStatus] = React.useState<ApiKeyStatus | null>(null);
  const [keyInput, setKeyInput] = React.useState('');
  // Entra sign-in (#63). `showKeyFallback` backs the opt-out disclosure: a
  // federation-configured machine can still choose to use its own API key.
  const [authStatus, setAuthStatus] = React.useState<AuthStatus | null>(null);
  const [signingIn, setSigningIn] = React.useState(false);
  const [showKeyFallback, setShowKeyFallback] = React.useState(false);
  const [busy, setBusy] = React.useState(false);
  const [testing, setTesting] = React.useState(false);
  const [testResult, setTestResult] = React.useState<{ ok: boolean; msg: string } | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [appInfo, setAppInfo] = React.useState<AppInfo | null>(null);
  const [projectsDir, setProjectsDir] = React.useState('');
  const [remoteVisible, setRemoteVisible] = React.useState(false);
  const [captureScale, setCaptureScale] = React.useState(CAPTURE_SCALE_DEFAULT);
  const [userName, setUserName] = React.useState('');
  const [includeName, setIncludeName] = React.useState(false);
  // Update check (#54): the daily-check opt-out, plus a manual check that bypasses
  // the throttle. `updateMsg` reports the outcome inline — including "up to date",
  // which the automatic check deliberately stays silent about.
  const [updateCheck, setUpdateCheck] = React.useState(true);
  const [updateBusy, setUpdateBusy] = React.useState(false);
  const [updateMsg, setUpdateMsg] = React.useState<string | null>(null);
  const toggleUpdateCheck = async (value: boolean) => {
    setUpdateCheck(value);
    setUpdateMsg(null);
    try {
      setUpdateCheck(await window.shotai.settings.setUpdateCheckEnabled(value));
    } catch {
      setUpdateCheck(!value); // put the switch back if it didn't persist
    }
  };
  const checkNow = async () => {
    setUpdateBusy(true);
    setUpdateMsg(null);
    try {
      const r = await window.shotai.updates.check();
      if (r.available && r.url) {
        setUpdateMsg(`shotAI ${r.version} is available.`);
        void window.shotai.openExternal(r.url);
      } else if (r.error) {
        setUpdateMsg(`Couldn't check: ${r.error}`);
      } else {
        setUpdateMsg("You're up to date.");
      }
    } catch (e) {
      setUpdateMsg(e instanceof Error ? e.message : String(e));
    } finally {
      setUpdateBusy(false);
    }
  };
  const [archiveAge, setArchiveAge] = React.useState(90);
  const [theme, setTheme] = React.useState<ThemePref>('system');
  const [tab, setTab] = React.useState<SettingsTab>('ai');
  const tabRefs = React.useRef<Record<string, HTMLButtonElement | null>>({});

  const fail = (e: unknown) => setError(e instanceof Error ? e.message : String(e));

  // Roving-tabindex arrow-key navigation for the tab bar (WAI-ARIA tabs pattern):
  // Left/Right (and Up/Down) cycle, Home/End jump to the ends, and focus follows.
  const onTabKey = (e: React.KeyboardEvent<HTMLButtonElement>) => {
    const ids = SETTINGS_TABS.map((t) => t.id);
    const i = ids.indexOf(tab);
    let next: SettingsTab | null = null;
    if (e.key === 'ArrowRight' || e.key === 'ArrowDown') next = ids[(i + 1) % ids.length];
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp')
      next = ids[(i - 1 + ids.length) % ids.length];
    else if (e.key === 'Home') next = ids[0];
    else if (e.key === 'End') next = ids[ids.length - 1];
    if (next) {
      e.preventDefault();
      setTab(next);
      tabRefs.current[next]?.focus();
    }
  };

  const refresh = React.useCallback(async () => {
    const [s, ks, info, dir, remoteVis, scale, name, incl, age, themePref, updChk, auth] =
      await Promise.all([
        window.shotai.settings.getSop(),
        window.shotai.claude.keyStatus(),
        window.shotai.getAppInfo(),
        window.shotai.projects.getDir(),
        window.shotai.settings.getRemoteVisible(),
        window.shotai.settings.getCaptureScale(),
        window.shotai.settings.getUserName(),
        window.shotai.settings.getIncludeNameInReports(),
        window.shotai.settings.getArchiveAgeDays(),
        window.shotai.settings.getTheme(),
        window.shotai.settings.getUpdateCheckEnabled(),
        window.shotai.auth.status(),
      ]);
    setSop(s);
    setKeyStatus(ks);
    setAppInfo(info);
    setProjectsDir(dir);
    setRemoteVisible(remoteVis);
    setCaptureScale(scale);
    setUserName(name);
    setIncludeName(incl);
    setArchiveAge(age);
    setTheme(themePref);
    setUpdateCheck(updChk);
    setAuthStatus(auth);
  }, []);

  const signIn = async () => {
    setError(null);
    setTestResult(null);
    setSigningIn(true);
    try {
      // Interactive every time, deliberately. After IT grants the app role a
      // SILENT retry keeps failing, because the cached 60-90 minute Entra token
      // predates the assignment and carries no roles claim — which reads as a
      // broken rule. An interactive sign-in always returns fresh claims.
      await window.shotai.auth.signIn();
      await refresh();
    } catch (e) {
      fail(e);
    } finally {
      setSigningIn(false);
    }
  };

  const signOut = async () => {
    setError(null);
    setTestResult(null);
    try {
      await window.shotai.auth.signOut();
      await refresh();
    } catch (e) {
      fail(e);
    }
  };

  const toggleRemoteVisible = async (value: boolean) => {
    setError(null);
    try {
      setRemoteVisible(await window.shotai.settings.setRemoteVisible(value));
    } catch (e) {
      fail(e);
    }
  };

  // Persist the quality slider on release (onChange updates the local value live).
  const persistCaptureScale = async (value: number) => {
    setError(null);
    try {
      setCaptureScale(await window.shotai.settings.setCaptureScale(value));
    } catch (e) {
      fail(e);
    }
  };

  // Persist the display name on blur (onChange updates the local value live).
  const persistUserName = async (value: string) => {
    setError(null);
    try {
      const stored = await window.shotai.settings.setUserName(value);
      setUserName(stored);
      // An empty name can't be included — keep the toggle honest.
      if (!stored.trim() && includeName) {
        setIncludeName(await window.shotai.settings.setIncludeNameInReports(false));
      }
    } catch (e) {
      fail(e);
    }
  };

  const toggleIncludeName = async (value: boolean) => {
    setError(null);
    try {
      setIncludeName(await window.shotai.settings.setIncludeNameInReports(value));
    } catch (e) {
      fail(e);
    }
  };

  const persistArchiveAge = async (value: number) => {
    setError(null);
    try {
      setArchiveAge(await window.shotai.settings.setArchiveAgeDays(value));
    } catch (e) {
      fail(e);
    }
  };

  const chooseTheme = async (value: ThemePref) => {
    setError(null);
    setTheme(value); // reflect immediately
    onThemeChanged?.(value); // App re-applies right away
    try {
      const stored = await window.shotai.settings.setTheme(value);
      setTheme(stored);
      onThemeChanged?.(stored);
    } catch (e) {
      fail(e);
    }
  };

  const chooseDir = async () => {
    try {
      const d = await window.shotai.projects.chooseDir();
      if (d) {
        setProjectsDir(d);
        onProjectsDirChanged?.(); // let Home re-list from the new folder
      }
    } catch (e) {
      fail(e);
    }
  };

  // Key-only refresh — used after Save/Clear so an in-flight (blur-persisted)
  // customInstructions edit isn't clobbered by a stale full re-read of `sop`.
  const refreshKeyStatus = React.useCallback(async () => {
    setKeyStatus(await window.shotai.claude.keyStatus());
  }, []);

  React.useEffect(() => {
    refresh().catch(fail);
  }, [refresh]);

  const patch = async (p: Partial<SopSettings>) => {
    setError(null);
    setTestResult(null); // any settings change invalidates a prior connection test
    try {
      setSop(await window.shotai.settings.setSop(p));
    } catch (e) {
      fail(e);
    }
  };

  const saveKey = async () => {
    if (!keyInput.trim()) return;
    setBusy(true);
    setError(null);
    setTestResult(null);
    try {
      await window.shotai.claude.setApiKey(keyInput.trim());
      setKeyInput('');
      await refreshKeyStatus();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  };

  const clearKey = async () => {
    setBusy(true);
    setError(null);
    setTestResult(null);
    try {
      await window.shotai.claude.clearApiKey();
      await refreshKeyStatus();
    } catch (e) {
      fail(e);
    } finally {
      setBusy(false);
    }
  };

  const testConnection = async () => {
    setTesting(true);
    setTestResult(null);
    setError(null);
    try {
      const r = await window.shotai.claude.testConnection();
      // Name the leg that failed. Under federation the three legs are the only way
      // to distinguish "not signed in" from "not assigned the role" from a scope or
      // model problem, because every assertion denial from Anthropic is the same
      // opaque 401 by design.
      const legLabel =
        r.leg === 'signIn'
          ? 'Microsoft sign-in'
          : r.leg === 'exchange'
            ? 'Claude access'
            : r.leg === 'api'
              ? 'Claude API'
              : null;
      setTestResult({
        ok: r.ok,
        msg: r.ok
          ? 'Connected' + (r.model ? ' (' + r.model + ')' : '') + '.'
          : (legLabel ? legLabel + ': ' : '') + (r.error ?? 'Test failed.'),
      });
    } catch (e) {
      fail(e);
    } finally {
      setTesting(false);
    }
  };

  // Three states (#63): unconfigured (every external user — the AI tab is
  // byte-identical to before, with no mention of Entra anywhere, because a
  // greyed-out SSO control in a public repo is a permanent support-question
  // generator), configured, and configured-but-opted-out.
  const fed = !!authStatus?.federationAvailable;
  const signedIn = !!authStatus?.signedIn;

  return (
    <section className="settings">
      <div className="settings__bar">
        <button type="button" className="btn btn--small" onClick={onBack}>
          ← Back
        </button>
        <h2 className="settings__title">Settings</h2>
      </div>

      {error && <p className="project__error">Error: {error}</p>}

      {!sop ? (
        <p className="project__hint">Loading…</p>
      ) : (
        <>
          <div className="settings__tabs" role="tablist" aria-label="Settings sections">
            {SETTINGS_TABS.map((t) => (
              <button
                key={t.id}
                ref={(el) => {
                  tabRefs.current[t.id] = el;
                }}
                type="button"
                role="tab"
                id={`settings-tab-${t.id}`}
                aria-selected={tab === t.id}
                // Only the active panel is in the DOM, so only the active tab
                // gets a resolvable aria-controls (avoids dangling IDREFs).
                aria-controls={tab === t.id ? `settings-panel-${t.id}` : undefined}
                tabIndex={tab === t.id ? 0 : -1}
                className={`settings__tab${tab === t.id ? ' settings__tab--on' : ''}`}
                onClick={() => setTab(t.id)}
                onKeyDown={onTabKey}
              >
                {t.label}
              </button>
            ))}
          </div>

          <div
            className="settings__panel"
            role="tabpanel"
            id={`settings-panel-${tab}`}
            aria-labelledby={`settings-tab-${tab}`}
            // Focusable so keyboard users can Tab into a panel whose content is
            // static (e.g. About), per the WAI-ARIA tabs pattern.
            tabIndex={0}
          >
            {tab === 'ai' && (
              <>
                <label className="settings__toggle">
                  <span className="settings__toggle-text">
                    <strong>AI SOP generation</strong>
                    <span className="settings__hint">
                      Use Claude to write a step-by-step guide from your capture —
                      this needs {fed ? 'you to sign in below' : 'an Anthropic API key (below)'}.
                      When off, no Claude features appear and nothing ever leaves your
                      machine.
                    </span>
                  </span>
                  <input
                    type="checkbox"
                    className="settings__switch"
                    checked={sop.enabled}
                    onChange={(e) => void patch({ enabled: e.target.checked })}
                  />
                </label>

                {!sop.enabled && (
                  <p className="settings__off">
                    Claude SOP generation is off. Turn it on to choose a model and tone
                    and {fed ? 'sign in with your work account' : 'connect your Anthropic API key'}.
                  </p>
                )}

                {sop.enabled && (
                  <>
                    {fed && (
                      <div className="settings__group">
                        <h3 className="settings__h">Microsoft sign-in</h3>
                        {signedIn ? (
                          <>
                            <p className="settings__hint">
                              shotAI uses your work account for AI features. No API key
                              needed. Configured by your organization.
                            </p>
                            <p className="settings__hint">
                              Signed in as <b>{authStatus?.account}</b>
                            </p>
                          </>
                        ) : (
                          <p className="settings__hint">
                            Sign in with your work account to use AI features. No API key
                            needed — access is granted by your organization.
                          </p>
                        )}
                        <div className="settings__keyactions">
                          {testResult && (
                            <span
                              className={`settings__chip ${testResult.ok ? 'settings__chip--ok' : 'settings__chip--err'}`}
                              title={testResult.msg}
                            >
                              {testResult.ok ? '● Connected' : '● Error'}
                            </span>
                          )}
                          {signedIn && (
                            <button
                              type="button"
                              className="btn btn--small"
                              onClick={() => void signOut()}
                              disabled={busy || signingIn}
                            >
                              Sign out
                            </button>
                          )}
                          {signedIn && (
                            <button
                              type="button"
                              className="btn btn--small"
                              onClick={() => void testConnection()}
                              disabled={testing}
                            >
                              {testing ? 'Testing…' : 'Test connection'}
                            </button>
                          )}
                          <button
                            type="button"
                            className="btn btn--small btn--primary"
                            onClick={() => void signIn()}
                            disabled={signingIn || busy}
                          >
                            {signingIn
                              ? 'Waiting for your browser…'
                              : signedIn
                                ? 'Sign in again'
                                : 'Sign in with Microsoft'}
                          </button>
                        </div>
                        {testResult && !testResult.ok && (
                          <>
                            <p className="project__error" style={{ marginTop: '0.5rem' }}>
                              {testResult.msg}
                            </p>
                            {authStatus?.supportUrl && (
                              <p className="settings__hint">
                                Your sign-in worked. If Claude access has not been granted
                                to your account yet,{' '}
                                <a
                                  className="settings__link"
                                  href={authStatus.supportUrl}
                                  onClick={(e) => {
                                    e.preventDefault();
                                    void window.shotai.openExternal(
                                      authStatus.supportUrl as string,
                                    );
                                  }}
                                >
                                  request access
                                </a>
                                , then use <b>Sign in with Microsoft</b> again — a retry only
                                picks up a new grant after a fresh sign-in.
                              </p>
                            )}
                          </>
                        )}
                      </div>
                    )}

                    {fed && (
                      <button
                        type="button"
                        className="btn btn--small"
                        aria-expanded={showKeyFallback}
                        onClick={() => setShowKeyFallback((v) => !v)}
                      >
                        {showKeyFallback ? '▾' : '▸'} Use my own Anthropic API key instead
                      </button>
                    )}

                    {(!fed || showKeyFallback) && (
                    <div className="settings__group">
                      <h3 className="settings__h">Anthropic API key</h3>
                      <p className="settings__hint">
                        shotAI uses <b>your own</b> Anthropic API key to write SOPs
                        (billed per use). Your organization may provide one — paste
                        it below. Otherwise, create your own at{' '}
                        <a
                          className="settings__link"
                          href="https://console.anthropic.com/settings/keys"
                          onClick={(e) => {
                            e.preventDefault();
                            void window.shotai.openExternal(
                              'https://console.anthropic.com/settings/keys',
                            );
                          }}
                        >
                          console.anthropic.com/settings/keys
                        </a>
                        .
                      </p>
                      {keyStatus && (
                        <p className="settings__hint">
                          {keyStatus.source === 'stored' &&
                            'A key is saved (encrypted) on this machine ✓'}
                          {keyStatus.source === 'env' &&
                            'Using the ANTHROPIC_API_KEY environment variable.'}
                          {keyStatus.source === 'none' &&
                            !keyStatus.hasStoredCiphertext &&
                            'No key set yet.'}
                          {keyStatus.hasStoredCiphertext &&
                            keyStatus.source !== 'stored' &&
                            ' A previously saved key couldn’t be read on this machine — clear it and enter a new one.'}
                          {!keyStatus.encryptionAvailable &&
                            ' Secure storage is unavailable on this system — a key can’t be saved here; set ANTHROPIC_API_KEY instead.'}
                        </p>
                      )}
                      <input
                        className="project__input settings__keyfield"
                        type="password"
                        placeholder="sk-ant-…"
                        value={keyInput}
                        autoComplete="off"
                        spellCheck={false}
                        onChange={(e) => setKeyInput(e.target.value)}
                        disabled={busy || (keyStatus ? !keyStatus.encryptionAvailable : false)}
                      />
                      <div className="settings__keyactions">
                        {testResult && (
                          <span
                            className={`settings__chip ${testResult.ok ? 'settings__chip--ok' : 'settings__chip--err'}`}
                            title={testResult.msg}
                          >
                            {testResult.ok ? '● Connected' : '● Error'}
                          </span>
                        )}
                        {(keyStatus?.source === 'stored' || keyStatus?.hasStoredCiphertext) && (
                          <button
                            type="button"
                            className="btn btn--small"
                            onClick={() => void clearKey()}
                            disabled={busy}
                          >
                            Clear
                          </button>
                        )}
                        <button
                          type="button"
                          className="btn btn--small"
                          onClick={() => void testConnection()}
                          disabled={testing || !keyStatus?.hasKey}
                        >
                          {testing ? 'Testing…' : 'Test connection'}
                        </button>
                        <button
                          type="button"
                          className="btn btn--small btn--primary"
                          onClick={() => void saveKey()}
                          disabled={busy || !keyInput.trim()}
                        >
                          Save
                        </button>
                      </div>
                      {testResult && !testResult.ok && (
                        <p className="project__error" style={{ marginTop: '0.5rem' }}>
                          {testResult.msg}
                        </p>
                      )}
                    </div>
                    )}

                    <div className="settings__group">
                      <h3 className="settings__h">Model</h3>
                      <div className="capmode__modes" role="radiogroup" aria-label="Model">
                        {SOP_MODELS.map((m) => (
                          <button
                            key={m.id}
                            type="button"
                            role="radio"
                            aria-checked={sop.model === m.id}
                            className={`capmode__chip${sop.model === m.id ? ' capmode__chip--on' : ''}`}
                            onClick={() => void patch({ model: m.id })}
                          >
                            {m.label}
                          </button>
                        ))}
                      </div>
                      <p className="settings__hint">
                        {SOP_MODELS.find((m) => m.id === sop.model)?.blurb}
                      </p>
                    </div>

                    <div className="settings__group">
                      <h3 className="settings__h">Tone</h3>
                      <div className="capmode__modes" role="radiogroup" aria-label="Tone">
                        {SOP_TONES.map((t) => (
                          <button
                            key={t.id}
                            type="button"
                            role="radio"
                            aria-checked={sop.tone === t.id}
                            className={`capmode__chip${sop.tone === t.id ? ' capmode__chip--on' : ''}`}
                            onClick={() => void patch({ tone: t.id })}
                          >
                            {t.label}
                          </button>
                        ))}
                      </div>
                      <p className="settings__hint">
                        {SOP_TONES.find((t) => t.id === sop.tone)?.blurb}
                      </p>
                    </div>

                    <div className="settings__group">
                      <h3 className="settings__h">Effort</h3>
                      <div className="capmode__modes" role="radiogroup" aria-label="Effort">
                        {SOP_EFFORTS.map((e) => (
                          <button
                            key={e.id}
                            type="button"
                            role="radio"
                            aria-checked={sop.effort === e.id}
                            className={`capmode__chip${sop.effort === e.id ? ' capmode__chip--on' : ''}`}
                            onClick={() => void patch({ effort: e.id })}
                          >
                            {e.label}
                          </button>
                        ))}
                      </div>
                      <p className="settings__hint">
                        {SOP_EFFORTS.find((e) => e.id === sop.effort)?.blurb}
                      </p>
                    </div>

                    <div className="settings__group">
                      <h3 className="settings__h">Custom instructions (optional)</h3>
                      <textarea
                        className="settings__textarea"
                        rows={3}
                        maxLength={SOP_CUSTOM_INSTRUCTIONS_MAX}
                        placeholder="e.g. Reference our ticketing system, avoid jargon, always note required permissions…"
                        value={sop.customInstructions}
                        onChange={(e) => setSop({ ...sop, customInstructions: e.target.value })}
                        onBlur={(e) => void patch({ customInstructions: e.target.value })}
                      />
                      <p className="settings__hint">
                        {sop.customInstructions.length}/{SOP_CUSTOM_INSTRUCTIONS_MAX}
                      </p>
                    </div>
                  </>
                )}
              </>
            )}

            {tab === 'capture' && (
              <>
                <div className="settings__group">
                  <h3 className="settings__h">Screenshot quality</h3>
                  <p className="settings__hint">
                    Downscales captured screenshots to cut file size and AI cost. Lower =
                    smaller and cheaper but softer text; a readability floor keeps small
                    captures legible to Claude. Applies to new captures.
                  </p>
                  <div className="settings__sliderrow">
                    <input
                      type="range"
                      className="settings__slider"
                      min={CAPTURE_SCALE_MIN}
                      max={CAPTURE_SCALE_MAX}
                      step={0.05}
                      value={captureScale}
                      aria-label="Screenshot quality"
                      onChange={(e) => setCaptureScale(Number(e.target.value))}
                      onPointerUp={(e) =>
                        void persistCaptureScale(Number((e.target as HTMLInputElement).value))
                      }
                      onKeyUp={(e) =>
                        void persistCaptureScale(Number((e.target as HTMLInputElement).value))
                      }
                    />
                    <span className="settings__slidval">{Math.round(captureScale * 100)}%</span>
                  </div>
                </div>

                <label className="settings__toggle">
                  <span className="settings__toggle-text">
                    <strong>Show shotAI in remote sessions &amp; screen shares</strong>
                    <span className="settings__hint">
                      By default shotAI is hidden from all screen capture, which is what
                      keeps it out of its own screenshots, but also makes it invisible in
                      Teams, Splashtop, GoToAssist and similar. Turn this on to see and
                      use the app over a remote connection. Your screenshots stay clean:
                      shotAI still hides itself while recording, and the capture pill is
                      excluded from each shot as it is taken.
                    </span>
                  </span>
                  <input
                    type="checkbox"
                    className="settings__switch"
                    checked={remoteVisible}
                    onChange={(e) => void toggleRemoteVisible(e.target.checked)}
                  />
                </label>

              </>
            )}

            {tab === 'appearance' && (
              <div className="settings__group">
                <h3 className="settings__h">Theme</h3>
                <p className="settings__hint">
                  Choose the app’s color theme. “System” follows your Windows
                  light/dark setting.
                </p>
                <div className="capmode__modes" role="radiogroup" aria-label="Theme">
                  {THEME_OPTIONS.map((t) => (
                    <button
                      key={t.id}
                      type="button"
                      role="radio"
                      aria-checked={theme === t.id}
                      className={`capmode__chip${theme === t.id ? ' capmode__chip--on' : ''}`}
                      onClick={() => void chooseTheme(t.id)}
                    >
                      {t.label}
                    </button>
                  ))}
                </div>
                <p className="settings__hint">
                  {THEME_OPTIONS.find((t) => t.id === theme)?.blurb}
                </p>
              </div>
            )}

            {tab === 'storage' && (
              <>
                <div className="settings__group">
                  <h3 className="settings__h">Projects folder</h3>
                  <p className="settings__hint">
                    Where shotAI stores each project — screenshots, manifest, and exports.
                  </p>
                  <div className="settings__dirrow">
                    <code className="settings__dir" title={projectsDir}>
                      {projectsDir || '…'}
                    </code>
                    <button
                      type="button"
                      className="btn btn--small"
                      onClick={() => void chooseDir()}
                    >
                      Change…
                    </button>
                  </div>
                </div>

                <div className="settings__group">
                  <h3 className="settings__h">Auto-archive old projects</h3>
                  <p className="settings__hint">
                    Projects you haven’t opened or edited in this long are compressed and
                    moved to the Archive tab automatically. They stay listed — opening one
                    restores it. You can also archive projects manually anytime.
                  </p>
                  <select
                    className="project__input settings__select"
                    value={archiveAge}
                    aria-label="Auto-archive age"
                    onChange={(e) => void persistArchiveAge(Number(e.target.value))}
                  >
                    <option value={0}>Never</option>
                    <option value={30}>After 1 month</option>
                    <option value={90}>After 3 months</option>
                    <option value={180}>After 6 months</option>
                    <option value={365}>After 1 year</option>
                  </select>
                </div>
              </>
            )}

            {tab === 'about' && (
              <>
                <div className="settings__group">
                  <h3 className="settings__h">Your name</h3>
                  <p className="settings__hint">
                    Optionally credited on exported guides. When included, the footer
                    reads “Created on &lt;date&gt; by &lt;your name&gt;”.
                  </p>
                  <input
                    className="project__input"
                    type="text"
                    placeholder="e.g. Dana Reyes"
                    value={userName}
                    maxLength={120}
                    onChange={(e) => setUserName(e.target.value)}
                    onBlur={(e) => void persistUserName(e.target.value)}
                  />
                  <label className="settings__toggle" style={{ marginTop: '0.75rem' }}>
                    <span className="settings__toggle-text">
                      <strong>Include my name in reports &amp; exports</strong>
                      <span className="settings__hint">
                        Adds “by &lt;your name&gt;” to the “Created on …” line of every
                        export. Set a name above to enable this.
                      </span>
                    </span>
                    <input
                      type="checkbox"
                      className="settings__switch"
                      checked={includeName}
                      disabled={!userName.trim()}
                      onChange={(e) => void toggleIncludeName(e.target.checked)}
                    />
                  </label>
                </div>
                <div className="settings__group">
                  <h3 className="settings__h">About</h3>
                  <p className="settings__hint">
                    {appInfo
                      ? `${appInfo.name} ${appInfo.version} · ${appInfo.platform}/${appInfo.arch} · Electron ${appInfo.electron}`
                      : '…'}
                  </p>
                </div>
                <div className="settings__group">
                  <h3 className="settings__h">Updates</h3>
                  <label className="settings__toggle">
                    <span className="settings__toggle-text">
                      <strong>Check for updates</strong>
                      <span className="settings__hint">
                        Asks GitHub once a day, when shotAI starts, whether a newer
                        version has been released, and tells you if one has. This is the
                        only time shotAI contacts the internet on its own. It never
                        installs anything by itself.
                      </span>
                    </span>
                    <input
                      type="checkbox"
                      className="settings__switch"
                      checked={updateCheck}
                      onChange={(e) => void toggleUpdateCheck(e.target.checked)}
                    />
                  </label>
                  <div className="settings__dirrow" style={{ marginTop: '0.75rem' }}>
                    <button
                      type="button"
                      className="btn btn--small"
                      disabled={updateBusy}
                      onClick={() => void checkNow()}
                    >
                      {updateBusy ? 'Checking…' : '↻ Check now'}
                    </button>
                    {updateMsg && <span className="settings__hint">{updateMsg}</span>}
                  </div>
                </div>
                {onReplayTour && (
                  <div className="settings__group">
                    <h3 className="settings__h">Getting started</h3>
                    <p className="settings__hint">
                      New to shotAI, or want a refresher? Replay the quick intro
                      tour on the home screen.
                    </p>
                    <div className="settings__dirrow">
                      <button
                        type="button"
                        className="btn btn--small"
                        onClick={onReplayTour}
                      >
                        ↺ Show intro tour
                      </button>
                    </div>
                  </div>
                )}
              </>
            )}
          </div>
        </>
      )}
    </section>
  );
}
