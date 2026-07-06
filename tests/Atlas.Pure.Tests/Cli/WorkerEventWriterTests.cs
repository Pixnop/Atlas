using System.Text.Json;
using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class WorkerEventWriterTests
{
    private static readonly WorkerEvent[] OneOfEach =
    [
        new RunStartEvent { Assembly = "/tmp/S.dll", Classes = ["Ns.A", "Ns.B"], Pid = 42, AtlasVersion = "0.5.0" },
        new DiscoveredEvent { Class = "Ns.A", Test = "Ns.A.Scenario_One" },
        new ClassStartEvent { Class = "Ns.A" },
        new TestPassEvent { Class = "Ns.A", Test = "Ns.A.Scenario_One", DurationMs = 1234 },
        new TestFailEvent { Class = "Ns.A", Test = "Ns.A.Scenario_Two", DurationMs = 5, Message = "X: boom", Stack = "at Ns.A" },
        new TestSkipEvent { Class = "Ns.A", Test = "Ns.A.Scenario_Three", Reason = "later" },
        new ClassEndEvent { Class = "Ns.A", Passed = 1, Failed = 1, Skipped = 1 },
        new ErrorEvent { Message = "Y: fixture crashed" },
        new RunEndEvent { Total = 3, Passed = 1, Failed = 1, Skipped = 1, Errors = 0, WallClockMs = 6789, ExitCode = 1 },
    ];

    [Fact]
    public void Serialize_Should_EmitVersionAndTypeFirst_When_GivenAnyEventType()
    {
        foreach (WorkerEvent evt in OneOfEach)
        {
            string line = WorkerEventWriter.Serialize(evt);

            Assert.StartsWith($"{{\"v\":1,\"type\":\"{evt.Type}\"", line);
        }
    }

    [Fact]
    public void Serialize_Should_ProduceSingleLineJson_When_GivenAnyEventType()
    {
        foreach (WorkerEvent evt in OneOfEach)
        {
            string line = WorkerEventWriter.Serialize(evt);

            Assert.DoesNotContain('\n', line);
            using var document = JsonDocument.Parse(line);
            Assert.Equal(1, document.RootElement.GetProperty("v").GetInt32());
            Assert.Equal(evt.Type, document.RootElement.GetProperty("type").GetString());
        }
    }

    [Fact]
    public void Serialize_Should_CoverEveryConcreteEventType_When_TheProtocolGrows()
    {
        // Guard: OneOfEach must stay exhaustive, so the invariants above hold for new events too.
        List<Type> concreteEventTypes = typeof(WorkerEvent).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(WorkerEvent)) && !type.IsAbstract)
            .ToList();

        Assert.Equal(
            concreteEventTypes.OrderBy(type => type.Name),
            OneOfEach.Select(evt => evt.GetType()).Distinct().OrderBy(type => type.Name));
    }

    [Fact]
    public void Serialize_Should_RoundTrip_When_DeserializedIntoTheSameRecord()
    {
        var original = new TestFailEvent
        {
            Class = "Ns.A",
            Test = "Ns.A.Scenario_Two",
            DurationMs = 5,
            Message = "X: boom",
            Stack = "at Ns.A.Scenario_Two()",
        };

        string line = WorkerEventWriter.Serialize(original);
        TestFailEvent? parsed = JsonSerializer.Deserialize<TestFailEvent>(line);

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Serialize_Should_RoundTripRunEnd_When_DeserializedIntoTheSameRecord()
    {
        var original = new RunEndEvent { Total = 2, Passed = 1, Failed = 1, Skipped = 0, Errors = 0, WallClockMs = 99, ExitCode = 1 };

        Assert.Equal(original, JsonSerializer.Deserialize<RunEndEvent>(WorkerEventWriter.Serialize(original)));
    }

    [Fact]
    public void Serialize_Should_OmitStack_When_FailureHasNone()
    {
        var evt = new TestFailEvent { Class = "Ns.A", Test = "t", DurationMs = 1, Message = "X: boom", Stack = null };

        Assert.DoesNotContain("stack", WorkerEventWriter.Serialize(evt));
    }

    [Fact]
    public void Serialize_Should_EmitNullClasses_When_RunCoversTheWholeAssembly()
    {
        var evt = new RunStartEvent { Assembly = "S.dll", Classes = null, Pid = 1, AtlasVersion = "x" };

        using var document = JsonDocument.Parse(WorkerEventWriter.Serialize(evt));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("classes").ValueKind);
    }

    [Fact]
    public void Write_Should_EmitOneLinePerEvent_When_GivenABatch()
    {
        var output = new StringWriter();
        var writer = new WorkerEventWriter(output);

        writer.WriteAll(OneOfEach);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(OneOfEach.Length, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line).Dispose());
    }
}
