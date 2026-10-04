using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.Pure.Tests.Api;

public class TestPlayerExtensionsTests
{
    [Fact]
    public void WithRole_Should_PutThePlayerOnTheRole_When_Called()
    {
        var fake = new FakePlayer("admin");

        using IDisposable scope = fake.Player.WithRole("suplayer");

        Assert.Equal("suplayer", fake.Role);
    }

    [Fact]
    public void Dispose_Should_PutTheRoleItReplacedBack_When_TheScopeEnds()
    {
        var fake = new FakePlayer("admin");
        IDisposable scope = fake.Player.WithRole("suplayer");

        scope.Dispose();

        Assert.Equal("admin", fake.Role);
    }

    [Fact]
    public void Dispose_Should_RestoreTheRoleTheScopeFound_When_ItWasNotTheDefault()
    {
        var fake = new FakePlayer("suplayer");
        IDisposable scope = fake.Player.WithRole("admin");

        scope.Dispose();

        Assert.Equal("suplayer", fake.Role);
    }

    [Fact]
    public void Dispose_Should_BeANoOp_When_CalledASecondTime()
    {
        var fake = new FakePlayer("admin");
        IDisposable scope = fake.Player.WithRole("suplayer");
        scope.Dispose();
        fake.Role = "other";

        scope.Dispose();

        Assert.Equal("other", fake.Role);
    }

    [Fact]
    public void Dispose_Should_RestoreEachScopesOwnRole_When_ScopesAreNested()
    {
        var fake = new FakePlayer("admin");
        IDisposable outer = fake.Player.WithRole("suplayer");
        IDisposable inner = fake.Player.WithRole("limited");
        Assert.Equal("limited", fake.Role);

        inner.Dispose();
        Assert.Equal("suplayer", fake.Role);

        outer.Dispose();
        Assert.Equal("admin", fake.Role);
    }

    [Fact]
    public void WithRole_Should_Throw_When_ThePlayerOrTheRoleIsMissing()
    {
        var fake = new FakePlayer("admin");

        Assert.Throws<ArgumentNullException>(() => TestPlayerExtensions.WithRole(null!, "suplayer"));
        Assert.Throws<ArgumentNullException>(() => fake.Player.WithRole(null!));
        Assert.Throws<ArgumentException>(() => fake.Player.WithRole(string.Empty));
        Assert.Equal("admin", fake.Role);
    }

    /// <summary>A test player whose server-side object keeps one role code, which
    /// <c>SetRole</c> overwrites and <c>Role</c> reads back, as the engine's role record does.</summary>
    private sealed class FakePlayer
    {
        public FakePlayer(string role)
        {
            Role = role;
            var server = Substitute.For<IServerPlayer>();
            server.Role.Returns(_ =>
            {
                var current = Substitute.For<IPlayerRole>();
                current.Code.Returns(Role);
                return current;
            });
            server.When(s => s.SetRole(Arg.Any<string>())).Do(call => Role = call.Arg<string>());
            Player = Substitute.For<ITestPlayer>();
            Player.Player.Returns(server);
        }

        public ITestPlayer Player { get; }

        public string Role { get; set; }
    }
}
