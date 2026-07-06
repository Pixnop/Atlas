using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>A scenario passed.</summary>
internal sealed record TestPassEvent() : WorkerEvent("test-pass")
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
}
