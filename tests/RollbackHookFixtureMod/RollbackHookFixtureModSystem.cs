using System.Globalization;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

[assembly: ModInfo(
    "Atlas Rollback Hook Fixture",
    "rollbackhookfixture",
    Version = "0.1.0",
    Side = "Server",
    Description = "Test fixture proving the Atlas rollback mod-cooperation contract: registry-style in-memory state keyed on SaveGame data, plus engine-api mini-dimension helpers.")]

namespace RollbackHookFixtureMod;

/// <summary>A deliberate miniature of Manifold's dimension registry, for the stage 3 rollback
/// E2E tests: in-memory state (a code-to-id registry plus an id allocator, ModSystem-owned)
/// seeded at boot from a manifest persisted in <c>SaveGame.ModData</c>. A world rollback
/// restores the manifest but not the memory, desyncing the two: that is the documented boundary
/// this fixture pins, and the <c>atlas:rollback:restored</c> cooperation hook is the designed
/// fix, implemented here exactly as the spec prescribes for a real mod: the boot-time hydrate
/// path refactored into a re-runnable method, called again from the hook handler. The mod
/// references ONLY VintagestoryAPI: the event name plus payload shape is the whole contract.</summary>
/// <remarks>Driven from tests through <c>/rollbackfx</c> (the scenario assembly cannot call
/// into this class: the game's ModLoader loads its own copy of the dll, a distinct assembly
/// instance). It also pregenerates a mini-dimension at boot (dimension 9, a 3x3 column patch
/// around spawn with a marker block), replicating the Manifold finding that boot-time
/// mini-dimensions must not disqualify rollback, and exposes <c>dim-create</c> to park world
/// state in mini-dimension chunk columns (the EntityTransit/BlockTransit shape).</remarks>
public sealed class RollbackHookFixtureModSystem : ModSystem
{
    /// <summary>SaveGame moddata key of the persisted manifest ("code=id" lines).</summary>
    private const string ManifestKey = "rollbackfx:manifest";

    /// <summary>The dimension pregenerated at boot.</summary>
    private const int PregenDimension = 9;

    /// <summary>The lowest id the allocator hands out (mirrors Manifold's 10-1023 range).</summary>
    private const int FirstId = 10;

    private readonly Dictionary<string, int> _registry = new(StringComparer.Ordinal);
    private readonly HashSet<int> _reservedIds = new();

    private ICoreServerAPI _api = null!;
    private string _hookMode = "off";
    private bool _pregenDone;
    private long _pregenListenerId;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;

        // The boot hydrate: seed the in-memory registry from the persisted manifest. This is
        // the exact method the restored-hook handler re-runs; making it re-runnable IS the
        // cooperation contract's demand on a mod.
        HydrateFromSaveGame();

        // Subscribed above the 0.5 default, the documented convention for library mods whose
        // consumers also subscribe. What the handler does is gated by /rollbackfx hook, so the
        // no-hook boundary test observes a mod that never resyncs.
        api.Event.RegisterEventBusListener(OnRollbackRestored, 0.6, "atlas:rollback:restored");

        // Boot-time mini-dimension pregeneration, deferred by a few ticks: chunk columns need
        // the spawn map chunk, which does not exist yet while mods start.
        _pregenListenerId = api.Event.RegisterGameTickListener(OnPregenTick, 50);

        api.ChatCommands.Create("rollbackfx")
            .WithDescription("Atlas rollback-hook fixture: registry state, hook modes, mini-dimension helpers.")
            .RequiresPrivilege(Privilege.controlserver)
            .WithArgs(
                api.ChatCommands.Parsers.Word("op"),
                api.ChatCommands.Parsers.OptionalWord("arg"),
                api.ChatCommands.Parsers.OptionalWord("arg2"))
            .HandleWith(HandleCommand);
    }

    /// <summary>Rebuilds the registry and the allocator from the manifest currently in
    /// <c>SaveGame.ModData</c>: registrations absent from it are dropped, persisted ids are
    /// re-reserved (Manifold's <c>SeedFromManifest</c>/<c>ReserveSpecific</c> shape).</summary>
    private void HydrateFromSaveGame()
    {
        _registry.Clear();
        _reservedIds.Clear();
        byte[]? manifest = _api.WorldManager.SaveGame.GetData(ManifestKey);
        foreach ((string code, int id) in ParseManifest(manifest))
        {
            _registry[code] = id;
            _reservedIds.Add(id);
        }
    }

    private void OnRollbackRestored(string eventName, ref EnumHandling handling, IAttribute data)
    {
        if (_hookMode == "throw")
        {
            throw new InvalidOperationException("rollbackfx: simulated handler failure");
        }

        if (_hookMode == "resync")
        {
            HydrateFromSaveGame();
        }
    }

    private void OnPregenTick(float dt)
    {
        BlockPos spawn = _api.World.DefaultSpawnPosition.AsBlockPos;
        int chunkX = spawn.X / GlobalConstants.ChunkSize;
        int chunkZ = spawn.Z / GlobalConstants.ChunkSize;
        if (_api.WorldManager.GetMapChunk(chunkX, chunkZ) == null)
        {
            return; // spawn area still loading; try again on a later tick
        }

        CreateColumnPatch(chunkX, chunkZ, PregenDimension);
        Block marker = _api.World.GetBlock(new AssetLocation("game:rock-granite"))
            ?? throw new InvalidOperationException("rollbackfx: block game:rock-granite not found");
        _api.World.BlockAccessor.SetBlock(
            marker.BlockId, PregenMarkerPos(chunkX, chunkZ));
        _pregenDone = true;
        _api.Event.UnregisterGameTickListener(_pregenListenerId);
    }

    /// <summary>Creates a 3x3 patch of empty chunk columns around (chunkX, chunkZ) in the given
    /// dimension, through the public engine api, so block writes always have loaded horizontal
    /// neighbors.</summary>
    private void CreateColumnPatch(int chunkX, int chunkZ, int dimension)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                _api.WorldManager.CreateChunkColumnForDimension(chunkX + dx, chunkZ + dz, dimension);
            }
        }
    }

    /// <summary>The boot-pregenerated marker's position, deterministic so tests can assert it.</summary>
    private static BlockPos PregenMarkerPos(int chunkX, int chunkZ)
        => new(
            (chunkX * GlobalConstants.ChunkSize) + 8,
            4,
            (chunkZ * GlobalConstants.ChunkSize) + 8,
            PregenDimension);

    private TextCommandResult HandleCommand(TextCommandCallingArgs args)
    {
        string op = (string)args[0];
        string? arg = args.Parsers[1].IsMissing ? null : (string)args[1];
        string? arg2 = args.Parsers[2].IsMissing ? null : (string)args[2];
        return op switch
        {
            "register" => Register(arg, ephemeral: arg2 == "ephemeral"),
            "remove" => Remove(arg),
            "state" => State(),
            "hook" => SetHookMode(arg),
            "dim-create" => DimCreate(arg),
            _ => TextCommandResult.Error($"unknown op '{op}'"),
        };
    }

    private TextCommandResult Register(string? code, bool ephemeral)
    {
        if (string.IsNullOrEmpty(code))
        {
            return TextCommandResult.Error("register needs a code");
        }

        if (_registry.TryGetValue(code, out int existing))
        {
            return TextCommandResult.Error($"duplicate: '{code}' is already registered (id {existing})");
        }

        int id = FirstId;
        while (_reservedIds.Contains(id))
        {
            id++;
        }

        _registry[code] = id;
        _reservedIds.Add(id);
        if (!ephemeral)
        {
            WriteManifest();
        }

        return TextCommandResult.Success($"registered {code}={id}");
    }

    private TextCommandResult Remove(string? code)
    {
        if (string.IsNullOrEmpty(code) || !_registry.Remove(code, out int id))
        {
            return TextCommandResult.Error($"'{code}' is not registered");
        }

        _reservedIds.Remove(id);
        WriteManifest();
        return TextCommandResult.Success($"removed {code}={id}");
    }

    private TextCommandResult State()
    {
        string registry = string.Join(
            ",",
            _registry.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
        string manifest = string.Join(
            ",",
            ParseManifest(_api.WorldManager.SaveGame.GetData(ManifestKey))
                .OrderBy(pair => pair.Code, StringComparer.Ordinal)
                .Select(pair => $"{pair.Code}={pair.Id}"));
        string pregen = _pregenDone ? "true" : "false";
        return TextCommandResult.Success($"registry:[{registry}] manifest:[{manifest}] pregen:{pregen}");
    }

    private TextCommandResult SetHookMode(string? mode)
    {
        if (mode is not ("off" or "resync" or "throw"))
        {
            return TextCommandResult.Error($"unknown hook mode '{mode}' (off|resync|throw)");
        }

        _hookMode = mode;
        return TextCommandResult.Success($"hook mode: {mode}");
    }

    private TextCommandResult DimCreate(string? dimensionArg)
    {
        if (!int.TryParse(dimensionArg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dimension)
            || dimension <= 0)
        {
            return TextCommandResult.Error($"dim-create needs a positive dimension, got '{dimensionArg}'");
        }

        BlockPos spawn = _api.World.DefaultSpawnPosition.AsBlockPos;
        int chunkX = spawn.X / GlobalConstants.ChunkSize;
        int chunkZ = spawn.Z / GlobalConstants.ChunkSize;
        CreateColumnPatch(chunkX, chunkZ, dimension);
        return TextCommandResult.Success($"created 9 columns in dimension {dimension} around chunk {chunkX},{chunkZ}");
    }

    private void WriteManifest()
    {
        var builder = new StringBuilder();
        foreach ((string code, int id) in _registry)
        {
            builder.Append(code).Append('=').Append(id.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        _api.WorldManager.SaveGame.StoreData(ManifestKey, Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static List<(string Code, int Id)> ParseManifest(byte[]? manifest)
    {
        var entries = new List<(string Code, int Id)>();
        if (manifest == null)
        {
            return entries;
        }

        foreach (string line in Encoding.UTF8.GetString(manifest).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('=');
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            {
                entries.Add((parts[0], id));
            }
        }

        return entries;
    }
}
