// First-run coach-mark tour (R2). A sequence of floating bubbles anchored to
// real home-screen controls (via `data-tour` attributes) that teaches the
// capture → annotate → AI → export path. Fires once on first launch; skippable
// (Skip / Esc / click-outside) and replayable from Settings → About.
import React from 'react';
import { createPortal } from 'react-dom';

type TourStep = {
  /** `data-tour` value of the element to spotlight, or null for a centered step. */
  anchor: string | null;
  headline: string;
  body: string;
  /** Show the mini recording-pill illustration (the pill doesn't exist yet). */
  pill?: boolean;
};

const STEPS: TourStep[] = [
  {
    anchor: 'hero',
    headline: 'Welcome to shotAI',
    body: 'Record a process, mark it up, and let Claude turn it into a step-by-step guide — an SOP — you can export and share. It all starts here.',
  },
  {
    anchor: 'capture',
    headline: 'Capture your process',
    body: 'Click “Capture ▸” to start recording. shotAI hides while you work — every click captures a screenshot and becomes a numbered step. Building from images or text instead? Use “Empty Project”.',
  },
  {
    anchor: 'mode',
    headline: 'Choose what gets captured',
    body: '“Screen” grabs a full monitor each step — the most predictable choice, and the default. Pick “Window” or “Area” to narrow it down. “Auto” guesses per click and can grab extra context.',
  },
  {
    anchor: null,
    pill: true,
    headline: 'Recording? Just click',
    body: 'Once recording, a small bar stays on top. Switch to any app and click anything to capture a step — or press Ctrl+Shift+S. Pause to stop capturing, Stop to finish, the red ✕ to discard.',
  },
  {
    anchor: 'settings',
    headline: 'Let Claude write the guide',
    body: 'When you’re ready for AI-written instructions, open ⚙ Settings → AI and add an Anthropic API key (your organization may provide one, or create your own — billed per use). Then hit “✨ Generate SOP with Claude”.',
  },
];

const BUBBLE_W = 330;
const GAP = 14;

export function Tour({ onClose }: { onClose: () => void }): React.JSX.Element {
  const [i, setI] = React.useState(0);
  const [rect, setRect] = React.useState<DOMRect | null>(null);
  const step = STEPS[i];

  const finish = React.useCallback(() => onClose(), [onClose]);
  const next = React.useCallback(
    () => setI((n) => (n >= STEPS.length - 1 ? (finish(), n) : n + 1)),
    [finish],
  );
  const back = React.useCallback(() => setI((n) => Math.max(0, n - 1)), []);

  // Measure the anchor for this step, and keep it fresh on resize/scroll.
  React.useLayoutEffect(() => {
    const measure = () => {
      if (!step.anchor) {
        setRect(null);
        return;
      }
      const el = document.querySelector(`[data-tour="${step.anchor}"]`);
      setRect(el ? el.getBoundingClientRect() : null);
    };
    measure();
    window.addEventListener('resize', measure);
    window.addEventListener('scroll', measure, true);
    return () => {
      window.removeEventListener('resize', measure);
      window.removeEventListener('scroll', measure, true);
    };
  }, [step.anchor]);

  // Keyboard: Esc dismisses, arrows navigate.
  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') finish();
      else if (e.key === 'ArrowRight') next();
      else if (e.key === 'ArrowLeft') back();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [finish, next, back]);

  // Bubble placement: below the anchor if there's room, else above; horizontally
  // clamped into the viewport. Centered when there's no anchor. Using `bottom` for
  // above-placement avoids needing to measure the bubble's height.
  let bubbleStyle: React.CSSProperties;
  let caretLeft: number | null = null;
  let caretPos: 'top' | 'bottom' | null = null;
  if (rect) {
    const below = rect.bottom + 220 < window.innerHeight;
    const left = Math.max(
      12,
      Math.min(rect.left + rect.width / 2 - BUBBLE_W / 2, window.innerWidth - BUBBLE_W - 12),
    );
    bubbleStyle = below
      ? { top: rect.bottom + GAP, left }
      : { bottom: window.innerHeight - rect.top + GAP, left };
    caretLeft = Math.max(18, Math.min(rect.left + rect.width / 2 - left, BUBBLE_W - 18));
    caretPos = below ? 'top' : 'bottom';
  } else {
    bubbleStyle = {
      top: '50%',
      left: '50%',
      transform: 'translate(-50%, -50%)',
    };
  }

  const spotStyle: React.CSSProperties | null = rect
    ? {
        top: rect.top - 6,
        left: rect.left - 6,
        width: rect.width + 12,
        height: rect.height + 12,
      }
    : null;

  return createPortal(
    <div className="tour" role="dialog" aria-modal="true" aria-label="Getting started">
      {/* Transparent full-screen click-catcher: clicking outside the bubble skips. */}
      <div className="tour__scrim" onClick={finish} />
      {spotStyle ? (
        <div className="tour__spot" style={spotStyle} aria-hidden="true" />
      ) : (
        <div className="tour__fulldim" aria-hidden="true" />
      )}
      <div className="tour__bubble" style={bubbleStyle} onClick={(e) => e.stopPropagation()}>
        {caretPos && caretLeft != null && (
          <span
            className={`tour__caret tour__caret--${caretPos}`}
            style={{ left: caretLeft }}
            aria-hidden="true"
          />
        )}
        <div className="tour__step">
          Step {i + 1} of {STEPS.length}
        </div>
        <h3 className="tour__h">{step.headline}</h3>
        {step.pill && (
          <div className="tour__pill" aria-hidden="true">
            <span className="tour__pill-dot" />
            <span className="tour__pill-lab">
              Capturing · 3
              <small>Click anything · Ctrl+Shift+S</small>
            </span>
            <span className="tour__pill-btn">Pause</span>
            <span className="tour__pill-btn tour__pill-btn--stop">Stop</span>
            <span className="tour__pill-btn tour__pill-btn--x">✕</span>
          </div>
        )}
        <p className="tour__body">{step.body}</p>
        <div className="tour__nav">
          <span className="tour__dots" aria-hidden="true">
            {STEPS.map((_, di) => (
              <i key={di} className={di === i ? 'on' : ''} />
            ))}
          </span>
          <button type="button" className="tour__btn tour__btn--skip" onClick={finish}>
            Skip
          </button>
          {i > 0 && (
            <button type="button" className="tour__btn" onClick={back}>
              Back
            </button>
          )}
          <button type="button" className="tour__btn tour__btn--pri" onClick={next}>
            {i === STEPS.length - 1 ? 'Done' : 'Next'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
