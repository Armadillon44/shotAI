using ShotAI.Core.Errors;

namespace ShotAI.Core.Store;

/// <summary>No step of the manifest has the id an operation named (spec 01 2.9.12, 7.13).</summary>
public sealed class StepNotFoundException : ShotAIException
{
    public StepNotFoundException(string stepId) : base($"step {stepId} not found")
    {
        StepId = stepId;
    }

    /// <summary>The id that matched no step.</summary>
    public string StepId { get; }
}
