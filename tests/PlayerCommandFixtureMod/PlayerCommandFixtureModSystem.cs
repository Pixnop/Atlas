using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

[assembly: ModInfo(
    "Atlas Player Command Fixture",
    "playercommandfixture",
    Version = "0.1.0",
    Side = "Server",
    Description = "Test fixture for a command run with a joined test player as caller: reports the caller the handler sees and its position, checks a privilege a downgraded caller lacks, and defers argument parsing to a later continuation.")]

namespace PlayerCommandFixtureMod;

/// <summary>Registers <c>/callerfx &lt;op&gt; [word]</c>, a <c>RequiresPlayer</c> command driven by
/// <c>ITestPlayer.ExecuteCommand</c> and <c>IWorldSession.ExecuteCommand</c> tests. Four ops:
/// <c>whoami</c> reports the caller's player name and uid, <c>admin</c> succeeds only for a
/// caller holding <see cref="Privilege.controlserver"/> (a joined test player is admin by
/// default; downgrading to <c>suplayer</c> removes it), <c>pos</c> reports the caller's own
/// position, and <c>deferred</c> reads its one word through <see cref="DeferredEchoArgParser"/>,
/// which always reports <see cref="EnumParseResult.Deferred"/> and resolves from a later
/// continuation, the same async-parsed shape a real player-lookup argument takes. The mod
/// references only VintagestoryAPI, like a shipping mod.</summary>
public sealed class PlayerCommandFixtureModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.ChatCommands.Create("callerfx")
            .WithDescription("Atlas caller fixture: whoami | admin | pos | deferred <word>.")
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            .WithArgs(api.ChatCommands.Parsers.Word("op"), new DeferredEchoArgParser("word"))
            .HandleWith(HandleCommand);
    }

    private static TextCommandResult HandleCommand(TextCommandCallingArgs args)
    {
        switch ((string)args[0])
        {
            case "whoami":
                IPlayer player = args.Caller.Player;
                return TextCommandResult.Success($"{player.PlayerName}:{player.PlayerUID}");
            case "admin":
                return args.Caller.HasPrivilege(Privilege.controlserver)
                    ? TextCommandResult.Success("admin ok")
                    : TextCommandResult.Error("missing controlserver", "noprivilege");
            case "pos":
                Vec3d pos = args.Caller.Pos;
                return TextCommandResult.Success(string.Format(
                    CultureInfo.InvariantCulture, "{0:F3},{1:F3},{2:F3}", pos.X, pos.Y, pos.Z));
            case "deferred":
                return TextCommandResult.Success($"echo:{args[1]}");
            default:
                return TextCommandResult.Error($"unknown op '{args[0]}'");
        }
    }
}
