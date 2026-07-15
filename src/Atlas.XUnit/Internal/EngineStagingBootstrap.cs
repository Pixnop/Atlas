using System.Runtime.CompilerServices;
using Atlas.Internal.Bootstrap;

namespace Atlas.XUnit.Internal;

/// <summary>Runs the engine-assembly staging preflight (issue #49) the moment any Atlas.XUnit
/// code first executes, which in a test process is xUnit DISCOVERY: instantiating
/// <see cref="AtlasScenarioDiscoverer"/>/<see cref="AtlasTheoryDiscoverer"/> for the first
/// <c>[AtlasScenario]</c> the runner encounters. Discovery reflects over attributes and never
/// JITs a test body, so at that moment no consumer code referencing VintagestoryAPI types has
/// been compiled yet and the test output's copy is still unbound and rewritable (measured: at
/// this trigger the assembly is not in the load context yet, while the first scenario method's
/// JIT is already too late). Atlas's own module initializer covers flows that execute Atlas code
/// before touching engine types; this one covers the common flow where a scenario body touches
/// engine types before any Atlas method runs.</summary>
internal static class EngineStagingBootstrap
{
    /// <summary>Stages the test output against the VINTAGE_STORY install, best-effort and
    /// exception-free (see <see cref="EngineStager.TryStageEarly()"/>).</summary>
#pragma warning disable CA2255 // Module initializer is the deliberate mechanism: the whole point
    // is running before the test runner JITs any consumer method that binds VintagestoryAPI.
    [ModuleInitializer]
    internal static void TryStageAtDiscovery() => EngineStager.TryStageEarly();
#pragma warning restore CA2255
}
