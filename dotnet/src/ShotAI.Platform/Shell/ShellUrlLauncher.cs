using System.Diagnostics;
using ShotAI.Core.Links;

namespace ShotAI.Platform.Shell;

/// <summary>
/// <see cref="IUrlLauncher"/> (spec 11 7.3.4, 10 7.7): the only code in the solution that hands a
/// URL to the shell (INV-IPC-3, ARCHITECTURE S15, <c>SingleUrlLauncherTests</c>). The shell's open
/// runs on its own <see cref="StaThread"/>, which ShellExecute wants, never on the UI thread.
/// </summary>
internal sealed class ShellUrlLauncher : IUrlLauncher
{
    private readonly Action<ProcessStartInfo> _start;

    /// <summary>The launcher the container makes, over <see cref="Process.Start(ProcessStartInfo)"/>.</summary>
    public ShellUrlLauncher()
        : this(static info => Process.Start(info)?.Dispose())
    {
    }

    /// <summary>The same over the given start, for the tests.</summary>
    internal ShellUrlLauncher(Action<ProcessStartInfo> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        _start = start;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <see cref="ExternalLinks"/> only ever passes an <c>https</c> URI the allowlist admitted.
    /// Anything else is refused here too, so this launcher never opens a file, a folder or
    /// another scheme's handler, whoever calls it. Nothing is added to the URI.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="absoluteUri"/> is not an absolute <c>https</c> URI.</exception>
    public Task LaunchAsync(string absoluteUri)
    {
        ArgumentNullException.ThrowIfNull(absoluteUri);
        if (!Uri.TryCreate(absoluteUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Only an absolute https URI is opened.", nameof(absoluteUri));
        var target = uri.AbsoluteUri;
        return StaThread.RunAsync(() => _start(new ProcessStartInfo(target) { UseShellExecute = true }));
    }
}
