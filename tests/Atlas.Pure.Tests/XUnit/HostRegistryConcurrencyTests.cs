namespace Atlas.Pure.Tests.XUnit;

using System.Reflection;
using Atlas.Api;
using Atlas.XUnit.Internal;

/// <summary>Exercises <see cref="HostRegistry"/>'s concurrent-request guard without booting a real
/// embedded server. <c>GetOrCreateAsync</c>/<c>RecycleAsync</c> both delegate their mutual-exclusion
/// check to the private <c>EnterExclusive</c>/<c>ExitExclusive</c> pair before ever touching
/// <c>ServerHost</c>; reflecting directly onto that pair exercises the exact guard those methods run,
/// without needing two live server boots.</summary>
public class HostRegistryConcurrencyTests
{
    [Fact]
    public void EnterExclusive_Should_ThrowAtlasSetupException_When_AlreadyBusy()
    {
        Type registryType = typeof(HostRegistry);
        MethodInfo enter = registryType.GetMethod("EnterExclusive", BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo exit = registryType.GetMethod("ExitExclusive", BindingFlags.NonPublic | BindingFlags.Static)!;

        enter.Invoke(null, null);
        try
        {
            TargetInvocationException wrapped = Assert.Throws<TargetInvocationException>(
                () => enter.Invoke(null, null));
            Assert.IsType<AtlasSetupException>(wrapped.InnerException);
        }
        finally
        {
            exit.Invoke(null, null);
        }
    }
}
