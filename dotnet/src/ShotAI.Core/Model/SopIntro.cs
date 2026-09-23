namespace ShotAI.Core.Model;

/// <summary>
/// The SOP overview, rendered as a preamble above the steps rather than as a step
/// (spec 01 2.4). Stored as <c>{heading, body}</c>.
/// </summary>
public sealed record SopIntro(string Heading, string Body);
