using System.Windows;
using System.Windows.Controls;

namespace ShotAI.App.Chrome;

/// <summary>
/// The one notice control (spec 03 7.4.10, 06 7.10): renders its data context's notices. Each
/// notice's text is a <see cref="LiveRegion"/>, announced when the notice appears and each time
/// its text is replaced, because WPF raises nothing for a live region by itself.
/// </summary>
public partial class NoticeHost : UserControl
{
    /// <summary>At most this wide, as <c>max-width: min(92%, 680px)</c> reads.</summary>
    public const double MaxStackWidth = 680;

    /// <summary>The share of the area the stack may take.</summary>
    public const double StackShare = 0.92;

    /// <summary>A host showing its data context's notices.</summary>
    public NoticeHost()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Stack.MaxWidth = StackWidth(e.NewSize.Width);
        AddHandler(LiveRegion.AnnouncedEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is TextBlock text) Announced?.Invoke(this, text);
        }));
    }

    /// <summary>Raised after a notice's text was announced; the tests' view of the announcement.</summary>
    internal event EventHandler<TextBlock>? Announced;

    /// <summary>The stack's width limit in an area <paramref name="width"/> wide.</summary>
    internal static double StackWidth(double width) => Math.Min(Math.Max(0, width) * StackShare, MaxStackWidth);
}
