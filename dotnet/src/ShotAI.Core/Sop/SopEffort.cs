namespace ShotAI.Core.Sop;

/// <summary>The generation effort, sent as <c>output_config.effort</c> (spec 07 2.1); the wire strings are <c>low</c>, <c>medium</c> and <c>high</c>.</summary>
public enum SopEffort
{
    /// <summary><c>low</c>.</summary>
    Low,

    /// <summary><c>medium</c>, the default.</summary>
    Medium,

    /// <summary><c>high</c>.</summary>
    High,
}
