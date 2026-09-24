using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

/// <summary>What a session is: a recording, or the one-shot screenshot's short-lived session.</summary>
internal enum SessionKind
{
    Recording,
    Screenshot,
}

/// <summary>
/// One session (Electron's <c>Session</c>, <c>CaptureController.ts:221-244</c>). The engine's
/// lock guards every mutable field; the rest is fixed at start.
/// </summary>
internal sealed class CaptureSession
{
    public CaptureSession(SessionKind kind, string projectPath, string projectDir, string projectTitle, CaptureTarget target,
        int stepCountAtStart, long counter, bool createdThisSession, int? insertCursor, int generation)
    {
        Kind = kind;
        ProjectPath = projectPath;
        ProjectDir = projectDir;
        ProjectTitle = projectTitle;
        Target = target;
        StepCountAtStart = stepCountAtStart;
        Counter = counter;
        CreatedThisSession = createdThisSession;
        InsertCursor = insertCursor;
        Generation = generation;
    }

    public SessionKind Kind { get; }

    /// <summary>The path as the caller gave it: what the state reports and the store calls take.</summary>
    public string ProjectPath { get; }

    /// <summary>The folder the store resolved, which every shot path is confined to.</summary>
    public string ProjectDir { get; }

    public string ProjectTitle { get; }

    public CaptureTarget Target { get; }

    public int StepCountAtStart { get; }

    public bool CreatedThisSession { get; }

    /// <summary>The generation this session was installed with; a job of another generation is stale (EDGE-CAP-28).</summary>
    public int Generation { get; }

    public bool Paused { get; set; }

    /// <summary>The filename counter: seeded past every orphan, incremented before each write (INV-CAP-8, INV-CAP-9).</summary>
    public long Counter { get; set; }

    /// <summary>The steps this session added (D4): the state's step count.</summary>
    public int Committed { get; set; }

    /// <summary>The rolling insert position of a "+ Capture" session, or null to append (2.2.3).</summary>
    public int? InsertCursor { get; set; }

    /// <summary>The ids of the steps this session added, for a discard (INV-CAP-12).</summary>
    public List<string> AddedStepIds { get; } = [];

    /// <summary>
    /// <c>discardDeletesProject</c> (<c>:654-656</c>): a project made for this recording that had
    /// no steps goes whole. Electron's <c>!single</c> is the screenshot session, which is never discarded (D21).
    /// </summary>
    public bool DiscardDeletesProject => CreatedThisSession && StepCountAtStart == 0;
}
