using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Atlas.Api;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Atlas.Internal.Bootstrap;

/// <summary>Single owner of every engine touchpoint whose shape varies across the supported game
/// versions (1.20.x through 1.22.x): the server exit lifecycle, the <c>GameVersion</c>
/// constants (which must be read from the loaded assembly's metadata, never compiled in), the
/// <c>EnumClientState.Playing</c> value (enum members are compile-time constants too, and its
/// position shifted in 1.22), and the <c>Entity.Pos</c>/<c>ServerPos</c> accessors (fields
/// before 1.22, properties since, so direct member access binds to only one shape).</summary>
/// <remarks><para>The exit lifecycle is the entire compile-level gap below 1.22 (measured in
/// docs/specs/2026-07-12-pre-122-compat.md): 1.22 has <c>ServerMain.exitState</c> of type
/// <c>GameExitState</c> and <c>Stop(string, EnumExitMode, ...)</c>, while 1.21/1.20 have
/// <c>ServerMain.exit</c> of type <c>GameExit</c> and
/// <c>Stop(string, string = null, EnumLogType = Notification)</c>. Both engines leave the exit
/// holder null on an embedded boot and dereference it from the packet parser thread, so Atlas
/// installs a fresh holder into whichever field the loaded engine has, before <c>PreLaunch()</c>.</para>
/// <para><c>GameVersion.NetworkVersion</c>/<c>ShortGameVersion</c> are <c>const</c>: the C#
/// compiler bakes the values of the referenced install into Atlas's own IL, and the server
/// hard-rejects a network-version mismatch on every join. Reading the loaded assembly's raw
/// constant metadata instead keeps a single Atlas binary honest on every supported engine.</para>
/// <para>Handles are resolved once per process (the embedded engine cannot change mid-process)
/// and validated at boot by <see cref="ValidateAtBoot"/>: a version whose layout drifted fails
/// fast with the game version and the missing symbol named, never mid-scenario. Source rule that
/// keeps the single binary working: outside this class, Atlas source never mentions an engine
/// type or member that does not exist on every supported version.</para></remarks>
internal static class EngineCompat
{
    private static readonly Lazy<string> LazyShortGameVersion =
        new(() => ReadVersionConstant(typeof(GameVersion), "ShortGameVersion"));

    private static readonly Lazy<string> LazyNetworkVersion =
        new(() => ReadVersionConstant(typeof(GameVersion), "NetworkVersion"));

    private static readonly Lazy<FieldInfo> LazyExitStateField =
        new(() => ResolveExitStateField(typeof(ServerMain), ShortGameVersion));

    private static readonly Lazy<StopBinding> LazyStop =
        new(() => StopBinding.Resolve(typeof(ServerMain), ShortGameVersion));

    private static readonly Lazy<EnumClientState> LazyClientStatePlaying = new(() =>
        (EnumClientState)ParseEnumMember(
            typeof(EnumClientState),
            "Playing",
            ShortGameVersion,
            "Atlas cannot recognize when a joined test player reaches the Playing client state."));

    private static readonly Lazy<Func<object, object?>> LazyEntityServerPosReader = new(() =>
        ResolveInstanceReader(
            typeof(Entity),
            "ServerPos",
            ShortGameVersion,
            "Atlas cannot position spawned entities before the engine registers them."));

    private static readonly Lazy<Func<object, object?>> LazyEntityPosReader = new(() =>
        ResolveInstanceReader(
            typeof(Entity),
            "Pos",
            ShortGameVersion,
            "Atlas cannot mirror a spawned entity's client-side position."));

    /// <summary>Gets the loaded engine's <c>GameVersion.ShortGameVersion</c>, read from assembly
    /// metadata at run time (see the const trap in the class remarks).</summary>
    public static string ShortGameVersion => LazyShortGameVersion.Value;

    /// <summary>Gets the loaded engine's <c>GameVersion.NetworkVersion</c>, read from assembly
    /// metadata at run time (see the const trap in the class remarks).</summary>
    public static string NetworkVersion => LazyNetworkVersion.Value;

    /// <summary>Gets the loaded engine's <c>EnumClientState.Playing</c> VALUE, resolved by name
    /// at run time: 1.22 inserted <c>Admitted</c> before it (Offline, Connecting, Admitted,
    /// Connected, Playing, Queued), shifting Playing from 3 (1.20.x/1.21.x) to 4, and the C#
    /// compiler bakes enum values into the referencing assembly's IL exactly like the
    /// <c>GameVersion</c> consts. A prebuilt Atlas comparing a client's state against its
    /// compiled-in value therefore misreads the join lifecycle on the other engine line
    /// (measured on the issue #49 cross-install run: every join-dependent scenario of a
    /// 1.22.3-built suite timed out in <c>WaitForPlaying</c> on 1.21.7, comparing that engine's
    /// Queued against Playing).</summary>
    public static EnumClientState ClientStatePlaying => LazyClientStatePlaying.Value;

    /// <summary>Reads <paramref name="entity"/>'s server-side <c>EntityPos</c> through whichever
    /// member shape the loaded engine has: 1.22 turned the <c>Pos</c>/<c>ServerPos</c> FIELDS
    /// into properties (<c>ServerPos =&gt; Pos</c>, one instance), so a direct member access
    /// compiles against every version but binds to only one shape, and a prebuilt binary dies
    /// with a <c>MissingMethodException</c>/<c>MissingFieldException</c> on the other line
    /// (measured on the issue #49 cross-install run). <c>SidedPos</c>, a property on every
    /// supported version, is the right surface for SPAWNED entities; this accessor exists for
    /// the pre-registration window where <c>SidedPos</c> is unusable (it dereferences
    /// <c>entity.World</c>, unset until <c>SpawnEntity</c>, on pre-1.22 engines).</summary>
    /// <param name="entity">The entity to read.</param>
    /// <returns>The server-side position instance (the one shared instance on 1.22).</returns>
    public static EntityPos ServerPosOf(Entity entity) => (EntityPos)LazyEntityServerPosReader.Value(entity)!;

    /// <summary>Reads <paramref name="entity"/>'s <c>Entity.Pos</c> through whichever member
    /// shape the loaded engine has (see <see cref="ServerPosOf"/> for the field-to-property
    /// trap): pre-1.22 it is a separate instance that nothing updates for a headless entity
    /// until it is explicitly mirrored from the server-side position; on 1.22 it is the same
    /// instance <see cref="ServerPosOf"/> returns.</summary>
    /// <param name="entity">The entity to read.</param>
    /// <returns>The client-side position instance.</returns>
    public static EntityPos PosOf(Entity entity) => (EntityPos)LazyEntityPosReader.Value(entity)!;

    /// <summary>Validates, before any engine state is touched, that the loaded engine is at or
    /// above the supported floor and exposes every member this shim adapts.</summary>
    /// <exception cref="AtlasSetupException">Thrown when the game version is below the supported
    /// floor, or when an exit-lifecycle member has an unknown shape; the message names the game
    /// version and the missing symbol.</exception>
    public static void ValidateAtBoot()
    {
        CheckSupportedFloor(ShortGameVersion);
        _ = NetworkVersion;
        _ = LazyExitStateField.Value;
        _ = LazyStop.Value;
        _ = LazyClientStatePlaying.Value;
        _ = LazyEntityServerPosReader.Value;
        _ = LazyEntityPosReader.Value;
    }

    /// <summary>Installs a fresh exit-state holder into the loaded engine's exit field
    /// (<c>exitState</c> on 1.22+, <c>exit</c> before), the null-by-default object the packet
    /// parser thread dereferences from boot. Must run before <c>PreLaunch()</c>.</summary>
    /// <param name="server">The just-constructed, not yet pre-launched server.</param>
    public static void InstallExitState(ServerMain server)
    {
        FieldInfo field = LazyExitStateField.Value;
        field.SetValue(server, Activator.CreateInstance(field.FieldType));
    }

    /// <summary>Stops the embedded server through whichever <c>Stop</c> signature the loaded
    /// engine has: <c>Stop(reason, EnumExitMode.SoftExit, ...)</c> on 1.22+, the pre-1.22
    /// <c>Stop(reason, finalLogMessage, finalLogType)</c> with its declared defaults otherwise.</summary>
    /// <param name="server">The live server to stop.</param>
    /// <param name="reason">The stop reason, logged by the engine.</param>
    public static void Stop(ServerMain server, string reason) => LazyStop.Value.Invoke(server, reason);

    /// <summary>Reads one <c>GameVersion</c> string constant from the loaded assembly's metadata
    /// (<see cref="FieldInfo.GetRawConstantValue"/>), so the value is the loaded engine's, not
    /// the compile-time install's. Accepts a static readonly string too, for forks that
    /// de-const the field.</summary>
    /// <param name="gameVersionType">The loaded engine's <c>GameVersion</c> type.</param>
    /// <param name="fieldName">The constant's field name.</param>
    /// <returns>The constant's value on the loaded engine.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the field is missing or not a string.</exception>
    internal static string ReadVersionConstant(Type gameVersionType, string fieldName)
    {
        FieldInfo? field = gameVersionType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
        object? value = field switch
        {
            null => null,
            { IsLiteral: true } => field.GetRawConstantValue(),
            _ => field.GetValue(null),
        };
        return value as string ?? throw new AtlasSetupException(
            $"Engine constant '{gameVersionType.Name}.{fieldName}' was not found as a string on the " +
            $"loaded engine assembly ('{gameVersionType.Assembly.Location}'): the engine layout " +
            "changed and Atlas cannot read the loaded game version.");
    }

    /// <summary>Resolves one enum member's VALUE on the loaded engine's enum type, by name.
    /// The C# compiler bakes enum values into the referencing assembly's IL, so any member
    /// whose position differs across supported versions must be resolved here at run time and
    /// never compared against a compiled-in value.</summary>
    /// <param name="enumType">The loaded engine's enum type.</param>
    /// <param name="memberName">The member to resolve.</param>
    /// <param name="gameVersion">The loaded game version, for the fail-fast message.</param>
    /// <param name="consequence">What Atlas cannot do without the member, appended to the
    /// fail-fast message.</param>
    /// <returns>The member's value on the loaded engine.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the member does not exist on the
    /// loaded enum.</exception>
    internal static object ParseEnumMember(Type enumType, string memberName, string gameVersion, string consequence)
    {
        if (Enum.TryParse(enumType, memberName, out object? value) && value != null)
        {
            return value;
        }

        throw new AtlasSetupException(
            $"Engine enum '{enumType.Name}' has no '{memberName}' member on game version " +
            $"{gameVersion}: {consequence}");
    }

    /// <summary>Resolves a reader for one public instance member that changed between a field
    /// and a property across supported versions (the <c>Entity.Pos</c>/<c>ServerPos</c> case):
    /// direct member access compiles against both shapes but the emitted IL binds to only one,
    /// so any such member must be read through this resolver. Prefers the property shape (the
    /// newer engines') when both exist.</summary>
    /// <param name="type">The loaded engine's declaring type.</param>
    /// <param name="memberName">The member to resolve.</param>
    /// <param name="gameVersion">The loaded game version, for the fail-fast message.</param>
    /// <param name="consequence">What Atlas cannot do without the member, appended to the
    /// fail-fast message.</param>
    /// <returns>A reader over instances of <paramref name="type"/>.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the member exists as neither a public
    /// instance property nor a public instance field.</exception>
    internal static Func<object, object?> ResolveInstanceReader(
        Type type, string memberName, string gameVersion, string consequence)
    {
        PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetMethod is { } getter)
        {
            return instance => getter.Invoke(instance, null);
        }

        FieldInfo? field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (field != null)
        {
            return field.GetValue;
        }

        throw new AtlasSetupException(
            $"Engine member '{type.Name}.{memberName}' was not found as a public instance " +
            $"property or field on game version {gameVersion}: {consequence}");
    }

    /// <summary>Rejects engines below the supported floor up front, with the floor named, instead
    /// of letting the boot die later on the pre-1.20 API differences (the <c>PreLaunch</c> and
    /// <c>ServerProgramArgs</c> signature forks reflection cannot bridge). Unrecognized version
    /// schemes (forks) are let through: the member-shape validation is the authority for them.</summary>
    /// <param name="shortGameVersion">The loaded engine's short game version, e.g. "1.21.7".</param>
    /// <exception cref="AtlasSetupException">Thrown when the version parses below 1.20.</exception>
    internal static void CheckSupportedFloor(string shortGameVersion)
    {
        string[] parts = shortGameVersion.Split('.');
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            return;
        }

        if (major > 1 || (major == 1 && minor >= 20))
        {
            return;
        }

        throw new AtlasSetupException(
            $"Vintage Story {shortGameVersion} is below the supported floor: Atlas supports 1.21.0 and " +
            "newer, plus 1.20.x best-effort (verified by the weekly compatibility sweep). Engines older " +
            "than 1.20 change the boot API itself (ServerMain.PreLaunch, ServerProgramArgs), which the " +
            "runtime compatibility shim cannot bridge; run against a supported game version.");
    }

    /// <summary>Resolves the engine's exit-state field: <c>exitState</c> (1.22+) preferred over
    /// <c>exit</c> (pre-1.22), and its holder type must be constructible for
    /// <see cref="InstallExitState"/>.</summary>
    /// <param name="serverType">The loaded engine's <c>ServerMain</c> type.</param>
    /// <param name="gameVersion">The loaded game version, for the fail-fast message.</param>
    /// <returns>The resolved field.</returns>
    /// <exception cref="AtlasSetupException">Thrown when neither field exists, or the holder type
    /// has no public parameterless constructor.</exception>
    internal static FieldInfo ResolveExitStateField(Type serverType, string gameVersion)
    {
        FieldInfo? field = serverType.GetField("exitState", BindingFlags.Public | BindingFlags.Instance)
            ?? serverType.GetField("exit", BindingFlags.Public | BindingFlags.Instance);
        if (field == null)
        {
            throw new AtlasSetupException(
                $"Engine field '{serverType.Name}.exitState' (1.22+) / '{serverType.Name}.exit' (pre-1.22) " +
                $"not found on game version {gameVersion}: the exit lifecycle changed shape and Atlas " +
                "cannot install the exit-state holder its embedded boot requires.");
        }

        if (field.FieldType.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new AtlasSetupException(
                $"Engine exit-state holder '{field.FieldType.Name}' (field " +
                $"'{serverType.Name}.{field.Name}') has no public parameterless constructor on game " +
                $"version {gameVersion}: Atlas cannot install it before boot.");
        }

        return field;
    }

    /// <summary>One resolved binding to the loaded engine's <c>ServerMain.Stop</c>: the method
    /// handle plus the pre-bound arguments after the reason (the 1.22+ <c>SoftExit</c> value, and
    /// <see cref="Type.Missing"/> for every optional parameter, which the default binder fills
    /// with the engine's own declared defaults).</summary>
    internal sealed class StopBinding
    {
        private readonly MethodInfo _method;
        private readonly object?[] _boundTail;

        private StopBinding(MethodInfo method, object?[] boundTail)
        {
            _method = method;
            _boundTail = boundTail;
        }

        /// <summary>Resolves the loaded engine's <c>Stop</c> into a binding: the 1.22+ shape (a
        /// required enum with a <c>SoftExit</c> member as the second parameter, optional tail) is
        /// preferred and bound with <c>SoftExit</c>; otherwise the pre-1.22 shape (reason plus an
        /// all-optional tail) is bound with the engine's declared defaults.</summary>
        /// <param name="serverType">The loaded engine's <c>ServerMain</c> type.</param>
        /// <param name="gameVersion">The loaded game version, for the fail-fast messages.</param>
        /// <returns>The resolved binding.</returns>
        /// <exception cref="AtlasSetupException">Thrown when no known <c>Stop</c> shape exists on
        /// <paramref name="serverType"/>, or its exit-mode enum lost the <c>SoftExit</c> member.</exception>
        internal static StopBinding Resolve(Type serverType, string gameVersion)
        {
            foreach (MethodInfo method in serverType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (method.Name != "Stop")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string))
                {
                    continue;
                }

                // 1.22+: Stop(string reason, EnumExitMode exitMode, ...all further optional...).
                if (parameters.Length >= 2
                    && !parameters[1].IsOptional
                    && parameters[1].ParameterType.IsEnum
                    && TailIsOptional(parameters, fromIndex: 2))
                {
                    object?[] tail = MissingTail(parameters.Length - 1);
                    tail[0] = EngineCompat.ParseEnumMember(
                        parameters[1].ParameterType,
                        "SoftExit",
                        gameVersion,
                        $"Atlas cannot request a soft exit through '{parameters[1].ParameterType.Name}'.");
                    return new StopBinding(method, tail);
                }

                // Pre-1.22: Stop(string reason, string finalLogMessage = null,
                // EnumLogType finalLogType = Notification) - every parameter after the reason
                // is optional, so the engine's own defaults are used.
                if (TailIsOptional(parameters, fromIndex: 1))
                {
                    return new StopBinding(method, MissingTail(parameters.Length - 1));
                }
            }

            throw new AtlasSetupException(
                $"No engine method '{serverType.Name}.Stop' with a known shape found on game version " +
                $"{gameVersion} (expected the 1.22+ 'Stop(string, EnumExitMode, ...)' or the pre-1.22 " +
                "'Stop(string, string = null, EnumLogType = Notification)'): the exit lifecycle changed " +
                "shape and Atlas cannot stop the embedded server cleanly.");
        }

        /// <summary>Invokes the bound <c>Stop</c> with <paramref name="reason"/>, filling every
        /// <see cref="Type.Missing"/> tail slot with the engine's declared default.</summary>
        /// <param name="server">The live server to stop.</param>
        /// <param name="reason">The stop reason.</param>
        internal void Invoke(object server, string reason)
        {
            object?[] args = new object?[_boundTail.Length + 1];
            args[0] = reason;
            Array.Copy(_boundTail, 0, args, 1, _boundTail.Length);
            try
            {
                _method.Invoke(server, BindingFlags.OptionalParamBinding, binder: null, args, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // Callers (ServerHost's teardown paths) must observe the engine's own exception,
                // exactly as the direct call sites this binding replaced did.
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        private static bool TailIsOptional(ParameterInfo[] parameters, int fromIndex)
        {
            for (int i = fromIndex; i < parameters.Length; i++)
            {
                if (!parameters[i].IsOptional)
                {
                    return false;
                }
            }

            return true;
        }

        private static object?[] MissingTail(int length)
        {
            object?[] tail = new object?[length];
            Array.Fill(tail, Type.Missing);
            return tail;
        }
    }
}
