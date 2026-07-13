/**
 * ClaudeService — the main-process boundary to the Anthropic API for SOP
 * generation. Ships the key-connectivity test, the system-prompt helper
 * (`buildSystemPrompt`), and the streaming vision/structured-output generation.
 *
 * The API key never leaves main (read here via secrets.getApiKey) and is never
 * logged. Model + tone come from the user's SOP settings.
 */
import Anthropic from '@anthropic-ai/sdk';
import { zodOutputFormat } from '@anthropic-ai/sdk/helpers/zod';
// The SDK's zod helper targets zod v4's type shape; zod 3.25 ships it at `zod/v4`.
import { z } from 'zod/v4';
import { promises as fs } from 'node:fs';
import type { SopEditPlan, SopModelId } from '../shared/sop';
import type { ProjectManifest } from '../shared/project';
import type { SopEstimate, SopProgress, TestKeyResult } from '../shared/ipc';
import { getApiKey } from './secrets';
import { getSopSettings } from './settings';
import { getProjectForRead } from './project-store';
import { applySopEdits } from './sop-apply';
import { resolveSendableRender } from './render-gate';
import { MODEL_PARAMS, TONE_PROMPT } from './claude-models';
import { claudeLog } from './logger';

/**
 * Structured-output schema Claude fills (main-only). Screenshots are referenced
 * by the 1-based step NUMBER shown in the prompt; we map number → real step id
 * after parsing. Kept within the structured-output subset (no min/max length).
 */
const SopEditSchema = z.object({
  // Always set: every generation should give the project a meaningful title.
  title: z.string(),
  intro: z.object({ heading: z.string(), body: z.string() }).nullable(),
  steps: z.array(
    z.object({
      stepNumber: z.number().int(),
      caption: z.string(),
      body: z.string(),
      sectionHeading: z.string().nullable(),
      sectionBody: z.string().nullable(),
    }),
  ),
});
type SopEdit = z.infer<typeof SopEditSchema>;

/** Rough output-token allowance for the cost estimate (input dominates anyway). */
const EST_OUTPUT_TOKENS = 2500;

const BASE_SYSTEM_PROMPT = [
  'You are an expert technical writer turning a captured screen recording into a polished Standard Operating Procedure (SOP) by EDITING the project in place. You are given an ordered sequence of steps: each screenshot step (labeled "Screenshot step N") has the exact click point marked on the image with a colored ring (a circle), plus metadata (application/window, an auto-generated caption, any user note); author-written "Text step" entries are interleaved.',
'Return an edit plan (structured output) that improves the project IN-LINE: for every screenshot step, write a concise, action-oriented `caption` (the step title, e.g. "Open the navigation menu") and a clear instruction `body` (the detail the reader follows); reference each screenshot by its number via `stepNumber`. You may add a leading `intro` (heading + body) and, where the procedure shifts to a new phase, a `sectionHeading`/`sectionBody` inserted before a step. Always set `title` to a clear, descriptive name for the overall procedure.',
  'Write each instruction about the control inside or directly under the marked ring — that ring is exactly where the user clicked, so describe THAT element, not some other field on the screen. If the screenshot does not show the result of the click (e.g. a menu or dropdown that opened only after clicking is not visible), describe the click itself and do not invent the resulting menu or its contents.',
  'Some steps include a "UI element" line in their metadata — the accessibility name and control type (e.g. Button, MenuItem, Hyperlink) of the control under the click, read from the operating system. Treat it as a STRONG, reliable signal for WHICH control was clicked. But choose the FRIENDLIEST name for the reader, based primarily on the screenshot: the accessibility name is occasionally technical or internal (e.g. an internal class/identifier, an overly long tooltip, or a developer string) rather than the label a person sees. When the element name and the visible on-screen label differ, name the control by what the user actually sees on screen; use the accessibility name only to confirm the target or when no clear visible label exists.',
  'Keep the screenshots in their original order — do not drop, reorder, or merge them; you only rewrite their text and insert text blocks between them. Ground every instruction in what the screenshots and metadata actually show; never invent UI elements, values, or steps that are not evidenced. Leave author-written text steps alone. Do not transcribe or guess at any redacted/blurred regions of the images.',
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

// Pin the Anthropic egress host. Without an explicit baseURL the SDK defaults to
// process.env.ANTHROPIC_BASE_URL, which would let a poisoned environment redirect
// the API key (x-api-key header) AND the captured screenshots to an attacker host.
// shotAI only ever talks to the real API.
const ANTHROPIC_BASE_URL = 'https://api.anthropic.com';
function makeClient(apiKey: string): Anthropic {
  return new Anthropic({ apiKey, baseURL: ANTHROPIC_BASE_URL });
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
    const client = makeClient(key);
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

interface AssembledRequest {
  system: Anthropic.TextBlockParam[];
  messages: Anthropic.MessageParam[];
  manifest: ProjectManifest;
  model: SopModelId;
}

/**
 * Build the Claude request from a project: a cached system prompt + one user
 * message interleaving each step's image (the flattened/redacted render) and its
 * metadata, with author text steps as prose. REDACTION-ENFORCED: a shot step with
 * a blur annotation but no flattened render throws rather than send raw pixels.
 */
async function assembleRequest(projectPath: string): Promise<AssembledRequest> {
  const settings = await getSopSettings();
  const { dir, manifest } = await getProjectForRead(projectPath);
  // Exclude text steps a prior run inserted — Claude never sees its own inserts,
  // so regeneration doesn't compound and the numbering matches applySopEdits.
  const source = manifest.steps.filter((s) => !s.aiInserted);
  const shotCount = source.filter((s) => s.kind !== 'text').length;
  if (shotCount === 0) {
    throw new Error('This project has no captured screenshots to build an SOP from.');
  }
  // Original (pre-AI) caption/note per step id — so regeneration never feeds Claude
  // its own prior rewrites; it always sees the ground-truth captured text.
  const originalById = manifest.sopBackup
    ? new Map(manifest.sopBackup.steps.map((s) => [s.id, s]))
    : null;

  const system: Anthropic.TextBlockParam[] = [
    { type: 'text', text: await buildSystemPrompt() },
  ];

  const content: Anthropic.ContentBlockParam[] = [
    {
      type: 'text',
      text:
        `Project: ${manifest.title}\n` +
        `The ${source.length} steps below are in order. Write one edit-plan entry ` +
        `per SCREENSHOT step, setting its stepNumber to that step's number. Keep the ` +
        `screenshots in this order. Redactions are already baked into the images — never ` +
        `describe or guess at blurred/obscured areas.`,
    },
  ];

  for (let idx = 0; idx < source.length; idx++) {
    const step = source[idx];
    const n = idx + 1;
    if (step.kind === 'text') {
      const parts = [`--- Text step ${n} (author-written — leave this content alone) ---`];
      if (step.heading) parts.push(`Heading: ${step.heading}`);
      if (step.body) parts.push(`Body: ${step.body}`);
      content.push({ type: 'text', text: parts.join('\n') });
      continue;
    }

    // Fail-closed redaction gate (shared with the export path).
    const { abs, mediaType } = resolveSendableRender(dir, step, `Step ${n}`, 'send');
    const bytes = await fs.readFile(abs);
    content.push({
      type: 'image',
      source: { type: 'base64', media_type: mediaType, data: bytes.toString('base64') },
    });

    const orig = originalById?.get(step.id);
    const caption = orig?.caption ?? step.caption;
    const note = orig?.note ?? step.note;
    const meta = [`--- Screenshot step ${n} ---`];
    if (step.window?.app) meta.push(`App: ${step.window.app}`);
    if (step.window?.title) meta.push(`Window: ${step.window.title}`);
    if (step.element?.available && step.element.name) {
      meta.push(
        `UI element: ${step.element.name}` +
          (step.element.controlType ? ` (${step.element.controlType})` : ''),
      );
    }
    if (step.click) {
      meta.push(`Action: ${step.click.button}-click (the colored ring marks where the user clicked)`);
    }
    if (caption) meta.push(`Auto-caption: ${caption}`);
    if (note) meta.push(`User note: ${note}`);
    content.push({ type: 'text', text: meta.join('\n') });
  }

  content.push({
    type: 'text',
    text: 'Now return the inline edit plan as the structured JSON output.',
    // Cache breakpoint on the last block → caches system + all images + metadata
    // (the large, stable prefix) so a regenerate within the TTL reads them cheaply.
    cache_control: { type: 'ephemeral' },
  });

  return {
    system,
    messages: [{ role: 'user', content }],
    manifest,
    model: settings.model,
  };
}

// Tracks the in-flight estimate/generateSop request so the renderer's Cancel
// button can abort the underlying HTTP mid-flight (the SDK honors the signal).
let currentRun: AbortController | null = null;

/** Abort any in-flight estimate/generateSop request (renderer Cancel button). */
export function cancelClaude(): void {
  currentRun?.abort();
  currentRun = null;
}

/** Estimate input tokens + cost for generating this project's SOP (review screen). */
export async function estimate(projectPath: string): Promise<SopEstimate> {
  // Register the abort controller BEFORE the pre-request awaits (assembleRequest
  // reads + base64-encodes every screenshot — a real cancel window). The outer
  // try/finally guarantees currentRun is cleared; the inner catch keeps the
  // friendly-error mapping around the network call only.
  const controller = new AbortController();
  currentRun = controller;
  try {
    const settings = await getSopSettings();
    if (!settings.enabled) throw new Error('AI SOP generation is turned off.');
    const key = await getApiKey();
    if (!key) throw new Error('No API key set.');
    const { system, messages } = await assembleRequest(projectPath);
    const client = makeClient(key);
    const params = MODEL_PARAMS[settings.model];
    let inputTokens: number;
    try {
      const r = await client.messages.countTokens(
        { model: settings.model, system, messages },
        { signal: controller.signal },
      );
      inputTokens = r.input_tokens;
    } catch (e) {
      throw new Error(friendlyError(e));
    }
    const estCostUsd =
      (inputTokens / 1e6) * params.inputPerMTok +
      (EST_OUTPUT_TOKENS / 1e6) * params.outputPerMTok;
    return { inputTokens, model: settings.model, estCostUsd };
  } finally {
    if (currentRun === controller) currentRun = null;
  }
}

/**
 * Generate the SOP: stream a vision + structured-output request, validate the
 * result against the schema, map screenshot references to real step ids, and
 * persist via saveSop. Returns the updated manifest. Progress via onProgress.
 */
export async function generateSop(
  projectPath: string,
  onProgress?: (p: SopProgress) => void,
): Promise<ProjectManifest> {
  const settings = await getSopSettings();
  if (!settings.enabled) throw new Error('AI SOP generation is turned off.');
  const key = await getApiKey();
  if (!key) throw new Error('No API key set.');

  onProgress?.({ stage: 'preparing' });
  const { system, messages } = await assembleRequest(projectPath);
  const params = MODEL_PARAMS[settings.model];
  const client = makeClient(key);

  const CUTOFF_MSG =
    'The SOP was cut off at the output limit. Try again, or split the project into fewer steps.';

  let finalText: string;
  // Track the stop reason from the stream: finalMessage() runs the SDK's
  // structured-output parse and REJECTS on truncated JSON before we can read
  // msg.stop_reason, so we capture it from the message_delta event too.
  let stopReason: string | null = null;
  const controller = new AbortController();
  currentRun = controller;
  try {
    const stream = client.messages.stream(
      {
        model: settings.model,
        max_tokens: params.maxTokens,
        system,
        messages,
        output_config: {
          format: zodOutputFormat(SopEditSchema),
          ...(params.supportsEffort ? { effort: settings.effort } : {}),
        },
        ...(params.thinking ? { thinking: params.thinking } : {}),
      },
      { signal: controller.signal },
    );

    let chars = 0;
    let lastEmit = 0;
    stream.on('streamEvent', (event) => {
      if (event.type === 'content_block_start') {
        if (event.content_block.type === 'thinking') onProgress?.({ stage: 'thinking' });
        else if (event.content_block.type === 'text') onProgress?.({ stage: 'writing', chars });
      } else if (event.type === 'message_delta' && event.delta.stop_reason) {
        stopReason = event.delta.stop_reason;
      }
    });
    stream.on('text', (delta) => {
      chars += delta.length;
      const now = Date.now();
      if (now - lastEmit > 250) {
        lastEmit = now;
        onProgress?.({ stage: 'writing', chars });
      }
    });

    const msg = await stream.finalMessage();
    stopReason = msg.stop_reason ?? stopReason;
    if (msg.stop_reason === 'refusal') {
      throw new Error('Claude declined to generate this SOP (the content was flagged).');
    }
    const textBlock = msg.content.find(
      (b): b is Anthropic.TextBlock => b.type === 'text',
    );
    if (!textBlock || !textBlock.text.trim()) {
      throw new Error(stopReason === 'max_tokens' ? CUTOFF_MSG : 'Claude returned no SOP content.');
    }
    finalText = textBlock.text;
  } catch (e) {
    if (stopReason === 'max_tokens') throw new Error(CUTOFF_MSG);
    throw new Error(friendlyError(e));
  } finally {
    if (currentRun === controller) currentRun = null;
  }

  let gen: SopEdit;
  try {
    gen = SopEditSchema.parse(JSON.parse(finalText));
  } catch {
    throw new Error(
      stopReason === 'max_tokens' ? CUTOFF_MSG : 'Claude returned malformed SOP data. Please try again.',
    );
  }

  const plan: SopEditPlan = {
    title: gen.title,
    intro: gen.intro,
    steps: gen.steps.map((s) => ({
      stepNumber: s.stepNumber,
      caption: s.caption,
      body: s.body,
      sectionHeading: s.sectionHeading,
      sectionBody: s.sectionBody,
    })),
  };

  // Apply the plan IN-LINE to the project's steps (snapshotting for revert).
  const updated = await applySopEdits(projectPath, plan, {
    model: settings.model,
    tone: settings.tone,
  });
  claudeLog.info(`SOP applied inline (${settings.model}, ${plan.steps.length} step edits).`);
  onProgress?.({ stage: 'done' });
  return updated;
}
