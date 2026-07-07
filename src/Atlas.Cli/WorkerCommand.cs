namespace Atlas.Cli;

/// <summary>How the orchestrator re-invokes the atlas executable to spawn a worker: the file to
/// start plus the arguments that come BEFORE the `run ...` verb.</summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="LeadingArguments">Arguments preceding the atlas command line proper.</param>
internal sealed record WorkerCommand(string FileName, IReadOnlyList<string> LeadingArguments)
{
    /// <summary>Resolves the worker invocation for the current process. Measured behavior
    /// (net10.0, Linux, 2026-07-06): under `dotnet Atlas.Cli.dll`, <c>Environment.ProcessPath</c>
    /// is the dotnet muxer and the assembly location is the dll, so the worker is
    /// `dotnet &lt;dll&gt;`; under `dotnet run`, a published apphost, or the packed `dotnet tool`
    /// shim, <c>ProcessPath</c> is a directly re-invokable executable (the tool shim's assembly
    /// location points into the NuGet .store, which is exactly why the shim, not the dll, is
    /// preferred whenever it exists). Rule: re-invoke the muxer with the dll only when the
    /// current process IS the muxer; otherwise re-invoke the process image itself.</summary>
    /// <param name="processPath">The current <c>Environment.ProcessPath</c>.</param>
    /// <param name="cliAssemblyPath">Location of the Atlas.Cli assembly.</param>
    /// <returns>The invocation to use for worker processes.</returns>
    public static WorkerCommand Resolve(string? processPath, string cliAssemblyPath)
    {
        bool hostedByMuxer = processPath is null || Path.GetFileNameWithoutExtension(processPath)
            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        return hostedByMuxer
            ? new WorkerCommand(processPath ?? "dotnet", [cliAssemblyPath])
            : new WorkerCommand(processPath!, []);
    }
}
