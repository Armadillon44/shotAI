using ShotAI.Core.SelfTest;
using Xunit;

namespace ShotAI.Core.Tests.SelfTest;

/// <summary>Spec 10 7.8: the switch and environment table, its order, case, and unknown arguments.</summary>
public sealed class StartupModeParserTests
{
    private static readonly Func<string, string?> NoEnv = _ => null;

    private static Func<string, string?> Env(params (string Name, string Value)[] vars) =>
        name => vars.FirstOrDefault(v => v.Name == name).Value;

    [Fact]
    public void NothingIsNormal()
    {
        Assert.Same(StartupMode.Normal, StartupModeParser.Parse([], NoEnv));
        Assert.Equal(new StartupMode(StartupModeKind.Normal), StartupMode.Normal);
    }

    [Theory]
    [InlineData("--selftest", StartupModeKind.StoreSelfTest)]
    [InlineData("--capture-selftest", StartupModeKind.CaptureSelfTest)]
    [InlineData("--update-selftest", StartupModeKind.UpdateSelfTest)]
    public void EachSwitch(string arg, StartupModeKind kind) =>
        Assert.Equal(new StartupMode(kind), StartupModeParser.Parse([arg], NoEnv));

    [Fact]
    public void UpdateSwitchCarriesAVersion() =>
        Assert.Equal(new StartupMode(StartupModeKind.UpdateSelfTest, "1.2.3"), StartupModeParser.Parse(["--update-selftest=1.2.3"], NoEnv));

    /// <summary>Only the switch is matched without case; the version is kept as written.</summary>
    [Theory]
    [InlineData("--SELFTEST", StartupModeKind.StoreSelfTest)]
    [InlineData("--SelfTest", StartupModeKind.StoreSelfTest)]
    [InlineData("--Capture-SelfTest", StartupModeKind.CaptureSelfTest)]
    [InlineData("--UPDATE-SELFTEST", StartupModeKind.UpdateSelfTest)]
    public void SwitchesIgnoreCase(string arg, StartupModeKind kind) =>
        Assert.Equal(new StartupMode(kind), StartupModeParser.Parse([arg], NoEnv));

    [Fact]
    public void VersionKeepsItsCase() =>
        Assert.Equal(new StartupMode(StartupModeKind.UpdateSelfTest, "2.0.0-RC1"), StartupModeParser.Parse(["--Update-SelfTest=2.0.0-RC1"], NoEnv));

    /// <summary>An empty value is the running version, as the bare switch is.</summary>
    [Fact]
    public void EmptyVersionIsTheRunningVersion() =>
        Assert.Equal(new StartupMode(StartupModeKind.UpdateSelfTest), StartupModeParser.Parse(["--update-selftest="], NoEnv));

    [Fact]
    public void FirstUpdateSwitchWins()
    {
        Assert.Equal("1.0.0", StartupModeParser.Parse(["--update-selftest=1.0.0", "--update-selftest=2.0.0"], NoEnv).UpdateSelfTestVersion);
        Assert.Null(StartupModeParser.Parse(["--update-selftest", "--update-selftest=2.0.0"], NoEnv).UpdateSelfTestVersion);
    }

    /// <summary>Electron's variables, kept for existing scripts and docs.</summary>
    [Fact]
    public void Variables()
    {
        Assert.Equal(new StartupMode(StartupModeKind.StoreSelfTest), StartupModeParser.Parse([], Env(("SHOTAI_SELFTEST", "1"))));
        Assert.Equal(new StartupMode(StartupModeKind.CaptureSelfTest), StartupModeParser.Parse([], Env(("SHOTAI_CAPTURE_TEST", "1"))));
    }

    /// <summary>A variable counts only when it is exactly <c>1</c>, as Electron's <c>=== '1'</c>.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("true")]
    [InlineData("01")]
    [InlineData("\u0661")]
    public void VariableMustBeExactlyOne(string value)
    {
        Assert.Same(StartupMode.Normal, StartupModeParser.Parse([], Env(("SHOTAI_SELFTEST", value))));
        Assert.Same(StartupMode.Normal, StartupModeParser.Parse([], Env(("SHOTAI_CAPTURE_TEST", value))));
    }

    /// <summary>The update self-test has no variable.</summary>
    [Fact]
    public void NoUpdateVariable() =>
        Assert.Same(StartupMode.Normal, StartupModeParser.Parse([], Env(("SHOTAI_UPDATE_SELFTEST", "1"), ("SHOTAI_UPDATE_TEST", "1"))));

    /// <summary>Store before capture, as <c>main.ts:403-414</c> checks; capture before update.</summary>
    [Fact]
    public void RowsInOrder()
    {
        Assert.Equal(StartupModeKind.StoreSelfTest, StartupModeParser.Parse(["--capture-selftest", "--selftest"], NoEnv).Kind);
        Assert.Equal(StartupModeKind.StoreSelfTest, StartupModeParser.Parse(["--capture-selftest"], Env(("SHOTAI_SELFTEST", "1"))).Kind);
        Assert.Equal(StartupModeKind.StoreSelfTest, StartupModeParser.Parse(["--selftest"], Env(("SHOTAI_CAPTURE_TEST", "1"))).Kind);
        Assert.Equal(StartupModeKind.StoreSelfTest, StartupModeParser.Parse([], Env(("SHOTAI_CAPTURE_TEST", "1"), ("SHOTAI_SELFTEST", "1"))).Kind);
        Assert.Equal(StartupModeKind.CaptureSelfTest, StartupModeParser.Parse(["--update-selftest", "--capture-selftest"], NoEnv).Kind);
        Assert.Equal(StartupModeKind.CaptureSelfTest, StartupModeParser.Parse(["--update-selftest=1.0.0"], Env(("SHOTAI_CAPTURE_TEST", "1"))).Kind);
        Assert.Equal(StartupModeKind.StoreSelfTest, StartupModeParser.Parse(["--update-selftest"], Env(("SHOTAI_SELFTEST", "1"))).Kind);
    }

    [Theory]
    [InlineData("--selftestx")]
    [InlineData("-selftest")]
    [InlineData("selftest")]
    [InlineData("--selftest=1")]
    [InlineData("/selftest")]
    [InlineData("--capture-selftest=1")]
    [InlineData("--update-selftestx")]
    [InlineData("--update-selftest 1.0.0")]
    [InlineData("")]
    public void UnknownArgumentsAreIgnored(string arg) => Assert.Same(StartupMode.Normal, StartupModeParser.Parse([arg, "--foo"], NoEnv));

    /// <summary>A switch counts wherever it is among the arguments.</summary>
    [Fact]
    public void AnyPosition() =>
        Assert.Equal(StartupModeKind.StoreSelfTest, StartupModeParser.Parse(["C:\\a.shotai", "--foo", "--selftest"], NoEnv).Kind);

    /// <summary>It reads exactly the two variables, and only while no earlier row matched.</summary>
    [Fact]
    public void ReadsOnlyItsVariables()
    {
        var read = new List<string>();
        StartupModeParser.Parse(["--update-selftest"], name => { read.Add(name); return null; });
        Assert.Equal(["SHOTAI_SELFTEST", "SHOTAI_CAPTURE_TEST"], read);
        read.Clear();
        StartupModeParser.Parse(["--selftest"], name => { read.Add(name); return null; });
        Assert.Empty(read);
    }

    [Fact]
    public void NamesAreTheSpecs()
    {
        Assert.Equal("--selftest", StartupModeParser.StoreSwitch);
        Assert.Equal("--capture-selftest", StartupModeParser.CaptureSwitch);
        Assert.Equal("--update-selftest", StartupModeParser.UpdateSwitch);
        Assert.Equal("SHOTAI_SELFTEST", StartupModeParser.StoreVariable);
        Assert.Equal("SHOTAI_CAPTURE_TEST", StartupModeParser.CaptureVariable);
    }

    /// <summary>The exit codes are the outcome's values (spec 10 7.8).</summary>
    [Fact]
    public void OutcomesAreExitCodes()
    {
        Assert.Equal(0, (int)SelfTestOutcome.Pass);
        Assert.Equal(1, (int)SelfTestOutcome.Fail);
        Assert.Equal(2, (int)SelfTestOutcome.Error);
    }

    /// <summary>Refused by name, before anything is read.</summary>
    [Fact]
    public void NullsAreRefused()
    {
        Assert.Equal("args", Assert.Throws<ArgumentNullException>(() => StartupModeParser.Parse(null!, NoEnv)).ParamName);
        Assert.Equal("env", Assert.Throws<ArgumentNullException>(() => StartupModeParser.Parse([], null!)).ParamName);
    }
}
