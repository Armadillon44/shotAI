/**
 * ClaudeService — the main-process boundary to the Anthropic API for SOP
 * generation. Phase 3a ships the key-connectivity test plus the system-prompt /
 * request-param helpers; the actual generation (vision + structured output +
 * streaming) lands in 3b and will build on `buildSystemPrompt` + `shapeParams`.
 *
 * The API key never leaves main (read here via secrets.getApiKey) and is never
 * logged. Model + tone come from the user's SOP settings.
 */
import Anthropic from '@anthropic-ai/sdk';
import type { SopModelId } from '../shared/sop';
import type { TestKeyResult } from '../shared/ipc';
import { getApiKey } from './secrets';
import { getSopSettings } from './settings';
import { MODEL_PARAMS, TONE_PROMPT, type ModelParams } from './claude-models';
import { claudeLog } from './logger';

const BASE_SYSTEM_PROMPT = [
  'You are an expert technical writer. You are given an ordered sequence of steps captured while a user performed a task on their computer — each step is a screenshot (with the click location marked) plus metadata (the application/window, an auto-generated caption, and any user note), interleaved with author-written text blocks.',
  'Produce a clear, accurate Standard Operating Procedure (SOP) that another person could follow to reproduce the task: a title, a short overview of the goal, any prerequisites, and a numbered list of steps with precise instructions and cautions where appropriate.',
  'Ground every instruction in what the screenshots and metadata actually show — never invent UI elements, values, or steps that are not evidenced. Preserve and integrate the author-written text blocks as prose. Do not transcribe or guess at any redacted/blurred regions.',
].join('\n\n');

/** Map an SDK error to a short, user-facing message (never leaks the key). */
function friendlyError(e: unknown): string {
  if (e instanceof Anthropic.AuthenticationError) return 'Invalid API key.';
  if (e instanceof Anthropic.PermissionDeniedError)
    return 'This key lacks permission for the selected model.';
  if (e instanceof Anthropic.NotFoundError)
    return 'The selected model is unavailable for this key.';
  if (e instanceof Anthropic.RateLimitError)
    return 'Rate limited — wait a moment and try again.';
  if (e instanceof Anthropic.APIConnectionError)
    return 'Could not reach Anthropic — check your network connection.';
  if (e instanceof Anthropic.APIError)
    return e.message || `API error${e.status ? ` (${e.status})` : ''}.`;
  return e instanceof Error ? e.message : String(e);
}

/**
 * Validate the configured key + model with a cheap, free call (Models API). Does
 * NOT run when SOP generation is disabled (no network when the feature is off).
 */
export async function testKey(): Promise<TestKeyResult> {
  const sop = await getSopSettings();
  if (!sop.enabled) return { ok: false, error: 'AI SOP generation is turned off.' };
  const key = await getApiKey();
  if (!key) return { ok: false, error: 'No API key set.' };
  try {
    const client = new Anthropic({ apiKey: key });
    await client.models.retrieve(sop.model);
    claudeLog.info(`API key validated against ${sop.model}.`);
    return { ok: true, model: sop.model };
  } catch (e) {
    const error = friendlyError(e);
    claudeLog.warn(`testKey failed: ${error}`);
    return { ok: false, error };
  }
}

/** Compose the system prompt from the base instructions + tone + custom guidance. */
export async function buildSystemPrompt(): Promise<string> {
  const sop = await getSopSettings();
  const parts = [BASE_SYSTEM_PROMPT, TONE_PROMPT[sop.tone]];
  const custom = sop.customInstructions.trim();
  if (custom) parts.push(`Additional instructions from the user:\n${custom}`);
  return parts.join('\n\n');
}

/** Per-model request parameters (thinking/effort/max_tokens/pricing). */
export function shapeParams(model: SopModelId): ModelParams {
  return MODEL_PARAMS[model];
}
