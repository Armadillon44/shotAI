// Shared capture mode/target picker logic — the state + helpers behind choosing
// what a capture grabs (a full Screen, one Window, a dragged Area, or Auto) and
// assembling the CaptureTarget for the main process. Extracted so the report's
// "+ Capture / + Screenshot" insert modal can reuse exactly what the home screen
// does (App.tsx still owns its own inline copy for the create action).
import React from 'react';
import type {
  CaptureMode,
  CaptureTarget,
  MonitorInfo,
  Rect,
  WindowInfo,
} from '../../shared/project';

export type CaptureModeOption = {
  mode: CaptureMode;
  label: string;
  hint: string;
};

/** All four modes, in the canonical order. A picker narrows this via allowedModes. */
export const CAPTURE_MODE_OPTIONS: CaptureModeOption[] = [
  { mode: 'screen', label: 'Screen', hint: 'Capture one full monitor' },
  { mode: 'auto', label: 'Auto', hint: 'Best-effort smart capture — may include extra/unintended context' },
  { mode: 'window', label: 'Window', hint: 'Capture one specific window' },
  { mode: 'area', label: 'Area', hint: 'Drag-select a fixed region to capture' },
];

type Targets = { windows: WindowInfo[]; monitors: MonitorInfo[] };

export type CapturePicker = {
  mode: CaptureMode;
  options: CaptureModeOption[];
  selectMode: (m: CaptureMode) => void;
  targets: Targets | null;
  targetsLoading: boolean;
  loadTargets: () => Promise<void>;
  pickedWindow: WindowInfo | null;
  setPickedWindow: (w: WindowInfo) => void;
  pickedMonitorId: number | null;
  setPickedMonitorId: (id: number) => void;
  pickedArea: Rect | null;
  selectArea: () => Promise<void>;
  selectingArea: boolean;
  pickerOpen: boolean;
  setPickerOpen: React.Dispatch<React.SetStateAction<boolean>>;
  pickerLabel: string;
  /** Whether the current mode has everything it needs to build a valid target. */
  modeReady: boolean;
  buildTarget: () => CaptureTarget;
};

/**
 * Own the capture-mode selection state. `allowedModes` narrows the offered modes
 * (e.g. the no-click screenshot flow omits 'auto'); `initialMode` seeds it. The
 * window/monitor list loads lazily the first time a mode that needs it is active.
 */
export function useCaptureTarget(opts?: {
  allowedModes?: CaptureMode[];
  initialMode?: CaptureMode;
  onError?: (e: unknown) => void;
}): CapturePicker {
  const allowed = opts?.allowedModes;
  const options = React.useMemo(
    () => (allowed ? CAPTURE_MODE_OPTIONS.filter((o) => allowed.includes(o.mode)) : CAPTURE_MODE_OPTIONS),
    [allowed],
  );
  const onError = opts?.onError;

  const [mode, setMode] = React.useState<CaptureMode>(opts?.initialMode ?? options[0]?.mode ?? 'screen');
  const [targets, setTargets] = React.useState<Targets | null>(null);
  const [targetsLoading, setTargetsLoading] = React.useState(false);
  const [pickedWindow, setPickedWindow] = React.useState<WindowInfo | null>(null);
  const [pickedMonitorId, setPickedMonitorId] = React.useState<number | null>(null);
  const [pickedArea, setPickedArea] = React.useState<Rect | null>(null);
  const [selectingArea, setSelectingArea] = React.useState(false);
  const [pickerOpen, setPickerOpen] = React.useState(false);

  const loadTargets = React.useCallback(async () => {
    setTargetsLoading(true);
    try {
      const t = await window.shotai.capture.listTargets();
      setTargets(t);
      // Keep the prior pick if it's still around, otherwise sensible defaults.
      setPickedWindow((prev) =>
        prev && t.windows.some((w) => w.id === prev.id) ? prev : t.windows[0] ?? null,
      );
      setPickedMonitorId((prev) =>
        prev != null && t.monitors.some((m) => m.id === prev)
          ? prev
          : (t.monitors.find((m) => m.isPrimary) ?? t.monitors[0])?.id ?? null,
      );
    } catch (e) {
      onError?.(e);
    } finally {
      setTargetsLoading(false);
    }
  }, [onError]);

  const selectMode = React.useCallback(
    (m: CaptureMode) => {
      setMode(m);
      setPickerOpen(false);
      if ((m === 'window' || m === 'screen') && !targets) void loadTargets();
    },
    [targets, loadTargets],
  );

  // Load the window/monitor list once if the initial mode needs it (so a modal
  // opening straight on Screen/Window pre-populates without a manual mode click).
  const initialLoad = React.useRef(false);
  React.useEffect(() => {
    if (initialLoad.current) return;
    initialLoad.current = true;
    if ((mode === 'window' || mode === 'screen') && !targets) void loadTargets();
  }, [mode, targets, loadTargets]);

  const selectArea = React.useCallback(async () => {
    setSelectingArea(true);
    try {
      const r = await window.shotai.region.selectArea();
      if (r) setPickedArea(r);
    } catch (e) {
      onError?.(e);
    } finally {
      setSelectingArea(false);
    }
  }, [onError]);

  const pickerLabel =
    mode === 'window'
      ? pickedWindow
        ? `${pickedWindow.app ? `${pickedWindow.app} — ` : ''}${pickedWindow.title || '(untitled)'}`
        : targetsLoading
          ? 'Loading…'
          : 'Select a window…'
      : (() => {
          const m = targets?.monitors.find((mm) => mm.id === pickedMonitorId);
          return m
            ? `${m.name} · ${m.width}×${m.height}${m.isPrimary ? ' · primary' : ''}`
            : targetsLoading
              ? 'Loading…'
              : 'Whole screen (primary monitor)';
        })();

  // 'window' needs a picked window and 'area' a selected rect; Screen defaults to
  // the primary monitor and Auto needs nothing, so both are ready immediately.
  const modeReady = mode === 'window' ? !!pickedWindow : mode === 'area' ? !!pickedArea : true;

  const buildTarget = React.useCallback((): CaptureTarget => {
    switch (mode) {
      case 'window':
        return pickedWindow
          ? { mode: 'window', window: { id: pickedWindow.id, pid: pickedWindow.pid, title: pickedWindow.title } }
          : { mode: 'auto' };
      case 'screen':
        return pickedMonitorId != null ? { mode: 'screen', monitorId: pickedMonitorId } : { mode: 'screen' };
      case 'area':
        return pickedArea ? { mode: 'area', area: pickedArea } : { mode: 'auto' };
      default:
        return { mode: 'auto' };
    }
  }, [mode, pickedWindow, pickedMonitorId, pickedArea]);

  return {
    mode,
    options,
    selectMode,
    targets,
    targetsLoading,
    loadTargets,
    pickedWindow,
    setPickedWindow,
    pickedMonitorId,
    setPickedMonitorId,
    pickedArea,
    selectArea,
    selectingArea,
    pickerOpen,
    setPickerOpen,
    pickerLabel,
    modeReady,
    buildTarget,
  };
}
