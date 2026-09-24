using System.Reflection;

namespace ShotAI.Core.Updates;

/// <summary>
/// The running version as the User-Agent, the log and About show it (spec 10 7.6.1): the
/// informational version cut at its first <c>+</c>, so the build metadata never shows
/// (<c>2.0.0-alpha.0</c>, later <c>2.0.0</c>; EDGE-INFRA-29).
/// </summary>
/// <param name="Display">The version text.</param>
public sealed record AppVersion(string Display)
{
    private static AppVersion _current = FromInformational(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "");

    /// <summary>
    /// The running version. The App sets it once at startup from its entry assembly's
    /// <see cref="AssemblyInformationalVersionAttribute"/>; until then it is Core's own, which
    /// <c>Directory.Build.props</c> gives the same <c>Version</c>.
    /// </summary>
    public static AppVersion Current
    {
        get => Volatile.Read(ref _current);
        set => Volatile.Write(ref _current, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>The version before the first <c>+</c>, or all of it when there is none.</summary>
    public static AppVersion FromInformational(string informationalVersion)
    {
        ArgumentNullException.ThrowIfNull(informationalVersion);
        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return new AppVersion(plus < 0 ? informationalVersion : informationalVersion[..plus]);
    }
}
