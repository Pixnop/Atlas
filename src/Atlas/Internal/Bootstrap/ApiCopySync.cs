namespace Atlas.Internal.Bootstrap;

/// <summary>File-identity primitives shared by the engine-assembly staging preflight (issue #49):
/// decides whether two file identities describe the same content and renders one identity for
/// error messages. Kept free of IO so the comparison and the rendering are testable without real
/// files; the hashing and metadata reads stay in the thin shell (<see cref="EngineStager"/>).</summary>
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

    /// <summary>Renders one identity as "N bytes, sha256 XXXXXXXXXXXX, assembly version V".</summary>
    /// <param name="identity">The identity to render.</param>
    /// <returns>The human-readable identity.</returns>
    public static string Describe(FileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
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
