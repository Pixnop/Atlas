namespace Atlas.Api;

/// <summary>Thrown at boot when a scenario class opted into strict boot diagnostics
/// (<c>[AtlasWorld(StrictBootDiagnostics = true)]</c>) and the engine logged at least one
/// <c>Warning</c>-or-above entry between the start of the boot and the world becoming ready. The
/// message lists every offending entry (level, source, message), with a short hint appended to
/// an entry Atlas can reliably explain (today: a dependency dll listed in <c>[AtlasMods]</c> as
/// if it were its own mod), and ends with the boot's kept scratch folder and the path of the
/// engine's <c>server-main.log</c> in it. A crash is never reported as this exception: a crash
/// during boot surfaces as its own exception, and one after boot as
/// <see cref="ServerCrashedException"/>.</summary>
/// <remarks>When the class's first boot fails, only the scenario that ran it sees this
/// exception. The class is not booted again: every later scenario fails at once with a
/// <see cref="ServerCrashedException"/> that repeats this failure (it is that exception's inner
/// exception) without booting the server, and only that boot's scratch folder is kept. A boot
/// that fails after the class booted once (a <c>FreshWorld</c> recycle, a <c>RestartWorld</c>
/// replacement) throws this exception into its own scenario only, keeps its own scratch folder,
/// and the next scenario boots as before.</remarks>
public sealed class AtlasBootDiagnosticsException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AtlasBootDiagnosticsException"/> class.</summary>
    /// <param name="message">The failure message, listing every offending entry.</param>
    public AtlasBootDiagnosticsException(string message)
        : base(message)
    {
    }
}
