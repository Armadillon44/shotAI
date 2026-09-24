namespace ShotAI.Core.SelfTest;

/// <summary>How a self-test ended; the value is the process exit code (spec 10 7.8).</summary>
public enum SelfTestOutcome
{
    /// <summary>The <c>PASS</c> line was printed.</summary>
    Pass = 0,

    /// <summary>The <c>FAIL</c> line was printed.</summary>
    Fail = 1,

    /// <summary>An exception ended the run, with its <c>ERROR</c> line and no verdict.</summary>
    Error = 2,
}
