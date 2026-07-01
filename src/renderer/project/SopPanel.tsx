// SOP generation banner (Phase 3b, inline model). Claude edits the project's
// steps IN-LINE (headings/instructions/captions/notes + intro/section text
// steps); this banner drives generate (with review-before-send + progress) and
// one-click revert. The polished result shows in the Report itself — there is no
// separate SOP document.
import React from 'react';
import { createPortal } from 'react-dom';
import type { ProjectStep } from '../../shared/project';
import type { SopEstimate, SopProgress } from '../../shared/ipc';
import { shotUrl, useProjectStore } from './store';
import { ensureFlattened } from './sop-prepare';

/** Prefer the flattened/redacted render (cache-busted), else the raw screenshot. */
function stepImageSrc(projectId: string, step: ProjectStep): string {
  return step.flattened
    ? `${shotUrl(projectId, step.flattened)}?v=${step.renderRev ?? 0}`
    : shotUrl(projectId, step.screenshot);
}

function progressText(p: SopProgress | null): string {
  switch (p?.stage) {
    case 'thinking':
      return 'Claude is analyzing the screenshots…';
    case 'writing':
      return `Writing the SOP… (${(p.chars ?? 0).toLocaleString()} characters)`;
    case 'done':
      return 'Applying edits…';
    default:
      return 'Preparing the request…';
  }
}

type Phase = 'idle' | 'preparing' | 'review' | 'generating';

export function SopPanel({ sopEnabled }: { sopEnabled: boolean }): React.JSX.Element | null {
  const projectId = useProjectStore((s) => s.projectId);
  const projectPath = useProjectStore((s) => s.projectPath);
  const steps = useProjectStore((s) => s.steps);
  const sopBackup = useProjectStore((s) => s.sopBackup);
  const applyManifest = useProjectStore((s) => s.applyManifest);

  const [phase, setPhase] = React.useState<Phase>('idle');
  const [error, setError] = React.useState<string | null>(null);
  const [estimate, setEstimate] = React.useState<SopEstimate | null>(null);
  const [progress, setProgress] = React.useState<SopProgress | null>(null);
  const [hasKey, setHasKey] = React.useState(false);
  // Abort the pre-send flatten (renderer) alongside the main-side estimate when
  // the user cancels; canceledRef suppresses the resulting rejection so no error
  // banner flashes for a user-initiated cancel (C2).
  const flattenAbortRef = React.useRef<AbortController | null>(null);
  const canceledRef = React.useRef(false);

  React.useEffect(() => {
    if (!sopEnabled) return;
    window.shotai.claude
      .keyStatus()
      .then((k) => setHasKey(k.hasKey))
      .catch(() => undefined);
  }, [sopEnabled]);

  const fail = (e: unknown) => setError(e instanceof Error ? e.message : String(e));

  // Steps actually sent to Claude = current steps minus a prior run's inserts
  // (matches assembleRequest), so the review preview + numbering are accurate.
  const sentSteps = steps.filter((s) => !s.aiInserted);
  const shotCount = sentSteps.filter((s) => s.kind !== 'text').length;
  const busy = phase === 'preparing' || phase === 'generating';

  // Nothing to show: feature off and no prior edits to revert.
  if (!sopEnabled && !sopBackup) return null;

  const startGenerate = async () => {
    if (!projectId || !projectPath) return;
    if (
      sopBackup &&
      !window.confirm(
        'Regenerate the SOP? This replaces the AI-written headings, instructions, and section text. You can revert afterward.',
      )
    ) {
      return;
    }
    setError(null);
    setEstimate(null);
    canceledRef.current = false;
    const controller = new AbortController();
    flattenAbortRef.current = controller;
    setPhase('preparing');
    try {
      const flattened = await ensureFlattened(projectId, projectPath, steps, controller.signal);
      if (canceledRef.current) return setPhase('idle');
      if (flattened) applyManifest(flattened);
      const est = await window.shotai.claude.estimate(projectPath);
      // The cancel path is fire-and-forget, so estimate() may still RESOLVE after
      // the user cancels (e.g. the request finished first). Don't pop the review
      // modal in that case — honor the cancel.
      if (canceledRef.current) return setPhase('idle');
      setEstimate(est);
      setPhase('review');
    } catch (e) {
      if (!canceledRef.current) fail(e); // swallow a user-initiated cancel
      setPhase('idle');
    } finally {
      if (flattenAbortRef.current === controller) flattenAbortRef.current = null;
    }
  };

  // Cancel the "Preparing and Estimating AI Cost" step: abort the flatten AND the
  // main-side estimate; startGenerate's catch is suppressed via canceledRef.
  const cancelPrepare = () => {
    canceledRef.current = true;
    flattenAbortRef.current?.abort();
    window.shotai.claude.cancel();
    setPhase('idle');
  };

  const runGenerate = async () => {
    if (!projectPath) return;
    setError(null);
    setProgress({ stage: 'preparing' });
    setPhase('generating');
    const off = window.shotai.claude.onSopProgress(setProgress);
    try {
      const manifest = await window.shotai.claude.generateSop(projectPath);
      applyManifest(manifest);
      setPhase('idle');
    } catch (e) {
      fail(e);
      setPhase('idle');
    } finally {
      off();
      setProgress(null);
    }
  };

  const revert = async () => {
    if (!projectPath) return;
    if (!window.confirm("Revert Claude's edits and restore the project as it was before generation?"))
      return;
    setError(null);
    try {
      applyManifest(await window.shotai.projects.revertSop(projectPath));
    } catch (e) {
      fail(e);
    }
  };

  const provenance = sopBackup
    ? `Generated with ${sopBackup.model || 'Claude'}${
        sopBackup.at ? ` · ${new Date(sopBackup.at).toLocaleString()}` : ''
      }`
    : null;

  return (
    <section className="sopbar">
      {error && <p className="project__error sopbar__err">{error}</p>}
      <div className="sopbar__row">
        {sopEnabled && (
          <button
            type="button"
            className="btn btn--primary"
            disabled={busy || shotCount === 0 || !hasKey}
            onClick={() => void startGenerate()}
            title={
              !hasKey
                ? 'Set an Anthropic API key in Settings first'
                : shotCount === 0
                  ? 'Capture or import at least one screenshot first'
                  : 'Have Claude write headings + instructions for each screenshot'
            }
          >
            {phase === 'preparing'
              ? 'Preparing…'
              : sopBackup
                ? '✨ Regenerate SOP'
                : '✨ Generate SOP with Claude'}
          </button>
        )}
        {sopBackup && (
          <button type="button" className="btn" disabled={busy} onClick={() => void revert()}>
            ↩ Revert AI edits
          </button>
        )}
        {provenance && phase === 'idle' && (
          <span className="sopbar__prov">{provenance}</span>
        )}
        {sopEnabled && !hasKey && (
          <span className="sopbar__hint">Set an API key in ⚙ Settings to generate.</span>
        )}
      </div>

      {phase === 'preparing' && createPortal(
        <div
          className="sop__overlay sop__overlay--top"
          role="dialog"
          aria-label="Preparing and estimating AI cost"
        >
          <div className="sop__prepcard" aria-live="polite">
            <span className="sop__spinner sop__spinner--sm" aria-hidden="true" />
            <span className="sop__prepcard-text">Preparing and Estimating AI Cost…</span>
            <button type="button" className="btn btn--small" onClick={cancelPrepare}>
              Cancel
            </button>
          </div>
        </div>,
        document.body,
      )}

      {phase === 'review' && estimate && createPortal(
        <div className="sop__overlay" role="dialog" aria-label="Review before sending">
          <div className="sop__modal">
            <h3 className="sop__modal-title">Review what’s sent to Claude</h3>
            <p className="sop__warn">
              These {shotCount} screenshot{shotCount === 1 ? '' : 's'} (with any redactions
              baked in) and their captions/notes are sent to Anthropic to write the SOP, which
              is then applied to your steps. Nothing else leaves your machine.
              {sopBackup ? ' Your current AI edits will be replaced; you can revert again.' : ''}
            </p>
            <p className="sop__cost">
              Model <strong>{estimate.model}</strong> · ~
              {estimate.inputTokens.toLocaleString()} input tokens · est.{' '}
              <strong>${estimate.estCostUsd.toFixed(2)}</strong>
            </p>
            <div className="sop__review-list">
              {sentSteps.map((st, i) =>
                st.kind === 'text' ? (
                  <div key={st.id} className="sop__review-item sop__review-item--text">
                    <span className="sop__review-n">{i + 1}</span>
                    <div className="sop__review-meta">
                      <strong>{st.heading || 'Text step'}</strong>
                      {st.body && <p className="sop__review-body">{st.body}</p>}
                    </div>
                  </div>
                ) : (
                  <div key={st.id} className="sop__review-item">
                    <span className="sop__review-n">{i + 1}</span>
                    {projectId && (
                      <img className="sop__thumb" src={stepImageSrc(projectId, st)} alt="" />
                    )}
                    <div className="sop__review-meta">
                      {st.window?.title && <div>{st.window.title}</div>}
                      {st.caption && <div className="sop__review-cap">{st.caption}</div>}
                    </div>
                  </div>
                ),
              )}
            </div>
            <div className="sop__modal-actions">
              <button type="button" className="btn" onClick={() => setPhase('idle')}>
                Cancel
              </button>
              <button
                type="button"
                className="btn btn--primary"
                onClick={() => void runGenerate()}
              >
                Send to Claude
              </button>
            </div>
          </div>
        </div>,
        document.body,
      )}

      {phase === 'generating' && createPortal(
        <div className="sop__overlay" role="dialog" aria-label="Generating SOP">
          <div className="sop__modal sop__modal--progress">
            <div className="sop__spinner" aria-hidden="true" />
            <p className="sop__progress">{progressText(progress)}</p>
            <p className="sop__hint">This can take up to a minute for longer projects.</p>
          </div>
        </div>,
        document.body,
      )}
    </section>
  );
}
