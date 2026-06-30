// Shared, dismissable, translucent notice — hovers OVER content instead of
// shifting the layout. Used by the editor and the project view (replaces the
// old inline `project__error` / editor `error` block banners that pushed every
// other UI element down). Place one or more <Notice> inside a `.notice-stack`
// container that sits in a `position: relative` parent.
import React from 'react';
import './notice.css';

export type NoticeKind = 'error' | 'info' | 'success';

export interface NoticeData {
  kind: NoticeKind;
  text: string;
}

export function Notice({
  kind = 'error',
  children,
  onDismiss,
}: {
  kind?: NoticeKind;
  children: React.ReactNode;
  onDismiss: () => void;
}): React.JSX.Element {
  return (
    <div className={`notice notice--${kind}`} role="status">
      <span className="notice__text">{children}</span>
      <button
        type="button"
        className="notice__dismiss"
        aria-label="Dismiss"
        title="Dismiss"
        onClick={onDismiss}
      >
        ×
      </button>
    </div>
  );
}
