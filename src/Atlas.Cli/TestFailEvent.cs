using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>A scenario failed (assertion, setup guard, watchdog timeout, or server crash: the
/// in-process watchdog already translates crashes into ordinary failures).</summary>
internal sealed record TestFailEvent() : WorkerEvent("test-fail")
{
    /// <summary>Fully qualified name of the scenario class.</summary>
    [JsonPropertyName("class")]
    public required string Class { get; init; }

    /// <summary>The scenario's display name.</summary>
    [JsonPropertyName("test")]
    public required string Test { get; init; }

    /// <summary>Execution time in whole milliseconds.</summary>
    [JsonPropertyName("durationMs")]
    public required long DurationMs { get; init; }

    /// <summary>The failure message, prefixed with the exception type ("Type: message").</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>The failing exception's stack trace, when one exists.</summary>
    [JsonPropertyName("stack")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Stack { get; init; }
}
