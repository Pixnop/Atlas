using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>A failure outside any single scenario: a runner-level error (whatever xUnit
/// reports through <c>AssemblyRunner.OnErrorMessage</c>, such as a class or collection that
/// threw outside a scenario body) or an environment error that prevented the run.</summary>
internal sealed record ErrorEvent() : WorkerEvent("error")
{
    /// <summary>The error message, prefixed with the exception type when one exists.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}
