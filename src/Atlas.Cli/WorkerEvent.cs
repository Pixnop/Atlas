using System.Text.Json.Serialization;

namespace Atlas.Cli;

/// <summary>Base of every worker protocol event: a single JSON line on the worker's stdout.
/// Every line carries the protocol version and a type discriminator first, so a consumer can
/// dispatch (and reject unknown versions) before looking at any payload field.</summary>
internal abstract record WorkerEvent
{
    /// <summary>Initializes a new instance of the <see cref="WorkerEvent"/> class with its wire
    /// type discriminator.</summary>
    /// <param name="type">The value of the "type" field on the wire.</param>
    protected WorkerEvent(string type) => Type = type;

    /// <summary>Protocol version of this line (currently always <see cref="WorkerProtocol.Version"/>).</summary>
    [JsonPropertyName("v")]
    [JsonPropertyOrder(-2)]
    public int V { get; init; } = WorkerProtocol.Version;

    /// <summary>The event type discriminator ("run-start", "test-pass", ...).</summary>
    [JsonPropertyName("type")]
    [JsonPropertyOrder(-1)]
    public string Type { get; }
}
