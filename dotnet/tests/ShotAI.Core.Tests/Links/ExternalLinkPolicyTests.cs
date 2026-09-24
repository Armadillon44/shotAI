using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShotAI.Core.Auth;
using ShotAI.Core.Links;
using ShotAI.Core.Tests.Support;
using Xunit;

namespace ShotAI.Core.Tests.Links;

/// <summary>
/// Spec 11 2.5.1, 7.3.4 and 8.2, and spec 10 7.7 and 8 (INV-IPC-3, INV-INFRA-29, EDGE-IPC-8):
/// the allowlist in front of the shell, with both specs' tables. The SupportUrl rows exercise
/// the extension through a fake <see cref="ISupportUrlAllowlist"/>; its exact-origin match is
/// spec 08's <c>SupportUrlAllowlistTests</c> (WP-D4).
/// </summary>
public sealed class ExternalLinkPolicyTests
{
    private const string RefusedPrefix = "refused openExternal for non-allowlisted URL: ";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Allowed => new()
    {
        // Spec 11 8.2.
        "https://anthropic.com",
        "https://console.anthropic.com/settings/keys",
        "https://docs.anthropic.com/x?y=1",
        "https://github.com/org/repo/releases/tag/v1.2.3",
        "https://GITHUB.com/x",
        "https://Console.Anthropic.COM/",
        // Spec 10 8.
        "https://anthropic.com/x",
        "https://github.com/Armadillon44/shotAI/releases/tag/v1.3.0",
        "HTTPS://GitHub.com/a",
        // Allowed by parity (Q-INFRA-21): user info and a non-default port on an allowed host.
        "https://user@github.com/",
        "https://github.com:8443/x",
    };

    public static TheoryData<string, string> Refused => new()
    {
        { "http://anthropic.com", "http://anthropic.com" },
        { "http://github.com/", "http://github.com" },
        { "https://evil-anthropic.com", "https://evil-anthropic.com" },
        { "https://evilanthropic.com/", "https://evilanthropic.com" },
        { "https://anthropic.com.evil.example", "https://anthropic.com.evil.example" },
        { "https://anthropic.com.evil.test/", "https://anthropic.com.evil.test" },
        { "https://raw.github.com/x", "https://raw.github.com" },
        { "https://gist.github.com/x", "https://gist.github.com" },
        { "https://objects.github.com", "https://objects.github.com" },
        { "https://codeload.github.com", "https://codeload.github.com" },
        { "https://raw.githubusercontent.com/", "https://raw.githubusercontent.com" },
        { "https://github.com./", "https://github.com." },
        { "javascript:alert(1)", "null" },
        { "file:///C:/x", "null" },
        { "mailto:a@b.c", "null" },
    };

    /// <summary>Each allowed URL passes the base rule, and the launcher gets it normalized, with nothing logged.</summary>
    [Theory]
    [MemberData(nameof(Allowed))]
    public async Task AllowedUrlsAreLaunched(string url)
    {
        var h = new Harness();
        Assert.True(ExternalLinkPolicy.IsBaseAllowed(new Uri(url)));
        Assert.True(await h.Service.OpenAsync(url, Ct));
        Assert.Equal([new Uri(url).AbsoluteUri], h.Launcher.Launched);
        Assert.Empty(h.Logs.Entries);
    }

    /// <summary>Each refused URL launches nothing and logs one Warning with its origin.</summary>
    [Theory]
    [MemberData(nameof(Refused))]
    public async Task RefusedUrlsAreLoggedByOrigin(string url, string origin)
    {
        var h = new Harness();
        Assert.False(ExternalLinkPolicy.IsBaseAllowed(new Uri(url)));
        Assert.False(await h.Service.OpenAsync(url, Ct));
        Assert.Empty(h.Launcher.Launched);
        var line = Assert.Single(h.Logs.Entries);
        Assert.Equal((LogLevel.Warning, "ShotAI.Core.Links.ExternalLinks", RefusedPrefix + origin), (line.Level, line.Category, line.Message));
    }

    /// <summary>A value that does not parse as an absolute URL is refused without a log line, null included.</summary>
    [Theory]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnparseableUrlsAreRefusedSilently(string? url)
    {
        var h = new Harness();
        Assert.False(await h.Service.OpenAsync(url!, Ct));
        Assert.Empty(h.Launcher.Launched);
        Assert.Empty(h.Logs.Entries);
    }

    /// <summary>The refusal line carries the origin only: no path, query or fragment.</summary>
    [Fact]
    public async Task TheRefusalLogsTheOriginOnly()
    {
        var h = new Harness();
        Assert.False(await h.Service.OpenAsync("https://user:pw@evil-anthropic.com:444/private/path?token=secret#frag", Ct));
        Assert.Equal(RefusedPrefix + "https://evil-anthropic.com:444", Assert.Single(h.Logs.Entries).Message);
    }

    /// <summary>The launcher gets <see cref="Uri.AbsoluteUri"/>, as Electron passes <c>parsed.toString()</c>.</summary>
    [Theory]
    [InlineData("https://GITHUB.com/x", "https://github.com/x")]
    [InlineData("https://github.com", "https://github.com/")]
    [InlineData("https://github.com/a b", "https://github.com/a%20b")]
    [InlineData("https://anthropic.com/x?y=1#z", "https://anthropic.com/x?y=1#z")]
    public async Task TheLauncherGetsTheNormalizedUrl(string url, string launched)
    {
        var h = new Harness();
        Assert.True(await h.Service.OpenAsync(url, Ct));
        Assert.Equal([launched], h.Launcher.Launched);
    }

    /// <summary>An https URL the base rule refuses and the SupportUrl extension admits is launched; the extension gets the URL and the token.</summary>
    [Fact]
    public async Task ASupportUrlTheExtensionAdmitsIsLaunched()
    {
        var support = new FakeSupportUrls(u => u.Host == "help.example.org");
        var h = new Harness(support);
        Assert.True(await h.Service.OpenAsync("https://help.example.org/other", Ct));
        Assert.Equal(["https://help.example.org/other"], h.Launcher.Launched);
        Assert.Equal([new Uri("https://help.example.org/other")], support.Asked);
        Assert.Equal(Ct, support.Token);
        Assert.Empty(h.Logs.Entries);
    }

    /// <summary>An https URL both rules refuse is logged by origin.</summary>
    [Fact]
    public async Task ASupportUrlTheExtensionRefusesIsLogged()
    {
        var support = new FakeSupportUrls(_ => false);
        var h = new Harness(support);
        Assert.False(await h.Service.OpenAsync("https://sub.help.example.org/x", Ct));
        Assert.Single(support.Asked);
        Assert.Empty(h.Launcher.Launched);
        Assert.Equal(RefusedPrefix + "https://sub.help.example.org", Assert.Single(h.Logs.Entries).Message);
    }

    /// <summary>Only an https URL reaches the extension, as in Electron's <c>parsed.protocol === 'https:'</c> guard.</summary>
    [Fact]
    public async Task SupportUrlConsultedOnlyForHttps()
    {
        var support = new FakeSupportUrls(_ => true);
        var h = new Harness(support);
        Assert.False(await h.Service.OpenAsync("http://help.example.org/x", Ct));
        Assert.False(await h.Service.OpenAsync("javascript:alert(1)", Ct));
        Assert.False(await h.Service.OpenAsync("file:///C:/x", Ct));
        Assert.Empty(support.Asked);
        Assert.Empty(h.Launcher.Launched);
        Assert.Equal(3, h.Logs.Entries.Count);
    }

    /// <summary>A URL the base rule admits never reaches the extension.</summary>
    [Fact]
    public async Task TheBaseRuleComesFirst()
    {
        var support = new FakeSupportUrls(_ => false);
        var h = new Harness(support);
        Assert.True(await h.Service.OpenAsync("https://github.com/x", Ct));
        Assert.Empty(support.Asked);
    }

    /// <summary>With no federation configuration, the extension admits nothing extra.</summary>
    [Fact]
    public async Task NoFederationAllowsNothingExtra()
    {
        Assert.False(await new NoFederationSupportUrlAllowlist().IsAllowedAsync(new Uri("https://help.example.org/access"), Ct));
        var h = new Harness();
        Assert.False(await h.Service.OpenAsync("https://help.example.org/access", Ct));
        Assert.Equal(RefusedPrefix + "https://help.example.org", Assert.Single(h.Logs.Entries).Message);
    }

    /// <summary>The host compare ignores the current culture, <c>tr-TR</c> included, where <c>"I"</c> lower-cases to a dotless i.</summary>
    [Fact]
    public void TheHostCompareIgnoresTheCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.True(ExternalLinkPolicy.IsBaseAllowed(new Uri("https://CONSOLE.ANTHROPIC.COM/")));
            Assert.True(ExternalLinkPolicy.IsBaseAllowed(new Uri("https://GITHUB.COM/")));
            Assert.False(ExternalLinkPolicy.IsBaseAllowed(new Uri("https://EVIL-ANTHROPIC.COM/")));
            Assert.Equal("https://github.com", UrlOrigin.Of(new Uri("https://GITHUB.COM/")));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>
    /// EDGE-INFRA-24: every URL the app opens passes the base rule: the console link, the
    /// default Request access URL and a release page under the update check's prefix, each read
    /// from the Electron source.
    /// </summary>
    [Fact]
    public void EveryUrlTheAppOpensPasses()
    {
        var console = Regex.Match(ElectronSource.Read("src/renderer/project/Settings.tsx"), @"'(https://console\.anthropic\.com/[^']*)'").Groups[1].Value;
        var support = Regex.Match(ElectronSource.Read("src/main/entra/config-validate.ts"), @"DEFAULT_SUPPORT_URL = '([^']+)'").Groups[1].Value;
        var releases = Regex.Match(ElectronSource.Read("src/main/update-check.ts"), @"html_url\.startsWith\('([^']+)'\)").Groups[1].Value;
        Assert.Equal("https://console.anthropic.com/settings/keys", console);
        Assert.Equal("https://github.com/Armadillon44/shotAI/issues", support);
        Assert.Equal("https://github.com/", releases);
        foreach (var url in new[] { console, support, releases + "Armadillon44/shotAI/releases/tag/v1.3.0" })
            Assert.True(ExternalLinkPolicy.IsBaseAllowed(new Uri(url)), url);
    }

    /// <summary>A launcher that throws makes the open throw the same exception (R-ARCH-25), with nothing logged.</summary>
    [Fact]
    public async Task LauncherExceptionPropagates()
    {
        var h = new Harness();
        var boom = new InvalidOperationException("no browser");
        h.Launcher.Throw = boom;
        Assert.Same(boom, await Assert.ThrowsAsync<InvalidOperationException>(() => h.Service.OpenAsync("https://github.com/x", Ct)));
        Assert.Empty(h.Logs.Entries);
    }

    /// <summary>A relative URI is never allowed.</summary>
    [Fact]
    public void ARelativeUriIsRefused() => Assert.False(ExternalLinkPolicy.IsBaseAllowed(new Uri("/x", UriKind.Relative)));

    [Fact]
    public async Task ArgumentsAreChecked()
    {
        using var logs = new CapturingLoggerProvider();
        var log = logs.CreateLogger<ExternalLinks>();
        Assert.Throws<ArgumentNullException>(() => new ExternalLinks(null!, new RecordingLauncher(), log));
        Assert.Throws<ArgumentNullException>(() => new ExternalLinks(new NoFederationSupportUrlAllowlist(), null!, log));
        Assert.Throws<ArgumentNullException>(() => new ExternalLinks(new NoFederationSupportUrlAllowlist(), new RecordingLauncher(), null!));
        Assert.Throws<ArgumentNullException>(() => ExternalLinkPolicy.IsBaseAllowed(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new NoFederationSupportUrlAllowlist().IsAllowedAsync(null!, Ct));
    }

    // A link service over a recording launcher and, unless given another, the no-federation extension.
    private sealed class Harness
    {
        public Harness(ISupportUrlAllowlist? supportUrls = null) =>
            Service = new ExternalLinks(supportUrls ?? new NoFederationSupportUrlAllowlist(), Launcher, Logs.CreateLogger<ExternalLinks>());

        public CapturingLoggerProvider Logs { get; } = new();

        public RecordingLauncher Launcher { get; } = new();

        public ExternalLinks Service { get; }
    }

    private sealed class RecordingLauncher : IUrlLauncher
    {
        public List<string> Launched { get; } = [];

        public Exception? Throw { get; set; }

        public Task LaunchAsync(string absoluteUri)
        {
            if (Throw is { } e) return Task.FromException(e);
            Launched.Add(absoluteUri);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSupportUrls(Func<Uri, bool> admits) : ISupportUrlAllowlist
    {
        public List<Uri> Asked { get; } = [];

        public CancellationToken Token { get; private set; }

        public Task<bool> IsAllowedAsync(Uri candidate, CancellationToken ct)
        {
            Asked.Add(candidate);
            Token = ct;
            return Task.FromResult(admits(candidate));
        }
    }
}
