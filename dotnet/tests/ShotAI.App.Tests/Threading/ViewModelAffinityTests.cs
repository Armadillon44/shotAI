using System.ComponentModel;
using System.Reflection;
using ShotAI.App.Tests.Support;
using Xunit;

namespace ShotAI.App.Tests.Threading;

/// <summary>
/// T1, spec 11 Q-IPC-17 and 8.2: with the affinity check on, which a Debug build has by default,
/// a view model made off the UI thread, or changed from another thread, fails at once.
/// </summary>
[Collection(ViewModelAffinityCollection.Name)]
public sealed class ViewModelAffinityTests : IDisposable
{
    private readonly bool _saved = ViewModelBase.CheckAffinity;

    public ViewModelAffinityTests() => ViewModelBase.CheckAffinity = true;

    public void Dispose() => ViewModelBase.CheckAffinity = _saved;

    /// <summary>
    /// On a thread of its own, never the pool: a pool thread keeps a <c>Dispatcher</c> once any code
    /// on it has made a WPF object, and the check then passes there (fixed in WP-A18, when
    /// <c>ReportImageDecoderTests</c> had left one).
    /// </summary>
    [Fact]
    public void ConstructedOffTheUiThreadThrows()
    {
        Exception? thrown = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = new Sample();
            }
            catch (Exception e)
            {
                thrown = e;
            }
        });
        thread.Start();
        thread.Join();
        var e = Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal("Sample must be created on the UI thread.", e.Message);
    }

    [Fact]
    public Task ChangeOffTheUiThreadThrows() => Sta.RunAsync(async () =>
    {
        var vm = new Sample();
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => vm.Name = "off"));
        Assert.Equal("Sample.Name changed off the UI thread; marshal with IUiDispatcher.Post.", e.Message);
    });

    [Fact]
    public Task ChangeOnTheUiThreadIsRaised() => Sta.RunAsync(() =>
    {
        var vm = new Sample();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Name = "on";
        Assert.Equal(["Name"], raised);
    });

    /// <summary>Off, nothing is checked: a Release build's default.</summary>
    [Fact]
    public async Task OffChecksNothing()
    {
        ViewModelBase.CheckAffinity = false;
        var vm = await Task.Run(() => new Sample());
        vm.Name = "anywhere";
        Assert.Equal("anywhere", vm.Name);
    }

    /// <summary>The check is on by default in a Debug build and off in Release (ARCHITECTURE 5.1).</summary>
    [Fact]
    public void OnByDefaultOnlyInDebug()
    {
#if DEBUG
        Assert.True(_saved);
#else
        Assert.False(_saved);
#endif
    }

    /// <summary>Every view model of the App derives from <see cref="ViewModelBase"/> (ARCHITECTURE 5.1).</summary>
    [Fact]
    public void EveryViewModelDerivesFromViewModelBase()
    {
        var offenders = typeof(App).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("ViewModel", StringComparison.Ordinal) && !typeof(ViewModelBase).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();
        Assert.Empty(offenders);
        Assert.True(typeof(ViewModelBase).IsAbstract);
        Assert.True(typeof(INotifyPropertyChanged).IsAssignableFrom(typeof(ViewModelBase)));
        Assert.NotNull(typeof(ViewModelBase).GetProperty("CheckAffinity", BindingFlags.NonPublic | BindingFlags.Static));
    }

    private sealed class Sample : ViewModelBase
    {
        private string _name = "";

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }
    }
}

/// <summary>The affinity switch is process-wide, so its tests run alone.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ViewModelAffinityCollection
{
    public const string Name = "view model affinity";
}
