using System.Reflection;
using Atlas.XUnit;
using Atlas.XUnit.Internal;
using NSubstitute;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.Pure.Tests.XUnit;

/// <summary>Round-trips <see cref="AtlasTestCase"/> through its xUnit serialization contract:
/// `dotnet test` serializes test cases between discovery and execution, so a flag that is not
/// carried (the historical trap for new attribute knobs) silently reverts to false on the
/// execution side.</summary>
public class AtlasTestCaseSerializationTests
{
    [Fact]
    public void SerializeDeserialize_Should_PreserveEveryIsolationFlag_When_RoundTripped()
    {
        var original = new AtlasTestCase(
            Substitute.For<IMessageSink>(),
            Xunit.Sdk.TestMethodDisplay.ClassAndMethod,
            Xunit.Sdk.TestMethodDisplayOptions.None,
            BuildTestMethod(),
            freshWorld: false,
            rollbackWorld: true,
            strictIsolation: true,
            timeoutMs: 1234);
        var data = new DictionarySerializationInfo();
        original.Serialize(data);

#pragma warning disable CS0618 // the parameterless ctor is the deserialization entry point
        var copy = new AtlasTestCase();
#pragma warning restore CS0618
        copy.Deserialize(data);

        Assert.False(copy.FreshWorld);
        Assert.True(copy.RollbackWorld);
        Assert.True(copy.StrictIsolation);
        Assert.Equal(1234, copy.TimeoutMs);
    }

    private static TestMethod BuildTestMethod()
    {
        MethodInfo method = typeof(SerializationProbeScenarios).GetMethod(
            nameof(SerializationProbeScenarios.Scenario_Should_Serialize))!;
        var testAssembly = new TestAssembly(Reflector.Wrap(typeof(SerializationProbeScenarios).Assembly));
        var collection = new TestCollection(testAssembly, null, "serialization probes");
        var testClass = new TestClass(collection, Reflector.Wrap(typeof(SerializationProbeScenarios)));
        return new TestMethod(testClass, Reflector.Wrap(method));
    }

    // Private on purpose: xUnit only discovers public classes, so this probe never runs as a
    // real scenario (running it would boot an embedded server inside the pure suite).
#pragma warning disable xUnit1000

    private sealed class SerializationProbeScenarios : AtlasScenarioBase
    {
        [AtlasScenario(RollbackWorld = true, StrictIsolation = true, TimeoutMs = 1234)]
        public Task Scenario_Should_Serialize() => Task.CompletedTask;
    }

#pragma warning restore xUnit1000

    /// <summary>Dictionary-backed <see cref="IXunitSerializationInfo"/>: the round trip only
    /// needs value fidelity, not xUnit's wire format.</summary>
    private sealed class DictionarySerializationInfo : IXunitSerializationInfo
    {
        private readonly Dictionary<string, object?> _values = [];

        public void AddValue(string key, object? value, Type? type = null) => _values[key] = value;

        public object? GetValue(string key, Type type) => _values.GetValueOrDefault(key);

        public T GetValue<T>(string key) => (T)GetValue(key, typeof(T))!;
    }
}
