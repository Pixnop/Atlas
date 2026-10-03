using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Atlas.ClientSpike;

/// <summary>Records, on a timeline, what the embedded server sees of the one real client.</summary>
internal sealed class ServerObserver
{
    private volatile string? _playerName;

    public string? PlayerName => _playerName;

    public static async Task<ServerObserver> AttachAsync(ServerHost host, Timeline timeline)
    {
        var observer = new ServerObserver();
        await host.RunOnGameThreadAsync((api, ticks) =>
        {
            api.Logger.EntryAdded += (type, message, args) =>
            {
                if (message.StartsWith("Client {0} uid {1} attempting identification", StringComparison.Ordinal))
                {
                    // args: client id, uid (never recorded), name
                    timeline.Mark("server.identification", $"name={args[2]}");
                }
            };
            api.Event.PlayerCreate += p => { observer._playerName = p.PlayerName; timeline.Mark("server.PlayerCreate", Describe(p)); };
            api.Event.PlayerJoin += p => { observer._playerName = p.PlayerName; timeline.Mark("server.PlayerJoin", Describe(p)); };
            api.Event.PlayerNowPlaying += p => timeline.Mark("server.PlayerNowPlaying (packet 26, level finalize handled)", Describe(p));
            // api.Event.PlayerReady never fires for mods on 1.22.3: HandlePlayerReady raises it on
            // CoreServerEventManager, which (unlike PlayerJoin/NowPlaying/Leave) does not forward
            // TriggerPlayerReady to the mod-level manager. The state is polled instead.
            api.Event.PlayerReady += p => timeline.Mark("server.PlayerReady event (expected to stay silent)", Describe(p));
            api.Event.RegisterGameTickListener(
                _ =>
                {
                    foreach (IPlayer pl in api.World.AllOnlinePlayers)
                    {
                        if (pl is IServerPlayer sp && sp.ConnectionState == EnumClientState.Playing)
                        {
                            timeline.Mark("server.Playing (ConnectionState, polled each tick)", Describe(sp));
                        }
                    }
                },
                1);
            api.Event.PlayerDisconnect += p => timeline.Mark("server.PlayerDisconnect", Describe(p));
            api.Event.PlayerLeave += p => timeline.Mark("server.PlayerLeave", Describe(p));
            return Task.CompletedTask;
        });
        return observer;
    }

    private static string Describe(IServerPlayer p)
        => $"name={p.PlayerName} state={p.ConnectionState} mode={p.WorldData.CurrentGameMode}";
}
