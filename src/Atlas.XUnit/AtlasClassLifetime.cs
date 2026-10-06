using Atlas.XUnit.Internal;
using Xunit;

namespace Atlas.XUnit;

/// <summary>The signal that tells Atlas a scenario class has ended, so its server is released
/// inside the test run instead of when the process exits. xUnit disposes a class fixture once,
/// after the last test of the class has run and before the runner reports the session as over;
/// <see cref="AtlasScenarioBase"/> declares this type as its class fixture, which is what ties
/// the two together. Under <c>dotnet test</c> the release has to happen there: vstest kills the
/// test host a hundred milliseconds after the session ends, and releasing a server takes about
/// a second, so the last class's scratch directory used to survive a green run.</summary>
/// <remarks>It is public only because xUnit builds class fixtures from public types. It holds no
/// state, has nothing to call, and a scenario class neither constructs it nor takes it as a
/// constructor parameter. The registry still owns the one live host (ADR 0002); this type only
/// tells it when a class is over. The scratch directory of a green class is deleted by the next
/// boot or by the test host as it exits, and <c>dotnet test</c> can return before that process is
/// gone: a script that counts the directories right after it returns can still see some for a few
/// seconds, so it should wait for the test host process to exit, or retry the count.</remarks>
public sealed class AtlasClassLifetime : IAsyncLifetime
{
    /// <inheritdoc/>
    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    Task IAsyncLifetime.DisposeAsync() => HostRegistry.ReleaseAtClassEndAsync();
}
