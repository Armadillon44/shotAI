using System.Diagnostics;
using System.Globalization;
using System.Text;
using ShotAI.Platform.Shell;
using Xunit;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// Another app with a window, as the apps a recording clicks in are: Windows PowerShell, which
/// ships with every supported Windows, shows a topmost WinForms form with one button. A hung app
/// shows it and then stops pumping messages, blocked on its input, until disposed.
/// </summary>
internal sealed class ChildFormWindow : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);
    private readonly Process _process;

    private ChildFormWindow(Process process, nint button, (int X, int Y) center)
    {
        _process = process;
        Button = button;
        ButtonCenter = center;
    }

    /// <summary>The button's window.</summary>
    public nint Button { get; }

    /// <summary>The button's centre, in physical pixels.</summary>
    public (int X, int Y) ButtonCenter { get; }

    /// <summary>Starts the app and returns once its form is shown.</summary>
    /// <param name="text">The button's text, which is its UI Automation name.</param>
    /// <param name="left">The form's left edge.</param>
    /// <param name="hung">Whether the app stops pumping messages once its form is shown.</param>
    public static async Task<ChildFormWindow> StartAsync(string text, int left, bool hung)
    {
        var script =
            "Add-Type -AssemblyName System.Windows.Forms; " +
            "$f = New-Object System.Windows.Forms.Form; " +
            "$f.Text = 'shotAI child " + text + "'; $f.StartPosition = 'Manual'; $f.Left = " + left.ToString(CultureInfo.InvariantCulture) + "; $f.Top = 420; " +
            "$f.Width = 360; $f.Height = 220; $f.TopMost = $true; $f.ShowInTaskbar = $false; " +
            "$b = New-Object System.Windows.Forms.Button; $b.Text = '" + text + "'; $b.Left = 20; $b.Top = 20; $b.Width = 200; $b.Height = 60; " +
            "$f.Controls.Add($b); $f.Show(); [System.Windows.Forms.Application]::DoEvents(); " +
            "[Console]::Out.WriteLine('shown ' + $b.Handle.ToInt64()); " +
            (hung ? "$null = [Console]::In.ReadLine(); " : "[System.Windows.Forms.Application]::Run($f); ") +
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
        try
        {
            var line = await process.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(Bound, TestContext.Current.CancellationToken);
            if (line is null || !line.StartsWith("shown ", StringComparison.Ordinal)) throw new InvalidOperationException($"the child did not show its form: {line}");
            var button = (nint)long.Parse(line["shown ".Length..], CultureInfo.InvariantCulture);
            var r = WindowStyles.GetWindowRect(button);
            return new ChildFormWindow(process, button, (r.X + (r.Width / 2), r.Y + (r.Height / 2)));
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        _process.Dispose();
    }
}
