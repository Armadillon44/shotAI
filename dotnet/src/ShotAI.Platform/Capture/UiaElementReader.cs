using System.Runtime.InteropServices;
using ShotAI.Core.Capture;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace ShotAI.Platform.Capture;

/// <summary>
/// The UI Automation read of the element under a point (spec 02 2.12.2, 7.6) for one query thread
/// of <see cref="UiaElementLocator"/>: <c>ElementFromPoint</c>, Core's climb
/// (<see cref="ElementMapping.Choose"/>) over the control view, and Core's mapping of the element
/// it chose, or of the hit when it chose none. Made on its MTA thread, used and disposed there.
/// </summary>
/// <remarks>
/// The automation object is <c>CUIAutomation8</c>'s, the class Windows 8 added with
/// <c>IUIAutomation2</c>, whose connection and transaction timeouts are set to
/// <see cref="CaptureConstants.UiaTimeoutMs"/> ms (the defaults are 2 s and 20 s), so a hung
/// provider gives up well inside the query's cap (D15); <c>CUIAutomation</c>'s only when that
/// class cannot be made. Every COM object a read takes is released when the read ends, so no
/// element of another process is held until a collection. A property that cannot be read gives
/// what the Rust locator gives: an empty name, type 0 and an empty rectangle.
/// </remarks>
internal sealed class UiaElementReader : IElementReader
{
    private readonly IUIAutomation _automation;
    private bool _disposed;

    /// <summary>Makes the automation object on the calling thread, which must be in the MTA.</summary>
    /// <exception cref="COMException">Neither automation class can be made.</exception>
    public UiaElementReader()
    {
        if (PInvoke.CoCreateInstance(typeof(CUIAutomation8).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out IUIAutomation automation).Failed)
            PInvoke.CoCreateInstance(typeof(CUIAutomation).GUID, null, CLSCTX.CLSCTX_INPROC_SERVER, out automation).ThrowOnFailure();
        if (automation is IUIAutomation2 timed)
        {
            timed.ConnectionTimeout = CaptureConstants.UiaTimeoutMs;
            timed.TransactionTimeout = CaptureConstants.UiaTimeoutMs;
        }
        _automation = automation;
    }

    /// <summary>The automation object, for the tests of its timeouts.</summary>
    internal IUIAutomation Automation => _automation;

    /// <inheritdoc/>
    /// <exception cref="COMException">The hit test or the control view failed, as the Rust locator's -2 and -3.</exception>
    public StepElement? Read(int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var held = new ComObjects();
        if (_automation.ElementFromPoint(new System.Drawing.Point(x, y)) is not { } hit) return null;
        held.Add(hit);
        var walker = held.Add(_automation.ControlViewWalker);
        var examined = new List<IUIAutomationElement>();
        var chosen = ElementMapping.Choose(Climb(hit, walker, examined, held));
        var element = chosen >= 0 ? examined[chosen] : hit;
        var bounds = BoundsOf(element);
        return ElementMapping.ToStep(chosen >= 0, chosen >= 0 ? NameOf(element) : null, TypeOf(element), bounds.left, bounds.top, bounds.right, bounds.bottom);
    }

    /// <summary>Releases the automation object; on the reader's own thread.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Marshal.IsComObject(_automation)) Marshal.FinalReleaseComObject(_automation);
    }

    // 2.12.2 step 4: the hit and then its control-view parents, nearest first, each read only when
    // the climb asks for it. Its type is read only when its name is not empty, as the Rust test
    // short-circuits; a parent that cannot be read ends the climb (.ok()).
    private static IEnumerable<ElementFacts> Climb(IUIAutomationElement hit, IUIAutomationTreeWalker walker, List<IUIAutomationElement> examined, ComObjects held)
    {
        for (var current = hit; current is not null; current = ParentOf(walker, current, held))
        {
            examined.Add(current);
            var name = NameOf(current);
            yield return new ElementFacts(name, name.Length > 0 ? TypeOf(current) : 0);
        }
    }

    private static IUIAutomationElement? ParentOf(IUIAutomationTreeWalker walker, IUIAutomationElement element, ComObjects held)
    {
        try
        {
            return walker.GetParentElement(element) is { } parent ? held.Add(parent) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // CurrentName, made well formed as Rust's lossy decode leaves it; "" when it cannot be read.
    private static string NameOf(IUIAutomationElement element)
    {
        BSTR name;
        try
        {
            name = element.CurrentName;
        }
        catch (Exception)
        {
            return "";
        }
        try
        {
            return JsString.ToWellFormed(name.ToString() ?? "");
        }
        finally
        {
            Marshal.FreeBSTR(name);
        }
    }

    private static int TypeOf(IUIAutomationElement element)
    {
        try
        {
            return (int)element.CurrentControlType;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static RECT BoundsOf(IUIAutomationElement element)
    {
        try
        {
            return element.CurrentBoundingRectangle;
        }
        catch (Exception)
        {
            return default;
        }
    }

    // The COM objects of one read, released newest first when it ends.
    private sealed class ComObjects : IDisposable
    {
        private readonly List<object> _objects = [];

        public T Add<T>(T comObject)
            where T : class
        {
            if (!_objects.Exists(o => ReferenceEquals(o, comObject))) _objects.Add(comObject);
            return comObject;
        }

        public void Dispose()
        {
            for (var i = _objects.Count - 1; i >= 0; i--)
            {
                if (Marshal.IsComObject(_objects[i])) Marshal.FinalReleaseComObject(_objects[i]);
            }
            _objects.Clear();
        }
    }
}
