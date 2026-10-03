namespace Atlas.Engine.Tests.RealClient;

/// <summary>A fact that is skipped, with the reason, wherever the sandbox itself cannot run: in
/// CI, on another platform, without <c>unshare</c>, <c>setpriv</c>, user namespaces or
/// <c>Xvfb</c>. It does not need a game client: the test starts a stand-in program in the real
/// sandbox, so the mechanics are proven on server-only installs as well. The check runs when the
/// attribute is read at discovery (xunit 2.9 has no runtime skip), which costs one namespace
/// probe and only when a rung before it did not already fail.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SandboxFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="SandboxFactAttribute"/> class.</summary>
    public SandboxFactAttribute()
    {
        if (RealClientEnvironment.SandboxFailure is { } failure)
        {
            Skip = failure.ToString();
        }
    }
}
