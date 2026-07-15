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
    public void Describe_Should_RenderSizeTruncatedHashAndVersion()
    {
        string rendered = ApiCopySync.Describe(new ApiCopySync.FileIdentity(1024, HashA, "1.22.3.0"));

        Assert.Equal($"1024 bytes, sha256 {HashA[..12]}, assembly version 1.22.3.0", rendered);
    }

    [Fact]
    public void Describe_Should_ReportUnknown_When_AssemblyVersionUnreadable()
    {
        string rendered = ApiCopySync.Describe(new ApiCopySync.FileIdentity(10, HashA, null));

        Assert.Contains("assembly version unknown", rendered);
    }

    [Fact]
    public void Describe_Should_NotTruncateShortHashes()
    {
        // Defensive path: identities built from something shorter than 12 hex chars still render.
        string rendered = ApiCopySync.Describe(new ApiCopySync.FileIdentity(10, "ABC", null));

        Assert.Contains("sha256 ABC", rendered);
    }
}
