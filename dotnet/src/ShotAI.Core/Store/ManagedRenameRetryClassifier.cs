namespace ShotAI.Core.Store;

/// <summary>
/// The managed <see cref="IRenameRetryClassifier"/> (spec 01 7.6), for Linux, the Core tests
/// and the self-test; never registered in the shipped app. A denied rename is <c>EACCES</c>, as
/// Node reports it on Linux, and a busy one is <c>EBUSY</c>.
/// </summary>
public sealed class ManagedRenameRetryClassifier : IRenameRetryClassifier
{
    // .NET puts the raw errno in HResult for an IOException on Linux and macOS, where EBUSY is 16.
    private const int Ebusy = 16;

    /// <inheritdoc/>
    public string? Classify(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ex switch
        {
            UnauthorizedAccessException => "EACCES",
            IOException io when io.HResult == Ebusy || io.Message.Contains("resource busy", StringComparison.OrdinalIgnoreCase) => "EBUSY",
            _ => null,
        };
    }
}
