using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

/// <summary>The guard every CLI-to-harness seam runs behind: version skew between the harness the
/// CLI compiled against and the copy the target directory ships must read as a diagnostic naming
/// both versions, and anything else must keep travelling as itself.</summary>
public class HarnessSeamTests
{
    [Fact]
    public void TryCall_Should_ReportNoError_When_TheSeamCallRuns()
    {
        bool called = false;

        Assert.Null(HarnessSeam.TryCall(HarnessSeam.AdapterAssemblyName, () => called = true));
        Assert.True(called);
    }

    [Theory]
    [InlineData(typeof(MissingMethodException))]
    [InlineData(typeof(MissingFieldException))]
    [InlineData(typeof(TypeLoadException))]
    [InlineData(typeof(MethodAccessException))]
    public void TryCall_Should_NameBothVersions_When_TheLoadedHarnessLacksTheMember(Type failure)
    {
        // The four shapes the runtime uses for "the assembly I loaded does not have the member
        // this caller was compiled against", one per skew direction the CLI can meet.
        string? error = HarnessSeam.TryCall(
            HarnessSeam.AdapterAssemblyName,
            () => throw (Exception)Activator.CreateInstance(failure)!);

        Assert.NotNull(error);
        Assert.Contains("Atlas.XUnit.dll is v", error, StringComparison.Ordinal);
        Assert.Contains("this atlas CLI is v", error, StringComparison.Ordinal);
        Assert.Contains("rebuild the test project", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCall_Should_ReportTheVersionAsUnknown_When_TheNamedAssemblyIsNotLoaded()
    {
        string? error = HarnessSeam.TryCall(
            "Atlas.NotLoaded", () => throw new TypeLoadException());

        Assert.NotNull(error);
        Assert.Contains($"Atlas.NotLoaded.dll is v{CliVersion.Unknown}", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCall_Should_LetTheFailureThrough_When_ItIsNotVersionSkew()
    {
        // A seam that reached the harness and failed inside it is a real failure, not skew:
        // swallowing it here would hide the very thing the command is reporting on.
        Assert.Throws<InvalidOperationException>(
            () => HarnessSeam.TryCall(HarnessSeam.AdapterAssemblyName, () => throw new InvalidOperationException()));
    }
}
