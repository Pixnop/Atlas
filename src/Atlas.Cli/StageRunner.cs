using System.Runtime.CompilerServices;
using Atlas.Internal.Bootstrap;

namespace Atlas.Cli;

/// <summary>Implements `atlas stage`: runs the exact staging decision the module initializers run
/// at test-process boot (issue #49), explicitly and up front, so a one-shot script can stage a
/// prebuilt test output BEFORE spawning the process that will bind its engine assemblies (issue
/// #95: StratumParity's run-parity.sh cannot absorb the documented fail-then-rerun, since each
/// install runs exactly once). Deliberately a thin IO shell around the real decision core
/// (<see cref="EngineStager"/>/<see cref="EngineStaging"/>, in Atlas.Internal.Bootstrap): path
/// resolution and message/exit-code selection are pure (<see cref="StagePathResolution"/>,
/// <see cref="StageReport"/>), loading the target's own Atlas.dll copy is
/// <see cref="StageAssemblyResolver"/>, and this class only sequences them. Never loads
/// VintagestoryAPI.dll or VintagestoryLib.dll itself: EngineStager/EngineStaging read files and
/// PE metadata, they do not load the engine assemblies, and this command touches nothing else in
/// Atlas.dll that would.</summary>
internal static class StageRunner
{
    private const string ApiLabel = "VintagestoryAPI.dll and VintagestoryAPI.pdb";
    private const string NewtonsoftLabel = "Newtonsoft.Json.dll";

    /// <summary>Stages the target directory against the given install and reports what happened,
    /// one line per file (group).</summary>
    /// <param name="arguments">The parsed `stage` arguments.</param>
    /// <param name="installDir">The VINTAGE_STORY install directory (already validated to exist
    /// and contain VintagestoryLib.dll by the caller).</param>
    /// <param name="output">Destination for the per-file report lines.</param>
    /// <param name="error">Destination for usage and staging-failure diagnostics.</param>
    /// <returns>0 when every file staged cleanly or needed nothing; 2 when the target directory
    /// does not exist, the target's own Atlas.dll is too old to carry the staging seam, or
    /// staging hit one of the core's defined failure cases (an unwritable output, an install
    /// without its pdb, a diverged copy already bound, the Newtonsoft direction refusal).</returns>
    public static int Run(StageArguments arguments, string installDir, TextWriter output, TextWriter error)
    {
        string targetDir = StagePathResolution.ResolveTargetDirectory(arguments.TargetPath);
        if (!Directory.Exists(targetDir))
        {
            error.WriteLine($"atlas: stage target directory not found: '{targetDir}'");
            return 2;
        }

        using var resolver = new StageAssemblyResolver(targetDir);

        List<StageFileResult> results = [];
        if (HarnessSeam.TryCall(HarnessSeam.EngineAssemblyName, () => results = Stage(targetDir, installDir))
            is { } mismatch)
        {
            error.WriteLine($"atlas: {mismatch}");
            return 2;
        }

        foreach (StageFileResult result in results)
        {
            (result.State == StageFileState.Failed ? error : output).WriteLine(StageReport.Line(result));
        }

        return StageReport.ExitCode(results);
    }

    /// <summary>The seam itself: the only method naming <see cref="EngineStager"/>, so the target's
    /// own Atlas.dll loads no earlier than the first call, once
    /// <see cref="StageAssemblyResolver"/> is installed and can answer for it.</summary>
    /// <param name="targetDir">The stage target directory.</param>
    /// <param name="installDir">The VINTAGE_STORY install directory.</param>
    /// <returns>One result per file (group) the run reached.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<StageFileResult> Stage(string targetDir, string installDir)
    {
        var results = new List<StageFileResult> { StageApiPair(targetDir, installDir) };
        if (results[0].State != StageFileState.Failed)
        {
            // Mirrors EngineStager.Stage's own short-circuit: when the API pair fails, a real
            // boot never gets far enough to check Newtonsoft either, so reporting on it here
            // would claim a cleanliness this run cannot actually reach.
            results.Add(StageNewtonsoft(targetDir, installDir));
        }

        return results;
    }

    private static StageFileResult StageApiPair(string targetDir, string installDir)
    {
        bool localExisted = File.Exists(Path.Combine(targetDir, EngineStager.ApiDllName));
        EngineStager.Outcome outcome = EngineStager.StageApi(targetDir, installDir, loaded: null);
        return ToResult(ApiLabel, outcome, localExisted);
    }

    private static StageFileResult StageNewtonsoft(string targetDir, string installDir)
    {
        bool localExisted = File.Exists(Path.Combine(targetDir, EngineStager.NewtonsoftDllName));
        EngineStager.Outcome outcome = EngineStager.StageNewtonsoft(targetDir, installDir, loaded: null);
        return ToResult(NewtonsoftLabel, outcome, localExisted);
    }

    private static StageFileResult ToResult(string label, EngineStager.Outcome outcome, bool localExisted)
    {
        if (outcome.FailureMessage != null)
        {
            return new StageFileResult(label, StageFileState.Failed, outcome.FailureMessage);
        }

        // Nothing was copied: either the target already had a byte-identical file, or it had no
        // file of its own for the staging decision to replace.
        StageFileState untouched = localExisted ? StageFileState.AlreadyIdentical : StageFileState.NothingToStage;
        return new StageFileResult(label, outcome.Staged ? StageFileState.Staged : untouched);
    }
}
