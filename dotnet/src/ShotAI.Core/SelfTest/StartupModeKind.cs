namespace ShotAI.Core.SelfTest;

/// <summary>What <c>shotAI.exe</c> was started to do (spec 10 7.8).</summary>
public enum StartupModeKind
{
    /// <summary>The app.</summary>
    Normal,

    /// <summary><c>--selftest</c> or <c>SHOTAI_SELFTEST=1</c>: <see cref="StoreSelfTest"/>.</summary>
    StoreSelfTest,

    /// <summary><c>--capture-selftest</c> or <c>SHOTAI_CAPTURE_TEST=1</c>: spec 02's capture self-test.</summary>
    CaptureSelfTest,

    /// <summary><c>--update-selftest</c>, optionally <c>=&lt;version&gt;</c>: one update check (IMPROVEMENT).</summary>
    UpdateSelfTest,
}
