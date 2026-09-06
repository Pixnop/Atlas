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
    [InlineData(typeof(FileNotFoundException))]
    [InlineData(typeof(FileLoadException))]
    public void TryCall_Should_NameBothVersions_When_TheLoadedHarnessLacksTheMember(Type failure)
    {
        // The shapes the runtime uses for "the assembly I loaded does not have the member this
        // caller was compiled against", plus the two for "it is not there to have it".
        string? error = HarnessSeam.TryCall(
            HarnessSeam.AdapterAssemblyName,
            () => throw (Exception)Activator.CreateInstance(failure)!);

        Assert.NotNull(error);
        Assert.Contains("Atlas.XUnit.dll is version ", error, StringComparison.Ordinal);
        Assert.Contains("this atlas CLI is version ", error, StringComparison.Ordinal);
        Assert.Contains("rebuild the test project", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCall_Should_NameTheRuntimeFailure_When_ItReportsTheSkew()
    {
        // The same exception shapes come out of a real failure inside the harness, so the line
        // has to carry enough of the cause to tell the two apart.
        string? error = HarnessSeam.TryCall(
            HarnessSeam.AdapterAssemblyName,
            () => throw new TypeLoadException("Could not load\nVintagestory.API.Common.Entity."));

        Assert.NotNull(error);
        Assert.Contains(
            "(TypeLoadException: Could not load Vintagestory.API.Common.Entity.)", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryCall_Should_ReportTheVersionAsUnknown_When_TheNamedAssemblyIsNotLoaded()
    {
        string? error = HarnessSeam.TryCall(
            "Atlas.NotLoaded", () => throw new TypeLoadException());

        Assert.NotNull(error);
        Assert.Contains($"Atlas.NotLoaded.dll is version {CliVersion.Unknown}", error, StringComparison.Ordinal);
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
