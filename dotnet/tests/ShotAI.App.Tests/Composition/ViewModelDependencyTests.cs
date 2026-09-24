using System.Reflection;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Settings;
using ShotAI.Core.Store;
using ShotAI.Core.Threading;
using ShotAI.Platform;
using Xunit;

namespace ShotAI.App.Tests.Composition;

/// <summary>
/// INV-ARCH-3, spec 11 8.2: a view model's constructor takes catalog interfaces, the C6
/// factories, <see cref="IUiDispatcher"/>, <see cref="ILogger{TCategoryName}"/>, value types or
/// other view models, never a concrete service or a Platform type.
/// </summary>
public sealed class ViewModelDependencyTests
{
    // The concrete services spec 11 8.2 names, and anything from Platform.
    private static readonly HashSet<string> Forbidden =
        ["ProjectStore", "EntraSession", "IApiKeyStore", "MsalGateway", "CaptureEngine", "SettingsService"];

    // What a view model may take besides catalog interfaces: 06's chrome services and the C6
    // factories (ARCHITECTURE 4.1), by name so later work packages need not touch this list.
    private static readonly HashSet<string> AllowedByName =
    [
        "IUiDispatcher", "INoticeService", "IConfirmService",
        "IProjectSessionFactory", "EditorFactory", "SopPanelViewModelFactory", "ReportViewModelFactory", "DocScaleEditorFactory",
        "ICaptureTargetSelection",
    ];

    // EditorViewModel's named extras (04 7.10.1).
    private static readonly HashSet<string> EditorExtras = ["Flattener", "IRenderCodec", "IPathProbe", "IColorPicker"];

    [Fact]
    public void EveryViewModelTakesOnlyAllowedDependencies()
    {
        var catalog = CatalogNames();
        var viewModels = typeof(App).Assembly.GetTypes().Where(t => typeof(ViewModelBase).IsAssignableFrom(t) && !t.IsAbstract).ToList();
        TestContext.Current.TestOutputHelper?.WriteLine($"{viewModels.Count} view model(s) checked");
        Assert.All(viewModels, vm => Assert.Empty(Violations(vm, catalog, [typeof(DllSearchHardening).Assembly])));
    }

    /// <summary>
    /// The check finds each kind of forbidden parameter. Every public Platform type is static, so
    /// a type of this assembly stands in for one.
    /// </summary>
    [Fact]
    public void ForbiddenParametersAreFound()
    {
        var found = Violations(typeof(ForbiddenViewModel), CatalogNames(), [typeof(DllSearchHardening).Assembly, typeof(PlatformStandIn).Assembly]);
        Assert.Equal(["settings: SettingsService", "store: ProjectStore", "platform: PlatformStandIn"], found);
    }

    [Fact]
    public void AllowedParametersPass() =>
        Assert.Empty(Violations(typeof(AllowedViewModel), CatalogNames(), [typeof(DllSearchHardening).Assembly, typeof(PlatformStandIn).Assembly]));

    internal static List<string> Violations(Type viewModel, IReadOnlySet<string> catalog, IReadOnlyCollection<Assembly> platform)
    {
        var violations = new List<string>();
        foreach (var ctor in viewModel.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            foreach (var p in ctor.GetParameters())
            {
                if (!Allowed(viewModel, p.ParameterType, catalog, platform)) violations.Add($"{p.Name}: {p.ParameterType.Name}");
            }
        }
        return violations;
    }

    private static bool Allowed(Type viewModel, Type t, IReadOnlySet<string> catalog, IReadOnlyCollection<Assembly> platform)
    {
        if (t.IsValueType || t == typeof(string)) return true;
        if (Forbidden.Contains(t.Name) || platform.Contains(t.Assembly)) return false;
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(ILogger<>)) return true;
        if (typeof(ViewModelBase).IsAssignableFrom(t)) return true;
        if (AllowedByName.Contains(t.Name) || catalog.Contains(t.Name)) return true;
        return viewModel.Name == "EditorViewModel" && EditorExtras.Contains(t.Name);
    }

    private static HashSet<string> CatalogNames() =>
    [
        .. ContainerTests.FirstColumnNames(Support.RepoFiles.ReadText("docs/native/ARCHITECTURE.md"), "### 4.4 The service catalog is the backbone"),
        .. ContainerTests.FirstColumnNames(Support.RepoFiles.ReadText("docs/native/spec/11-service-boundary.md"), "### 7.3 Service catalog"),
    ];

    internal sealed class PlatformStandIn;

    private sealed class ForbiddenViewModel(SettingsService settings, ProjectStore store, PlatformStandIn platform) : ViewModelBase
    {
        public object[] Held { get; } = [settings, store, platform];
    }

    private sealed class AllowedViewModel(ISettingsService settings, IProjectService projects, IUiDispatcher ui, ILogger<AllowedViewModel> log, int count, string name) : ViewModelBase
    {
        public object[] Held { get; } = [settings, projects, ui, log, count, name];
    }
}
