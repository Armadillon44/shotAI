/**
 * MAIN-side model + tone configuration for SOP generation. This is the single
 * source for per-model request shaping and pricing — the picker only offers ids
 * from `SOP_MODELS` (shared), and a bad/unsupported id can never reach the API.
 *
 * Why a map: thinking/effort support differs by model. Today both offered models
 * take adaptive thinking + effort, but a future model where `effort` 400s would
 * declare `effort: null` here and the request shaper would simply omit it — no
 * call-site change.
 */
import type { SopModelId, SopTone } from '../shared/sop';

export interface ModelParams {
  /** Adaptive thinking config, or null to omit the `thinking` param entirely. */
  thinking: { type: 'adaptive' } | null;
  /** Effort level, or null to omit `output_config.effort`. */
  effort: 'low' | 'medium' | 'high' | null;
  /** USD per 1M input/output tokens — feeds the pre-send cost estimate (3b). */
  inputPerMTok: number;
  outputPerMTok: number;
  /** Output cap for the (streamed) generation request. */
  maxTokens: number;
}

export const MODEL_PARAMS: Record<SopModelId, ModelParams> = {
  'claude-sonnet-4-6': {
    thinking: { type: 'adaptive' },
    effort: 'medium',
    inputPerMTok: 3,
    outputPerMTok: 15,
    maxTokens: 16000,
  },
  'claude-opus-4-8': {
    thinking: { type: 'adaptive' },
    effort: 'high',
    inputPerMTok: 5,
    outputPerMTok: 25,
    maxTokens: 16000,
  },
};

/** System-prompt modifier per tone (appended to the base SOP instructions). */
export const TONE_PROMPT: Record<SopTone, string> = {
  professional:
    'Write in a formal, professional register suitable for a corporate Standard Operating Procedure. Use clear, third-person imperative instructions.',
  friendly:
    'Write in a warm, approachable, second-person voice ("you") as if guiding a colleague through the task, while staying clear and accurate.',
  concise:
    'Write as concisely as possible. Use short, action-first instructions and omit unnecessary words and background.',
  detailed:
    'Write thoroughly. Explain the purpose behind each step and include the context, prerequisites, and cautions a newcomer would need.',
};
