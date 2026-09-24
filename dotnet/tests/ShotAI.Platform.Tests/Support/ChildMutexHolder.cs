using System.Diagnostics;
using System.Text;
using Xunit;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// Another process that holds a named mutex, as a running shotAI holds its lock: Windows
/// PowerShell, which ships with every supported Windows, takes the mutex and waits for a word on
/// its input, then releases it or exits without releasing it.
/// </summary>
internal sealed class ChildMutexHolder : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private readonly Process _process;

    private ChildMutexHolder(Process process) => _process = process;

    /// <summary>Starts the child and returns once it owns <paramref name="mutexName"/>.</summary>
    public static async Task<ChildMutexHolder> StartAsync(string mutexName)
    {
        var script =
            "$m = New-Object System.Threading.Mutex($false, '" + mutexName + "'); " +
            "if (-not $m.WaitOne(0)) { [Console]::Out.WriteLine('busy'); exit 1 }; " +
            "[Console]::Out.WriteLine('owned'); " +
            "$c = [Console]::In.ReadLine(); " +
            "if ($c -eq 'release') { $m.ReleaseMutex() }; " +
            "exit 0";
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            psi.ArgumentList.Add(a);
        var process = Process.Start(psi) ?? throw new InvalidOperationException("PowerShell did not start.");
        var holder = new ChildMutexHolder(process);
        try
        {
            var line = await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Bound, TestContext.Current.CancellationToken);
            return line == "owned" ? holder : throw new InvalidOperationException($"the child did not take the mutex: {line}");
        }
        catch
        {
            holder.Dispose();
            throw;
        }
    }

    /// <summary>The child releases the mutex and exits.</summary>
    public Task ReleaseAsync() => EndAsync("release");

    /// <summary>The child exits still owning the mutex, which leaves it abandoned.</summary>
    public Task AbandonAsync() => EndAsync("abandon");

    public void Dispose()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }

    private async Task EndAsync(string word)
    {
        await _process.StandardInput.WriteLineAsync(word);
        await _process.StandardInput.FlushAsync(TestContext.Current.CancellationToken);
        await _process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.Equal(0, _process.ExitCode);
    }
}
