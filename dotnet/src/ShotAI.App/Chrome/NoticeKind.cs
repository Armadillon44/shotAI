namespace ShotAI.App.Chrome;

/// <summary>A notice's kind, its fill (spec 06 2.21): the same under every brand and appearance.</summary>
public enum NoticeKind
{
    /// <summary>A failure; announced assertively (D-HOME-24).</summary>
    Error,

    /// <summary>Information, such as an update; announced politely.</summary>
    Info,

    /// <summary>A success; announced politely.</summary>
    Success,
}
