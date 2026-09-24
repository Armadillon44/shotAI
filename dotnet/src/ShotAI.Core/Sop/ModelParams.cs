namespace ShotAI.Core.Sop;

/// <summary>
/// How a request to one model is shaped and priced (spec 07 2.1, <c>MODEL_PARAMS</c> in
/// <c>src/main/claude-models.ts</c>).
/// </summary>
/// <param name="AdaptiveThinking">Send <c>thinking: {type: 'adaptive'}</c>; false omits the parameter.</param>
/// <param name="SupportsEffort">Send <c>output_config.effort</c>; false omits it.</param>
/// <param name="InputPerMTok">USD per million input tokens, for the pre-send estimate.</param>
/// <param name="OutputPerMTok">USD per million output tokens.</param>
/// <param name="MaxTokens">The output cap of the streamed generation request.</param>
public sealed record ModelParams(bool AdaptiveThinking, bool SupportsEffort, double InputPerMTok, double OutputPerMTok, long MaxTokens);
