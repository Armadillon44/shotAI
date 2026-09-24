using System.Runtime.InteropServices;

namespace ShotAI.Platform.Imaging;

/// <summary>
/// The WIC objects of one decode (spec 02 7.12: each image's objects are made and released inside
/// one job), released in the reverse of their creation when the scope ends, so a decoder never
/// holds its pixels or its stream until the garbage collector finds it (X19).
/// </summary>
internal sealed class WicScope : IDisposable
{
    private readonly List<object> _objects = [];

    /// <summary>Adds <paramref name="comObject"/> to the objects released at the end, and returns it.</summary>
    internal T Add<T>(T comObject)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(comObject);
        if (!_objects.Exists(o => ReferenceEquals(o, comObject))) _objects.Add(comObject);
        return comObject;
    }

    /// <summary>The number of objects the scope holds.</summary>
    internal int Count => _objects.Count;

    /// <summary>Releases every object, newest first.</summary>
    public void Dispose()
    {
        for (var i = _objects.Count - 1; i >= 0; i--)
        {
            // Final: an interface array marshaled in and back out raises an object's count twice.
            if (Marshal.IsComObject(_objects[i])) Marshal.FinalReleaseComObject(_objects[i]);
        }
        _objects.Clear();
    }
}
