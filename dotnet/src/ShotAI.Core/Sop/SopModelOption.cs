namespace ShotAI.Core.Sop;

/// <summary>One entry of the model picker (spec 07 2.1): the id, its label and the line under it.</summary>
public sealed record SopModelOption(string Id, string Label, string Blurb);
