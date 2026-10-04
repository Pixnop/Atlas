using Atlas.Internal.Player;

namespace Atlas.Pure.Tests.Player;

public class RoleScopeTests
{
    [Fact]
    public void Enter_Should_SwitchTheRole_When_Called()
    {
        var player = new FakePlayer("admin");

        using RoleScope scope = player.Enter("suplayer");

        Assert.Equal("suplayer", player.Role);
    }

    [Fact]
    public void Dispose_Should_PutTheRoleItReplacedBack_When_TheScopeEnds()
    {
        var player = new FakePlayer("admin");
        RoleScope scope = player.Enter("suplayer");

        scope.Dispose();

        Assert.Equal("admin", player.Role);
    }

    [Fact]
    public void Dispose_Should_RestoreTheRoleTheScopeFound_When_ItWasNotTheDefault()
    {
        var player = new FakePlayer("suplayer");
        RoleScope scope = player.Enter("admin");

        scope.Dispose();

        Assert.Equal("suplayer", player.Role);
    }

    [Fact]
    public void Dispose_Should_BeANoOp_When_CalledASecondTime()
    {
        var player = new FakePlayer("admin");
        RoleScope scope = player.Enter("suplayer");
        scope.Dispose();
        player.Role = "other";

        scope.Dispose();

        Assert.Equal("other", player.Role);
    }

    [Fact]
    public void Dispose_Should_RestoreEachScopesOwnRole_When_ScopesAreNested()
    {
        var player = new FakePlayer("admin");
        RoleScope outer = player.Enter("suplayer");
        RoleScope inner = player.Enter("limited");
        Assert.Equal("limited", player.Role);

        inner.Dispose();
        Assert.Equal("suplayer", player.Role);

        outer.Dispose();
        Assert.Equal("admin", player.Role);
    }

    [Fact]
    public void Enter_Should_LeaveTheRoleAlone_When_TheRoleIsMissingOrTheEngineRefusesIt()
    {
        var player = new FakePlayer("admin", known: ["admin", "suplayer"]);

        Assert.Throws<ArgumentNullException>(() => player.Enter(null!));
        Assert.Throws<ArgumentException>(() => player.Enter(string.Empty));
        Assert.Throws<ArgumentException>(() => player.Enter("no-such-role"));

        Assert.Equal("admin", player.Role);
    }

    [Fact]
    public void WithRole_Should_Throw_When_ThePlayerIsMissing()
    {
        Assert.Throws<ArgumentNullException>(() => TestPlayerExtensions.WithRole(null!, "suplayer"));
    }

    /// <summary>One role code the delegates read and write, with the engine's refusal of a role
    /// that is not configured.</summary>
    private sealed class FakePlayer(string role, string[]? known = null)
    {
        public string Role { get; set; } = role;

        public RoleScope Enter(string toRole) => RoleScope.Enter(() => Role, SetRole, toRole);

        private void SetRole(string code)
        {
            if (known != null && !known.Contains(code))
            {
                throw new ArgumentException($"No such role configured '{code}'");
            }

            Role = code;
        }
    }
}
