namespace Atlas.Internal.RealClient;

/// <summary>The programs the sandbox itself needs, found by
/// <see cref="ClientAvailability.CheckSandboxTools"/>: what a run needs whatever it starts inside,
/// the game client or a stand-in.</summary>
/// <param name="UnsharePath">The <c>unshare</c> program.</param>
/// <param name="SetprivPath">The <c>setpriv</c> program.</param>
/// <param name="XvfbPath">The <c>Xvfb</c> program.</param>
/// <param name="ImportPath">ImageMagick's <c>import</c>, or <see langword="null"/> when it is not
/// installed.</param>
internal sealed record SandboxTools(string UnsharePath, string SetprivPath, string XvfbPath, string? ImportPath);
