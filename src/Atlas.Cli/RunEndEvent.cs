using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>Last line of every worker stream, run and list mode alike. A stream without a
/// run-end line means the worker died; the orchestrator must treat its assignment as failed.</summary>
internal sealed record RunEndEvent() : WorkerEvent("run-end")
{
    /// <summary>Total scenarios reported (in list mode: total scenarios discovered).</summary>
    [JsonPropertyName("total")]
    public required int Total { get; init; }

    /// <summary>Scenarios that passed.</summary>
    [JsonPropertyName("passed")]
    public required int Passed { get; init; }

    /// <summary>Scenarios that failed.</summary>
    [JsonPropertyName("failed")]
    public required int Failed { get; init; }

    /// <summary>Scenarios that were skipped.</summary>
    [JsonPropertyName("skipped")]
    public required int Skipped { get; init; }

    /// <summary>Runner-level errors (see <see cref="ErrorEvent"/>).</summary>
    [JsonPropertyName("errors")]
    public required int Errors { get; init; }

    /// <summary>Wall-clock duration of the whole worker run in milliseconds.</summary>
    [JsonPropertyName("wallClockMs")]
    public required long WallClockMs { get; init; }

    /// <summary>The exit code the worker process is about to return (0 ok, 1 failures or empty
    /// run, 2 environment error).</summary>
    [JsonPropertyName("exitCode")]
    public required int ExitCode { get; init; }
}
