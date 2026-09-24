using System.Globalization;
using System.Text.Json.Nodes;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Shell;
using ShotAI.Core.Store;

namespace ShotAI.Core.Capture;

public sealed partial class CaptureEngine
{
    /// <summary>
    /// <c>captureStep</c> (spec 02 2.6, 7.11): the one capture routine. Null, silently, for a
    /// suppressed or failed capture; the landed step otherwise. Anything it throws is a loud
    /// failure: the queue raises it as <c>CaptureFailed</c>, the screenshot throws it.
    /// </summary>
    private async Task<ProjectStep?> CaptureStepAsync(CaptureJob job)
    {
        CaptureSession s;
        lock (_gate)
        {
            // A pause or stop that lands while jobs are queued suppresses the backlog (INV-CAP-11).
            if (_session is not { Paused: false } live || !SessionAlive(job.Generation)) return null;
            s = live;
        }
        var point = job.Point;
        var foreground = _windows.Foreground();
        if (!job.SkipOwnWindowGuard && IsOwnUi(foreground, point))
        {
            Observe(job.Element);
            return null;
        }

        var element = job.Element ?? (point is { } p ? _elements.ElementAtAsync(p.X, p.Y) : Task.FromResult<StepElement?>(null));
        var mode = s.Target.Mode;
        var clickMonitor = (point is { } q ? _screen.FromPoint(q.X, q.Y) : null) ?? PrimaryOrFirst();
        AutoMode? autoMode = mode == "auto" ? AutoClassifier.Classify(foreground) : null;

        var grabStart = _clock.NowMs();
        var grabbed = Grab(s.Target, mode, autoMode, foreground, clickMonitor, point);
        if (grabbed is null)
        {
            // Electron returns at once: the query's result is dropped, and its fault observed.
            Observe(element);
            ReportGrabFailure(job);
            return null;
        }
        var grabMs = _clock.NowMs() - grabStart;
        var downStart = _clock.NowMs();
        var shot = DownscalePolicy.Encode(grabbed.Frame, _settings.CaptureScaleNow(), _codec);
        var downMs = _clock.NowMs() - downStart;
        if (grabMs + downMs > CaptureConstants.TimingLogThresholdMs) CaptureTiming(_log, grabMs, downMs);

        long order;
        lock (_gate)
        {
            if (!SessionAlive(job.Generation)) return null;
            order = ++s.Counter;
        }
        var filename = ShotNaming.Format(order);
        var screenshot = ShotsFolder + "/" + filename;
        var path = PathConfine.ConfineNoLinks(s.ProjectDir, screenshot, _probe) ?? throw new CaptureException(CaptureMessages.ShotsOutsideProject);
        await WriteShotAsync(path, shot.Png).ConfigureAwait(false);

        var window = foreground is null ? null : new CapturedWindow(foreground.App, foreground.Title, foreground.Pid, foreground.WindowRect);
        var el = await ElementOrUnavailableAsync(element).ConfigureAwait(false);
        var appName = ClickCaptions.AppName(window?.App);
        var caption = job.Trigger == StepTrigger.Click ? ClickCaptions.Build(job.Button, false, appName, el) : ClickCaptions.Hotkey(window?.Title);
        var id = Guid.NewGuid().ToString("D");
        var step = BuildStep(id, order, screenshot, job, grabbed, shot.Scale, window, el, caption);

        int? index;
        bool fromCursor;
        lock (_gate)
        {
            if (!SessionAlive(job.Generation)) return null;
            index = job.InsertAt ?? s.InsertCursor;
            fromCursor = job.InsertAt is null && s.InsertCursor is not null;
        }
        ProjectManifest manifest;
        try
        {
            manifest = index is { } at
                ? await _projects.InsertStepAtAsync(s.ProjectPath, step, at).ConfigureAwait(false)
                : await _projects.AddStepAsync(s.ProjectPath, step).ConfigureAwait(false);
        }
        catch
        {
            // The number is burned and the file stays; the next session seeds past it (EDGE-CAP-52).
            OrphanShot(_log, order, path);
            throw;
        }
        var landedIndex = manifest.Steps.FindIndex(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        var landed = landedIndex >= 0 ? manifest.Steps[landedIndex] : step;
        lock (_gate)
        {
            if (SessionAlive(job.Generation))
            {
                if (fromCursor) s.InsertCursor = index + 1;
                s.AddedStepIds.Add(id);
                s.Committed++;
                _lastGrabFailed = false;
            }
        }
        LogStep(order, job, mode, autoMode, window, el, filename, shot);
        if (job.Broadcast)
        {
            Raise(StepLanded, new StepLandedEventArgs(landed, landedIndex, s.ProjectPath), nameof(StepLanded));
            RaiseState();
        }
        return landed;
    }

    // The grab cascade of 2.8, first match wins: window, area, then on the resolved monitor the
    // auto region and the whole monitor. The menu selection path is WP-B3's.
    private Grabbed? Grab(CaptureTarget target, string mode, AutoMode? autoMode, ForegroundInfo? foreground, MonitorDescriptor? clickMonitor, (int X, int Y)? point)
    {
        if (mode == "window" || autoMode == AutoMode.Window)
        {
            var rect = mode == "window" ? PickedWindowRect(target.Window) : ForegroundRect(foreground);
            if (rect is { } r)
            {
                var mon = _screen.FromPoint((int)r.X, (int)r.Y) ?? clickMonitor;
                if (mon is not null)
                {
                    try
                    {
                        return GrabCropped(mon, CaptureGeometry.CropRect(mon.Bounds, r));
                    }
                    catch (Exception e)
                    {
                        WindowCaptureFailed(_log, e);
                    }
                }
            }
            else if (mode == "window")
            {
                PickedWindowNotFound(_log);
            }
        }

        if (mode == "area" && target.Area is { } area)
        {
            var mon = _screen.FromPoint((int)area.X, (int)area.Y) ?? clickMonitor;
            if (mon is not null)
            {
                try
                {
                    return GrabCropped(mon, CaptureGeometry.AreaCrop(mon.Bounds, area));
                }
                catch (Exception e)
                {
                    AreaCaptureFailed(_log, e);
                }
            }
        }

        var monitor = clickMonitor;
        if (mode == "screen" && target.MonitorId is { } monitorId) monitor = _screen.Monitors().FirstOrDefault(m => m.Id == monitorId) ?? clickMonitor;
        if (monitor is null) return null;

        if (autoMode == AutoMode.Region && point is { } p)
        {
            try
            {
                return GrabCropped(monitor, CaptureGeometry.RegionCrop(monitor.Bounds, monitor.ScaleFactor, new Point(p.X, p.Y)));
            }
            catch (Exception e)
            {
                RegionCaptureFailed(_log, e);
            }
        }

        try
        {
            return new Grabbed(_screen.Grab(monitor), monitor.Bounds.X, monitor.Bounds.Y, monitor);
        }
        catch (Exception e)
        {
            MonitorCaptureFailed(_log, e);
            return null;
        }
    }

    private Grabbed GrabCropped(MonitorDescriptor monitor, PixelRect crop)
    {
        var frame = _screen.Grab(monitor);
        var cropped = _codec.Crop(frame, crop.X, crop.Y, crop.Width, crop.Height);
        return new Grabbed(cropped, monitor.Bounds.X + crop.X, monitor.Bounds.Y + crop.Y, monitor);
    }

    // The picked window as it is now: gone, minimized or parked off screen gives null (EDGE-CAP-29).
    private Rect? PickedWindowRect(CaptureTargetWindow? picked) =>
        picked is not null && _windows.Resolve(picked) is { Minimized: false } w && OnScreen(w.FrameBounds) ? w.FrameBounds : null;

    private static Rect? ForegroundRect(ForegroundInfo? foreground) =>
        foreground is { Minimized: false } && (foreground.FrameBounds ?? foreground.WindowRect) is { } r && OnScreen(r) ? r : null;

    private static bool OnScreen(Rect r) => r.X > CaptureConstants.OffScreenSentinel && r.Y > CaptureConstants.OffScreenSentinel;

    // The own-window guard (2.7.1, INV-CAP-6): shotAI in the foreground, or the point on one of its windows. D3: no title check.
    private bool IsOwnUi(ForegroundInfo? foreground, (int X, int Y)? point) =>
        foreground is not null && foreground.Pid == _own.ProcessId || point is { } p && _own.PointHitsOwnWindow(p.X, p.Y);

    private MonitorDescriptor? PrimaryOrFirst()
    {
        var all = _screen.Monitors();
        return all.FirstOrDefault(m => m.IsPrimary) ?? all.FirstOrDefault();
    }

    // D6: a recorded capture that grabbed nothing is reported once per run of failures; a
    // landed step ends the run. The screenshot throws its own message instead.
    private void ReportGrabFailure(CaptureJob job)
    {
        if (!job.Broadcast) return;
        lock (_gate)
        {
            if (!SessionAlive(job.Generation) || _lastGrabFailed) return;
            _lastGrabFailed = true;
        }
        Raise(CaptureFailed, new CaptureErrorEventArgs(CaptureMessages.GrabFailed), nameof(CaptureFailed));
    }

    // The element query never fails a capture (INV-CAP-15): a fault reads as unavailable.
    private async Task<StepElement> ElementOrUnavailableAsync(Task<StepElement?> element)
    {
        try
        {
            return await element.ConfigureAwait(false) ?? StepElement.Unavailable;
        }
        catch (Exception e)
        {
            ElementQueryFailed(_log, e);
            return StepElement.Unavailable;
        }
    }

    // A dropped capture's query is left to finish; a fault is observed, never unobserved.
    private static void Observe(Task<StepElement?>? element) =>
        _ = element?.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    // writeFile(path, png, { flag: 'wx' }): created exclusively, so nothing is overwritten (INV-CAP-8).
    private static async Task WriteShotAsync(string path, byte[] png)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(png, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // The step of 2.6 step 15, keys in Electron's order. click.image is the click in the stored
    // image's pixels (INV-CAP-13); imageScale is written only when it is not exactly 1.
    private static ProjectStep BuildStep(string id, long order, string screenshot, CaptureJob job, Grabbed grabbed, double scale,
        CapturedWindow? window, StepElement element, string caption)
    {
        JsonObject? click = null;
        if (job.Point is { } p)
        {
            var image = new Point(JsMath.Round((p.X - grabbed.OriginX) * scale), JsMath.Round((p.Y - grabbed.OriginY) * scale));
            click = new StepClick(new Point(p.X, p.Y), image, ButtonWire(job.Button), null, scale != 1 ? scale : null).ToJson();
        }
        var monitor = grabbed.Monitor;
        return new ProjectStep(new JsonObject
        {
            ["id"] = id,
            ["order"] = (double)order,
            ["screenshot"] = screenshot,
            ["trigger"] = job.Trigger == StepTrigger.Click ? "click" : "hotkey",
            ["click"] = click,
            ["monitor"] = new CapturedMonitor(monitor.Id, monitor.Bounds, monitor.ScaleFactor).ToJson(),
            ["window"] = window?.ToJson(),
            ["element"] = element.ToJson(),
            ["caption"] = caption,
            ["crop"] = null,
            ["annotations"] = new JsonArray(),
        });
    }

    // 2.6 step 18. Q-CAP-17: the element's name goes to the debug line only; its type stays at info.
    private void LogStep(long order, CaptureJob job, string mode, AutoMode? autoMode, CapturedWindow? window, StepElement element, string filename, EncodedShot shot)
    {
        var trigger = job.Trigger == StepTrigger.Click ? "click" : "hotkey";
        var how = autoMode is { } auto ? "auto:" + AutoModeWire(auto) : mode;
        var right = job.Button == MouseButton.Right ? " right" : "";
        var insert = job.InsertAt is { } at ? " (insert@" + at.ToString(CultureInfo.InvariantCulture) + ")" : "";
        var elementType = element.Name is { Length: > 0 } ? " el=(" + (element.ControlType ?? "null") + ")" : "";
        var kb = JsMath.Round(shot.Png.Length / 1024.0).ToString(CultureInfo.InvariantCulture);
        var scale = shot.Scale != 1 ? " @" + shot.Scale.ToString("F2", CultureInfo.InvariantCulture) + "x" : "";
        StepCaptured(_log, string.Create(CultureInfo.InvariantCulture, $"step #{order} [{trigger}/{how}{right}]{insert} {window?.App ?? "screen"}{elementType} -> {filename} ({kb} KB{scale})"));
        if (element.Name is { Length: > 0 } name) StepElementNamed(_log, order, name, element.ControlType ?? "null");
    }

    private static string ButtonWire(MouseButton button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => "other",
    };

    private static string AutoModeWire(AutoMode mode) => mode switch
    {
        AutoMode.Window => "window",
        AutoMode.Region => "region",
        _ => "fullscreen",
    };

    // A grab: the image and the global pixel of its top-left, before the downscale (Electron's Grab).
    private sealed record Grabbed(PixelFrame Frame, double OriginX, double OriginY, MonitorDescriptor Monitor);
}
