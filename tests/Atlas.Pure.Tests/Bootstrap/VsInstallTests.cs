using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class VsInstallTests
{
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
        string dir = Directory.CreateTempSubdirectory("atlas-pdb-preflight").FullName;
        try
        {
            // Simulate the real failure mode: a consumer's build copied VintagestoryAPI.dll into
            // the test output but the pdb never made it (vendored dll, custom copy step).
            File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.dll"), "stub");
            File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.pdb"), "stub");
            File.Delete(Path.Combine(dir, "VintagestoryAPI.pdb"));

            var ex = Assert.Throws<AtlasSetupException>(() => VsInstall.VerifyApiPdbPresent(dir));
            Assert.Contains("VintagestoryAPI.pdb", ex.Message);
            Assert.Contains(dir, ex.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyApiPdbPresent_Should_Pass_When_PdbShipsNextToDll()
    {
        string dir = Directory.CreateTempSubdirectory("atlas-pdb-preflight").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.dll"), "stub");
            File.WriteAllText(Path.Combine(dir, "VintagestoryAPI.pdb"), "stub");

            VsInstall.VerifyApiPdbPresent(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void VerifyApiPdbPresent_Should_Pass_When_OutputHoldsNoApiDll()
    {
        string dir = Directory.CreateTempSubdirectory("atlas-pdb-preflight").FullName;
        try
        {
            // No VintagestoryAPI.dll copy at all: probing falls through to the AssemblyResolve
            // hook, which loads the game install's copy (pdb beside it). Nothing to check.
            VsInstall.VerifyApiPdbPresent(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
