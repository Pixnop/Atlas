using System.Runtime.Versioning;
using Atlas.Internal.RealClient;

namespace Atlas.Engine.Tests.RealClient;

/// <summary>The entry point of this test assembly, used for one purpose: being a host that can be
/// killed. A test re-runs the assembly with <see cref="Argument"/> as a child process; the child
/// starts a sandbox exactly as a test host would, prints its launcher pid, and waits forever. The
/// test then SIGKILLs it, which is the one way to prove the sandbox does not outlive a host that
/// never got to dispose it. Without the argument it does nothing (the test runner loads the
/// assembly as a library and never calls it).</summary>
[UnsupportedOSPlatform("windows")]
internal static class SandboxHolder
{
    /// <summary>The first argument that makes the assembly act as the holder.</summary>
    public const string Argument = "--hold-real-client-sandbox";

    /// <summary>The holder's arguments: run folder, data path, a stand-in program (<c>-</c> for
    /// the real client, which needs a full install) and the ceiling in seconds.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>0 when it was not asked to hold anything, 2 when the client cannot run here.</returns>
    public static int Main(string[] args)
    {
        if (args is not [Argument, string run, string data, string program, string timeoutSeconds])
        {
            return 0;
        }

        ClientToolchain toolchain;
        if (program == "-")
        {
            ClientAvailability availability = ClientAvailability.Check(null, data, ClientProbes.OfThisMachine());
            if (availability.Toolchain is not { } resolved)
            {
                Console.WriteLine($"UNAVAILABLE {availability}");
                return 2;
            }

            toolchain = resolved;
        }
        else
        {
            if (ClientAvailability.CheckSandboxTools(ClientProbes.OfThisMachine(), out SandboxTools? tools) is { } failure)
            {
                Console.WriteLine($"UNAVAILABLE {failure}");
                return 2;
            }

            toolchain = RealClientEnvironment.StandInToolchain(tools!, Path.GetDirectoryName(run)!, data);
        }

        ClientSandbox sandbox = ClientSandbox.Start(
            new ClientSandboxOptions
            {
                RunDirectory = run,
                ProgramOverride = program == "-" ? null : program,
                Timeout = TimeSpan.FromSeconds(int.Parse(timeoutSeconds, System.Globalization.CultureInfo.InvariantCulture)),
            },
            toolchain);
        Console.WriteLine($"LAUNCHER {sandbox.LauncherPid}");
        Thread.Sleep(Timeout.Infinite);
        GC.KeepAlive(sandbox);
        return 0;
    }
}
