import React from 'react';
import {
  SOP_MODELS,
  SOP_TONES,
  SOP_CUSTOM_INSTRUCTIONS_MAX,
  type SopSettings,
} from '../../shared/sop';
import type { ApiKeyStatus, AppInfo } from '../../shared/ipc';

/**
 * Settings panel — the master AI on/off toggle, the (encrypted) Anthropic API
 * key, and the SOP model + tone + custom-instructions controls. Self-contained:
 * loads its own state via IPC and persists each change immediately. The key value
 * is never read back from main (only a status), so the input is write-only.
 */
export function Settings({
  onBack,
  onProjectsDirChanged,
}: {
  onBack: () => void;
  /** Called when the projects folder actually changes, so Home re-lists. */
  onProjectsDirChanged?: () => void;
}): React.JSX.Element {
  const [sop, setSop] = React.useState<SopSettings | null>(null);
  const [keyStatus, setKeyStatus] = React.useState<ApiKeyStatus | null>(null);
  const [keyInput, setKeyInput] = React.useState('');
  const [busy, setBusy] = React.useState(false);
  const [testing, setTesting] = React.useState(false);
  const [testResult, setTestResult] = React.useState<{ ok: boolean; msg: string } | null>(null);
  const [error, setError] = React.useState<string | null>(null);
  const [appInfo, setAppInfo] = React.useState<AppInfo | null>(null);
  const [projectsDir, setProjectsDir] = React.useState('');

  const fail = (e: unknown) => setError(e instanceof Error ? e.message : String(e));

  const refresh = React.useCallback(async () => {
    const [s, ks, info, dir] = await Promise.all([
      window.shotai.settings.getSop(),
      window.shotai.claude.keyStatus(),
      window.shotai.getAppInfo(),
      window.shotai.projects.getDir(),
    ]);
    setSop(s);
    setKeyStatus(ks);
    setAppInfo(info);
    setProjectsDir(dir);
  }, []);

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

  const testKey = async () => {
    setTesting(true);
    setTestResult(null);
    setError(null);
    try {
      const r = await window.shotai.claude.testKey();
      setTestResult({
        ok: r.ok,
        msg: r.ok ? `Connected${r.model ? ` (${r.model})` : ''}.` : (r.error ?? 'Test failed.'),
      });
    } catch (e) {
      fail(e);
    } finally {
      setTesting(false);
    }
  };

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
          <label className="settings__toggle">
            <span className="settings__toggle-text">
              <strong>AI SOP generation</strong>
              <span className="settings__hint">
                Use Claude to write a step-by-step guide from your capture. When
                off, no Claude features appear and nothing ever leaves your machine.
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
              and connect your Anthropic API key.
            </p>
          )}

          {sop.enabled && (
            <>
              <div className="settings__group">
                <h3 className="settings__h">Anthropic API key</h3>
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
                    onClick={() => void testKey()}
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
            <h3 className="settings__h">About</h3>
            <p className="settings__hint">
              {appInfo
                ? `${appInfo.name} · ${appInfo.platform}/${appInfo.arch} · Electron ${appInfo.electron}`
                : '…'}
            </p>
          </div>
        </>
      )}
    </section>
  );
}
