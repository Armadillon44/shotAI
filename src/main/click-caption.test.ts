import { describe, it, expect } from 'vitest';
import { controlWord, buildClickCaption } from './click-caption';
import type { StepElement } from '../shared/project';

const el = (name: string | null, controlType: string | null): StepElement => ({
  available: true,
  name,
  controlType,
  bounds: null,
});

describe('controlWord', () => {
  it('maps known control types to friendly nouns', () => {
    expect(controlWord('Button')).toBe('button');
    expect(controlWord('SplitButton')).toBe('button');
    expect(controlWord('Hyperlink')).toBe('link');
    expect(controlWord('Edit')).toBe('field');
    expect(controlWord('ComboBox')).toBe('dropdown');
    expect(controlWord('ListItem')).toBe('item');
  });
  it('returns "" for unknown / null (omit the noun)', () => {
    expect(controlWord('MenuItem')).toBe('');
    expect(controlWord('Whatever')).toBe('');
    expect(controlWord(null)).toBe('');
    expect(controlWord(undefined)).toBe('');
  });
});

describe('buildClickCaption', () => {
  it('names a clicked control with its noun', () => {
    expect(buildClickCaption('left', false, 'Notepad', el('OK', 'Button'))).toBe("Click 'OK' button in Notepad");
    expect(buildClickCaption('left', false, 'Chrome', el('Docs', 'Hyperlink'))).toBe("Click 'Docs' link in Chrome");
  });
  it('phrases a MenuItem as a selection', () => {
    expect(buildClickCaption('left', true, 'Notepad', el('Copy', 'MenuItem'))).toBe("Select 'Copy' in Notepad");
  });
  it('right-click named control', () => {
    expect(buildClickCaption('right', false, 'Notepad', el('File', 'Button'))).toBe("Right-click 'File' button in Notepad");
  });
  it('omits the noun when the control type is unknown', () => {
    expect(buildClickCaption('left', false, 'Notepad', el('Thing', null))).toBe("Click 'Thing' in Notepad");
    expect(buildClickCaption('right', false, 'Notepad', el('Thing', 'Custom'))).toBe("Right-click 'Thing' in Notepad");
  });
  it('falls back to window-only phrasing with no element name', () => {
    expect(buildClickCaption('left', false, 'Notepad', null)).toBe('Click in Notepad');
    expect(buildClickCaption('right', false, 'Notepad', null)).toBe('Right-click in Notepad');
    expect(buildClickCaption('left', true, 'Notepad', null)).toBe('Select from context menu in Notepad');
    expect(buildClickCaption('left', false, 'Notepad', el(null, 'Button'))).toBe('Click in Notepad');
  });
});
