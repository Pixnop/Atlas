using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>The per-class isolation summary of a scenario class whose host was handed off
/// (rollback/restart counts and their measured costs). Emitted between the class's last
/// <c>test-*</c> line and its <c>class-end</c>, and only when the class requested rollback or
/// restart isolation at least once. Additive protocol event (no <c>v</c> bump): consumers that
/// predate it ignore it by contract.</summary>
internal sealed record ClassSummaryEvent() : WorkerEvent("class-summary")
{
    /// <summary>Fully qualified name of the scenario class.</summary>
    [JsonPropertyName("class")]
    public required string Class { get; init; }

    /// <summary>The formatted isolation summary line, identical to the stderr line plain runs
    /// print (starts with "[Atlas] isolation summary for ...").</summary>
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }
}
