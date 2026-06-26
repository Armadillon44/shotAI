/**
 * SOP (Standard Operating Procedure) domain — shared across main, preload, and
 * renderer. Phase 3a: the user-facing generation SETTINGS plus the model/tone
 * option lists the Settings UI renders. Per-model pricing + request-param shaping
 * live MAIN-side in `src/main/claude-models.ts`; the generated `SopDoc` shape
 * lands here in 3b.
 */

/** Models offered for SOP generation (curated allowlist — a bad id can't reach the API). */
export type SopModelId = 'claude-sonnet-4-6' | 'claude-opus-4-8';

export interface SopModelOption {
  id: SopModelId;
  label: string;
  /** One-line description shown under the picker. */
  blurb: string;
}

export const SOP_MODELS: readonly SopModelOption[] = [
  {
    id: 'claude-sonnet-4-6',
    label: 'Sonnet 4.6 — balanced (recommended)',
    blurb: 'Fast and cost-effective; great for most SOPs.',
  },
  {
    id: 'claude-opus-4-8',
    label: 'Opus 4.8 — most capable',
    blurb: 'Highest quality for complex procedures; slower and pricier.',
  },
];

export const DEFAULT_SOP_MODEL: SopModelId = 'claude-sonnet-4-6';

export function isSopModel(v: unknown): v is SopModelId {
  return typeof v === 'string' && SOP_MODELS.some((m) => m.id === v);
}

/** Output tone — maps to a system-prompt modifier (main-side). */
export type SopTone = 'professional' | 'friendly' | 'concise' | 'detailed';

export interface SopToneOption {
  id: SopTone;
  label: string;
  blurb: string;
}

export const SOP_TONES: readonly SopToneOption[] = [
  { id: 'professional', label: 'Professional', blurb: 'Formal, third-person, SOP-standard phrasing.' },
  { id: 'friendly', label: 'Friendly', blurb: 'Warm, second-person, approachable.' },
  { id: 'concise', label: 'Concise', blurb: 'Minimal words, action-first.' },
  { id: 'detailed', label: 'Detailed', blurb: 'Thorough; explains the "why" and adds context.' },
];

export const DEFAULT_SOP_TONE: SopTone = 'professional';

export function isSopTone(v: unknown): v is SopTone {
  return typeof v === 'string' && SOP_TONES.some((t) => t.id === v);
}

/** Cap on the optional free-text custom instructions appended to the system prompt. */
export const SOP_CUSTOM_INSTRUCTIONS_MAX = 2000;

/**
 * User-facing SOP generation settings (NON-SECRET — persisted in settings.json).
 * The API key is NOT here; it lives encrypted via safeStorage (see secrets.ts).
 */
export interface SopSettings {
  /** Master switch for all Claude SOP features. Off ⇒ no Claude UI, no network. */
  enabled: boolean;
  model: SopModelId;
  tone: SopTone;
  /** Optional extra system-prompt guidance, appended verbatim (length-capped). */
  customInstructions: string;
}

export const DEFAULT_SOP_SETTINGS: SopSettings = {
  enabled: true,
  model: DEFAULT_SOP_MODEL,
  tone: DEFAULT_SOP_TONE,
  customInstructions: '',
};

/**
 * Validate/coerce a possibly-partial SopSettings from an untrusted source (IPC
 * boundary or on-disk manifest) onto a known-good base. Unknown model/tone values
 * fall back to the base; customInstructions is string-coerced and length-capped.
 */
export function coerceSopSettings(
  raw: unknown,
  base: SopSettings = DEFAULT_SOP_SETTINGS,
): SopSettings {
  const r = (raw && typeof raw === 'object' ? raw : {}) as Record<string, unknown>;
  return {
    enabled: typeof r.enabled === 'boolean' ? r.enabled : base.enabled,
    model: isSopModel(r.model) ? r.model : base.model,
    tone: isSopTone(r.tone) ? r.tone : base.tone,
    customInstructions:
      typeof r.customInstructions === 'string'
        ? r.customInstructions.slice(0, SOP_CUSTOM_INSTRUCTIONS_MAX)
        : base.customInstructions,
  };
}
