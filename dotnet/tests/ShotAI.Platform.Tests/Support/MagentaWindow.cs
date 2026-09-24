using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ShotAI.Platform.Tests.Support;

/// <summary>
/// The protection probe's window as a test fixture (spec 02 2.16, 8.4): a solid magenta WPF
/// window, frameless, topmost and never activated, on a UI thread of its own. With
/// <c>layered</c> it is an <c>AllowsTransparency</c> window, which WPF draws with
/// <c>UpdateLayeredWindow</c>, as the app's pill and overlay are drawn (Q-CAP-15).
/// </summary>
internal sealed class MagentaWindow : IDisposable
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private readonly Window _window;

    public MagentaWindow(bool layered, int x = 120, int y = 120, int width = 700, int height = 500)
    {
        Ui = new UiThread(layered ? "layered magenta window" : "magenta window");
        (_window, Handle) = Ui.Invoke(() =>
        {
            var w = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = layered,
                Background = new SolidColorBrush(Color.FromRgb(255, 0, 255)),
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = x,
                Top = y,
                Width = width,
                Height = height,
            };
            w.Show();
            var hwnd = new WindowInteropHelper(w).Handle;
            // Physical pixels whatever the monitor's scale, on top, not activated.
            if (!User32.SetWindowPos(hwnd, User32.HwndTopmost, x, y, width, height, SwpNoActivate | SwpShowWindow))
                throw new InvalidOperationException("SetWindowPos failed.");
            return (w, hwnd);
        });
    }

    public UiThread Ui { get; }

    public nint Handle { get; }

    public void Dispose()
    {
        Ui.Invoke(_window.Close);
        Ui.Dispose();
    }
}
