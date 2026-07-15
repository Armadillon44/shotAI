import React from 'react';

// One overflow/dropdown menu used app-wide (project rows, report steps, …) so
// the three ad-hoc dropdown implementations collapse into a single component
// and a single .menu stylesheet block. A custom popover (not a native menu)
// because native popups are unreliable on the software-render VM.

export type MenuItem =
  | { kind: 'sep' }
  | { kind: 'header'; label: React.ReactNode }
  | {
      label: React.ReactNode;
      onClick: () => void;
      danger?: boolean;
      disabled?: boolean;
    };

export function OverflowMenu({
  items,
  label = '⋯',
  title = 'More actions',
  disabled = false,
  triggerClassName = 'btn btn--small btn--ghost btn--icon',
}: {
  items: MenuItem[];
  label?: React.ReactNode;
  title?: string;
  disabled?: boolean;
  /** Override the trigger button's classes (e.g. to match a toolbar button). */
  triggerClassName?: string;
}): React.JSX.Element {
  const [open, setOpen] = React.useState(false);
  // Flip the popover upward when there isn't room below the trigger (e.g. a row
  // near the bottom of the window) so it doesn't fly off-screen / force scroll.
  const [dropUp, setDropUp] = React.useState(false);
  const btnRef = React.useRef<HTMLButtonElement | null>(null);

  // Close on Escape while open.
  React.useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  const onTrigger = () => {
    if (!open && btnRef.current) {
      const rect = btnRef.current.getBoundingClientRect();
      const estHeight = items.length * 34 + 16; // ~item height + padding
      const below = window.innerHeight - rect.bottom;
      setDropUp(below < estHeight && rect.top > below);
    }
    setOpen((o) => !o);
  };

  return (
    <span className="menu">
      <button
        ref={btnRef}
        type="button"
        className={triggerClassName}
        aria-haspopup="menu"
        aria-expanded={open}
        title={title}
        disabled={disabled}
        onClick={onTrigger}
      >
        {label}
      </button>
      {open && (
        <>
          <div className="menu__backdrop" onClick={() => setOpen(false)} />
          <div className={`menu__pop${dropUp ? ' menu__pop--up' : ''}`} role="menu">
            {items.map((it, i) =>
              'kind' in it ? (
                it.kind === 'sep' ? (
                  <div key={i} className="menu__sep" />
                ) : (
                  <div key={i} className="menu__header" role="presentation">
                    {it.label}
                  </div>
                )
              ) : (
                <button
                  key={i}
                  type="button"
                  role="menuitem"
                  className={`menu__item${it.danger ? ' menu__item--danger' : ''}`}
                  disabled={it.disabled}
                  onClick={() => {
                    setOpen(false);
                    it.onClick();
                  }}
                >
                  {it.label}
                </button>
              ),
            )}
          </div>
        </>
      )}
    </span>
  );
}
