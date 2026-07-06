// Pure auto-caption helpers for click steps — extracted from CaptureController
// so the phrasing is unit-testable without electron / native modules.
import type { StepClick, StepElement } from '../shared/project';

/** A friendly noun for a UIA control type, for captions ('' = omit the noun). */
export function controlWord(ct: string | null | undefined): string {
  switch (ct) {
    case 'Button':
    case 'SplitButton':
      return 'button';
    case 'Hyperlink':
      return 'link';
    case 'CheckBox':
      return 'checkbox';
    case 'RadioButton':
      return 'option';
    case 'Tab':
    case 'TabItem':
      return 'tab';
    case 'Edit':
      return 'field';
    case 'ComboBox':
      return 'dropdown';
    case 'ListItem':
    case 'TreeItem':
    case 'DataItem':
      return 'item';
    case 'Slider':
      return 'slider';
    case 'Spinner':
      return 'spinner';
    default:
      return '';
  }
}

/**
 * Auto-caption for a click step. Names the clicked UI element when one resolved
 * (e.g. "Click 'OK' button in <app>"); otherwise falls back to window-only
 * phrasing.
 */
export function buildClickCaption(
  button: StepClick['button'],
  isMenuSelect: boolean,
  appName: string,
  element: StepElement | null,
): string {
  const name = element?.name;
  if (name) {
    // Only call it a menu selection when the clicked element really IS a menu
    // item. The proximity gate sometimes flags a click in a dialog the menu
    // opened (e.g. an OK button in a Properties dialog) as a "selection" — caption
    // it as the actual control instead of "Select from context menu".
    if (element?.controlType === 'MenuItem') return `Select '${name}' in ${appName}`;
    const word = controlWord(element?.controlType);
    const tail = word ? ` ${word}` : '';
    return button === 'right'
      ? `Right-click '${name}'${tail} in ${appName}`
      : `Click '${name}'${tail} in ${appName}`;
  }
  if (button === 'right') return `Right-click in ${appName}`;
  if (isMenuSelect) return `Select from context menu in ${appName}`;
  return `Click in ${appName}`;
}
