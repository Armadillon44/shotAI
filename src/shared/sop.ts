/**
 * SOP (Standard Operating Procedure) domain — shared across main, preload, and
 * renderer. Phase 3a: the user-facing generation SETTINGS plus the model/tone
 * option lists the Settings UI renders. Per-model pricing + request-param shaping
 * live MAIN-side in `src/main/claude-models.ts`; the generated `SopDoc` shape
 * lands here in 3b.
 */

/** Models offered for SOP generation (curated allowlist — a bad id can't reach the API). */
export type SopModelId = 'claude-sonnet-5';

export interface SopModelOption {
  id: SopModelId;
  label: string;
  /** One-line description shown under the picker. */
  blurb: string;
}

export const SOP_MODELS: readonly SopModelOption[] = [
  {
    id: 'claude-sonnet-5',
    label: 'Sonnet 5 — latest (recommended)',
    blurb: 'Anthropic’s latest Sonnet: capable, fast, and cost-effective.',
  },
];

export const DEFAULT_SOP_MODEL: SopModelId = 'claude-sonnet-5';

export function isSopModel(v: unknown): v is SopModelId {
  return typeof v === 'string' && SOP_MODELS.some((m) => m.id === v);
}

/** Generation effort — maps to `output_config.effort` main-side (higher = more
 *  deliberation, slower, pricier). User-configurable in Settings. */
export type SopEffort = 'low' | 'medium' | 'high';

export interface SopEffortOption {
  id: SopEffort;
  label: string;
  blurb: string;
}

export const SOP_EFFORTS: readonly SopEffortOption[] = [
  { id: 'low', label: 'Low', blurb: 'Fastest and cheapest; least deliberation.' },
  { id: 'medium', label: 'Medium', blurb: 'Balanced quality, speed, and cost (recommended).' },
  { id: 'high', label: 'High', blurb: 'Most thorough; slower and pricier.' },
];

export const DEFAULT_SOP_EFFORT: SopEffort = 'medium';

export function isSopEffort(v: unknown): v is SopEffort {
  return typeof v === 'string' && SOP_EFFORTS.some((e) => e.id === v);
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
  /** Generation effort (output_config.effort) — user-configurable. */
  effort: SopEffort;
  /** Optional extra system-prompt guidance, appended verbatim (length-capped). */
  customInstructions: string;
}

export const DEFAULT_SOP_SETTINGS: SopSettings = {
  enabled: true,
  model: DEFAULT_SOP_MODEL,
  tone: DEFAULT_SOP_TONE,
  effort: DEFAULT_SOP_EFFORT,
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
    effort: isSopEffort(r.effort) ? r.effort : base.effort,
    customInstructions:
      typeof r.customInstructions === 'string'
        ? r.customInstructions.slice(0, SOP_CUSTOM_INSTRUCTIONS_MAX)
        : base.customInstructions,
  };
}

// --- Inline SOP edit plan (Phase 3b) ---
// Claude does NOT produce a separate document; it returns an edit plan that is
// applied IN-LINE to the project's steps (rewriting each screenshot step's
// heading/instruction/caption and inserting intro + section text steps).
// A pre-edit snapshot is kept for one-click revert (see SopBackup in project.ts).

/** An edit Claude proposes for one screenshot step (referenced by its 1-based number). */
export interface SopStepEdit {
  /** 1-based number of the screenshot step (as shown to Claude) this applies to. */
  stepNumber: number;
  /** The step title (short, action-oriented) shown above the screenshot. */
  caption: string;
  /** Instruction text (the detail) shown under the screenshot. */
  body: string;
  /** If set, insert a section-heading text step immediately BEFORE this step. */
  sectionHeading: string | null;
  sectionBody: string | null;
}

/** The full inline edit plan Claude returns, applied transactionally by the store. */
export interface SopEditPlan {
  /** Refined project title, or null to keep the current one. */
  title: string | null;
  /** Optional leading intro inserted as a text step at the top. */
  intro: { heading: string; body: string } | null;
  /** Per-screenshot-step edits. */
  steps: SopStepEdit[];
}
