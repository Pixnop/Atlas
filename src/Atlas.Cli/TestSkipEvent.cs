using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>A scenario was skipped.</summary>
internal sealed record TestSkipEvent() : WorkerEvent("test-skip")
{
    /// <summary>Fully qualified name of the scenario class.</summary>
    [JsonPropertyName("class")]
    public required string Class { get; init; }

    /// <summary>The scenario's display name.</summary>
    [JsonPropertyName("test")]
    public required string Test { get; init; }

    /// <summary>Always 0: xunit does not time skipped tests. Present so every test-* event has
    /// the same shape.</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    /// <summary>The skip reason.</summary>
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}
