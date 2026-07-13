// The report "+ Capture / + Screenshot" mode picker, shown as a small modal when
// the user inserts a capture at a gap. It only CHOOSES a CaptureTarget — the
// caller (ProjectDetail) routes +Capture to a recording session and +Screenshot
// to a one-shot grab. Reuses the shared useCaptureTarget hook + the home screen's
// picker CSS classes; wrapped in the existing sop__overlay/sop__modal shell.
import React from 'react';
import type { CaptureMode, CaptureTarget } from '../../shared/project';
import { useCaptureTarget } from './useCaptureTarget';

/** 'capture' = record multiple steps here; 'screenshot' = one no-click grab. */
export type CaptureInsertVariant = 'capture' | 'screenshot';

// +Screenshot is a no-click grab, so 'auto' (which classifies off the clicked
// window) is not offered — screen / window / area only. +Capture is a real
// recording and offers all four.
const CAPTURE_MODES: CaptureMode[] = ['screen', 'auto', 'window', 'area'];
const SCREENSHOT_MODES: CaptureMode[] = ['screen', 'window', 'area'];

export function CaptureInsertModal({
  variant,
  onConfirm,
  onClose,
}: {
  variant: CaptureInsertVariant;
  onConfirm: (target: CaptureTarget) => void;
  onClose: () => void;
}): React.JSX.Element {
  const [err, setErr] = React.useState<string | null>(null);
  const picker = useCaptureTarget({
    allowedModes: variant === 'capture' ? CAPTURE_MODES : SCREENSHOT_MODES,
    initialMode: 'screen',
    onError: (e) => setErr(e instanceof Error ? e.message : String(e)),
  });
  const { mode, options, selectMode, pickerLabel, modeReady, selectingArea } = picker;

  // Esc closes the modal (matches the app's other overlays).
  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const isCapture = variant === 'capture';
  const title = isCapture ? 'Record more steps here' : 'Add a screenshot here';
  const confirmLabel = isCapture ? 'Start capture' : 'Capture';
  const helper = isCapture
    ? 'Pick what to capture, then click through the steps as usual — they’ll be inserted at this spot. shotAI hides while you record.'
    : 'Grabs one image right now — no clicking. shotAI is left out of the shot.';

  const confirm = () => {
    if (!modeReady || selectingArea) return;
    onConfirm(picker.buildTarget());
  };

  return (
    <div
      className="sop__overlay"
      role="dialog"
      aria-modal="true"
      aria-label={title}
      onClick={onClose}
    >
      <div className="sop__modal capmodal" onClick={(e) => e.stopPropagation()}>
        <h3 className="sop__modal-title">{title}</h3>
        <p className="capmodal__help">{helper}</p>

        <div className="home__mode capmodal__modes" role="radiogroup" aria-label="Capture mode">
          <span className="home__mode-label">Mode</span>
          {options.map((opt) => (
            <button
              key={opt.mode}
              type="button"
              role="radio"
              aria-checked={mode === opt.mode}
              className={`capmode__chip${mode === opt.mode ? ' capmode__chip--on' : ''}`}
              title={opt.hint}
              onClick={() => selectMode(opt.mode)}
            >
              {opt.label}
            </button>
          ))}
        </div>

        {(mode === 'window' || mode === 'screen') && (
          <div className="home__dd capmodal__dd">
            <button
              type="button"
              className="home__dd-trigger"
              aria-haspopup="listbox"
              aria-expanded={picker.pickerOpen}
              onClick={() => picker.setPickerOpen((o) => !o)}
            >
              <span className="home__dd-current">{pickerLabel}</span>
              <span className="home__dd-caret" aria-hidden="true">
                ▾
              </span>
            </button>
            {picker.pickerOpen && (
              <>
                <div className="menu__backdrop" onClick={() => picker.setPickerOpen(false)} />
                <div
                  className="home__dd-pop"
                  role="listbox"
                  aria-label={mode === 'window' ? 'Window to capture' : 'Monitor to capture'}
                >
                  <div className="home__dd-head">
                    <span>{mode === 'window' ? 'Windows' : 'Monitors'}</span>
                    <button
                      type="button"
                      className="btn btn--small btn--ghost"
                      onClick={() => void picker.loadTargets()}
                      disabled={picker.targetsLoading}
                      title="Refresh the list"
                    >
                      ↻ Refresh
                    </button>
                  </div>
                  <div className="home__dd-list">
                    {mode === 'window' ? (
                      picker.targets?.windows.length ? (
                        picker.targets.windows.map((w) => (
                          <button
                            key={w.id}
                            type="button"
                            role="option"
                            aria-selected={picker.pickedWindow?.id === w.id}
                            className={`home__picker-item${picker.pickedWindow?.id === w.id ? ' home__picker-item--on' : ''}`}
                            onClick={() => {
                              picker.setPickedWindow(w);
                              picker.setPickerOpen(false);
                            }}
                          >
                            {w.app && <span className="home__picker-app">{w.app}</span>}
                            <span className="home__picker-name">{w.title || '(untitled)'}</span>
                          </button>
                        ))
                      ) : (
                        <p className="home__picker-empty">
                          {picker.targetsLoading ? 'Loading…' : 'No windows found'}
                        </p>
                      )
                    ) : picker.targets?.monitors.length ? (
                      picker.targets.monitors.map((m) => (
                        <button
                          key={m.id}
                          type="button"
                          role="option"
                          aria-selected={picker.pickedMonitorId === m.id}
                          className={`home__picker-item${picker.pickedMonitorId === m.id ? ' home__picker-item--on' : ''}`}
                          onClick={() => {
                            picker.setPickedMonitorId(m.id);
                            picker.setPickerOpen(false);
                          }}
                        >
                          <span className="home__picker-name">{m.name}</span>
                          <span className="home__picker-app">
                            {m.width}×{m.height}
                            {m.isPrimary ? ' · primary' : ''}
                          </span>
                        </button>
                      ))
                    ) : (
                      <p className="home__picker-empty">
                        {picker.targetsLoading ? 'Loading…' : 'No monitors found'}
                      </p>
                    )}
                  </div>
                </div>
              </>
            )}
          </div>
        )}

        {mode === 'area' && (
          <div className="capmode__picker capmodal__area">
            <button
              type="button"
              className="btn"
              onClick={() => void picker.selectArea()}
              disabled={selectingArea}
            >
              {selectingArea ? 'Selecting…' : picker.pickedArea ? 'Re-select area' : 'Select area…'}
            </button>
            {picker.pickedArea && (
              <span className="capmode__area">
                {picker.pickedArea.width} × {picker.pickedArea.height}px @ ({picker.pickedArea.x},{' '}
                {picker.pickedArea.y})
              </span>
            )}
          </div>
        )}

        {mode === 'window' && !picker.pickedWindow && (
          <p className="capmode__warn">Pick a window above to capture.</p>
        )}
        {mode === 'area' && !picker.pickedArea && (
          <p className="capmode__warn">Drag out the area you want to capture.</p>
        )}

        {err && <p className="capmodal__err">{err}</p>}

        <div className="sop__modal-actions">
          <button type="button" className="btn" onClick={onClose}>
            Cancel
          </button>
          <button
            type="button"
            className="btn btn--primary"
            disabled={!modeReady || selectingArea}
            onClick={confirm}
          >
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}
