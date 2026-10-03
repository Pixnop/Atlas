namespace Atlas.Engine.Tests.RealClient;

/// <summary>A fact that is skipped, with the reason, wherever the real client cannot run: in CI,
/// on another platform, without the sandbox's tools or a full client install. xunit 2.9 has no
/// runtime skip, so the check runs when the attribute is read at discovery, which costs the one
/// namespace probe the ladder makes and only when a rung before it did not already fail. A test
/// that only needs the sandbox, with a stand-in program, uses
/// <see cref="SandboxFactAttribute"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class RealClientFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RealClientFactAttribute"/> class.</summary>
    public RealClientFactAttribute()
    {
        if (!RealClientEnvironment.Availability.IsAvailable)
        {
            Skip = RealClientEnvironment.Availability.ToString();
        }
    }
}
