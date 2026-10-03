using Atlas.Internal.RealClient;

namespace Atlas.Pure.Tests.RealClient;

/// <summary>Contract of the availability ladder of the real client: one reason and one remedy per
/// case, in the order of the design's skip ladder, decided without starting anything.</summary>
public class ClientAvailabilityTests
{
    [Fact]
    public void Check_Should_BeAvailableWithTheResolvedToolchain_When_NothingIsInTheWay()
    {
        var machine = new FakeMachine();

        ClientAvailability result = machine.Check();

        Assert.True(result.IsAvailable);
        Assert.Equal(ClientUnavailableReason.None, result.Reason);
        ClientToolchain toolchain = Assert.IsType<ClientToolchain>(result.Toolchain);
        Assert.Equal("/usr/bin/unshare", toolchain.UnsharePath);
        Assert.Equal("/usr/bin/Xvfb", toolchain.XvfbPath);
        Assert.Equal("/usr/bin/import", toolchain.ImportPath);
        Assert.Equal("/opt/vs", toolchain.InstallDirectory);
        Assert.Equal("/usr/share/dotnet", toolchain.DotnetRoot);
        Assert.Equal("/data/client", toolchain.DataPath);
        Assert.Equal("/opt/vs/Vintagestory", toolchain.ClientProgram);
    }

    [Fact]
    public void Check_Should_LeaveScreenshotsOut_When_ImportIsNotInstalled()
    {
        var machine = new FakeMachine();
        machine.Tools.Remove("import");

        Assert.Null(machine.Check().Toolchain!.ImportPath);
    }

    [Theory]
    [InlineData("CI")]
    [InlineData("GITHUB_ACTIONS")]
    [InlineData("TF_BUILD")]
    [InlineData("GITLAB_CI")]
    public void Check_Should_NameTheVariable_When_ACiVariableIsSet(string variable)
    {
        var machine = new FakeMachine();
        machine.Environment[variable] = "true";

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.CiEnvironment, result.Reason);
        Assert.Contains(variable, result.Message);
        Assert.Contains("developer machines only", result.Remedy);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("1")]
    [InlineData("yes")]
    public void Check_Should_CountAnyOtherValueAsCi_When_TheVariableIsSet(string value)
    {
        var machine = new FakeMachine();
        machine.Environment["CI"] = value;

        Assert.Equal(ClientUnavailableReason.CiEnvironment, machine.Check().Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("FALSE")]
    public void Check_Should_NotCountTheUsualSpellingsOfNoAsCi_When_TheVariableHoldsOne(string value)
    {
        var machine = new FakeMachine();
        machine.Environment["CI"] = value;

        Assert.True(machine.Check().IsAvailable);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData(" Off ")]
    public void Check_Should_ReportTheSwitch_When_AtlasClientIsOff(string value)
    {
        var machine = new FakeMachine();
        machine.Environment["ATLAS_CLIENT"] = value;

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.SwitchedOff, result.Reason);
        Assert.Contains("ATLAS_CLIENT", result.Message);
        Assert.Contains("Unset ATLAS_CLIENT", result.Remedy);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("required")]
    [InlineData("")]
    public void Check_Should_IgnoreOtherValuesOfTheSwitch_When_AtlasClientIsNotOff(string value)
    {
        var machine = new FakeMachine();
        machine.Environment["ATLAS_CLIENT"] = value;

        Assert.True(machine.Check().IsAvailable);
    }

    [Fact]
    public void Check_Should_ReportThePlatform_When_NotLinux()
    {
        var machine = new FakeMachine { IsLinux = false };

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.NotLinux, result.Reason);
        Assert.Contains("WSL2", result.Remedy);
    }

    [Fact]
    public void Check_Should_ReportUnshare_When_ItIsNotInstalled()
    {
        var machine = new FakeMachine();
        machine.Tools.Remove("unshare");

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.UnshareMissing, result.Reason);
        Assert.Contains("util-linux", result.Remedy);
    }

    [Fact]
    public void Check_Should_QuoteTheProbeFailure_When_UserNamespacesAreRefused()
    {
        var machine = new FakeMachine { NamespaceFailure = "unshare: unshare failed: Operation not permitted" };

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.UserNamespacesUnusable, result.Reason);
        Assert.Contains("Operation not permitted", result.Message);
        Assert.Contains("kernel.unprivileged_userns_clone", result.Remedy);
        Assert.Equal("/usr/bin/unshare", machine.ProbedUnshare);
    }

    [Fact]
    public void Check_Should_ReportXvfb_When_ItIsNotInstalled()
    {
        var machine = new FakeMachine();
        machine.Tools.Remove("Xvfb");

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.XvfbMissing, result.Reason);
        Assert.Contains("xorg-server-xvfb", result.Remedy);
    }

    [Theory]
    [InlineData("Vintagestory")]
    [InlineData("Vintagestory.dll")]
    [InlineData("Vintagestory.runtimeconfig.json")]
    [InlineData("Lib/libglfw.so.3")]
    public void Check_Should_ListTheMissingFile_When_TheInstallLacksAClientFile(string missing)
    {
        var machine = new FakeMachine();
        machine.Files.Remove("/opt/vs/" + missing);

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.InstallIncomplete, result.Reason);
        Assert.Contains(missing, result.Message);
        Assert.Contains("/opt/vs", result.Message);
        Assert.Contains("server-only", result.Remedy);
    }

    [Fact]
    public void Check_Should_ListEveryMissingFile_When_TheInstallIsServerOnly()
    {
        var machine = new FakeMachine();
        machine.Files.Clear();

        ClientAvailability result = machine.Check();

        Assert.All(ClientAvailability.ClientFiles, file => Assert.Contains(file, result.Message));
    }

    [Fact]
    public void Check_Should_TakeTheInstallFromVintageStory_When_NoneIsGiven()
    {
        var machine = new FakeMachine { Install = null };
        machine.Environment["VINTAGE_STORY"] = "/opt/vs";

        Assert.Equal("/opt/vs", machine.Check().Toolchain!.InstallDirectory);
    }

    [Fact]
    public void Check_Should_PreferTheGivenInstall_When_VintageStoryNamesAnother()
    {
        var machine = new FakeMachine { Install = "/opt/vs" };
        machine.Environment["VINTAGE_STORY"] = "/elsewhere";

        Assert.Equal("/opt/vs", machine.Check().Toolchain!.InstallDirectory);
    }

    [Fact]
    public void Check_Should_ReportTheInstall_When_NoneIsGivenAndTheVariableIsUnset()
    {
        var machine = new FakeMachine { Install = null };

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.InstallIncomplete, result.Reason);
        Assert.Contains("VINTAGE_STORY", result.Message);
    }

    [Fact]
    public void Check_Should_ReportTheRuntime_When_NoDotnetFolderHasWhatTheClientNeeds()
    {
        var machine = new FakeMachine();
        machine.Runtimes.Clear();
        machine.Runtimes["/opt/private-dotnet"] = ["10.0.11"];

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.DotnetRuntimeMissing, result.Reason);
        Assert.Contains("Microsoft.NETCore.App 10.0.0", result.Message);
        Assert.Contains("/usr/share/dotnet", result.Message);
        Assert.Contains("DOTNET_ROOT", result.Remedy);
        Assert.Contains("does not search PATH", result.Remedy);
    }

    [Fact]
    public void Check_Should_UseTheFolderOfTheVariable_When_ItHoldsTheRuntime()
    {
        var machine = new FakeMachine();
        machine.Environment["DOTNET_ROOT"] = "/opt/private-dotnet";
        machine.Runtimes["/opt/private-dotnet"] = ["10.0.11"];

        Assert.Equal("/opt/private-dotnet", machine.Check().Toolchain!.DotnetRoot);
    }

    [Fact]
    public void Check_Should_FallBackToTheDefaultFolder_When_TheVariablesFolderLacksTheRuntime()
    {
        var machine = new FakeMachine();
        machine.Environment["DOTNET_ROOT"] = "/opt/old-dotnet";
        machine.Runtimes["/opt/old-dotnet"] = ["8.0.30"];

        Assert.Equal("/usr/share/dotnet", machine.Check().Toolchain!.DotnetRoot);
    }

    [Fact]
    public void Check_Should_UseTheRegisteredFolder_When_TheVariableIsUnset()
    {
        var machine = new FakeMachine();
        machine.Runtimes.Clear();
        machine.Runtimes["/opt/registered"] = ["10.0.0"];
        machine.Texts["/etc/dotnet/install_location"] = "/opt/registered\n";

        Assert.Equal("/opt/registered", machine.Check().Toolchain!.DotnetRoot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    public void Check_Should_ReportTheRuntimeConfig_When_ItNamesNoFramework(string json)
    {
        var machine = new FakeMachine();
        machine.Texts["/opt/vs/Vintagestory.runtimeconfig.json"] = json;

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.DotnetRuntimeMissing, result.Reason);
        Assert.Contains("Vintagestory.runtimeconfig.json", result.Message);
        Assert.Contains("Reinstall", result.Remedy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Check_Should_ReportTheDataPath_When_NoneIsGiven(string? dataPath)
    {
        var machine = new FakeMachine { DataPath = dataPath };

        ClientAvailability result = machine.Check();

        Assert.Equal(ClientUnavailableReason.NoDataPath, result.Reason);
        Assert.Contains("never the game's own data folder", result.Remedy);
    }

    [Fact]
    public void Check_Should_ReportRungsInTheDocumentedOrder_When_EachIsRepairedInTurn()
    {
        // Everything broken at once; fixing the rungs one by one must walk the enum in order.
        var machine = new FakeMachine { IsLinux = false, NamespaceFailure = "refused", DataPath = null, Install = null };
        machine.Environment["CI"] = "true";
        machine.Environment["ATLAS_CLIENT"] = "off";
        machine.Tools.Clear();
        machine.Files.Clear();
        machine.Runtimes.Clear();

        var seen = new List<ClientUnavailableReason> { machine.Check().Reason };
        machine.Environment.Remove("CI");
        seen.Add(machine.Check().Reason);
        machine.Environment.Remove("ATLAS_CLIENT");
        seen.Add(machine.Check().Reason);
        machine.IsLinux = true;
        seen.Add(machine.Check().Reason);
        machine.Tools["unshare"] = "/usr/bin/unshare";
        seen.Add(machine.Check().Reason);
        machine.NamespaceFailure = null;
        seen.Add(machine.Check().Reason);
        machine.Tools["Xvfb"] = "/usr/bin/Xvfb";
        seen.Add(machine.Check().Reason);
        machine.Install = "/opt/vs";
        machine.RestoreInstallFiles();
        seen.Add(machine.Check().Reason);
        machine.Runtimes["/usr/share/dotnet"] = ["10.0.11"];
        seen.Add(machine.Check().Reason);
        machine.DataPath = "/data/client";
        seen.Add(machine.Check().Reason);

        Assert.Equal(
            [
                ClientUnavailableReason.CiEnvironment,
                ClientUnavailableReason.SwitchedOff,
                ClientUnavailableReason.NotLinux,
                ClientUnavailableReason.UnshareMissing,
                ClientUnavailableReason.UserNamespacesUnusable,
                ClientUnavailableReason.XvfbMissing,
                ClientUnavailableReason.InstallIncomplete,
                ClientUnavailableReason.DotnetRuntimeMissing,
                ClientUnavailableReason.NoDataPath,
                ClientUnavailableReason.None,
            ],
            seen);
    }

    [Fact]
    public void Check_Should_NotProbeTheMachine_When_AnEarlierRungAlreadyFailed()
    {
        var machine = new FakeMachine();
        machine.Environment["GITHUB_ACTIONS"] = "true";

        machine.Check();

        Assert.Equal(0, machine.ToolLookups);
        Assert.Null(machine.ProbedUnshare);
    }

    [Fact]
    public void Check_Should_GiveEveryUnavailableResultAMessageAndARemedy()
    {
        var failing = new List<Action<FakeMachine>>
        {
            m => m.Environment["CI"] = "true",
            m => m.Environment["ATLAS_CLIENT"] = "off",
            m => m.IsLinux = false,
            m => m.Tools.Remove("unshare"),
            m => m.NamespaceFailure = "refused",
            m => m.Tools.Remove("Xvfb"),
            m => m.Files.Clear(),
            m => m.Runtimes.Clear(),
            m => m.DataPath = null,
        };

        foreach (Action<FakeMachine> breakIt in failing)
        {
            var machine = new FakeMachine();
            breakIt(machine);

            ClientAvailability result = machine.Check();

            Assert.False(result.IsAvailable);
            Assert.NotEqual(ClientUnavailableReason.None, result.Reason);
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
            Assert.False(string.IsNullOrWhiteSpace(result.Remedy));
            Assert.Contains(result.Message, result.ToString());
            Assert.Contains(result.Remedy, result.ToString());
        }
    }

    // A machine on which everything works, with each fact mutable and every lookup counted.
    private sealed class FakeMachine
    {
        private const string Config = """{ "runtimeOptions": { "framework": { "name": "Microsoft.NETCore.App", "version": "10.0.0" } } }""";

        public FakeMachine() => RestoreInstallFiles();

        public Dictionary<string, string> Environment { get; } = [];

        public Dictionary<string, string> Tools { get; } = new()
        {
            ["unshare"] = "/usr/bin/unshare",
            ["Xvfb"] = "/usr/bin/Xvfb",
            ["import"] = "/usr/bin/import",
        };

        public HashSet<string> Files { get; } = [];

        public Dictionary<string, string> Texts { get; } = new() { ["/opt/vs/Vintagestory.runtimeconfig.json"] = Config };

        public Dictionary<string, string[]> Runtimes { get; } = new() { ["/usr/share/dotnet"] = ["8.0.30", "10.0.11"] };

        public bool IsLinux { get; set; } = true;

        public string? NamespaceFailure { get; set; }

        public string? Install { get; set; } = "/opt/vs";

        public string? DataPath { get; set; } = "/data/client";

        public int ToolLookups { get; private set; }

        public string? ProbedUnshare { get; private set; }

        public void RestoreInstallFiles()
        {
            foreach (string file in ClientAvailability.ClientFiles)
            {
                Files.Add("/opt/vs/" + file);
            }
        }

        public ClientAvailability Check() => ClientAvailability.Check(Install, DataPath, new ClientProbes
        {
            Env = name => Environment.GetValueOrDefault(name),
            IsLinux = IsLinux,
            FindTool = tool =>
            {
                ToolLookups++;
                return Tools.GetValueOrDefault(tool);
            },
            FileExists = Files.Contains,
            ReadText = path => Texts.GetValueOrDefault(path),
            ListFolders = path => Runtimes.GetValueOrDefault(path.Replace("/shared/Microsoft.NETCore.App", string.Empty)) ?? [],
            ProbeNamespaces = unshare =>
            {
                ProbedUnshare = unshare;
                return NamespaceFailure;
            },
        });
    }
}
