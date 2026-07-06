using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>A scenario class finished (its server, if any, is being torn down).</summary>
internal sealed record ClassEndEvent() : WorkerEvent("class-end")
{
    /// <summary>Fully qualified name of the scenario class.</summary>
    [JsonPropertyName("class")]
    public required string Class { get; init; }

    /// <summary>Scenarios of this class that passed.</summary>
    [JsonPropertyName("passed")]
    public required int Passed { get; init; }

    /// <summary>Scenarios of this class that failed.</summary>
    [JsonPropertyName("failed")]
    public required int Failed { get; init; }

    /// <summary>Scenarios of this class that were skipped.</summary>
    [JsonPropertyName("skipped")]
    public required int Skipped { get; init; }
}
