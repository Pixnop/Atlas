using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>A failure outside any single scenario: a runner-level error (crashed fixture,
/// unhandled collection exception) or an environment error that prevented the run.</summary>
internal sealed record ErrorEvent() : WorkerEvent("error")
{
    /// <summary>The error message, prefixed with the exception type when one exists.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
