using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ShotAI.Core.Json;
using ShotAI.Core.Model;
using ShotAI.Core.Report.Operations;

namespace ShotAI.Core.Store;

// The step operations of spec 01 2.9.12. Each is one queued job that runs the gate, reads the
// manifest fresh, changes the step list, renumbers it, re-dates the project and writes.
public sealed partial class ProjectStore
{
    /// <inheritdoc/>
    /// <remarks>The step is copied at the call, so a later change by the caller does not reach the queued write.</remarks>
    public Task<ProjectManifest> AddStepAsync(string projectPath, ProjectStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var copy = step.DeepClone();
        return EditStepsAsync(projectPath, (_, m) =>
        {
            m.Steps.Add(copy);
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc/>
    public Task<ProjectManifest> InsertStepAtAsync(string projectPath, ProjectStep step, double? atIndex)
    {
        ArgumentNullException.ThrowIfNull(step);
        var copy = step.DeepClone();
        return EditStepsAsync(projectPath, (_, m) =>
        {
            m.Steps.Insert(InsertIndex(atIndex, m.Steps.Count), copy);
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc/>
    /// <remarks>The step's files stay on disk; the first step with the id is removed.</remarks>
    public Task<ProjectManifest> DeleteStepAsync(string projectPath, string stepId)
    {
        ArgumentNullException.ThrowIfNull(stepId);
        return EditStepsAsync(projectPath, (_, m) =>
        {
            var i = m.Steps.FindIndex(s => string.Equals(s.Id, stepId, StringComparison.Ordinal));
            if (i < 0) throw new StepNotFoundException(stepId);
            m.Steps.RemoveAt(i);
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// After the manifest write, each removed step's <c>screenshot</c> and <c>flattened</c> are
    /// deleted through <see cref="PathConfine.ConfineNoLinks"/>, errors ignored. A path that is
    /// not a string is no file, so a hand-edited step never fails an operation that already
    /// committed (IMPROVEMENT D-23, EDGE-MODEL-50).
    /// </remarks>
    public Task<ProjectManifest> DeleteStepsAsync(string projectPath, IReadOnlyCollection<string> stepIds)
    {
        ArgumentNullException.ThrowIfNull(stepIds);
        var ids = new HashSet<string>(stepIds, StringComparer.Ordinal);
        List<ProjectStep> removed = [];
        return EditStepsAsync(
            projectPath,
            (_, m) =>
            {
                removed = m.Steps.FindAll(s => s.Id is { } id && ids.Contains(id));
                m.Steps.RemoveAll(s => s.Id is { } id && ids.Contains(id));
                return Task.CompletedTask;
            },
            (resolved, _) =>
            {
                foreach (var step in removed)
                {
                    foreach (var rel in new[] { step.Screenshot, step.Flattened })
                    {
                        if (string.IsNullOrEmpty(rel)) continue;
                        var abs = PathConfine.ConfineNoLinks(resolved, rel, _probe);
                        if (abs is not null) TryDeleteFile(abs);
                    }
                }
            });
    }

    /// <inheritdoc/>
    /// <remarks>Never drops a step: <see cref="StepList.Reorder"/> (IMPROVEMENT D-10).</remarks>
    public Task<ProjectManifest> ReorderStepsAsync(string projectPath, IReadOnlyList<string> orderedIds)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        var order = orderedIds.ToArray();
        return EditStepsAsync(projectPath, (_, m) =>
        {
            var reordered = StepList.Reorder(m.Steps, order);
            m.Steps.Clear();
            m.Steps.AddRange(reordered);
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The step comes from <see cref="TextStepFactory"/>, as the report's optimistic operation
    /// does. A non-null <paramref name="callout"/> that is not a known kind is refused before
    /// anything is queued (V13).
    /// </remarks>
    public Task<ProjectManifest> AddTextStepAsync(string projectPath, double atIndex, string? callout)
    {
        var step = TextStepFactory.Create(_newId(), callout);
        return EditStepsAsync(projectPath, (_, m) =>
        {
            m.Steps.Insert(JsMath.ClampIndex(atIndex, m.Steps.Count), step);
            return Task.CompletedTask;
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The checks run in Electron's order (EDGE-IPC-46): <see cref="ImportLimits"/>, then the
    /// magic bytes, then, in the job, the gate. The file is <c>shots/step-NNNN.png</c> or
    /// <c>.jpg</c>, numbered past both the step count and every <c>step-N.</c> file already in
    /// <c>shots/</c>, written only through <see cref="PathConfine.ConfineNoLinks"/> (IMPROVEMENT
    /// [SECURITY] D-22) and never over an existing file. The image is written before the
    /// manifest, so a failed manifest write leaves an orphan the next counter skips (EDGE-MODEL-57).
    /// </remarks>
    public async Task<ProjectManifest> ImportStepAsync(string projectPath, ReadOnlyMemory<byte> bytes, double? atIndex)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ImportLimits.Check(bytes.Length);
        var ext = DetectImage(bytes.Span) ?? throw new UnsupportedImageException();
        return await EditStepsAsync(projectPath, async (resolved, m) =>
        {
            var shots = Path.Join(resolved, "shots");
            Directory.CreateDirectory(shots);
            var filename = ShotFileName(NextShotNumber(ListNames(shots), m.Steps.Count), ext);
            var abs = PathConfine.ConfineNoLinks(resolved, "shots/" + filename, _probe)
                ?? throw ImportRejectedException.OutsideShots(filename);
            await WriteNewFileAsync(abs, bytes).ConfigureAwait(false);
            m.Steps.Insert(InsertIndex(atIndex, m.Steps.Count), ImportedStep(_newId(), "shots/" + filename));
        }).ConfigureAwait(false);
    }

    // The body every step operation shares; afterWrite runs in the same job, once the manifest is written.
    private Task<ProjectManifest> EditStepsAsync(
        string projectPath, Func<string, ProjectManifest, Task> edit, Action<string, ProjectManifest>? afterWrite = null)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        return _queue.EnqueueAsync(async _ =>
        {
            var resolved = await ResolveKnownProjectAsync(projectPath).ConfigureAwait(false);
            // A started job always completes (7.12), so the queue's token is not passed on.
            var manifest = await ReadAsync(resolved, CancellationToken.None).ConfigureAwait(false);
            await edit(resolved, manifest).ConfigureAwait(false);
            StepList.Renumber(manifest.Steps);
            manifest.UpdatedAt = IsoTime.ToIsoString(_time.GetUtcNow());
            await WriteAsync(resolved, manifest).ConfigureAwait(false);
            afterWrite?.Invoke(resolved, manifest);
            return manifest;
        });
    }

    // atIndex == null appends; anything else is clampIndex.
    private static int InsertIndex(double? atIndex, int count) => atIndex is { } at ? JsMath.ClampIndex(at, count) : count;

    // detectImage: the magic bytes, never the extension.
    private static string? DetectImage(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        return null;
    }

    /// <summary>
    /// The number after the step count and after every <c>step-N.</c> name, matched without
    /// regard to case: <c>Number(m[1])</c> is a double, so a huge counter gives <c>1e+21</c> or
    /// Infinity rather than an exception (spec 01 7.8).
    /// </summary>
    internal static double NextShotNumber(IEnumerable<string> names, int stepCount)
    {
        double max = stepCount;
        foreach (var name in names)
        {
            var match = ShotNumber().Match(name);
            if (match.Success) max = Math.Max(max, double.Parse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture));
        }
        return max + 1;
    }

    /// <summary><c>`step-${String(n).padStart(4, '0')}.${ext}`</c>.</summary>
    internal static string ShotFileName(double number, string ext) => "step-" + JsNumber.ToJsString(number).PadLeft(4, '0') + "." + ext;

    // readdir names, or none when shots/ cannot be listed: then the step count alone counts,
    // as Electron falls back.
    private static string[] ListNames(string dir)
    {
        try
        {
            return Directory.GetFileSystemEntries(dir).Select(Path.GetFileName).ToArray()!;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // The step importStep builds (src/main/project-store.ts:1071-1083), keys in that order.
    private static ProjectStep ImportedStep(string id, string screenshot) => new(new JsonObject
    {
        ["id"] = id,
        ["order"] = 0d,
        ["screenshot"] = screenshot,
        ["trigger"] = "hotkey",
        ["click"] = null,
        ["monitor"] = null,
        ["window"] = null,
        ["element"] = StepElement.Unavailable.ToJson(),
        ["caption"] = "Imported screenshot",
        ["crop"] = null,
        ["annotations"] = new JsonArray(),
    });

    // writeFile(path, bytes, { flag: 'wx' }): fails when the file exists, so nothing is overwritten.
    private static async Task WriteNewFileAsync(string path, ReadOnlyMemory<byte> bytes)
    {
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
        }
    }

    // fs.rm(abs, { force: true }) with errors ignored.
    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best-effort, as in Electron: the manifest no longer names the file.
        }
    }

    // /^step-(\d+)\./i, with [0-9] because .NET's \d also matches other digits (D-26).
    [GeneratedRegex(@"^step-([0-9]+)\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShotNumber();
}
