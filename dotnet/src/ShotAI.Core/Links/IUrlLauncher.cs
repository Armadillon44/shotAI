namespace ShotAI.Core.Links;

/// <summary>
/// Opens an absolute URI in the default browser (spec 11 7.3.4). Platform's
/// <c>ShellUrlLauncher</c> is the one implementation and the only code in the solution that
/// hands a URL to the shell (INV-IPC-3); only <see cref="ExternalLinks"/> calls it, with a URI
/// the allowlist admitted.
/// </summary>
public interface IUrlLauncher
{
    /// <summary>Opens <paramref name="absoluteUri"/>; the task faults with what the shell throws.</summary>
    Task LaunchAsync(string absoluteUri);
}
