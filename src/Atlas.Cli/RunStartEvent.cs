using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>First line of every worker run: identifies the process and its assignment.</summary>
internal sealed record RunStartEvent() : WorkerEvent("run-start")
{
    /// <summary>Full path of the scenario assembly under execution.</summary>
    [JsonPropertyName("assembly")]
    public required string Assembly { get; init; }

    /// <summary>The fully qualified class names this worker was asked to run; null means the
    /// whole assembly (no <c>--classes</c> filter).</summary>
    [JsonPropertyName("classes")]
    public IReadOnlyList<string>? Classes { get; init; }

    /// <summary>The worker's process id, for orchestrator bookkeeping and log forensics.</summary>
    [JsonPropertyName("pid")]
    public required int Pid { get; init; }

    /// <summary>Version of the atlas CLI emitting the stream.</summary>
    [JsonPropertyName("atlasVersion")]
    public required string AtlasVersion { get; init; }
}
