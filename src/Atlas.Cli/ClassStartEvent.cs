using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>A scenario class is about to run (its embedded server boots after this line).</summary>
internal sealed record ClassStartEvent() : WorkerEvent("class-start")
{
    /// <summary>Fully qualified name of the scenario class.</summary>
    [JsonPropertyName("class")]
    public required string Class { get; init; }
}
