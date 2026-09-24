namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// A window of the system <c>STATIC</c> class, which needs no class of its own and no message
/// loop, owned by the creating thread and destroyed on dispose. A test that uses one creates,
/// uses and disposes it on one thread, so it has no <c>await</c> in between.
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private bool _destroyed;

    private TestWindow(nint handle) => Handle = handle;

    public nint Handle { get; }

    /// <summary>A hidden popup, top-level like every window the app shows.</summary>
    public static TestWindow Popup(int exStyle = 0, int x = 10, int y = 20, int width = 300, int height = 200, string? name = null) =>
        Create(exStyle, User32.WsPopup, x, y, width, height, parent: 0, name);

    /// <summary>A visible top-level window with a caption, which can be minimized.</summary>
    public static TestWindow Overlapped() =>
        Create(0, User32.WsOverlappedWindow | User32.WsVisible, 10, 20, 300, 200, parent: 0, name: "shotAI test");

    /// <summary>A message-only window named <paramref name="name"/>, as the running instance creates.</summary>
    public static TestWindow MessageOnly(string name) =>
        Create(0, 0, 0, 0, 0, 0, User32.HwndMessage, name);

    public int ExStyle => User32.GetWindowLong(Handle, User32.GwlExStyle);

    /// <summary>Destroys the window now; the handle stays readable, as a handle that no longer names a window.</summary>
    public void Destroy()
    {
        if (!User32.DestroyWindow(Handle)) throw new InvalidOperationException("DestroyWindow failed.");
        _destroyed = true;
    }

    public void Dispose()
    {
        if (_destroyed) return;
        _destroyed = true;
        User32.DestroyWindow(Handle);
    }

    private static TestWindow Create(int exStyle, int style, int x, int y, int width, int height, nint parent, string? name)
    {
        var handle = User32.CreateWindowEx(exStyle, "STATIC", name, style, x, y, width, height, parent, 0, 0, 0);
        return handle != 0 ? new TestWindow(handle) : throw new InvalidOperationException("CreateWindowEx failed.");
    }
}
