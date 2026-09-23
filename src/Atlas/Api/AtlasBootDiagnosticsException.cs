namespace Atlas.Api;

/// <summary>Thrown at boot when a scenario class opted into strict boot diagnostics
/// (<c>[AtlasWorld(StrictBootDiagnostics = true)]</c>) and the engine logged at least one
/// <c>Warning</c>-or-above entry between the start of the boot and the world becoming ready. The
/// message lists every offending entry (level, source, message). A crash is never reported as
/// this exception: a crash during boot surfaces as its own exception, and one after boot as
/// <see cref="ServerCrashedException"/>.</summary>
public sealed class AtlasBootDiagnosticsException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AtlasBootDiagnosticsException"/> class.</summary>
    /// <param name="message">The failure message, listing every offending entry.</param>
    public AtlasBootDiagnosticsException(string message)
        : base(message)
    {
    }
}
