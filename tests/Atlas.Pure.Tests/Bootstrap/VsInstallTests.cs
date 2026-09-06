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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_Should_ReturnError_When_VariableIsUnset(string? directory)
    {
        string? error = VsInstall.Validate(directory, _ => true);

        Assert.NotNull(error);
        Assert.Contains("VINTAGE_STORY", error);
    }

    [Fact]
    public void Validate_Should_ReturnErrorNamingTheValue_When_InstallIsIncomplete()
    {
        string? error = VsInstall.Validate("/opt/empty", _ => false);

        Assert.NotNull(error);
        Assert.Contains("/opt/empty", error);
        Assert.Contains("VintagestoryLib.dll", error);
    }

    [Fact]
    public void Validate_Should_ProbeForVintagestoryLib_When_DirectoryIsGiven()
    {
        string? probed = null;

        VsInstall.Validate("/opt/vs", path =>
        {
            probed = path;
            return true;
        });

        Assert.Equal(Path.Combine("/opt/vs", "VintagestoryLib.dll"), probed);
    }

    [Fact]
    public void Validate_Should_ReturnNull_When_InstallLooksValid()
    {
        Assert.Null(VsInstall.Validate("/opt/vs", _ => true));
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

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(() => VsInstall.VerifyApiPdbPresent(_dir.FullName));
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
