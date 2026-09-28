using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Capture;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Paths;
using ShotAI.Core.Store;

namespace ShotAI.Core.SelfTest;

/// <summary>
/// What the capture self-test drives: the engine and the seams it reads, over a store and
/// settings of the test's own. Disposing <see cref="Owner"/> ends all of them.
/// </summary>
public sealed record CaptureSelfTestServices(
    CaptureEngine Engine,
    IProjectService Projects,
    IScreenCapture Screen,
    IWindowInfoProvider Windows,
    IImageCodec Codec,
    ITriggerSource Triggers,
    ICaptureSettings Settings,
    IAsyncDisposable Owner);

/// <summary>
/// The capture self-test, <c>--capture-selftest</c> (spec 02 2.16, 10 7.8): Electron's
/// <c>runCaptureTest</c> (<c>src/main/capture-selftest.ts</c>) against a store and settings of its
/// own, so the user's <c>settings.json</c> and projects are never touched (INV-INFRA-30).
/// </summary>
/// <remarks>
/// The checks run in Electron's order, each even when an earlier one failed: the seams the engine
/// reads, one hotkey step through the whole pipeline, then each capture mode. Each session ends
/// in a <c>finally</c>, since one engine runs them all. The full-monitor sizes allow for the
/// downscale (EDGE-CAP-34), and the auto mode's menu selection must be exactly the owner and the
/// click box's crop (Q-CAP-25). Lines go to standard output, the failures to standard error, and
/// every line to the log at Information; the log's copy carries no window title or caption (10
/// 7.5.5). The steps run on the pool: the grab takes the shield's lock and the codec refuses an
/// STA thread.
/// </remarks>
public static partial class CaptureSelfTest
{
    /// <summary>The pipeline check's project title (<c>capture-selftest.ts:71</c>).</summary>
    public const string PipelineTitle = "Capture Pipeline Test";

    private const string Prefix = "[capture-test] ";

    // How long the clean-up waits for the queued writes: the exit flush's budget, as the store self-test.
    private static readonly TimeSpan DisposeBound = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs the test and returns its outcome, which is the process exit code: PASS, FAIL, or an
    /// error outside the checks (10 7.8).
    /// </summary>
    /// <param name="compose">Builds the engine and its seams over the isolated paths it is given.</param>
    /// <param name="paths">The app's paths; only <see cref="IAppPaths.TempDirectory"/> is used as it is.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="log">The self-test's logger, under <c>main</c>.</param>
    public static Task<SelfTestOutcome> RunAsync(Func<IAppPaths, CaptureSelfTestServices> compose, IAppPaths paths, TextWriter output, TextWriter error, ILogger log) =>
        RunAsync(compose, paths, output, error, log, TimeProvider.System);

    /// <summary>The same with the clock of the clean-up's bound given.</summary>
    internal static async Task<SelfTestOutcome> RunAsync(
        Func<IAppPaths, CaptureSelfTestServices> compose, IAppPaths paths, TextWriter output, TextWriter error, ILogger log, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(compose);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(time);
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var pipelineRoot = Path.Combine(paths.TempDirectory, "shotai-capture-" + pid);
        var modesRoot = Path.Combine(paths.TempDirectory, "shotai-modes-" + pid);
        var settingsFile = pipelineRoot + ".settings.json";
        var print = new Printer(output, error, log);
        CaptureSelfTestServices? services = null;
        try
        {
            services = compose(new SelfTestPaths(paths, pipelineRoot, settingsFile));
            await print.OutAsync(Prefix + "runtime win32/" + ArchName(RuntimeInformation.ProcessArchitecture) + " \u00b7 .NET " + Environment.Version).ConfigureAwait(false);
            var nativesOk = await CheckSeamsAsync(services, print).ConfigureAwait(false);
            var pipelineOk = await CheckPipelineAsync(services, pipelineRoot, print).ConfigureAwait(false);
            var modesOk = await CheckModesAsync(services, modesRoot, print).ConfigureAwait(false);
            var ok = nativesOk && pipelineOk && modesOk;
            await print.OutAsync(Prefix + (ok ? "PASS" : "FAIL")).ConfigureAwait(false);
            return ok ? SelfTestOutcome.Pass : SelfTestOutcome.Fail;
        }
        catch (Exception e)
        {
            await print.ErrAsync(Prefix + "ERROR " + e).ConfigureAwait(false);
            return SelfTestOutcome.Error;
        }
        finally
        {
            await CleanUpAsync(services, [pipelineRoot, modesRoot], settingsFile, time).ConfigureAwait(false);
        }
    }

    /// <summary>A PNG's size from its IHDR chunk, after the 8-byte signature (<c>capture-selftest.ts:18-20</c>).</summary>
    internal static (int Width, int Height) PngSize(ReadOnlySpan<byte> png) =>
        ((int)BinaryPrimitives.ReadUInt32BigEndian(png[16..]), (int)BinaryPrimitives.ReadUInt32BigEndian(png[20..]));

    /// <summary>A shot of <paramref name="width"/> x <paramref name="height"/> as the engine stores it at <paramref name="captureScale"/> (EDGE-CAP-34, Q-CAP-26).</summary>
    internal static (int Width, int Height) Stored(int width, int height, double captureScale) =>
        DownscalePolicy.Compute(width, height, captureScale) is { } t ? (t.Width, t.Height) : (width, height);

    /// <summary>
    /// The auto mode's menu selection as stored (Q-CAP-25): the owner's bounds joined with the
    /// click box at the monitor's scale, cropped to the monitor, then downscaled.
    /// </summary>
    internal static (int Width, int Height) MenuAutoStored(MonitorDescriptor monitor, Rect owner, Point point, double captureScale)
    {
        var crop = CaptureGeometry.CropRect(monitor.Bounds, CaptureGeometry.UnionRect(owner, CaptureGeometry.ClickBox(point, monitor.ScaleFactor)));
        return Stored(crop.Width, crop.Height, captureScale);
    }

    // checkNativeModules (capture-selftest.ts:22-62): the seams in place of the native modules.
    private static async Task<bool> CheckSeamsAsync(CaptureSelfTestServices s, Printer print)
    {
        var ok = true;
        try
        {
            var foreground = s.Windows.Foreground();
            const string head = Prefix + "window info     OK \u2014 active: ";
            await print.OutAsync(
                head + (foreground is { } f ? f.App + " :: " + f.Title : "(no active window)"),
                head + (foreground is { } g ? g.App + " :: (title of " + Count(g.Title.Length) + " characters)" : "(no active window)")).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            ok = false;
            await print.ErrAsync(Prefix + "window info     FAILED: " + e.Message).ConfigureAwait(false);
        }
        try
        {
            var monitors = s.Screen.Monitors();
            var bytes = 0;
            if (monitors.Count > 0)
            {
                using var frame = s.Screen.Grab(monitors[0]);
                bytes = s.Codec.EncodePng(frame).Length;
            }
            await print.OutAsync(Prefix + "screen capture  OK \u2014 " + Count(monitors.Count) + " monitor(s); PNG " + Count(bytes) + " bytes").ConfigureAwait(false);
        }
        catch (Exception e)
        {
            ok = false;
            await print.ErrAsync(Prefix + "screen capture  FAILED: " + e.Message).ConfigureAwait(false);
        }
        // The hook is resolved, never attached (Q-CAP-28): the mouse hook's own tests cover it.
        await print.OutAsync(Prefix + "input hook      OK \u2014 loaded: " + (s.Triggers is not null ? "true" : "false")).ConfigureAwait(false);
        return ok;
    }

    // checkCapturePipeline (capture-selftest.ts:64-108): one hotkey step with a point, into a new project.
    private static async Task<bool> CheckPipelineAsync(CaptureSelfTestServices s, string root, Printer print)
    {
        try
        {
            await s.Projects.SetProjectsDirAsync(root).ConfigureAwait(false);
            var created = await s.Projects.CreateProjectAsync(PipelineTitle).ConfigureAwait(false);
            await s.Engine.StartAsync(created.Path, new CaptureStartOptions(AttachTriggers: false)).ConfigureAwait(false);
            ProjectStep? step;
            try
            {
                step = await s.Engine.CaptureStepForSelfTestAsync(StepTrigger.Hotkey, (10, 10), MouseButton.Left).ConfigureAwait(false);
            }
            finally
            {
                await s.Engine.StopAsync().ConfigureAwait(false);
            }

            var manifest = JsJson.Parse(await File.ReadAllBytesAsync(Path.Combine(created.Path, "project.json")).ConfigureAwait(false)) as JsonObject;
            var steps = manifest?["steps"] as JsonArray ?? throw new InvalidDataException("project.json is not an object with a steps array");
            var shotExists = step is not null && ShotWritten(Path.Combine(created.Path, step.Screenshot));
            var caption = step is null ? "undefined" : step.Caption;
            await print.OutAsync(Prefix + "pipeline step    = " + (step is null ? "(none)" : step.Screenshot)).ConfigureAwait(false);
            await print.OutAsync(Prefix + "pipeline caption  = " + caption, Prefix + "pipeline caption  = (caption of " + Count(caption.Length) + " characters)").ConfigureAwait(false);
            await print.OutAsync(Prefix + "pipeline monitor  = " + MonitorText(step)).ConfigureAwait(false);
            await print.OutAsync(Prefix + "manifest steps    = " + Count(steps.Count)).ConfigureAwait(false);
            await print.OutAsync(Prefix + "shot written       = " + (shotExists ? "true" : "false")).ConfigureAwait(false);

            return step is not null
                && steps.Count == 1
                && JsValue.TryGetString((steps[0] as JsonObject)?["id"], out var id) && id == step.Id
                && shotExists;
        }
        catch (Exception e)
        {
            await print.ErrAsync(Prefix + "pipeline FAILED: " + e.Message).ConfigureAwait(false);
            return false;
        }
    }

    // checkCaptureModes (capture-selftest.ts:116-232): one click step per mode, each into its own project.
    private static async Task<bool> CheckModesAsync(CaptureSelfTestServices s, string root, Printer print)
    {
        var all = s.Screen.Monitors();
        var mon = all.FirstOrDefault(m => m.IsPrimary) ?? all.FirstOrDefault();
        if (mon is null)
        {
            await print.ErrAsync(Prefix + "modes: no monitor available").ConfigureAwait(false);
            return false;
        }
        var ok = true;
        try
        {
            await s.Projects.SetProjectsDirAsync(root).ConfigureAwait(false);
            var targets = await s.Engine.ListTargetsAsync().ConfigureAwait(false);
            await print.OutAsync(Prefix + "listTargets       = " + Count(targets.Windows.Count) + " windows, " + Count(targets.Monitors.Count) + " monitors").ConfigureAwait(false);
            ok = ok && targets.Monitors.Count >= 1;

            // A point safely inside the primary monitor.
            var x = (int)mon.Bounds.X;
            var y = (int)mon.Bounds.Y;
            var point = (X: x + 100, Y: y + 100);
            var scale = s.Settings.CaptureScaleNow();
            var full = Stored((int)mon.Bounds.Width, (int)mon.Bounds.Height, scale);

            var screen = await RunModeAsync(s, root, print, "screen", new CaptureTarget("screen", MonitorId: mon.Id), point).ConfigureAwait(false);
            ok = ok && screen == full;

            var area = await RunModeAsync(s, root, print, "area", new CaptureTarget("area", Area: new Rect(x + 100, y + 100, 300, 200)), point).ConfigureAwait(false);
            ok = ok && area == Stored(300, 200, scale);

            if (targets.Windows.Count > 0)
            {
                var w = targets.Windows[0];
                var windowTarget = new CaptureTarget("window", Window: new CaptureTargetWindow(w.Id, w.Pid, w.Title));
                var win = await RunModeAsync(s, root, print, "window", windowTarget, point).ConfigureAwait(false);
                ok = ok && win is { Width: >= 1, Height: >= 1 };
                // A menu selection in window mode crops to the picked window and the menu box.
                var menuWin = await RunModeAsync(s, root, print, "menu(window)", windowTarget, point, menuPopup: true).ConfigureAwait(false);
                ok = ok && menuWin is { Width: >= 1 } mw && mw.Width <= (int)mon.Bounds.Width;
            }
            else
            {
                await print.OutAsync(Prefix + "mode window        = (no pickable windows \u2014 skipped)").ConfigureAwait(false);
            }

            // An auto mode menu selection frames the owner window and the menu, a crop, not the screen (EDGE-CAP-8).
            var owner = new Rect(x + 200, y + 200, 900, 600);
            var menuAuto = await RunModeAsync(s, root, print, "menu(auto)", new CaptureTarget("auto"), point, menuPopup: true, owner).ConfigureAwait(false);
            ok = ok && menuAuto == MenuAutoStored(mon, owner, new Point(point.X, point.Y), scale);

            // A screen mode menu selection keeps the chosen monitor whole.
            var menuScreen = await RunModeAsync(s, root, print, "menu(screen)", new CaptureTarget("screen", MonitorId: mon.Id), point, menuPopup: true).ConfigureAwait(false);
            ok = ok && menuScreen == full;
            return ok;
        }
        catch (Exception e)
        {
            await print.ErrAsync(Prefix + "modes FAILED: " + e.Message).ConfigureAwait(false);
            return false;
        }
    }

    // runMode: a project, a session with the target, one left click step, then the stop.
    private static async Task<(int Width, int Height)?> RunModeAsync(
        CaptureSelfTestServices s, string root, Printer print, string label, CaptureTarget target, (int X, int Y) point, bool menuPopup = false, Rect? owner = null)
    {
        var project = await s.Projects.CreateProjectAsync("Mode " + label).ConfigureAwait(false);
        await s.Engine.StartAsync(project.Path, new CaptureStartOptions(target, AttachTriggers: false)).ConfigureAwait(false);
        ProjectStep? step;
        try
        {
            step = await s.Engine.CaptureStepForSelfTestAsync(StepTrigger.Click, point, MouseButton.Left, menuPopup, owner).ConfigureAwait(false);
        }
        finally
        {
            await s.Engine.StopAsync().ConfigureAwait(false);
        }
        var head = Prefix + "mode " + label.PadRight(13) + " = ";
        if (step is null)
        {
            await print.OutAsync(head + "(no step)").ConfigureAwait(false);
            return null;
        }
        var size = PngSize(await File.ReadAllBytesAsync(Path.Combine(project.Path, step.Screenshot)).ConfigureAwait(false));
        await print.OutAsync(head + Count(size.Width) + "x" + Count(size.Height)).ConfigureAwait(false);
        return size;
    }

    // step.monitor.bounds.width x height @ scaleFactor x, as JavaScript prints the numbers.
    private static string MonitorText(ProjectStep? step)
    {
        if (step?.Raw["monitor"] is not JsonObject monitor) return "(none)";
        var bounds = monitor["bounds"] as JsonObject;
        return Js(bounds?["width"]) + "x" + Js(bounds?["height"]) + " @" + Js(monitor["scaleFactor"]) + "x";
    }

    private static string Js(JsonNode? node) => JsValue.TryGetNumber(node, out var d) ? JsNumber.ToJsString(d) : "undefined";

    private static bool ShotWritten(string path)
    {
        try
        {
            return new FileInfo(path) is { Exists: true, Length: > 0 };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    // RuntimeInformation.ProcessArchitecture lowercased, as the log's banner prints it.
    private static string ArchName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "x64",
        Architecture.Arm64 => "arm64",
        Architecture.X86 => "x86",
        _ => architecture.ToString().ToLowerInvariant(),
    };

    // Step 7 of 10 7.8. Nothing here changes the outcome or holds up the exit for long.
    private static async Task CleanUpAsync(CaptureSelfTestServices? services, string[] roots, string settingsFile, TimeProvider time)
    {
        if (services is not null)
        {
            try
            {
                // The bound starts before the disposal does.
                using var bound = new CancellationTokenSource(DisposeBound, time);
                await services.Owner.DisposeAsync().AsTask().WaitAsync(bound.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A write that failed or did not finish in time; the deletes below may then leave files.
            }
        }
        foreach (var root in roots)
        {
            try
            {
                ReparseSafeDelete.DeleteTree(root, new ManagedPathProbe());
            }
            catch (Exception)
            {
                // Left for the temp folder's own clean-up.
            }
        }
        try
        {
            File.Delete(settingsFile);
        }
        catch (Exception)
        {
            // Same.
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
    private static partial void SelfTestLine(ILogger logger, string line);

    // Each line to its stream and to the log; a line that names a window or a caption is logged without it.
    private sealed class Printer(TextWriter output, TextWriter error, ILogger log)
    {
        public async Task OutAsync(string line, string? logged = null)
        {
            await output.WriteLineAsync(line).ConfigureAwait(false);
            SelfTestLine(log, logged ?? line);
        }

        public async Task ErrAsync(string line)
        {
            await error.WriteLineAsync(line).ConfigureAwait(false);
            SelfTestLine(log, line);
        }
    }
}
