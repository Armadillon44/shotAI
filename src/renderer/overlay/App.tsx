import React from 'react';

// Smallest drag (CSS px) we treat as a real selection rather than a stray click.
const MIN_DRAG = 4;

type Pt = { x: number; y: number };

/**
 * Fullscreen drag-select overlay. Reports the dragged rectangle in CSS pixels
 * relative to this window; the main process (RegionService) converts it to
 * global physical pixels. A click without a drag, or Esc, cancels.
 */
export function App(): React.JSX.Element {
  const [start, setStart] = React.useState<Pt | null>(null);
  const [cur, setCur] = React.useState<Pt | null>(null);
  const dragging = start !== null;

  const rect = React.useMemo(() => {
    if (!start || !cur) return null;
    return {
      x: Math.min(start.x, cur.x),
      y: Math.min(start.y, cur.y),
      width: Math.abs(cur.x - start.x),
      height: Math.abs(cur.y - start.y),
    };
  }, [start, cur]);

  React.useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') window.shotai.region.cancel();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const onDown = (e: React.MouseEvent) => {
    if (e.button !== 0) {
      window.shotai.region.cancel();
      return;
    }
    const p = { x: e.clientX, y: e.clientY };
    setStart(p);
    setCur(p);
  };

  const onMove = (e: React.MouseEvent) => {
    if (dragging) setCur({ x: e.clientX, y: e.clientY });
  };

  const onUp = () => {
    if (rect && rect.width >= MIN_DRAG && rect.height >= MIN_DRAG) {
      window.shotai.region.complete(rect);
    } else {
      window.shotai.region.cancel();
    }
  };

  return (
    <div
      className="ov"
      onMouseDown={onDown}
      onMouseMove={onMove}
      onMouseUp={onUp}
    >
      {!dragging && (
        <div className="ov__hint">
          Drag to select a capture area
          <span className="ov__hint-sub">Press Esc to cancel</span>
        </div>
      )}
      {rect && (
        <div
          className="ov__sel"
          style={{
            left: rect.x,
            top: rect.y,
            width: rect.width,
            height: rect.height,
          }}
        >
          {rect.width >= 40 && rect.height >= 22 && (
            <span className="ov__dims">
              {Math.round(rect.width * window.devicePixelRatio)} ×{' '}
              {Math.round(rect.height * window.devicePixelRatio)}px
            </span>
          )}
        </div>
      )}
    </div>
  );
}
