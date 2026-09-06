using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Atlas.Internal.Hosting;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace Atlas.Pure.Tests.Bootstrap;

/// <summary>Per-version contract net for every engine shape Atlas resolves by reflection: loads
/// one real install's two engine assemblies into a collectible <see cref="AssemblyLoadContext"/>
/// and runs the resolvers against the loaded types, with no server boot. One row per install
/// (see <see cref="CompatInstallsAttribute"/>), about a second each.</summary>
/// <remarks><para>This is the cheap half of the compatibility promise: it catches SHAPE drift
/// (the issue #49 class - a renamed field, a member that turned from a field into a property, a
/// Stop overload that changed signature) on every supported version at pure-suite speed, from a
/// single Atlas binary. It can never catch a BEHAVIOURAL change, so it complements the E2E
/// matrix rather than replacing any leg of it.</para>
/// <para>Two rules the probe that designed this test had to learn: the engine types must come
/// from the install's own load context, never from <c>typeof</c> (which would resolve to the
/// compile-time install and make every row assert the same shape twice); and that includes the
/// <c>fieldType</c> argument of <see cref="EngineCompat.ResolveNonPublicInstanceField"/> for
/// engine-owned types, while framework-owned ones (<see cref="int"/>,
/// <see cref="Dictionary{TKey, TValue}"/>, <see cref="Queue{T}"/>) are shared with the default
/// context and can stay as <c>typeof</c>.</para></remarks>
public class EngineContractTests
{
    /// <summary>What the resolver's own fail-fast message would say; the contract net only needs
    /// the resolution to succeed, so one placeholder serves every call.</summary>
    private const string Consequence = "checked by the per-version engine contract test.";

    [Theory]
    [CompatInstalls]
    public void Resolvers_Should_BindEveryAdaptedShape_When_RunAgainstARealInstall(string install)
    {
        // A listed install that holds no engine assemblies fails here rather than vanishing from
        // the matrix: whoever set the variable asked for this version to be checked.
        Assert.True(
            CompatInstallsAttribute.HoldsEngineAssemblies(install),
            $"{CompatInstallsAttribute.ListVariable} lists '{install}', which holds no engine dlls.");

        var engine = new EngineInstallContext(install);
        try
        {
            AssertEveryShapeResolves(engine);
        }
        finally
        {
            engine.Unload();
        }
    }

    /// <summary>Runs every <see cref="EngineCompat"/> and signal resolver against one install's
    /// loaded types.</summary>
    /// <param name="engine">The install's load context.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AssertEveryShapeResolves(EngineInstallContext engine)
    {
        Type gameVersionType = engine.Type("Vintagestory.API.Config.GameVersion");

        // The whole point of the load context: these are the install's types, not the ones this
        // assembly was compiled against (same file for the VINTAGE_STORY row, different identity).
        Assert.NotSame(typeof(Vintagestory.API.Config.GameVersion), gameVersionType);

        string version = EngineCompat.ReadVersionConstant(gameVersionType, "ShortGameVersion");
        Assert.NotEmpty(EngineCompat.ReadVersionConstant(gameVersionType, "NetworkVersion"));
        EngineCompat.CheckSupportedFloor(version);

        // The exit lifecycle: exitState/GameExitState on 1.22+, exit/GameExit before.
        Type serverType = engine.Type("Vintagestory.Server.ServerMain");
        FieldInfo exitField = EngineCompat.ResolveExitStateField(serverType, version);
        Assert.Contains(exitField.Name, new[] { "exitState", "exit" });
        Assert.NotNull(EngineCompat.StopBinding.Resolve(serverType, version));

        // Playing is 3 before 1.22 and 4 since (Admitted was inserted ahead of it).
        Assert.NotNull(EngineCompat.ParseEnumMember(
            engine.Type("Vintagestory.API.Server.EnumClientState"), "Playing", version, Consequence));

        // Fields before 1.22, properties since.
        Type entityType = engine.Type("Vintagestory.API.Common.Entities.Entity");
        Assert.NotNull(EngineCompat.ResolveInstanceReader(entityType, "Pos", version, Consequence));
        Assert.NotNull(EngineCompat.ResolveInstanceReader(entityType, "ServerPos", version, Consequence));

        Type channelType = engine.Type("Vintagestory.Common.NetworkChannelBase");
        AssertField(channelType, "channelId", typeof(int), version);
        AssertField(channelType, "messageTypes", typeof(Dictionary<Type, int>), version);

        // The dummy connection's inbound queue, reached through an engine-owned field type: it
        // must come from this context, which is why the resolver takes the type as an argument.
        Type dummyNetworkType = engine.Type("Vintagestory.Common.DummyNetwork");
        AssertField(engine.Type("Vintagestory.Server.DummyTcpNetServer"), "network", dummyNetworkType, version);
        AssertField(dummyNetworkType, "ServerReceiveBuffer", typeof(Queue<object>), version);

        // The two signals the host degrades on rather than failing: the assets-build box and the
        // entity-simulation tick stamp. Their live shells resolve the owning member first, so the
        // row pins that member too.
        Assert.NotNull(AssetsBuildSignal.ResolveBoxFields(NonPublicField(serverType, "serverAssetsPacket").FieldType));
        Assert.NotNull(NonPublicField(serverType, "Systems"));
        Assert.NotNull(SimulationTickSignal.ResolveStampField(
            engine.Type("Vintagestory.Server." + SimulationTickSignal.EntitySimulationTypeName)));

        // Rollback's three internals, resolved by the same EngineCompat resolvers as the rest.
        // Nothing else checks them: their absence does not fail a boot, it silently degrades
        // rollback to a full host recycle on whichever version drifted.
        Type chunkThreadType = engine.Type(typeof(ChunkServerThread).FullName!);
        AssertField(serverType, "chunkThread", chunkThreadType, version);
        AssertField(chunkThreadType, "gameDatabase", engine.Type(typeof(GameDatabase).FullName!), version);

        // The binder behind a name-only lookup widens, so it would also hand back a method taking
        // an int where rollback passes a long; the resolver compares the signature it picked.
        Type[] discardSignature =
        [
            typeof(long),
            engine.Type(typeof(ChunkPos).FullName!),
            engine.Type(typeof(ServerChunk).FullName!),
            typeof(List<>).MakeGenericType(engine.Type(typeof(ServerChunkWithCoord).FullName!)),
            serverType,
        ];
        Assert.NotNull(EngineCompat.ResolveStaticMethod(
            engine.Type("Vintagestory.Server.ServerSystemUnloadChunks"),
            "TryUnloadChunk",
            discardSignature,
            version,
            Consequence));
    }

    private static void AssertField(Type declaring, string name, Type fieldType, string version)
        => Assert.NotNull(EngineCompat.ResolveNonPublicInstanceField(
            declaring, name, fieldType, version, Consequence));

    private static FieldInfo NonPublicField(Type declaring, string name)
    {
        FieldInfo? field = declaring.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.True(field != null, $"'{declaring.Name}.{name}' is gone from this engine.");
        return field!;
    }

    /// <summary>Loads one install's assemblies in isolation from the install the suite was
    /// compiled against: everything the install ships (its root and its <c>Lib</c> folder) is
    /// loaded here, and only framework assemblies fall through to the default context.</summary>
    private sealed class EngineInstallContext : AssemblyLoadContext
    {
        private readonly string _install;
        private readonly Assembly[] _engine;

        public EngineInstallContext(string install)
            : base(isCollectible: true)
        {
            _install = install;
            _engine =
            [
                LoadFromAssemblyPath(Path.Combine(install, "VintagestoryAPI.dll")),
                LoadFromAssemblyPath(Path.Combine(install, "VintagestoryLib.dll")),
            ];
        }

        /// <summary>Resolves one engine type by full name from this install.</summary>
        /// <param name="fullName">The type's namespace-qualified name.</param>
        /// <returns>The loaded type.</returns>
        public Type Type(string fullName)
        {
            Type? type = _engine.Select(assembly => assembly.GetType(fullName)).FirstOrDefault(t => t != null);
            Assert.True(type != null, $"Engine type '{fullName}' is gone from '{_install}'.");
            return type!;
        }

        /// <inheritdoc/>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? path = new[] { _install, Path.Combine(_install, "Lib") }
                .Select(dir => Path.Combine(dir, assemblyName.Name + ".dll"))
                .FirstOrDefault(File.Exists);
            return path == null ? null : LoadFromAssemblyPath(path);
        }
    }
}
