using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ShotAI.Core.Json;
using ShotAI.Platform;
using ShotAI.Platform.Shell;

namespace ShotAI.ProtectionProbe;

/// <summary>
/// The protection probe (spec 02 2.16 and 8.4, AC-CAP-13, Q-CAP-15), a port of
/// <c>scripts/protection-probe.cjs</c> with its delays, repetitions, tolerance and verdict text:
/// for a 700 x 500 magenta window it counts the magenta pixels of a GDI read of the primary
/// monitor, then measures how soon after <c>SetWindowDisplayAffinity</c> the window is gone
/// from a read, and back. It runs for a normal WPF window and an <c>AllowsTransparency</c> one,
/// with the affinity set from the window's own thread (as Electron sets it) and from a worker
/// thread with the read right after (as the app's shield does).
/// </summary>
/// <remarks>
/// Run by hand on an interactive desktop: <c>dotnet run --project tools/ShotAI.ProtectionProbe</c>
/// from <c>dotnet/</c>. Exit 0 when every run is clean at +0 ms, 1 when one is not, 2 when a
/// window never showed up in a read.
/// </remarks>
internal static class Program
{
    // scripts/protection-probe.cjs:61, 75, 76, 84.
    private const int SettleMs = 900;
    private const int Reps = 5;
    private const int ResetMs = 160;
    private static readonly int[] Delays = [0, 4, 8, 16, 32, 64, 120, 250];

    [STAThread]
    private static int Main()
    {
        if (!DllSearchHardening.Apply())
        {
            Console.WriteLine("[probe] FAIL SetDefaultDllDirectories failed");
            return 1;
        }
        var dispatcher = Dispatcher.CurrentDispatcher;
        var exit = 1;
        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                exit = await RunAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine("[probe] FAIL " + e);
            }
            finally
            {
                dispatcher.InvokeShutdown();
            }
        });
        Dispatcher.Run();
        return exit;
    }

    private static async Task<int> RunAsync()
    {
        var clean = new List<bool?>();
        foreach (var layered in new[] { false, true })
        {
            foreach (var worker in new[] { false, true }) clean.Add(await ProbeAsync(layered, worker));
        }
        Console.WriteLine();
        if (clean.Any(c => c is null))
        {
            Console.WriteLine("[probe] INCONCLUSIVE: a window never showed up in a read.");
            return 2;
        }
        var all = clean.All(c => c == true);
        Console.WriteLine(all ? "[probe] CLEAN at +0ms for both windows, from both threads." : "[probe] NOT CLEAN at +0ms for every window and thread.");
        return all ? 0 : 1;
    }

    // One window, one thread: scripts/protection-probe.cjs:48-121. Null when the window is not in
    // a read even unprotected; else whether the exclusion was clean at +0 ms in every rep.
    private static async Task<bool?> ProbeAsync(bool layered, bool worker)
    {
        Console.WriteLine();
        Console.WriteLine($"[probe] {(layered ? "AllowsTransparency (layered)" : "normal")} WPF window, affinity set from {(worker ? "a worker thread" : "the window's thread")}");
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = layered,
            Background = new SolidColorBrush(Color.FromRgb(255, 0, 255)),
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 120,
            Top = 120,
            Width = 700,
            Height = 500,
        };
        window.Show();
        var hwnd = new WindowInteropHelper(window).Handle;
        try
        {
            await Task.Delay(SettleMs);
            var screen = MonitorQueries.Primary().Bounds;
            int Grab() => ScreenRead.Magenta(screen.X, screen.Y, screen.Width, screen.Height);

            var baseline = Grab();
            Console.WriteLine($"[probe] baseline (unprotected, visible): {baseline} px");
            if (baseline == 0)
            {
                Console.WriteLine("[probe] INCONCLUSIVE: window not captured even unprotected.");
                return null;
            }

            Console.WriteLine();
            Console.WriteLine("[probe] time-to-exclude after SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE):");
            var excludeFloor = new Dictionary<int, int>();
            foreach (var d in Delays)
            {
                var worst = 0;
                for (var i = 0; i < Reps; i++)
                {
                    Protect(hwnd, false);
                    await Task.Delay(ResetMs);
                    worst = Math.Max(worst, await ToggleThenGrabAsync(hwnd, true, d, worker, Grab));
                }
                excludeFloor[d] = worst;
                Console.WriteLine($"  +{d,3}ms -> worst-case leak {worst} px{(worst == 0 ? "  CLEAN" : "")}");
            }

            Console.WriteLine();
            Console.WriteLine("[probe] time-to-restore after SetWindowDisplayAffinity(WDA_NONE):");
            foreach (var d in Delays)
            {
                var worstMissing = baseline;
                for (var i = 0; i < Reps; i++)
                {
                    Protect(hwnd, true);
                    await Task.Delay(ResetMs);
                    worstMissing = Math.Min(worstMissing, await ToggleThenGrabAsync(hwnd, false, d, worker, Grab));
                }
                var pct = (int)JsMath.Round(worstMissing / (double)baseline * 100);
                Console.WriteLine($"  +{d,3}ms -> worst-case restored {pct}%{(pct > 95 ? "  FULL" : "")}");
            }

            var clean = Delays.Where(d => excludeFloor[d] == 0).ToList();
            Console.WriteLine();
            Console.WriteLine("[probe] VERDICT");
            if (clean.Count == 0)
            {
                Console.WriteLine("  No tested delay reliably excluded the window. Per-shot toggling is NOT safe;");
                Console.WriteLine("  the pill would leak into some screenshots. Keep the pill protected instead.");
            }
            else
            {
                Console.WriteLine($"  Smallest reliably-clean exclude delay: +{clean[0]}ms (worst of {Reps} runs).");
                Console.WriteLine("  Per-shot toggling is safe at that settle, versus the 350ms window-hide settle.");
            }
            return excludeFloor[0] == 0;
        }
        finally
        {
            Protect(hwnd, false);
            window.Close();
        }
    }

    // The toggle, the delay and the read: on the window's thread with an awaited delay, as the
    // Electron probe does it, or together on a worker, as the shield takes and then reads.
    private static Task<int> ToggleThenGrabAsync(nint hwnd, bool excluded, int delayMs, bool worker, Func<int> grab)
    {
        if (worker)
        {
            return Task.Run(() =>
            {
                Protect(hwnd, excluded);
                var watch = Stopwatch.StartNew();
                while (watch.ElapsedMilliseconds < delayMs) Thread.SpinWait(100);
                return grab();
            });
        }
        return OnThisThreadAsync();

        async Task<int> OnThisThreadAsync()
        {
            Protect(hwnd, excluded);
            if (delayMs > 0) await Task.Delay(delayMs);
            return grab();
        }
    }

    private static void Protect(nint hwnd, bool excluded)
    {
        if (!CaptureExclusion.Apply(hwnd, excluded)) Console.WriteLine($"[probe] SetWindowDisplayAffinity failed (Win32 error {Marshal.GetLastPInvokeError()})");
    }
}
