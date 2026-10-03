using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

[assembly: ModInfo(
    "Spike Crash Client",
    "spikecrashclient",
    Version = "0.0.1",
    Side = "Client",
    Description = "Throwaway spike mod: makes the client crash a few seconds after the player is in the world. The mode comes from the name of the folder the dll sits in.")]

namespace SpikeCrashClientMod;

public sealed class SpikeCrashClientModSystem : ModSystem
{
    private const int DelayMs = 5000;

    private ICoreClientAPI _capi = null!;
    private string _mode = "none";
    private readonly Stopwatch _clock = new();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        string? src = Mod.SourcePath;
        _mode = src == null ? "none" : Path.GetFileName(Path.GetDirectoryName(src)) ?? "none";
        api.Logger.Notification("SPIKE: loaded, mode={0}, source={1}", _mode, src);
        api.Event.LevelFinalize += Arm;
        if (_mode == "main-render")
        {
            api.Event.RegisterRenderer(new CrashRenderer(this), EnumRenderStage.Ortho, "spikecrash");
        }
    }

    private void Arm()
    {
        _capi.Logger.Notification("SPIKE: level finalized, mode={0}, crash in {1} ms", _mode, DelayMs);
        _clock.Restart();
        switch (_mode)
        {
            case "main-tick":
                _capi.Event.RegisterGameTickListener(
                    _ =>
                    {
                        if (_clock.ElapsedMilliseconds > DelayMs)
                        {
                            throw new InvalidOperationException("SPIKE client main-thread crash (game tick listener)");
                        }
                    },
                    100);
                break;
            case "exit":
                _capi.Event.RegisterCallback(
                    _ =>
                    {
                        _capi.Logger.Notification("SPIKE: asking the engine to exit (SoftExit)");
                        Vintagestory.Client.ScreenManager.Platform.WindowExit("spike: scenario finished", EnumExitMode.SoftExit);
                    },
                    DelayMs);
                break;
            case "main-callback":
                _capi.Event.RegisterCallback(
                    _ => throw new InvalidOperationException("SPIKE client main-thread crash (callback)"),
                    DelayMs);
                break;
            case "thread":
                var t = new Thread(() =>
                {
                    Thread.Sleep(DelayMs);
                    throw new InvalidOperationException("SPIKE client background-thread crash");
                })
                { Name = "spike-crash", IsBackground = true };
                t.Start();
                break;
        }
    }

    private sealed class CrashRenderer(SpikeCrashClientModSystem owner) : IRenderer
    {
        public double RenderOrder => 0.99;

        public int RenderRange => 1;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (owner._clock.IsRunning && owner._clock.ElapsedMilliseconds > DelayMs)
            {
                throw new InvalidOperationException("SPIKE client main-thread crash (renderer, Ortho stage)");
            }
        }

        public void Dispose()
        {
        }
    }
}
