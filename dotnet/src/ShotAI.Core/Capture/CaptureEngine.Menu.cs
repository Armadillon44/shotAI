using ShotAI.Core.Model;

namespace ShotAI.Core.Capture;

public sealed partial class CaptureEngine
{
    // The dispatcher's mousedown (2.3, 7.11), in Electron's order with the native gates: the
    // own-window check first (D1), then the left-button double-click collapse, the element query
    // only for a click that will be captured (D2), then the right-click arm, the menu selection,
    // or a plain capture that disarms.
    private void OnMouseDown(MouseDown e)
    {
        int generation;
        lock (_gate)
        {
            if (_session is not { Kind: SessionKind.Recording, Paused: false } s || _stopping > 0 || _tornDown) return;
            generation = s.Generation;
        }
        if (_own.PointHitsOwnWindow(e.X, e.Y)) return;
        var point = (e.X, e.Y);
        var now = _clock.NowMs();

        if (e.Button == MouseButton.Left)
        {
            var max = CaptureConstants.DoubleClickDist * ScaleAt(point);
            bool isDouble;
            lock (_gate)
            {
                isDouble = _lastLeftClick is { } last && now - last.At <= CaptureConstants.DoubleClickMs && WithinBox(point, last.Point, max, max);
                _lastLeftClick = (now, point);
            }
            if (isDouble)
            {
                DoubleClickIgnored(_log, e.X, e.Y);
                return;
            }
        }

        var element = _elements.ElementAtAsync(e.X, e.Y);
        if (e.Button == MouseButton.Right)
        {
            // The owner is read now, while it is still the focused window (2.4.2).
            var owner = FocusedOwnerBounds();
            if (Arm(new MenuArm(now + CaptureConstants.MenuFollowupWindowMs, owner, point, 0, generation))) MenuArmed(_log, e.X, e.Y);
            Enqueue(new CaptureJob(StepTrigger.Click, point, MouseButton.Right, MenuPopup: false, null, null, null, element, Broadcast: true, SkipOwnWindowGuard: false, generation));
            return;
        }

        MenuArm? arm;
        lock (_gate) arm = _menuArm;
        if (e.Button == MouseButton.Left && arm is not null && now < arm.Until && NearMenuPoint(point, arm.LastPoint))
        {
            MonitorFrame? polled;
            Rect? owner;
            lock (_gate)
            {
                // The selection takes the polled frame: the arm no longer holds it (7.7).
                polled = arm.Frame;
                arm.Frame = null;
                owner = arm.OwnerBounds;
            }
            var preGrab = polled ?? GrabClickMonitorSync(point);
            if (polled is not null) MenuSelectionPolled(_log, e.X, e.Y);
            else if (preGrab is not null) MenuSelectionGrabbed(_log, e.X, e.Y);
            else MenuSelectionWithoutFrame(_log, e.X, e.Y);

            var chain = arm.Chain + 1;
            if (chain < CaptureConstants.MaxMenuChain)
            {
                // The clock is read again, after the click-time grab, as Electron does.
                Arm(new MenuArm(_clock.NowMs() + CaptureConstants.SubmenuFollowupWindowMs, owner, point, chain, generation));
            }
            else
            {
                MenuChainLimit(_log, CaptureConstants.MaxMenuChain);
                Disarm();
            }
            Enqueue(new CaptureJob(StepTrigger.Click, point, MouseButton.Left, MenuPopup: true, owner, preGrab, null, element, Broadcast: true, SkipOwnWindowGuard: false, generation));
            return;
        }

        if (arm is not null)
        {
            var reason = e.Button != MouseButton.Left ? "button=" + ButtonWire(e.Button)
                : now >= arm.Until ? "window expired"
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"too far from ({arm.LastPoint.X},{arm.LastPoint.Y})");
            MenuDisarmed(_log, e.X, e.Y, reason);
        }
        Disarm();
        Enqueue(new CaptureJob(StepTrigger.Click, point, e.Button, MenuPopup: false, null, null, null, element, Broadcast: true, SkipOwnWindowGuard: false, generation));
    }

    // Installs an arm and starts its poll, replacing any earlier arm. An arm whose session has
    // ended, paused or started stopping in the meantime is dropped, so no arm outlives its
    // recording or lives in a paused one. The replaced arm's frame is taken under the lock, so
    // no selection can take it too, and disposed after it (7.7).
    private bool Arm(MenuArm arm)
    {
        MenuArm? replaced;
        MonitorFrame? frame;
        lock (_gate)
        {
            if (_session is not { Kind: SessionKind.Recording, Paused: false } s || s.Generation != arm.Generation || _stopping > 0 || _tornDown)
            {
                replaced = arm;
            }
            else
            {
                replaced = _menuArm;
                _menuArm = arm;
            }
            frame = TakeFrame(replaced);
        }
        replaced?.Dispose();
        frame?.Frame.Dispose();
        if (ReferenceEquals(replaced, arm)) return false;
        var poll = PollAsync(arm);
        lock (_gate) _lastPoll = poll;
        return true;
    }

    // disarmMenu: the arm goes, and its poll and its frame with it.
    private void Disarm()
    {
        MenuArm? arm;
        MonitorFrame? frame;
        lock (_gate)
        {
            arm = _menuArm;
            _menuArm = null;
            frame = TakeFrame(arm);
        }
        arm?.Dispose();
        frame?.Frame.Dispose();
    }

    // The arm's frame, which the arm no longer holds; under the lock.
    private static MonitorFrame? TakeFrame(MenuArm? arm)
    {
        var frame = arm?.Frame;
        if (arm is not null) arm.Frame = null;
        return frame;
    }

    /// <summary>The poll of the latest arm, for the tests.</summary>
    internal Task? LastPollForTest
    {
        get
        {
            lock (_gate) return _lastPoll;
        }
    }

    /// <summary>The current arm, for the tests.</summary>
    internal MenuArm? ArmForTest
    {
        get
        {
            lock (_gate) return _menuArm;
        }
    }

    // startMenuPolling (2.4.3): every 400 ms while the arm is current, unexpired and unpaused,
    // grab the monitor under the arm's point, at most 32 frames. A tick while this arm's grab is
    // in flight, or with no monitor, is skipped without counting. The grab is not awaited, so the
    // ticks keep Electron's interval. It never faults (7.11): a failed lookup or grab is logged,
    // and a disarm ends it through the arm's token.
    private async Task PollAsync(MenuArm arm)
    {
        var frames = 0;
        try
        {
            while (true)
            {
                await _clock.DelayAsync(CaptureConstants.MenuPollMs, arm.Token).ConfigureAwait(false);
                var now = _clock.NowMs();
                lock (_gate)
                {
                    if (!ReferenceEquals(_menuArm, arm) || _session is not { } s || s.Generation != arm.Generation) return;
                    if (now >= arm.Until || s.Paused) return;
                    if (frames >= CaptureConstants.MaxPollFrames) return;
                    if (arm.Polling) continue;
                }
                MonitorDescriptor? monitor;
                try
                {
                    monitor = _screen.FromPoint(arm.LastPoint.X, arm.LastPoint.Y) ?? PrimaryOrFirst();
                }
                catch (Exception e)
                {
                    MenuPollFailed(_log, e);
                    continue;
                }
                if (monitor is null) continue;
                lock (_gate)
                {
                    if (!ReferenceEquals(_menuArm, arm)) return;
                    arm.Polling = true;
                }
                frames++;
                _ = PollGrabAsync(arm, monitor);
            }
        }
        catch (OperationCanceledException)
        {
            // Disarmed, re-armed, paused, stopped or torn down.
        }
    }

    // One poll grab: the frame lands only on the arm that asked for it (INV-CAP-19). The frame
    // it replaces, or the frame itself when its arm is gone, is disposed outside the lock (7.7).
    private async Task PollGrabAsync(MenuArm arm, MonitorDescriptor monitor)
    {
        try
        {
            var frame = await Task.Run(() => _screen.Grab(monitor), arm.Token).ConfigureAwait(false);
            PixelFrame? drop;
            lock (_gate)
            {
                if (ReferenceEquals(_menuArm, arm))
                {
                    drop = arm.Frame?.Frame;
                    arm.Frame = new MonitorFrame(frame, monitor);
                }
                else
                {
                    drop = frame;
                }
            }
            drop?.Dispose();
        }
        catch (OperationCanceledException)
        {
            // The arm went before the grab started.
        }
        catch (Exception e)
        {
            MenuPollFailed(_log, e);
        }
        finally
        {
            lock (_gate) arm.Polling = false;
        }
    }

    // grabClickMonitorSync (2.4.2): the monitor under the selection, read on the dispatcher at
    // mousedown, because any later read races the menu's dismissal (EDGE-CAP-1).
    private MonitorFrame? GrabClickMonitorSync((int X, int Y) point)
    {
        try
        {
            var monitor = _screen.FromPoint(point.X, point.Y) ?? PrimaryOrFirst();
            return monitor is null ? null : new MonitorFrame(_screen.Grab(monitor), monitor);
        }
        catch (Exception e)
        {
            SyncMenuGrabFailed(_log, e);
            return null;
        }
    }

    // focusedWindowBounds (2.4.2): the foreground window's bounds, off-screen or failed reads
    // giving null.
    private Rect? FocusedOwnerBounds()
    {
        try
        {
            return ForegroundRect(_windows.Foreground());
        }
        catch (Exception)
        {
            return null;
        }
    }

    // nearMenuPoint (2.3.1): within 640 x 680 logical pixels, at the new click's monitor scale.
    private bool NearMenuPoint((int X, int Y) point, (int X, int Y) last)
    {
        var scale = ScaleAt(point);
        return WithinBox(point, last, CaptureConstants.MenuProximityX * scale, CaptureConstants.MenuProximityY * scale);
    }

    // The scale factor of the monitor under the point (2.3.1): Electron's `?? 1`, so 1 off every
    // monitor or when the lookup throws, and a factor of 0 is kept (the click box's `|| 1` is 2.8.2's).
    private double ScaleAt((int X, int Y) point)
    {
        try
        {
            return _screen.FromPoint(point.X, point.Y)?.ScaleFactor ?? 1;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private static bool WithinBox((int X, int Y) a, (int X, int Y) b, double maxX, double maxY) =>
        Math.Abs((long)a.X - b.X) <= maxX && Math.Abs((long)a.Y - b.Y) <= maxY;
}
