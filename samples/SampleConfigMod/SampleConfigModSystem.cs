using Vintagestory.API.Common;
using Vintagestory.API.Server;

[assembly: ModInfo(
    "Atlas Sample Config Mod",
    "sampleconfigmod",
    Version = "0.1.0",
    Side = "Server",
    Description = "Mod-under-test proving Atlas seeds ModConfig files before StartServerSide runs.")]

namespace SampleConfigMod;

/// <summary>Reads its config once in <see cref="StartServerSide"/> — the very common mod pattern
/// Atlas's data file seeding exists for — and reports what it saw via <c>/sampleconfig</c>.</summary>
public sealed class SampleConfigModSystem : ModSystem
{
    private string _greeting = "config-not-loaded";

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        // The one-shot startup read: if the file is not already in <dataPath>/ModConfig when the
        // server boots, this returns null and the mod runs unconfigured forever.
        SampleConfig? config = api.LoadModConfig<SampleConfig>("sampleconfig.json");
        _greeting = config?.Greeting ?? "config-not-loaded";

        api.ChatCommands.Create("sampleconfig")
            .WithDescription("Reports the greeting loaded from ModConfig/sampleconfig.json at startup.")
            .RequiresPrivilege(Privilege.chat)
            .HandleWith(_ => TextCommandResult.Success(_greeting));
    }
}
