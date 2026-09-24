namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// A small visible topmost window near the primary monitor's top-left that the input tests
/// click, so no synthetic click lands on anything else on the runner. It lives on a thread of
/// its own that pumps its messages, so it never hangs, and that destroys it, since only the
/// creating thread can; a static control ignores the clicks it gets.
/// </summary>
internal sealed class ClickTarget : IDisposable
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint WmQuit = 0x0012;

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private uint _threadId;
    private Exception? _failure;

    public ClickTarget(int x = 40, int y = 40, int width = 240, int height = 160)
    {
        (X, Y, Width, Height) = (x, y, width, height);
        _thread = new Thread(Run) { IsBackground = true, Name = "click target" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The click target did not open.");
        if (_failure is not null) throw new InvalidOperationException("The click target could not open.", _failure);
        // The runner's Start menu, when it is open, would take the clicks meant for the target.
        ShellOverlay.CloseIfOpen();
    }

    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>A point inside the window, <paramref name="dx"/> pixels right of its left third.</summary>
    public (int X, int Y) Point(int dx = 0) => (X + (Width / 3) + dx, Y + (Height / 2));

    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;

    public void Dispose()
    {
        User32.PostThreadMessage(_threadId, WmQuit, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(10));
        _ready.Dispose();
    }

    private void Run()
    {
        TestWindow window;
        try
        {
            window = TestWindow.Popup(User32.WsExTopmost | User32.WsExToolWindow | User32.WsExNoActivate, X, Y, Width, Height);
            if (!User32.SetWindowPos(window.Handle, User32.HwndTopmost, X, Y, Width, Height, SwpNoActivate | SwpShowWindow))
            {
                window.Dispose();
                throw new InvalidOperationException("SetWindowPos failed.");
            }
            // The queue exists before the post that ends it can come.
            User32.PeekMessage(out _, 0, 0, 0, 0);
            _threadId = User32.GetCurrentThreadId();
        }
        catch (Exception e)
        {
            _failure = e;
            _ready.Set();
            return;
        }
        _ready.Set();
        using (window)
        {
            while (User32.GetMessage(out var message, 0, 0, 0) > 0) User32.DispatchMessage(in message);
        }
    }
}
