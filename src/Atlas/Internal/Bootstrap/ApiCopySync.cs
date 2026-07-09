namespace Atlas.Internal.Bootstrap;

/// <summary>The pure core of the API-copy sync preflight (see
/// <c>VsInstall.VerifyApiCopyMatchesInstall</c>): decides whether two file identities describe
/// the same content and formats the mismatch error. Kept free of IO so the comparison and the
/// message it produces are testable without real files; the hashing and metadata reads stay in
/// the thin shell.</summary>
internal static class ApiCopySync
{
    /// <summary>Decides whether the two identities describe byte-identical content.</summary>
    /// <param name="local">Identity of the test-output copy.</param>
    /// <param name="install">Identity of the game install's copy.</param>
    /// <returns><see langword="true"/> when size and content hash both match.</returns>
    public static bool AreIdentical(FileIdentity local, FileIdentity install)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(install);
        return local.Length == install.Length
            && string.Equals(local.Sha256, install.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Formats the setup error for a diverged test-output copy: both paths, both file
    /// identities, why the boot would die, and the two remedies.</summary>
    /// <param name="localPath">Path of the test-output copy.</param>
    /// <param name="local">Identity of the test-output copy.</param>
    /// <param name="installPath">Path of the game install's copy.</param>
    /// <param name="install">Identity of the game install's copy.</param>
    /// <returns>The complete error message.</returns>
    public static string DescribeMismatch(
        string localPath, FileIdentity local, string installPath, FileIdentity install)
    {
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(install);
        return
            "VintagestoryAPI.dll in the test output differs from the VINTAGE_STORY install's copy. " +
            $"Test output: '{localPath}' ({Describe(local)}); install: '{installPath}' " +
            $"({Describe(install)}). The test-output copy wins default assembly probing, so booting " +
            "would mix this stale VintagestoryAPI with the install's VintagestoryLib and die deep " +
            "into server boot with a cryptic MissingFieldException or MissingMethodException. This " +
            "typically means VINTAGE_STORY was pointed at a different install without rebuilding. " +
            "Either rebuild the test project against this install, or copy the install's " +
            "VintagestoryAPI.dll AND VintagestoryAPI.pdb over the test-output copies.";
    }

    /// <summary>Renders one identity as "N bytes, sha256 XXXXXXXXXXXX, assembly version V".</summary>
    /// <param name="identity">The identity to render.</param>
    /// <returns>The human-readable identity.</returns>
    private static string Describe(FileIdentity identity)
    {
        string hash = identity.Sha256.Length > 12 ? identity.Sha256[..12] : identity.Sha256;
        return $"{identity.Length} bytes, sha256 {hash}, assembly version " +
            (identity.AssemblyVersion ?? "unknown");
    }

    /// <summary>Content identity of one VintagestoryAPI.dll file.</summary>
    /// <param name="Length">File size in bytes.</param>
    /// <param name="Sha256">Uppercase hex SHA-256 of the file content.</param>
    /// <param name="AssemblyVersion">The assembly version read from metadata, or
    /// <see langword="null"/> when the file is not a readable assembly. Display-only: forks
    /// rebuild the API at the SAME assembly version, so versions never participate in the
    /// comparison.</param>
    internal sealed record FileIdentity(long Length, string Sha256, string? AssemblyVersion);
}
