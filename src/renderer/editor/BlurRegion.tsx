// A blur/redact region rendered as a LIVE preview that matches flatten.ts — split
// out of Editor.tsx. Presentational (all inputs via props); no editor state.
import React from 'react';
import Konva from 'konva';
import { Image as KImage, Rect as KRect } from 'react-konva';
import type { BlurAnnotation } from '../../shared/project';
import { MIN_REDACT_BLOCK } from './flatten';

/**
 * The region is AVERAGE-downsampled into a tiny canvas, which Konva then upscales
 * smoothly — a soft blur (not hard pixel blocks), with detail destroyed. 'solid'
 * renders a black box. The preview canvas is recomputed when geometry/amount
 * change, so the blur-amount slider shows its real effect.
 */
export function BlurRegion({
  img,
  a,
  draggable,
  onSelect,
  onDragEnd,
  onTransformEnd,
  registerRef,
}: {
  img: HTMLImageElement;
  a: BlurAnnotation;
  draggable: boolean;
  onSelect: () => void;
  onDragEnd: (e: Konva.KonvaEventObject<DragEvent>) => void;
  onTransformEnd: () => void;
  registerRef: (node: Konva.Node | null) => void;
}): React.JSX.Element {
  const preview = React.useMemo(() => {
    if (a.mode !== 'pixelate') return null;
    const w = Math.max(1, Math.round(a.width));
    const h = Math.max(1, Math.round(a.height));
    const block = Math.max(MIN_REDACT_BLOCK, Math.round(a.blockSize));
    const sw = Math.max(1, Math.round(w / block));
    const sh = Math.max(1, Math.round(h / block));
    const small = document.createElement('canvas');
    small.width = sw;
    small.height = sh;
    const sctx = small.getContext('2d');
    if (!sctx) return null;
    sctx.imageSmoothingEnabled = true;
    try {
      sctx.drawImage(img, a.x, a.y, a.width, a.height, 0, 0, sw, sh);
    } catch {
      return null;
    }
    // Rebuild at full size matching flatten.ts: opaque base + soft Gaussian.
    const out = document.createElement('canvas');
    out.width = w;
    out.height = h;
    const octx = out.getContext('2d');
    if (!octx) return null;
    octx.imageSmoothingEnabled = true;
    octx.drawImage(small, 0, 0, sw, sh, 0, 0, w, h);
    octx.filter = `blur(${Math.max(1, Math.round(block * 0.6))}px)`;
    octx.drawImage(small, 0, 0, sw, sh, 0, 0, w, h);
    octx.filter = 'none';
    return out;
  }, [img, a.x, a.y, a.width, a.height, a.blockSize, a.mode]);

  if (a.mode === 'solid' || !preview) {
    return (
      <KRect
        ref={(n) => registerRef(n)}
        x={a.x}
        y={a.y}
        width={a.width}
        height={a.height}
        fill={a.mode === 'solid' ? '#000000' : 'rgba(15,23,42,0.55)'}
        draggable={draggable}
        onClick={onSelect}
        onTap={onSelect}
        onDragEnd={onDragEnd}
        onTransformEnd={onTransformEnd}
      />
    );
  }
  return (
    <KImage
      ref={(n) => registerRef(n)}
      image={preview}
      x={a.x}
      y={a.y}
      width={a.width}
      height={a.height}
      draggable={draggable}
      onClick={onSelect}
      onTap={onSelect}
      onDragEnd={onDragEnd}
      onTransformEnd={onTransformEnd}
    />
  );
}
