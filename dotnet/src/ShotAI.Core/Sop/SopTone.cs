namespace ShotAI.Core.Sop;

/// <summary>The output tone (spec 07 2.1); the wire strings are <c>professional</c>, <c>friendly</c>, <c>concise</c> and <c>detailed</c>.</summary>
public enum SopTone
{
    /// <summary><c>professional</c>, the default.</summary>
    Professional,

    /// <summary><c>friendly</c>.</summary>
    Friendly,

    /// <summary><c>concise</c>.</summary>
    Concise,

    /// <summary><c>detailed</c>.</summary>
    Detailed,
}
