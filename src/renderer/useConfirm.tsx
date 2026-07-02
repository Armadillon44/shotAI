// In-app confirmation/alert dialogs (promise-based) to REPLACE native
// window.confirm / window.alert.
//
// Native confirm/alert steal the BrowserWindow's keyboard focus on this Electron
// build: after the dialog closes, mouse clicks work but text fields won't accept
// typing until the window is re-focused (minimize/restore) — which made callouts
// look un-editable after deleting a step (B4). webContents.focus() from main does
// NOT reliably restore it on Windows. A DOM modal involves no native dialog, so
// focus never leaves the page.
//
// Usage:
//   const { confirm, alert, confirmModal } = useConfirm();
//   ... if (!(await confirm('Delete this?', { danger: true }))) return;
//   return (<>{confirmModal}{/* rest */}</>);
import React from 'react';
import { createPortal } from 'react-dom';

interface ConfirmOpts {
  confirmLabel?: string;
  danger?: boolean;
}
interface ConfirmState extends ConfirmOpts {
  message: string;
  alertOnly: boolean;
  resolve: (ok: boolean) => void;
}

export function useConfirm(): {
  confirm: (message: string, opts?: ConfirmOpts) => Promise<boolean>;
  alert: (message: string) => Promise<void>;
  confirmModal: React.ReactNode;
} {
  const [state, setState] = React.useState<ConfirmState | null>(null);

  const confirm = React.useCallback(
    (message: string, opts?: ConfirmOpts) =>
      new Promise<boolean>((resolve) => setState({ message, alertOnly: false, resolve, ...opts })),
    [],
  );

  const alert = React.useCallback(
    (message: string) =>
      new Promise<void>((resolve) =>
        setState({ message, alertOnly: true, confirmLabel: 'OK', resolve: () => resolve() }),
      ),
    [],
  );

  const close = (ok: boolean) => {
    setState((s) => {
      s?.resolve(ok);
      return null;
    });
  };

  const confirmModal = state
    ? createPortal(
        <div
          className="sop__overlay"
          role="dialog"
          aria-modal="true"
          aria-label="Confirm"
          onKeyDown={(e) => {
            if (e.key === 'Escape') close(false);
          }}
        >
          <div className="confirm">
            <p className="confirm__msg">{state.message}</p>
            <div className="confirm__actions">
              {!state.alertOnly && (
                <button type="button" className="btn" onClick={() => close(false)}>
                  Cancel
                </button>
              )}
              <button
                type="button"
                className={`btn ${state.danger ? 'btn--danger' : 'btn--primary'}`}
                onClick={() => close(true)}
                autoFocus
              >
                {state.confirmLabel ?? 'OK'}
              </button>
            </div>
          </div>
        </div>,
        document.body,
      )
    : null;

  return { confirm, alert, confirmModal };
}
