using ShotAI.Core.Store;

namespace ShotAI.Platform.FileSystem;

/// <summary>
/// The shipped <see cref="IRenameRetryClassifier"/> (spec 01 7.6): the Win32 error in a failed
/// <see cref="File.Move(string, string, bool)"/> mapped to the code Electron's
/// <c>renameWithRetry</c> sees, so the same failures are retried.
/// </summary>
/// <remarks>
/// The mapping is libuv 1.52.1's <c>uv_translate_sys_error</c> (<c>src/win/error.c</c>), the
/// libuv of Electron 42.5.0 (Node 24.17.0), restricted to the three retried codes. Before
/// libuv 1.50, <c>ERROR_NOACCESS</c> also mapped to EACCES; it is EFAULT now, so it is not
/// retried.
/// </remarks>
internal sealed class WindowsRenameRetryClassifier : IRenameRetryClassifier
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;
    private const int ErrorPipeBusy = 231;
    private const int ErrorElevationRequired = 740;
    private const int ErrorPrivilegeNotHeld = 1314;
    private const int ErrorCantAccessFile = 1920;
    private const int WsaEAccess = 10013;

    /// <inheritdoc/>
    public string? Classify(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        // .NET carries the Win32 error as HRESULT_FROM_WIN32, facility 7; an access-denied
        // UnauthorizedAccessException has the same value, 0x80070005.
        var hr = unchecked((uint)ex.HResult);
        if (hr >> 16 != 0x8007) return null;
        return (int)(hr & 0xFFFF) switch
        {
            ErrorAccessDenied or ErrorPrivilegeNotHeld => "EPERM",
            ErrorSharingViolation or ErrorLockViolation or ErrorPipeBusy => "EBUSY",
            ErrorElevationRequired or ErrorCantAccessFile or WsaEAccess => "EACCES",
            _ => null,
        };
    }
}
