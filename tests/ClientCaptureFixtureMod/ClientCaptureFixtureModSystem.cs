using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo(
    "Atlas Client Capture Fixture",
    "clientcapturefixture",
    Version = "0.1.0",
    Side = "Server",
    Description = "Test fixture for the Atlas client observation surface: a mod network channel with one protobuf message, sent to a player on join and on command.")]

namespace ClientCaptureFixtureMod;

/// <summary>A miniature of a mod whose server side talks to its client side over a mod network
/// channel (the Caminus overlay shape): channel <c>atlasfixture</c>, one message type registered
/// with <c>RegisterMessageType</c> in <c>StartServerSide</c>, sent to a player when it starts
/// playing and on <c>/atlasfixture send &lt;player&gt; &lt;text&gt;</c>. The mod references only
/// VintagestoryAPI and protobuf-net, like a shipping mod.</summary>
public sealed class ClientCaptureFixtureModSystem : ModSystem
{
    private IServerNetworkChannel _channel = null!;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _channel = api.Network.RegisterChannel("atlasfixture").RegisterMessageType<AtlasFixtureMessage>();
        api.Event.PlayerNowPlaying += player =>
            _channel.SendPacket(new AtlasFixtureMessage { Text = "welcome " + player.PlayerName }, player);

        api.ChatCommands.Create("atlasfixture")
            .WithDescription("Atlas client-capture fixture: send a channel message to a player.")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(api.ChatCommands.Parsers.Word("op"), api.ChatCommands.Parsers.Word("player"), api.ChatCommands.Parsers.All("text"))
            .HandleWith(args => HandleCommand(api, args));
    }

    private TextCommandResult HandleCommand(ICoreServerAPI api, TextCommandCallingArgs args)
    {
        if ((string)args[0] != "send")
        {
            return TextCommandResult.Error($"unknown op '{args[0]}'");
        }

        string playerName = (string)args[1];
        if (api.World.AllOnlinePlayers.FirstOrDefault(p => p.PlayerName == playerName) is not IServerPlayer player)
        {
            return TextCommandResult.Error($"no online player named '{playerName}'");
        }

        _channel.SendPacket(new AtlasFixtureMessage { Text = (string)args[2] }, player);
        return TextCommandResult.Success($"sent to {playerName}");
    }
}
