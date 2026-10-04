using System.ComponentModel;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Atlas.Internal.Hosting;

/// <summary>The witness file every scratch data directory carries, so a folder under
/// <c>&lt;temp&gt;/atlas/&lt;32 hex&gt;</c> can be traced to the run that made it: a run
/// identifier shared by the folders of one run, the process id, the test assembly and the time
/// the process started. The identifier lives in a file, not in the folder name, so anything that
/// looks for the 32-hex folder names keeps working. A thin IO shell around a pure core
/// (<see cref="Describe"/> and <see cref="ResolveRunId"/>), the <see cref="ScratchRetention"/>
/// pattern.</summary>
/// <remarks>Process id and start time together name one process even after the operating system
/// reuses the id, which is what lets a script tell a folder left by a dead run from one a live
/// run is still using. The run identifier is a fresh one per process unless
/// <see cref="RunIdVariable"/> is set, in which case every process that inherits the variable
/// writes that value: the way to give the folders of several test projects, or of the workers of
/// <c>atlas run --parallel</c>, one identifier.</remarks>
internal static class ScratchWitness
{
    /// <summary>Name of the witness file, at the root of the scratch data directory.</summary>
    public const string FileName = "atlas-run.json";

    /// <summary>Name of the variable that overrides the generated run identifier.</summary>
    public const string RunIdVariable = "ATLAS_RUN_ID";

    private static readonly string ProcessRunId = Guid.NewGuid().ToString("N");
    private static readonly Lazy<DateTime> ProcessStartedUtc = new(ReadProcessStart);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // The file is read from disk, never embedded in HTML, so the default escaping would only
        // turn a run id like "build+12" or a non-ASCII assembly name into \u002B and \u00E9
        // sequences that a grep for the value as written would miss.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Resolves the run identifier: the value of <see cref="RunIdVariable"/> when it is
    /// set to something other than whitespace, the process's generated one otherwise.</summary>
    /// <param name="variableValue">The raw variable value, or <see langword="null"/> when unset.</param>
    /// <param name="generated">The identifier generated for this process.</param>
    /// <returns>The run identifier to write.</returns>
    public static string ResolveRunId(string? variableValue, string generated)
        => string.IsNullOrWhiteSpace(variableValue) ? generated : variableValue.Trim();

    /// <summary>Words the witness file's content: indented JSON, one property per line, so a
    /// shell script can <c>grep</c> it as well as a program parse it.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="processId">The id of the process hosting the server.</param>
    /// <param name="testAssembly">The simple name of the test assembly whose class the host boots
    /// for, or <see langword="null"/> for a host no scenario class owns.</param>
    /// <param name="processStartedUtc">When the process started, in UTC.</param>
    /// <returns>The file content.</returns>
    public static string Describe(string runId, int processId, string? testAssembly, DateTime processStartedUtc)
        => JsonSerializer.Serialize(new Witness(runId, processId, testAssembly, processStartedUtc), Options);

    /// <summary>Writes the witness file into <paramref name="dataPath"/>, creating the directory
    /// when it does not exist yet. Best-effort: a scratch directory without a witness is untidy,
    /// not wrong, so a failure to write it never fails a boot.</summary>
    /// <param name="dataPath">The host's scratch data directory.</param>
    /// <param name="testAssembly">The simple name of the test assembly the host boots for, or
    /// <see langword="null"/> when no scenario class owns it.</param>
    public static void Write(string dataPath, string? testAssembly)
        => Write(dataPath, testAssembly, Environment.GetEnvironmentVariable(RunIdVariable));

    /// <summary>The body of <see cref="Write(string, string?)"/> with the variable's value
    /// passed in, so a test can state the override without touching the process
    /// environment.</summary>
    /// <param name="dataPath">The host's scratch data directory.</param>
    /// <param name="testAssembly">The simple name of the test assembly the host boots for.</param>
    /// <param name="runIdVariableValue">The raw value of <see cref="RunIdVariable"/>.</param>
    internal static void Write(string dataPath, string? testAssembly, string? runIdVariableValue)
    {
        try
        {
            Directory.CreateDirectory(dataPath);
            File.WriteAllText(
                Path.Combine(dataPath, FileName),
                Describe(
                    ResolveRunId(runIdVariableValue, ProcessRunId),
                    Environment.ProcessId,
                    testAssembly,
                    ProcessStartedUtc.Value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort by design; see the method summary.
        }
    }

    /// <summary>Reads when this process started. A platform that cannot say falls back to the
    /// first time a witness is written, which is close enough to tell two runs apart.</summary>
    /// <returns>The process start time, in UTC.</returns>
    private static DateTime ReadProcessStart()
    {
        try
        {
            using Process self = Process.GetCurrentProcess();
            return self.StartTime.ToUniversalTime();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return DateTime.UtcNow;
        }
    }

    /// <summary>The serialized shape of the witness file.</summary>
    /// <param name="RunId">The run identifier.</param>
    /// <param name="ProcessId">The id of the process hosting the server.</param>
    /// <param name="TestAssembly">The test assembly's simple name, or <see langword="null"/>.</param>
    /// <param name="ProcessStartedUtc">When the process started, in UTC.</param>
    private sealed record Witness(string RunId, int ProcessId, string? TestAssembly, DateTime ProcessStartedUtc);
}
