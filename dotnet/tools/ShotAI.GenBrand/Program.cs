using System.Text;

namespace ShotAI.GenBrand;

/// <summary>
/// <c>dotnet run --project dotnet/tools/ShotAI.GenBrand [-- --check] [--root &lt;dir&gt;]</c>:
/// writes the brand table, or with <c>--check</c> fails when it is stale (spec 10 7.3).
/// </summary>
public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error, Environment.CurrentDirectory);

    /// <summary>The command, with its output and starting directory given, for the tests.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var check = args.Contains("--check");
        string? root;
        var at = args.ToList().IndexOf("--root");
        if (at >= 0)
        {
            if (at + 1 >= args.Count) return Die(stderr, "--root needs a directory");
            root = Path.GetFullPath(args[at + 1], workingDirectory);
        }
        else
        {
            root = FindRoot(workingDirectory);
        }
        if (root is null) return Die(stderr, "cannot find the repository root (contract/brand.json)");

        var contract = Path.Combine(root, "contract", "brand.json");
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(contract);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Die(stderr, $"cannot read {contract}: {e.Message}");
        }

        string text;
        try
        {
            text = BrandGenerator.Generate(bytes);
        }
        catch (GenBrandException e)
        {
            return Die(stderr, e.Message);
        }

        var output = Path.Combine(root, BrandGenerator.OutputRelativePath);
        if (check)
        {
            string existing;
            try
            {
                existing = File.ReadAllText(output);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                existing = "";   // missing counts as stale
            }
            // Line-ending-normalized, so a CRLF worktree copy is not stale forever (EDGE-INFRA-2).
            if (Normalize(existing) != Normalize(text))
            {
                return Die(
                    stderr,
                    "BrandPalette.Generated.cs is STALE.\nRun `dotnet run --project dotnet/tools/ShotAI.GenBrand` and commit the result.");
            }
            stdout.Write("gen-brand: up to date\n");
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        stdout.Write("gen-brand: wrote BrandPalette.Generated.cs\n");
        return 0;
    }

    private static int Die(TextWriter stderr, string message)
    {
        stderr.Write("gen-brand: " + message + "\n");
        return 1;
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    // The first directory up from `start` that holds both contract/brand.json and dotnet/ShotAI.slnx.
    private static string? FindRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "contract", "brand.json"))
                && File.Exists(Path.Combine(dir.FullName, "dotnet", "ShotAI.slnx")))
            {
                return dir.FullName;
            }
        }
        return null;
    }
}
