using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>One scenario found by `atlas run --list --worker` (discovery only, no server).</summary>
internal sealed record DiscoveredEvent() : WorkerEvent("discovered")
{
    /// <summary>Fully qualified name of the scenario class.</summary>
    [JsonPropertyName("class")]
    public required string Class { get; init; }

    /// <summary>The scenario's display name, exactly as `dotnet test` would report it.</summary>
    [JsonPropertyName("test")]
    public required string Test { get; init; }
}
