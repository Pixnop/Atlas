using System.Reflection;

namespace Atlas.Pure.Tests.Bootstrap;

/// <summary>Tests for the pure resolution logic of <see cref="EngineCompat"/> over fake engine
/// shapes: the real reflective shell is exercised by every E2E boot (ValidateAtBoot,
/// InstallExitState and Stop run on each embedded host).</summary>
public class EngineCompatTests
{
    private enum FakeExitMode
    {
        None,
        SoftExit,
        FastExit,
    }

    private enum FakeLogType
    {
        Notification,
        Warning,
    }

    [Fact]
    public void ReadVersionConstant_Should_ReadConstFromMetadata()
    {
        Assert.Equal("9.9.9", EngineCompat.ReadVersionConstant(typeof(FakeGameVersion), "ShortGameVersion"));
        Assert.Equal("9.9.12", EngineCompat.ReadVersionConstant(typeof(FakeGameVersion), "NetworkVersion"));
    }

    [Fact]
    public void ReadVersionConstant_Should_AcceptStaticReadonlyString()
    {
        // Fork robustness: a fork that de-consts the field still reports its version.
        Assert.Equal("8.8.8", EngineCompat.ReadVersionConstant(typeof(FakeGameVersionReadonly), "ShortGameVersion"));
    }

    [Fact]
    public void ReadVersionConstant_Should_Throw_When_FieldMissing()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => EngineCompat.ReadVersionConstant(typeof(FakeGameVersion), "NoSuchConstant"));

        Assert.Contains("NoSuchConstant", ex.Message);
        Assert.Contains(nameof(FakeGameVersion), ex.Message);
    }

    [Fact]
    public void ReadVersionConstant_Should_Throw_When_ConstantIsNotAString()
    {
        Assert.Throws<AtlasSetupException>(
            () => EngineCompat.ReadVersionConstant(typeof(FakeGameVersion), "APIVersion"));
    }

    [Theory]
    [InlineData("1.20.0")]
    [InlineData("1.20.12")]
    [InlineData("1.21.7")]
    [InlineData("1.22.3")]
    [InlineData("1.23.0-rc.1")]
    [InlineData("2.0.0")]
    public void CheckSupportedFloor_Should_AcceptSupportedVersions(string version)
        => EngineCompat.CheckSupportedFloor(version);

    [Theory]
    [InlineData("1.19.8")]
    [InlineData("1.19.0-rc.2")]
    [InlineData("1.18.15")]
    [InlineData("0.9.9")]
    public void CheckSupportedFloor_Should_Throw_When_BelowFloor(string version)
    {
        var ex = Assert.Throws<AtlasSetupException>(() => EngineCompat.CheckSupportedFloor(version));

        // The message must cite the supported floor and the version that was rejected.
        Assert.Contains("1.21.0", ex.Message);
        Assert.Contains(version, ex.Message);
    }

    [Theory]
    [InlineData("fork-nightly")]
    [InlineData("v2")]
    [InlineData("one.two.three")]
    public void CheckSupportedFloor_Should_LetUnrecognizedSchemesThrough(string version)
    {
        // Forks with custom version strings are decided by the member-shape validation instead.
        EngineCompat.CheckSupportedFloor(version);
    }

    [Fact]
    public void ResolveExitStateField_Should_FindModernExitState()
    {
        FieldInfo field = EngineCompat.ResolveExitStateField(typeof(ModernExitServer), "1.22.3");

        Assert.Equal("exitState", field.Name);
        Assert.Equal(typeof(FakeExitHolder), field.FieldType);
    }

    [Fact]
    public void ResolveExitStateField_Should_FindLegacyExit()
    {
        FieldInfo field = EngineCompat.ResolveExitStateField(typeof(LegacyExitServer), "1.21.7");

        Assert.Equal("exit", field.Name);
    }

    [Fact]
    public void ResolveExitStateField_Should_PreferModernOverLegacy()
    {
        FieldInfo field = EngineCompat.ResolveExitStateField(typeof(BothExitServer), "1.22.3");

        Assert.Equal("exitState", field.Name);
    }

    [Fact]
    public void ResolveExitStateField_Should_Throw_When_NeitherFieldExists()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => EngineCompat.ResolveExitStateField(typeof(NoExitServer), "1.99.0"));

        Assert.Contains("exitState", ex.Message);
        Assert.Contains("exit", ex.Message);
        Assert.Contains("1.99.0", ex.Message);
    }

    [Fact]
    public void ResolveExitStateField_Should_Throw_When_HolderNotConstructible()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => EngineCompat.ResolveExitStateField(typeof(UnconstructibleExitServer), "1.99.0"));

        Assert.Contains(nameof(UnconstructibleHolder), ex.Message);
        Assert.Contains("1.99.0", ex.Message);
    }

    [Fact]
    public void StopBinding_Should_BindModernStopWithSoftExitAndEngineDefaults()
    {
        // The live 1.22.3 shape: Stop(string, EnumExitMode, string = null, EnumLogType = Notification).
        var server = new ModernStopServer();

        EngineCompat.StopBinding.Resolve(typeof(ModernStopServer), "1.22.3").Invoke(server, "test reason");

        Assert.Equal("test reason", server.Reason);
        Assert.Equal(FakeExitMode.SoftExit, server.Mode);
        Assert.Null(server.FinalLogMessage);
        Assert.Equal(FakeLogType.Notification, server.FinalLogType);
    }

    [Fact]
    public void StopBinding_Should_BindTwoParameterModernStop()
    {
        // The shape the compat spec recorded from the archived sketch: Stop(string, EnumExitMode).
        var server = new TwoParameterModernStopServer();

        EngineCompat.StopBinding.Resolve(typeof(TwoParameterModernStopServer), "1.22.0").Invoke(server, "r");

        Assert.Equal("r", server.Reason);
        Assert.Equal(FakeExitMode.SoftExit, server.Mode);
    }

    [Fact]
    public void StopBinding_Should_BindLegacyStopWithDeclaredDefaults()
    {
        // The 1.21.7/1.20.12 shape: Stop(string, string = null, EnumLogType = Notification).
        var server = new LegacyStopServer();

        EngineCompat.StopBinding.Resolve(typeof(LegacyStopServer), "1.21.7").Invoke(server, "class finished");

        Assert.Equal("class finished", server.Reason);
        Assert.Null(server.FinalLogMessage);
        Assert.Equal(FakeLogType.Notification, server.FinalLogType);
    }

    [Fact]
    public void StopBinding_Should_Throw_When_NoStopExists()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => EngineCompat.StopBinding.Resolve(typeof(NoExitServer), "1.99.0"));

        Assert.Contains("Stop", ex.Message);
        Assert.Contains("1.99.0", ex.Message);
    }

    [Fact]
    public void StopBinding_Should_Throw_When_ExitModeEnumLostSoftExit()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => EngineCompat.StopBinding.Resolve(typeof(NoSoftExitStopServer), "1.99.0"));

        Assert.Contains("SoftExit", ex.Message);
        Assert.Contains("1.99.0", ex.Message);
    }

    [Fact]
    public void ParseEnumMember_Should_ResolveTheLoadedEnumsValueByName()
    {
        // The whole point of the runtime resolution: the VALUE comes from the loaded enum's
        // metadata, so a member whose position shifted across versions (EnumClientState.Playing
        // moved from 3 to 4 in 1.22) reads correctly on every engine.
        object value = EngineCompat.ParseEnumMember(
            typeof(FakeExitMode), "FastExit", "9.9.9", "unused consequence.");

        Assert.Equal(FakeExitMode.FastExit, value);
    }

    [Fact]
    public void ResolveInstanceReader_Should_ReadAPropertyShape()
    {
        // The 1.22 shape: Pos/ServerPos are properties.
        Func<object, object?> reader = EngineCompat.ResolveInstanceReader(
            typeof(FakePositionedEntity), "PropertyPos", "9.9.9", "unused consequence.");

        Assert.Equal("from-property", reader(new FakePositionedEntity()));
    }

    [Fact]
    public void ResolveInstanceReader_Should_ReadAFieldShape()
    {
        // The pre-1.22 shape: Pos/ServerPos are public fields.
        Func<object, object?> reader = EngineCompat.ResolveInstanceReader(
            typeof(FakePositionedEntity), "FieldPos", "9.9.9", "unused consequence.");

        Assert.Equal("from-field", reader(new FakePositionedEntity()));
    }

    [Fact]
    public void ResolveInstanceReader_Should_PreferThePropertyShape_When_BothExist()
    {
        // Fork robustness: if a fork ships both shapes, the property (the newer engines') wins.
        Func<object, object?> reader = EngineCompat.ResolveInstanceReader(
            typeof(FakePositionedEntity), "Both", "9.9.9", "unused consequence.");

        Assert.Equal("both-property", reader(new FakePositionedEntity()));
    }

    [Fact]
    public void ResolveInstanceReader_Should_Throw_WithVersionAndConsequence_When_MemberMissing()
    {
        var ex = Assert.Throws<AtlasSetupException>(() => EngineCompat.ResolveInstanceReader(
            typeof(FakePositionedEntity),
            "ServerPos",
            "9.9.9",
            "Atlas cannot position spawned entities before the engine registers them."));

        Assert.Contains("FakePositionedEntity", ex.Message);
        Assert.Contains("ServerPos", ex.Message);
        Assert.Contains("9.9.9", ex.Message);
        Assert.Contains("cannot position spawned entities", ex.Message);
    }

    [Fact]
    public void ParseEnumMember_Should_Throw_WithVersionAndConsequence_When_MemberMissing()
    {
        var ex = Assert.Throws<AtlasSetupException>(() => EngineCompat.ParseEnumMember(
            typeof(FakeExitMode),
            "Playing",
            "9.9.9",
            "Atlas cannot recognize when a joined test player reaches the Playing client state."));

        Assert.Contains("FakeExitMode", ex.Message);
        Assert.Contains("Playing", ex.Message);
        Assert.Contains("9.9.9", ex.Message);
        Assert.Contains("cannot recognize when a joined test player", ex.Message);
    }

    [Fact]
    public void StopBinding_Should_SkipUnknownStopShapes()
    {
        var ex = Assert.Throws<AtlasSetupException>(
            () => EngineCompat.StopBinding.Resolve(typeof(UnknownStopShapeServer), "1.99.0"));

        Assert.Contains("known shape", ex.Message);
    }

    [Fact]
    public void StopBinding_Should_UnwrapEngineExceptions()
    {
        // Teardown paths must observe the engine's own exception type, exactly as the direct
        // call sites the binding replaced did (no TargetInvocationException wrapper).
        var server = new ThrowingStopServer();

        var ex = Assert.Throws<InvalidOperationException>(
            () => EngineCompat.StopBinding.Resolve(typeof(ThrowingStopServer), "1.22.3").Invoke(server, "r"));

        Assert.Equal("engine stop failed", ex.Message);
    }

    private static class FakeGameVersion
    {
        public const string ShortGameVersion = "9.9.9";
        public const string NetworkVersion = "9.9.12";
        public const int APIVersion = 7;
    }

    private static class FakeGameVersionReadonly
    {
        public static readonly string ShortGameVersion = "8.8.8";
    }

    private sealed class FakeExitHolder
    {
    }

    private sealed class UnconstructibleHolder
    {
        public UnconstructibleHolder(bool exit) => Exit = exit;

        public bool Exit { get; }
    }

#pragma warning disable CS0649 // Fields are assigned by EngineCompat through reflection only.
#pragma warning disable SA1307 // Fields deliberately mirror the engine's exitState/exit casing.
#pragma warning disable SA1401 // ... and must be public instance fields, like the engine's.
    private sealed class ModernExitServer
    {
        public FakeExitHolder? exitState;
    }

    private sealed class LegacyExitServer
    {
        public FakeExitHolder? exit;
    }

    private sealed class BothExitServer
    {
        public FakeExitHolder? exitState;
        public FakeExitHolder? exit;
    }

    private sealed class UnconstructibleExitServer
    {
        public UnconstructibleHolder? exitState;
    }
#pragma warning restore SA1401
#pragma warning restore SA1307
#pragma warning restore CS0649

    private sealed class NoExitServer
    {
    }

    private sealed class ModernStopServer
    {
        public string? Reason { get; private set; }

        public FakeExitMode? Mode { get; private set; }

        public string? FinalLogMessage { get; private set; } = "sentinel";

        public FakeLogType? FinalLogType { get; private set; }

        public void Stop(
            string reason,
            FakeExitMode exitMode,
            string? finalLogMessage = null,
            FakeLogType finalLogType = FakeLogType.Notification)
        {
            Reason = reason;
            Mode = exitMode;
            FinalLogMessage = finalLogMessage;
            FinalLogType = finalLogType;
        }
    }

    private sealed class TwoParameterModernStopServer
    {
        public string? Reason { get; private set; }

        public FakeExitMode? Mode { get; private set; }

        public void Stop(string reason, FakeExitMode exitMode)
        {
            Reason = reason;
            Mode = exitMode;
        }
    }

    private sealed class LegacyStopServer
    {
        public string? Reason { get; private set; }

        public string? FinalLogMessage { get; private set; } = "sentinel";

        public FakeLogType? FinalLogType { get; private set; }

        public void Stop(
            string reason,
            string? finalLogMessage = null,
            FakeLogType finalLogType = FakeLogType.Notification)
        {
            Reason = reason;
            FinalLogMessage = finalLogMessage;
            FinalLogType = finalLogType;
        }
    }

    private sealed class NoSoftExitStopServer
    {
        public void Stop(string reason, FakeLogType logType)
        {
            _ = reason;
            _ = logType;
        }
    }

    private sealed class UnknownStopShapeServer
    {
        // First parameter is not a string, and the string overload has a required non-enum
        // second parameter: neither known shape matches.
        public void Stop(int code)
            => _ = code;

        public void Stop(string reason, object requiredBag)
        {
            _ = reason;
            _ = requiredBag;
        }
    }

    private sealed class ThrowingStopServer
    {
        public void Stop(string reason, FakeExitMode exitMode)
        {
            _ = reason;
            _ = exitMode;
            throw new InvalidOperationException("engine stop failed");
        }
    }

    private class FakePositionedEntityBase
    {
#pragma warning disable SA1401 // The public field IS the shape under test (the pre-1.22 engines').
        public string FieldPos = "from-field";

        public string Both = "both-field";
#pragma warning restore SA1401
    }

    private sealed class FakePositionedEntity : FakePositionedEntityBase
    {
        public string PropertyPos { get; } = "from-property";

        // A same-name field and property cannot coexist in one class; hiding the base field
        // models a fork that ships both shapes at once.
        public new string Both => "both-property";
    }
}
