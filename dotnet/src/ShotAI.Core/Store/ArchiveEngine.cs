using System.Buffers;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ShotAI.Core.Store;

/// <summary>
/// Packs a project's bulk folders into <c>archive.zip</c> and restores them (spec 01 2.9.13 and
/// 7.9): <c>src/main/archive.ts</c>, streamed instead of built in memory, and checked by content
/// before any original is deleted.
/// </summary>
/// <remarks>
/// Only <c>shots/</c> and <c>export/</c> are archived; <c>project.json</c> is never touched, so an
/// archived project keeps listing. Both directions fail closed: the originals go only after the
/// zip is written and verified, and the zip goes only after every entry is extracted.
/// </remarks>
public sealed partial class ArchiveEngine(IPathProbe probe, AtomicFile atomic, ILogger<ArchiveEngine> log)
{
    /// <summary>The archive's name at the project root.</summary>
    public const string ZipName = "archive.zip";

    private const int BufferSize = 81920;

    private readonly IPathProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));
    private readonly AtomicFile _atomic = atomic ?? throw new ArgumentNullException(nameof(atomic));
    private readonly ILogger<ArchiveEngine> _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <summary>The folders archived and removed; nothing else in the project is.</summary>
    public static IReadOnlyList<string> ArchivedDirs { get; } = ["shots", "export"];

    /// <summary>Runs between writing the temporary zip and verifying it; a test's way to damage the file.</summary>
    internal Func<string, Task>? BeforeVerify { get; set; }

    /// <summary><c>isArchivedOnDisk</c>: something named <c>archive.zip</c> is at the root, as Node's <c>fs.access</c> sees it.</summary>
    public static bool IsArchivedOnDisk(string projectDir)
    {
        ArgumentNullException.ThrowIfNull(projectDir);
        var zip = Path.Join(projectDir, ZipName);
        return File.Exists(zip) || Directory.Exists(zip);
    }

    /// <summary>
    /// <c>packArchive</c>: a no-op when already archived; otherwise zips every file under the
    /// bulk folders, verifies the zip against what was read (IMPROVEMENT D-12), renames it into
    /// place with the retry schedule (D-14), and only then removes the folders.
    /// </summary>
    /// <remarks>
    /// A link is neither followed nor zipped, and is removed with its folder (EDGE-MODEL-38). A
    /// cloud placeholder is an ordinary file, read in full, which hydrates it (D-15); a read
    /// failure, or an entry the probe cannot classify, fails the pack before anything is
    /// deleted. A project with no files becomes an empty zip (EDGE-MODEL-16).
    /// <paramref name="ct"/> is honored until the zip is in place, never after.
    /// </remarks>
    /// <exception cref="ArchiveException">The written zip does not match the files.</exception>
    public async Task PackAsync(string projectDir, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectDir);
        if (IsArchivedOnDisk(projectDir)) return;
        var zipPath = Path.Join(projectDir, ZipName);

        var files = new List<(string Rel, string Abs)>();
        foreach (var dir in ArchivedDirs) Collect(dir, Path.Join(projectDir, dir), files);

        var tmp = zipPath + ".tmp";
        try
        {
            var written = await WriteZipAsync(tmp, files, ct).ConfigureAwait(false);
            if (BeforeVerify is { } hook) await hook(tmp).ConfigureAwait(false);
            await VerifyAsync(tmp, written, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            await _atomic.RenameWithRetryAsync(tmp, zipPath).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }

        foreach (var dir in ArchivedDirs) ReparseSafeDelete.DeleteTree(Path.Join(projectDir, dir), _probe);
        Packed(_log, files.Count, zipPath);
    }

    /// <summary>
    /// <c>unpackArchive</c>: a no-op when there is no <c>archive.zip</c>; otherwise extracts every
    /// file entry into <c>shots/</c> or <c>export/</c>, overwriting, then deletes the zip.
    /// </summary>
    /// <remarks>
    /// Names are read as UTF-8 whatever their flag says, as JSZip reads them. An entry is a
    /// directory, and skipped, when its name ends in <c>/</c> or its attributes carry 0x10. A
    /// name with a <c>.</c>, <c>..</c> or empty segment, split on both <c>/</c> and <c>\</c>, is
    /// refused rather than resolved (IMPROVEMENT [SECURITY] D-13, D-25). Every write goes through
    /// <see cref="PathConfine.ConfineNoLinks"/>. On any failure the zip is kept, and a later
    /// restore overwrites what this one extracted. No size cap applies (Q-MODEL-7).
    /// </remarks>
    /// <exception cref="ArchiveException">An entry that may not be restored; the zip is kept.</exception>
    public async Task UnpackAsync(string projectDir, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectDir);
        if (!IsArchivedOnDisk(projectDir)) return;
        var zipPath = Path.Join(projectDir, ZipName);

        var written = new List<string>();
        var zip = await ZipFile.OpenAsync(zipPath, ZipArchiveMode.Read, Encoding.UTF8, ct).ConfigureAwait(false);
        await using (zip.ConfigureAwait(false))
        {
            foreach (var entry in zip.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (IsDirectory(entry)) continue;
                var name = entry.FullName;
                var rel = name.Replace('\\', '/');
                if (!IsArchivedName(rel) || HasUnsafeSegment(name)) throw ArchiveException.UnexpectedPath(name);
                var abs = PathConfine.ConfineNoLinks(projectDir, rel, _probe) ?? throw ArchiveException.OutsideProject(name);
                Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                await entry.ExtractToFileAsync(abs, overwrite: true, ct).ConfigureAwait(false);
                written.Add(abs);
            }
        }

        foreach (var abs in written)
        {
            if (!File.Exists(abs)) throw new FileNotFoundException("a restored file is missing, so archive.zip was kept", abs);
        }
        File.Delete(zipPath);
        Restored(_log, written.Count, zipPath);
    }

    /// <summary>
    /// The written zip holds exactly <paramref name="expected"/>'s names, and each entry
    /// decompresses to the length and CRC-32 recorded while reading the original. The zip's own
    /// stored CRC is not trusted: checking the file against itself would prove nothing (D-12).
    /// </summary>
    internal static async Task VerifyAsync(string zipPath, IReadOnlyDictionary<string, (long Length, uint Crc)> expected, CancellationToken ct)
    {
        var got = new List<string>();
        var matches = true;
        try
        {
            var zip = await ZipFile.OpenAsync(zipPath, ZipArchiveMode.Read, Encoding.UTF8, ct).ConfigureAwait(false);
            await using (zip.ConfigureAwait(false))
            {
                foreach (var entry in zip.Entries)
                {
                    if (IsDirectory(entry)) continue;
                    got.Add(entry.FullName);
                    // An unexpected name fails the name comparison below; a known one must also match by content.
                    if (expected.TryGetValue(entry.FullName, out var want) && await HashAsync(entry, ct).ConfigureAwait(false) != want) matches = false;
                }
            }
        }
        catch (InvalidDataException e)
        {
            throw ArchiveException.VerificationFailed(got.Count, expected.Count, e);
        }
        got.Sort(StringComparer.Ordinal);
        if (!matches || !got.SequenceEqual(expected.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw ArchiveException.VerificationFailed(got.Count, expected.Count);
    }

    // walkFiles, through the probe: a directory is descended in name order, a file collected,
    // a link skipped, and anything the probe cannot classify stops the pack. The entry name
    // joins with '/', the path with the platform's separator.
    private void Collect(string rel, string abs, List<(string Rel, string Abs)> files)
    {
        switch (_probe.Probe(abs))
        {
            case PathKind.Missing:
            case PathKind.Link:
                return;
            case PathKind.File:
                files.Add((rel, abs));
                return;
            case PathKind.Directory:
                foreach (var child in Directory.GetFileSystemEntries(abs).Select(Path.GetFileName).Order(StringComparer.Ordinal))
                    Collect(rel + "/" + child, Path.Join(abs, child), files);
                return;
            default:
                throw new IOException($"cannot tell what '{abs}' is, so the project was not archived");
        }
    }

    // One entry per file, no directory entries, names relative to the project with '/'. Each
    // source is read once; its length and CRC-32 are recorded from those same bytes.
    private static async Task<Dictionary<string, (long Length, uint Crc)>> WriteZipAsync(
        string tmp, List<(string Rel, string Abs)> files, CancellationToken ct)
    {
        var written = new Dictionary<string, (long, uint)>(StringComparer.Ordinal);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var stream = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                var zip = await ZipArchive.CreateAsync(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8, ct).ConfigureAwait(false);
                await using (zip.ConfigureAwait(false))
                {
                    foreach (var (rel, abs) in files)
                    {
                        var crc = new Crc32();
                        long length = 0;
                        var source = new FileStream(abs, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using (source.ConfigureAwait(false))
                        {
                            var target = await zip.CreateEntry(rel, CompressionLevel.Optimal).OpenAsync(ct).ConfigureAwait(false);
                            await using (target.ConfigureAwait(false))
                            {
                                int read;
                                while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                                {
                                    crc.Append(buffer.AsSpan(0, read));
                                    length += read;
                                    await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                                }
                            }
                        }
                        written[rel] = (length, crc.GetCurrentHashAsUInt32());
                    }
                }
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return written;
    }

    private static async Task<(long Length, uint Crc)> HashAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        var crc = new Crc32();
        long length = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var stream = await entry.OpenAsync(ct).ConfigureAwait(false);
            await using (stream.ConfigureAwait(false))
            {
                int read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    crc.Append(buffer.AsSpan(0, read));
                    length += read;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return (length, crc.GetCurrentHashAsUInt32());
    }

    // JSZip's dir: a name ending in '/', or the DOS directory attribute.
    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') || (entry.ExternalAttributes & 0x10) != 0;

    // rel === 'shots' || rel === 'export' || rel.startsWith('shots/') || rel.startsWith('export/').
    private static bool IsArchivedName(string rel) =>
        ArchivedDirs.Any(d => rel == d || rel.StartsWith(d + "/", StringComparison.Ordinal));

    // Split on both separators: '.', '..' or an empty segment would be resolved by a lenient
    // reader, and resolving '..' is how a name reaches project.json (EDGE-MODEL-14, EDGE-MODEL-48).
    private static bool HasUnsafeSegment(string name) =>
        name.Split('/', '\\').Any(s => s.Length == 0 || s == "." || s == "..");

    // Errors ignored: the failure that led here is the one that matters.
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "archive: packed {Count} file(s) \u2192 {ZipPath}")]
    private static partial void Packed(ILogger logger, int count, string zipPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "archive: restored {Count} file(s) from {ZipPath}")]
    private static partial void Restored(ILogger logger, int count, string zipPath);
}
