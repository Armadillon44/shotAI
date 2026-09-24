using ShotAI.Platform;

namespace ShotAI.App;

/// <summary>The entry point (spec 12 7.9.1, ARCHITECTURE 4.2 step 0).</summary>
public static class Program
{
    /// <summary>Hardens the DLL search, then runs the app; the result is the exit code.</summary>
    /// <param name="args">Unused: WPF passes the arguments to <c>App.OnStartup</c> itself.</param>
    [STAThread]
    public static int Main(string[] args)
    {
        // Must precede any native load, WPF's own included (PresentationNative, wpfgfx).
        if (!DllSearchHardening.Apply()) Environment.FailFast("SetDefaultDllDirectories failed");
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
