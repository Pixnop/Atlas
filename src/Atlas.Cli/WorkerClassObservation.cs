using System.Text.Json;

namespace Atlas.Cli;

/// <summary>What the orchestrator saw on one worker's stdout: parses each JSONL protocol line as
/// it arrives (returning test outcomes so the shell can print them live) and remembers the facts
/// crash translation needs afterwards (did a run-end arrive, which errors were reported). Pure;
/// not thread-safe by design, since a worker's stdout is pumped by a single reader.</summary>
internal sealed class WorkerClassObservation
{
    private readonly List<TestOutcome> _tests = [];
    private readonly List<string> _errors = [];

    /// <summary>Gets the scenario outcomes reported so far.</summary>
    public IReadOnlyList<TestOutcome> Tests => _tests;

    /// <summary>Gets the runner-level error messages reported so far.</summary>
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Gets a value indicating whether a run-end line arrived. Per the protocol, a
    /// stream without one means the worker process died mid-run.</summary>
    public bool SawRunEnd { get; private set; }

    /// <summary>Gets a value indicating whether any reported scenario failed.</summary>
    public bool SawFailure => _tests.Exists(test => test.Kind == TestOutcomeKind.Failed);

    /// <summary>Accepts one stdout line from the worker.</summary>
    /// <param name="line">The raw line.</param>
    /// <returns>The scenario outcome the line carried, when it was a test event; null for every
    /// other event and for lines that are not protocol events at all (stdout is protocol-only by
    /// contract, but a lenient consumer survives stray noise and unknown future event types).</returns>
    public TestOutcome? AcceptLine(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return Accept(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private TestOutcome? Accept(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        switch (StringOf(root, "type"))
        {
            case "test-pass":
                return Add(root, TestOutcomeKind.Passed, message: null);
            case "test-fail":
                return Add(root, TestOutcomeKind.Failed, StringOf(root, "message") ?? "unknown failure");
            case "test-skip":
                return Add(root, TestOutcomeKind.Skipped, StringOf(root, "reason") ?? "no reason given");
            case "error":
                _errors.Add(StringOf(root, "message") ?? "unknown worker error");
                return null;
            case "run-end":
                SawRunEnd = true;
                return null;
            default:
                return null;
        }
    }

    private TestOutcome Add(JsonElement root, TestOutcomeKind kind, string? message)
    {
        var outcome = new TestOutcome(
            StringOf(root, "class") ?? string.Empty,
            StringOf(root, "test") ?? string.Empty,
            kind,
            LongOf(root, "durationMs"),
            message,
            StringOf(root, "stack"));
        _tests.Add(outcome);
        return outcome;
    }

    private static string? StringOf(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long LongOf(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out long number)
            ? number
            : 0;
}
