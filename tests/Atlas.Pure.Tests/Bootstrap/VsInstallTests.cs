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

    [Fact]
    public void VerifyApiCopyMatchesInstall_Should_Pass_When_CopiesAreIdentical()
    {
        (string output, string install) = CreateOutputAndInstallDirs();
        try
        {
            // Use a real assembly so the identity read exercises the assembly-version path too.
            string source = typeof(VsInstallTests).Assembly.Location;
            File.Copy(source, Path.Combine(output, "VintagestoryAPI.dll"));
            File.Copy(source, Path.Combine(install, "VintagestoryAPI.dll"));

            VsInstall.VerifyApiCopyMatchesInstall(output, install);
        }
        finally
        {
            DeleteDirs(output, install);
        }
    }

    [Fact]
    public void VerifyApiCopyMatchesInstall_Should_ThrowSetupException_When_ContentsDiffer()
    {
        (string output, string install) = CreateOutputAndInstallDirs();
        try
        {
            // Same length, different bytes: the fork trap from issue #49, where the API is
            // rebuilt at the SAME assembly version. Only a content comparison catches it.
            File.WriteAllText(Path.Combine(output, "VintagestoryAPI.dll"), "stale-copy");
            File.WriteAllText(Path.Combine(install, "VintagestoryAPI.dll"), "forks-copy");

            var ex = Assert.Throws<AtlasSetupException>(
                () => VsInstall.VerifyApiCopyMatchesInstall(output, install));

            // The message must name both files and both remedies.
            Assert.Contains(Path.Combine(output, "VintagestoryAPI.dll"), ex.Message);
            Assert.Contains(Path.Combine(install, "VintagestoryAPI.dll"), ex.Message);
            Assert.Contains("rebuild", ex.Message);
            Assert.Contains("copy the install's", ex.Message);
            Assert.Contains("VintagestoryAPI.pdb", ex.Message);
        }
        finally
        {
            DeleteDirs(output, install);
        }
    }

    [Fact]
    public void VerifyApiCopyMatchesInstall_Should_Pass_When_OutputHoldsNoApiDll()
    {
        (string output, string install) = CreateOutputAndInstallDirs();
        try
        {
            // No local copy (Private=false consumers): probing reaches the install's own copy,
            // so nothing can diverge and the check must skip silently.
            File.WriteAllText(Path.Combine(install, "VintagestoryAPI.dll"), "install-copy");

            VsInstall.VerifyApiCopyMatchesInstall(output, install);
        }
        finally
        {
            DeleteDirs(output, install);
        }
    }

    [Fact]
    public void VerifyApiCopyMatchesInstall_Should_Pass_When_InstallHoldsNoApiDll()
    {
        (string output, string install) = CreateOutputAndInstallDirs();
        try
        {
            // Locate() has already vetted the install; without an install-side API dll there is
            // nothing to compare against, and this check must not invent its own install error.
            File.WriteAllText(Path.Combine(output, "VintagestoryAPI.dll"), "local-copy");

            VsInstall.VerifyApiCopyMatchesInstall(output, install);
        }
        finally
        {
            DeleteDirs(output, install);
        }
    }

    private static (string Output, string Install) CreateOutputAndInstallDirs() =>
        (Directory.CreateTempSubdirectory("atlas-apisync-output").FullName,
         Directory.CreateTempSubdirectory("atlas-apisync-install").FullName);

    private static void DeleteDirs(params string[] dirs)
    {
        foreach (string dir in dirs)
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
