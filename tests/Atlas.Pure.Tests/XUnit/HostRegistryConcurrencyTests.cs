namespace Atlas.Pure.Tests.XUnit;

using Atlas.Api;
using Atlas.XUnit.Internal;

/// <summary>Exercises <see cref="HostRegistry"/>'s concurrent-request guard without booting a real
/// embedded server. <c>GetOrCreateAsync</c>/<c>RecycleAsync</c> both delegate their mutual-exclusion
/// check to the internal <c>EnterExclusive</c>/<c>ExitExclusive</c> pair before ever touching
/// <c>ServerHost</c>; calling that pair directly exercises the exact guard those methods run,
/// without needing two live server boots. The collection serializes this class with every other
/// test touching the registry's process-wide gate (e.g. the fixture-harvest tests), which would
/// otherwise observe the gate held busy here and fail spuriously.</summary>
[Collection("HostRegistry")]
public class HostRegistryConcurrencyTests
{
    [Fact]
    public void EnterExclusive_Should_ThrowAtlasSetupException_When_AlreadyBusy()
    {
        HostRegistry.EnterExclusive();
        try
        {
            var ex = Assert.Throws<AtlasSetupException>(HostRegistry.EnterExclusive);

            // The message has to name the fix, because the symptom (a second scenario class
            // starting mid-boot) says nothing about parallelization.
            Assert.Contains("DisableTestParallelization", ex.Message);
        }
        finally
        {
            HostRegistry.ExitExclusive();
        }
    }

    [Fact]
    public void ExitExclusive_Should_ReleaseTheGate_When_TheRequestIsDone()
    {
        HostRegistry.EnterExclusive();
        HostRegistry.ExitExclusive();

        HostRegistry.EnterExclusive();
        HostRegistry.ExitExclusive();
    }
}
