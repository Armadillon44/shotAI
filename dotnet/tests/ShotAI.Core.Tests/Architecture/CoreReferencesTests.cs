using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using ShotAI.Core.Errors;
using ShotAI.Core.Tests.SourceGuards;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Architecture;

/// <summary>
/// ShotAI.Core stays free of Windows so it builds and runs on Linux (INV-ARCH-1, INV-IPC-19,
/// AC-IPC-16, AC-MODEL-26, AC-SHELL-29).
/// </summary>
public sealed class CoreReferencesTests
{
    private static readonly Assembly Core = typeof(ShotAIException).Assembly;

    /// <summary>The Windows assemblies INV-IPC-19 names.</summary>
    private static readonly string[] WindowsAssemblies =
    [
        "PresentationCore", "PresentationFramework", "WindowsBase", "System.Windows.Forms",
        "Microsoft.Windows.SDK.NET", "Microsoft.Web.WebView2.Core", "Microsoft.Web.WebView2.Wpf",
        "Microsoft.Identity.Client.Broker", "Microsoft.Win32.Registry",
    ];

    /// <summary>
    /// Where an interface Core consumes may come from besides Core itself: the BCL and the
    /// Core package allowlist of ARCHITECTURE 3.2.
    /// </summary>
    private static readonly string[] AllowedInterfaceAssemblyPrefixes =
    [
        "System", "mscorlib", "netstandard", "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions", "CommunityToolkit.Mvvm",
        "DocumentFormat.OpenXml", "Anthropic",
    ];

    [Fact]
    public void CoreReferencesNoWindowsAssemblies()
    {
        var references = Core.GetReferencedAssemblies().Select(a => a.Name ?? "").ToList();

        Assert.Empty(references.Intersect(WindowsAssemblies, StringComparer.OrdinalIgnoreCase));
        // WinRT projections, produced from .winmd files, are named for the Windows namespace.
        Assert.DoesNotContain(references, n =>
            n.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            || n.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase)
            || n.Equals("WinRT.Runtime", StringComparison.OrdinalIgnoreCase));
        // A Windows target framework would stamp a TargetPlatformAttribute on the assembly.
        Assert.Null(Core.GetCustomAttribute<TargetPlatformAttribute>());
        Assert.Equal(".NETCoreApp,Version=v10.0", Core.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
    }

    /// <summary>
    /// AC-SHELL-29: the shell's rules name no Windows namespace. Core's framework has some of
    /// them (<c>System.Windows.Input</c>, <c>Microsoft.Win32</c>), so the build alone does not
    /// keep them out; each file of the folder is read without its comments, which may name them.
    /// </summary>
    [Fact]
    public void TheShellFolderNamesNoWindowsNamespace()
    {
        var folder = Path.Combine(RepoFiles.Root, "dotnet", "src", "ShotAI.Core", "Shell");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var offenders = files
            .Where(f => Regex.IsMatch(CSharpText.StripComments(File.ReadAllText(f)), @"\b(System\.Windows|Windows\.Win32|Microsoft\.Win32)\b"))
            .Select(Path.GetFileName)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void CatalogInterfacesConsumedByCoreLiveInCore()
    {
        var offenders = Core.GetTypes()
            .SelectMany(ReferencedTypes)
            .Where(t => t.IsInterface && t.Assembly != Core && !IsAllowed(t.Assembly))
            .Select(t => $"{t.FullName} ({t.Assembly.GetName().Name})")
            .Distinct()
            .Order()
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsAllowed(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? "";
        return AllowedInterfaceAssemblyPrefixes.Any(p =>
            name.Equals(p, StringComparison.Ordinal) || name.StartsWith(p + ".", StringComparison.Ordinal));
    }

    /// <summary>Every type a Core type's signatures mention, generic arguments unwrapped.</summary>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var direct = new List<Type>(type.GetInterfaces());
        if (type.BaseType is not null) direct.Add(type.BaseType);
        direct.AddRange(type.GetFields(all).Select(f => f.FieldType));
        direct.AddRange(type.GetProperties(all).Select(p => p.PropertyType));
        direct.AddRange(type.GetEvents(all).Select(e => e.EventHandlerType).OfType<Type>());
        foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
        {
            direct.AddRange(method.GetParameters().Select(p => p.ParameterType));
            if (method is MethodInfo m) direct.Add(m.ReturnType);
        }
        return direct.SelectMany(Unwrap);
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var inner in Unwrap(type.GetElementType()!)) yield return inner;
            yield break;
        }
        if (type.IsGenericParameter) yield break;
        yield return type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        foreach (var argument in type.GetGenericArguments())
            foreach (var inner in Unwrap(argument)) yield return inner;
    }
}
