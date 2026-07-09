using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class ApiCopySyncTests
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public void AreIdentical_Should_ReturnTrue_When_SizeAndHashMatch()
    {
        var local = new ApiCopySync.FileIdentity(1024, HashA, "1.22.3.0");
        var install = new ApiCopySync.FileIdentity(1024, HashA, "1.22.3.0");

        Assert.True(ApiCopySync.AreIdentical(local, install));
    }

    [Fact]
    public void AreIdentical_Should_IgnoreHashCasing()
    {
        var local = new ApiCopySync.FileIdentity(1024, HashA, null);
        var install = new ApiCopySync.FileIdentity(1024, HashA.ToLowerInvariant(), null);

        Assert.True(ApiCopySync.AreIdentical(local, install));
    }

    [Fact]
    public void AreIdentical_Should_ReturnFalse_When_OnlyContentDiffersAtSameVersion()
    {
        // The issue #49 trap: a fork rebuilds the API at the SAME assembly version and even the
        // same size can collide; content is the only reliable discriminator.
        var local = new ApiCopySync.FileIdentity(1024, HashA, "1.22.3.0");
        var install = new ApiCopySync.FileIdentity(1024, HashB, "1.22.3.0");

        Assert.False(ApiCopySync.AreIdentical(local, install));
    }

    [Fact]
    public void AreIdentical_Should_ReturnFalse_When_SizesDiffer()
    {
        var local = new ApiCopySync.FileIdentity(1024, HashA, null);
        var install = new ApiCopySync.FileIdentity(2048, HashA, null);

        Assert.False(ApiCopySync.AreIdentical(local, install));
    }

    [Fact]
    public void DescribeMismatch_Should_NameBothPathsIdentitiesAndRemedies()
    {
        var local = new ApiCopySync.FileIdentity(1024, HashA, "1.22.3.0");
        var install = new ApiCopySync.FileIdentity(2048, HashB, "1.22.3.0");

        string message = ApiCopySync.DescribeMismatch(
            "/tests/bin/VintagestoryAPI.dll", local, "/install/VintagestoryAPI.dll", install);

        Assert.Contains("/tests/bin/VintagestoryAPI.dll", message);
        Assert.Contains("/install/VintagestoryAPI.dll", message);
        Assert.Contains("1024 bytes", message);
        Assert.Contains("2048 bytes", message);
        Assert.Contains(HashA[..12], message);
        Assert.Contains(HashB[..12], message);
        Assert.Contains("1.22.3.0", message);

        // Both remedies: rebuild against the target install, or copy dll AND pdb over.
        Assert.Contains("rebuild the test project against this install", message);
        Assert.Contains(
            "copy the install's VintagestoryAPI.dll AND VintagestoryAPI.pdb over the test-output copies",
            message);
    }

    [Fact]
    public void DescribeMismatch_Should_ReportUnknown_When_AssemblyVersionUnreadable()
    {
        var local = new ApiCopySync.FileIdentity(10, HashA, null);
        var install = new ApiCopySync.FileIdentity(10, HashB, null);

        string message = ApiCopySync.DescribeMismatch("local.dll", local, "install.dll", install);

        Assert.Contains("assembly version unknown", message);
    }

    [Fact]
    public void DescribeMismatch_Should_NotTruncateShortHashes()
    {
        // Defensive path: identities built from something shorter than 12 hex chars still render.
        var local = new ApiCopySync.FileIdentity(10, "ABC", null);
        var install = new ApiCopySync.FileIdentity(10, "DEF", null);

        string message = ApiCopySync.DescribeMismatch("local.dll", local, "install.dll", install);

        Assert.Contains("sha256 ABC", message);
        Assert.Contains("sha256 DEF", message);
    }
}
