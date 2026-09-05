using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class VsInstallTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("atlas-pdb-preflight");

    public void Dispose()
    {
        _dir.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Locate_Should_ThrowSetupException_When_EnvVarPointsNowhere()
    {
        string? saved = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        try
        {
            Environment.SetEnvironmentVariable("VINTAGE_STORY", @"C:\definitely\not\here");
            Assert.Throws<AtlasSetupException>(() => VsInstall.Locate());
        }
        finally
        {
            Environment.SetEnvironmentVariable("VINTAGE_STORY", saved);
        }
    }

    [Fact]
    public void VerifyApiPdbPresent_Should_ThrowSetupException_When_DllShippedWithoutPdb()
    {
        // Simulate the real failure mode: a consumer's build copied VintagestoryAPI.dll into
        // the test output but the pdb never made it (vendored dll, custom copy step).
        File.WriteAllText(Path.Combine(_dir.FullName, "VintagestoryAPI.dll"), "stub");

        var ex = Assert.Throws<AtlasSetupException>(() => VsInstall.VerifyApiPdbPresent(_dir.FullName));
        Assert.Contains("VintagestoryAPI.pdb", ex.Message);
        Assert.Contains(_dir.FullName, ex.Message);
    }

    [Fact]
    public void VerifyApiPdbPresent_Should_Pass_When_PdbShipsNextToDll()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, "VintagestoryAPI.dll"), "stub");
        File.WriteAllText(Path.Combine(_dir.FullName, "VintagestoryAPI.pdb"), "stub");

        Assert.Null(Record.Exception(() => VsInstall.VerifyApiPdbPresent(_dir.FullName)));
    }

    [Fact]
    public void VerifyApiPdbPresent_Should_Pass_When_OutputHoldsNoApiDll()
    {
        // No VintagestoryAPI.dll copy at all: probing falls through to the AssemblyResolve
        // hook, which loads the game install's copy (pdb beside it). Nothing to check.
        Assert.Null(Record.Exception(() => VsInstall.VerifyApiPdbPresent(_dir.FullName)));
    }
}
