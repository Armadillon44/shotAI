using System.Reflection;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Composition;

/// <summary>
/// T11, spec 11 8.2: every async command refuses concurrent execution, so a second click while
/// it runs is ignored, unless its view model is on the allowlist.
/// </summary>
public sealed partial class CommandConventionsTests
{
    // Commands that may run concurrently, as "ViewModel.Method". None so far.
    private static readonly HashSet<string> Allowlist = [];

    /// <summary>No <c>[RelayCommand]</c> in the App allows concurrent execution.</summary>
    [Fact]
    public void NoAsyncCommandAllowsConcurrentExecution() =>
        Assert.DoesNotContain(ConcurrentCommands(typeof(App).Assembly.GetTypes()), c => !Allowlist.Contains(c));

    /// <summary>Nor does a command made by hand with the option.</summary>
    [Fact]
    public void NoHandMadeCommandAllowsConcurrentExecution()
    {
        var root = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.App");
        var hits = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => ConcurrentOption().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();
        Assert.Empty(hits);
    }

    /// <summary>The check finds a command that allows it.</summary>
    [Fact]
    public void AConcurrentCommandIsFound() =>
        Assert.Equal(["ConcurrentViewModel.GoAsync"], ConcurrentCommands([typeof(ConcurrentViewModel), typeof(SerialViewModel)]));

    /// <summary>The text check finds the option too.</summary>
    [Fact]
    public void TheOptionIsFoundInText()
    {
        Assert.Matches(ConcurrentOption(), "new AsyncRelayCommand(RunAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions)");
        Assert.DoesNotMatch(ConcurrentOption(), "new AsyncRelayCommand(RunAsync)");
    }

    internal static List<string> ConcurrentCommands(IEnumerable<Type> types) =>
        types.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<RelayCommandAttribute>() is { AllowConcurrentExecutions: true })
                .Select(m => $"{t.Name}.{m.Name}"))
            .ToList();

    [GeneratedRegex(@"AsyncRelayCommandOptions\.AllowConcurrentExecutions")]
    private static partial Regex ConcurrentOption();

    // The check reads the attribute; the toolkit generates the command properties as well.
    private sealed partial class ConcurrentViewModel : ViewModelBase
    {
        [RelayCommand(AllowConcurrentExecutions = true)]
        public Task GoAsync() => Task.CompletedTask;
    }

    private sealed partial class SerialViewModel : ViewModelBase
    {
        [RelayCommand]
        public Task GoAsync() => Task.CompletedTask;
    }
}
