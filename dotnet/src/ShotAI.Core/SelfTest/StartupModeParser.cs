namespace ShotAI.Core.SelfTest;

/// <summary>
/// The self-test switches of startup step 3 (spec 10 7.8, ARCHITECTURE 4.2): command-line
/// switches first, and Electron's environment variables kept for existing scripts and docs.
/// </summary>
public static class StartupModeParser
{
    /// <summary>The store self-test's switch.</summary>
    public const string StoreSwitch = "--selftest";

    /// <summary>The capture self-test's switch.</summary>
    public const string CaptureSwitch = "--capture-selftest";

    /// <summary>The update self-test's switch, alone or followed by <c>=&lt;version&gt;</c>.</summary>
    public const string UpdateSwitch = "--update-selftest";

    /// <summary>Electron's store self-test variable (<c>main.ts:403</c>).</summary>
    public const string StoreVariable = "SHOTAI_SELFTEST";

    /// <summary>Electron's capture self-test variable (<c>main.ts:409</c>).</summary>
    public const string CaptureVariable = "SHOTAI_CAPTURE_TEST";

    /// <summary>
    /// The first row that matches, in the order store, capture, update, as Electron checked the
    /// store variable before the capture one (<c>main.ts:403-414</c>); a row matches on its
    /// switch or its variable. Switches match ordinally, ignoring case; a variable must be
    /// exactly <c>1</c>; unknown arguments are ignored. <c>--update-selftest=</c> with nothing
    /// after it is the running version, as the bare switch is.
    /// </summary>
    /// <param name="args">The arguments after the program name.</param>
    /// <param name="env">Reads an environment variable; null when it is not set.</param>
    public static StartupMode Parse(IReadOnlyList<string> args, Func<string, string?> env)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);
        if (HasSwitch(args, StoreSwitch) || env(StoreVariable) == "1") return new StartupMode(StartupModeKind.StoreSelfTest);
        if (HasSwitch(args, CaptureSwitch) || env(CaptureVariable) == "1") return new StartupMode(StartupModeKind.CaptureSelfTest);
        foreach (var arg in args)
        {
            if (arg is null) continue;
            if (arg.Equals(UpdateSwitch, StringComparison.OrdinalIgnoreCase)) return new StartupMode(StartupModeKind.UpdateSelfTest);
            if (arg.StartsWith(UpdateSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                var version = arg[(UpdateSwitch.Length + 1)..];
                return new StartupMode(StartupModeKind.UpdateSelfTest, version.Length == 0 ? null : version);
            }
        }
        return StartupMode.Normal;
    }

    private static bool HasSwitch(IReadOnlyList<string> args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
}
