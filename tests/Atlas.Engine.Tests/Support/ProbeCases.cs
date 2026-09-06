using System.Reflection;
using Atlas.XUnit.Internal;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Atlas.Engine.Tests.Support;

/// <summary>Runs one private probe scenario through the real Atlas xUnit pipeline (case runner,
/// test runner, invoker, registry, host), spying on the xUnit message bus. A test cannot assert
/// its own output or its own failure, so the tests that pin what the pipeline reports drive a
/// nested case instead, the same technique as <see cref="GuineaPigRunner"/> but per test
/// case.</summary>
internal static class ProbeCases
{
    /// <summary>Runs one probe scenario and collects every message the runner reports.</summary>
    /// <param name="probeClass">The probe scenario class.</param>
    /// <param name="methodName">The scenario method to run.</param>
    /// <param name="strictIsolation">The strict-isolation flag of the synthetic test case.</param>
    /// <param name="freshWorld">Runs the case as FreshWorld instead of RollbackWorld.</param>
    /// <param name="restartWorld">Runs the case as RestartWorld instead of RollbackWorld.</param>
    /// <returns>The messages the pipeline queued, in order.</returns>
    public static async Task<IReadOnlyList<IMessageSinkMessage>> RunAsync(
        Type probeClass,
        string methodName,
        bool strictIsolation,
        bool freshWorld = false,
        bool restartWorld = false)
    {
        var diagnosticSink = new NullDiagnosticSink();
        var testCase = new AtlasTestCase(
            diagnosticSink,
            Xunit.Sdk.TestMethodDisplay.ClassAndMethod,
            Xunit.Sdk.TestMethodDisplayOptions.None,
            BuildTestMethod(probeClass, methodName),
            freshWorld: freshWorld,
            rollbackWorld: !restartWorld && !freshWorld,
            restartWorld: restartWorld,
            strictIsolation: strictIsolation,
            timeoutMs: 60_000);

        using var bus = new SpyMessageBus();
        await testCase.RunAsync(
            diagnosticSink, bus, Array.Empty<object>(), new ExceptionAggregator(), new CancellationTokenSource());
        return bus.Messages;
    }

    /// <summary>Builds the xUnit test-method object graph for a probe scenario, the same shape
    /// the real discoverer produces.</summary>
    /// <param name="probeClass">The probe scenario class.</param>
    /// <param name="methodName">The scenario method.</param>
    /// <returns>The test method.</returns>
    private static TestMethod BuildTestMethod(Type probeClass, string methodName)
    {
        MethodInfo method = probeClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"probe method '{methodName}' not found on '{probeClass}'");
        var testAssembly = new TestAssembly(Reflector.Wrap(probeClass.Assembly));
        var collection = new TestCollection(testAssembly, null, "Atlas probe scenarios");
        var testClass = new TestClass(collection, Reflector.Wrap(probeClass));
        return new TestMethod(testClass, Reflector.Wrap(method));
    }

    /// <summary>Message bus spy: collects every message the pipeline queues.</summary>
    private sealed class SpyMessageBus : IMessageBus
    {
        private readonly List<IMessageSinkMessage> _messages = [];

        public IReadOnlyList<IMessageSinkMessage> Messages => _messages;

        public bool QueueMessage(IMessageSinkMessage message)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }

            return true;
        }

        public void Dispose()
        {
            // Nothing to release; the spy only holds managed state.
        }
    }

    /// <summary>Diagnostic sink that swallows everything (the probes' diagnostics are noise).</summary>
    private sealed class NullDiagnosticSink : Xunit.Sdk.LongLivedMarshalByRefObject, IMessageSink
    {
        public bool OnMessage(IMessageSinkMessage message) => true;
    }
}
