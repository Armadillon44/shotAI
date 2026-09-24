using System.Diagnostics;
using System.Runtime.InteropServices;
using ShotAI.Core.Shell;
using ShotAI.Core.Store;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace ShotAI.Platform.Shell;

/// <summary>The two shell calls <see cref="ShellReveal"/> makes; <c>ShellRevealTests</c> records them instead.</summary>
internal interface IShellCalls
{
    /// <summary>Explorer on the parent of <paramref name="path"/>, with it selected: <c>SHParseDisplayName</c>, <c>SHOpenFolderAndSelectItems</c>, <c>ILFree</c>.</summary>
    void OpenFolderAndSelect(string path);

    /// <summary><see cref="Process.Start(ProcessStartInfo)"/>.</summary>
    void Start(ProcessStartInfo info);
}

/// <summary>
/// Explorer reveals (spec 11 7.3.3, ARCHITECTURE S17): each shell call runs on its own
/// <see cref="StaThread"/>, so the UI thread never waits on Explorer (D-IPC-6, AC-IPC-20).
/// </summary>
internal sealed class ShellReveal : IShellReveal
{
    private readonly IProjectService _projects;
    private readonly IShellCalls _shell;

    /// <summary>The reveal the container makes, over the Windows shell.</summary>
    public ShellReveal(IProjectService projects)
        : this(projects, WindowsShell.Instance)
    {
    }

    /// <summary>The same over the given shell calls, for the tests.</summary>
    internal ShellReveal(IProjectService projects, IShellCalls shell)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(shell);
        _projects = projects;
        _shell = shell;
    }

    /// <inheritdoc/>
    public async Task RevealProjectAsync(string projectPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        var resolved = await _projects.ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await RevealInExplorerAsync(resolved).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task RevealInExplorerAsync(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return StaThread.RunAsync(() => _shell.OpenFolderAndSelect(path));
    }

    /// <inheritdoc/>
    public Task OpenFolderAsync(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        return StaThread.RunAsync(() =>
        {
            // Checked again here, just before the launch: the shell's open runs a file, which is
            // exactly what Electron's directory check exists to prevent (src/main/export.ts:912-917).
            if (!Directory.Exists(directory)) return;
            _shell.Start(new ProcessStartInfo(directory) { UseShellExecute = true, Verb = "open" });
        });
    }

    /// <summary>The <see cref="IOException"/> a failed shell call throws, with the system's message for an HRESULT that carries a Win32 error.</summary>
    internal static IOException ShellFailure(int hresult)
    {
        const int Win32Facility = unchecked((int)0x80070000);
        var message = (hresult & unchecked((int)0xFFFF0000)) == Win32Facility
            ? Marshal.GetPInvokeErrorMessage(hresult & 0xFFFF)
            : Marshal.GetExceptionForHR(hresult)?.Message ?? $"0x{hresult:X8}";
        return new IOException(message, hresult);
    }

    private sealed unsafe class WindowsShell : IShellCalls
    {
        public static readonly WindowsShell Instance = new();

        public void OpenFolderAndSelect(string path)
        {
            var hr = PInvoke.SHParseDisplayName(path, null, out var pidl, 0);
            if (hr.Failed) throw ShellFailure(hr.Value);
            try
            {
                hr = PInvoke.SHOpenFolderAndSelectItems(pidl, 0, null, 0);
                if (hr.Failed) throw ShellFailure(hr.Value);
            }
            finally
            {
                PInvoke.ILFree(pidl);
            }
        }

        public void Start(ProcessStartInfo info) => Process.Start(info)?.Dispose();
    }
}
