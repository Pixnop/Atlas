using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using Atlas.Api;

namespace Atlas.Internal.Bootstrap;

/// <summary>The thin IO/reflection shell of the engine-assembly auto-staging preflight (issue
/// #49; decisions and messages live in <see cref="EngineStaging"/>): reads file identities and
/// version resources, finds the process's already-bound copies, and rewrites the consumer test
/// output's engine-provided files (VintagestoryAPI.dll+pdb always when diverged; the game's
/// Newtonsoft.Json.dll only when the output carries an OLDER game build than the install, the
/// one direction that kills the boot) from the VINTAGE_STORY install, so a prebuilt test
/// assembly runs against whichever install the variable points at, without a rebuild.</summary>
/// <remarks><para>Two entry points, one evaluation. <see cref="TryStageEarly()"/> is the
/// module-initializer trigger (Atlas and Atlas.XUnit register one each): it never throws, stages
/// best-effort, and covers both the app base directory (the test output under VSTest) and this
/// assembly's own directory (the scenario directory under `atlas run`, where Atlas.dll is loaded
/// from next to the scenario dll while the app base is the CLI's own).
/// <see cref="EnsureStagedForBoot"/> is the boot preflight: same evaluation, but a recorded
/// failure surfaces as an <see cref="AtlasSetupException"/> on the caller thread, BEFORE the game
/// thread whose JIT would otherwise kill the process with a raw TypeLoadException.</para>
/// <para>Outcomes are cached per (consumer directory, install) pair: staging is idempotent (a
/// re-staged copy compares identical on the next evaluation) and a recorded failure stays the
/// truth for the process (once a stale copy is bound, it stays bound).</para></remarks>
internal static class EngineStager
{
    /// <summary>File name of the engine's API assembly. Internal (not private): atlas stage
    /// (Atlas.Cli's StageRunner) constructs the same local paths to check pre-staging existence
    /// for its own report, without retyping the file names.</summary>
    internal const string ApiDllName = "VintagestoryAPI.dll";

    /// <summary>File name of the engine API assembly's debug symbols, staged alongside the dll
    /// as a unit.</summary>
    internal const string ApiPdbName = "VintagestoryAPI.pdb";

    /// <summary>File name of the game-provided Newtonsoft.Json build, staged direction-aware
    /// (see <see cref="EngineStaging.DecideNewtonsoft"/>).</summary>
    internal const string NewtonsoftDllName = "Newtonsoft.Json.dll";

    private static readonly ConcurrentDictionary<(string ConsumerDir, string InstallDir), Lazy<Outcome>> Outcomes = new();

    /// <summary>Module-initializer trigger: stages the app base directory and this assembly's own
    /// directory against the VINTAGE_STORY install, best-effort. Never throws; a process without
    /// a configured install, or without a local VintagestoryAPI.dll copy, is unaffected beyond an
    /// environment read and a few file-existence probes.</summary>
    public static void TryStageEarly()
    {
        string? install = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        TryStageEarly(AppContext.BaseDirectory, install);
        string? assemblyDir = GetOwnAssemblyDirectory();
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            TryStageEarly(assemblyDir, install);
        }
    }

    /// <summary>Boot preflight: ensures the process binds the install's VintagestoryAPI bytes,
    /// staging the consumer copy if that has not happened yet, and throws when it genuinely
    /// cannot (a diverged copy already loaded, an unwritable output, an install without its
    /// pdb).</summary>
    /// <param name="consumerDir">The consumer test output (the assembly probing base):
    /// <see cref="AppContext.BaseDirectory"/> BEFORE <see cref="GameEnvironment.Initialize"/>
    /// redirects it; on hosts booted after the redirect the evaluation degenerates to comparing
    /// the install with itself, which is harmless.</param>
    /// <param name="installDir">The install directory returned by <see cref="VsInstall.Locate"/>.</param>
    /// <exception cref="AtlasSetupException">Thrown when the process cannot be brought onto the
    /// install's bytes; the message names both identities and the remedies.</exception>
    public static void EnsureStagedForBoot(string consumerDir, string installDir)
    {
        Outcome outcome = Ensure(consumerDir, installDir);
        if (outcome.FailureMessage != null)
        {
            throw new AtlasSetupException(outcome.FailureMessage);
        }
    }

    /// <summary>Stages one consumer directory against one install, best-effort: invalid installs
    /// are skipped silently (the boot preflight owns that error), and unexpected failures are
    /// reduced to a stderr line because this runs inside module initializers, where a throw
    /// surfaces as an unrelated TypeInitializationException.</summary>
    /// <param name="consumerDir">The directory whose VintagestoryAPI.dll copy shadows probing.</param>
    /// <param name="installDir">The VINTAGE_STORY install directory, possibly unset or invalid.</param>
    internal static void TryStageEarly(string consumerDir, string? installDir)
    {
        try
        {
            if (string.IsNullOrEmpty(installDir)
                || !File.Exists(Path.Combine(installDir, "VintagestoryLib.dll")))
            {
                // Not a usable install: nothing to stage against. VsInstall.Locate() reports
                // this properly at boot; a pure-test process never gets that far and stays
                // untouched, which is the module-initializer contract.
                return;
            }

            _ = Ensure(consumerDir, installDir);
        }
        catch (Exception ex)
        {
            // Deliberately catch-all: this is a module-initializer path, and any throw here
            // would surface as an opaque TypeInitializationException far from the cause. The
            // boot preflight re-reads the cached outcome (or re-evaluates) and reports properly.
            Console.Error.WriteLine($"[Atlas] engine-assembly staging preflight skipped: {ex.Message}");
        }
    }

    /// <summary>Evaluates and executes the staging decision for one directory pair. Total: every
    /// failure, including unexpected IO, becomes an <see cref="Outcome"/> rather than a throw.</summary>
    /// <param name="consumerDir">The consumer directory holding the (possibly stale) copy.</param>
    /// <param name="installDir">The install directory.</param>
    /// <param name="loadedApi">The process's already-bound VintagestoryAPI, if any; parameterized
    /// so tests can exercise every decision without loading real assemblies.</param>
    /// <param name="loadedNewtonsoft">The process's already-bound Newtonsoft.Json, if any (the
    /// VSTest host binds it at process start for its own protocol).</param>
    /// <returns>The combined outcome; an API failure wins over evaluating Newtonsoft at all.</returns>
    internal static Outcome Evaluate(
        string consumerDir, string installDir, LoadedAssembly? loadedApi, LoadedAssembly? loadedNewtonsoft)
    {
        Outcome api = EvaluateApi(consumerDir, installDir, loadedApi);
        if (api.FailureMessage != null)
        {
            return api;
        }

        Outcome newtonsoft = EvaluateNewtonsoft(consumerDir, installDir, loadedNewtonsoft);
        return new Outcome(api.Staged || newtonsoft.Staged, newtonsoft.FailureMessage);
    }

    /// <summary>Reads a file's content identity, or <see langword="null"/> when it does not exist.</summary>
    /// <param name="path">The file to identify.</param>
    /// <returns>The identity, or <see langword="null"/>.</returns>
    internal static ApiCopySync.FileIdentity? TryReadIdentity(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        long length;
        string hash;
        using (FileStream stream = File.OpenRead(path))
        {
            hash = Convert.ToHexString(SHA256.HashData(stream));
            length = stream.Length;
        }

        string? version;
        try
        {
            version = AssemblyName.GetAssemblyName(path).Version?.ToString();
        }
        catch (BadImageFormatException)
        {
            // Not a .NET assembly (or unreadable metadata): identity still works on size + hash,
            // messages just report the version as unknown.
            version = null;
        }

        return new ApiCopySync.FileIdentity(length, hash, version);
    }

    /// <summary>Reads a file's version resource, or <see langword="null"/> when the file does
    /// not exist, is not a PE image, or carries no parseable file version. Loaded-assembly
    /// locations may be placeholders (byte-loaded images), which read as null too.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The file version, or <see langword="null"/>.</returns>
    internal static Version? TryReadFileVersion(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string? raw = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion;
        return raw != null && Version.TryParse(raw, out Version? version) ? version : null;
    }

    /// <summary>Evaluates and executes the staging decision for the VintagestoryAPI.dll+pdb pair
    /// alone. Internal (not private): atlas stage (Atlas.Cli's StageRunner) calls this and
    /// <see cref="EvaluateNewtonsoft"/> separately, one per file group, for the per-file report
    /// <see cref="Evaluate"/>'s combined <see cref="Outcome"/> cannot express; <paramref
    /// name="loaded"/> is always null there (an explicit CLI invocation, not a module-initializer
    /// race against an already-bound assembly).</summary>
    /// <param name="consumerDir">The consumer directory holding the (possibly stale) copy.</param>
    /// <param name="installDir">The install directory.</param>
    /// <param name="loaded">The process's already-bound VintagestoryAPI, if any.</param>
    /// <returns>The evaluated outcome.</returns>
    internal static Outcome EvaluateApi(string consumerDir, string installDir, LoadedAssembly? loaded)
    {
        string localPath = Path.Combine(consumerDir, ApiDllName);
        string installPath = Path.Combine(installDir, ApiDllName);
        try
        {
            ApiCopySync.FileIdentity? local = TryReadIdentity(localPath);
            ApiCopySync.FileIdentity? install = TryReadIdentity(installPath);
            bool installPdbPresent = File.Exists(Path.Combine(installDir, ApiPdbName));
            switch (EngineStaging.Decide(local, install, installPdbPresent, loaded?.Identity))
            {
                case EngineStaging.StageAction.None:
                    return new Outcome(Staged: false, FailureMessage: null);

                case EngineStaging.StageAction.FailInstallPdbMissing:
                    return new Outcome(
                        Staged: false,
                        EngineStaging.DescribeInstallPdbMissing(localPath, installPath));

                case EngineStaging.StageAction.FailLoadedStale:
                    // Still rewrite the disk copy when possible: it cannot save this run (the
                    // stale image is bound), but it makes a plain re-run pass without a rebuild.
                    bool restaged = TryCopyPair(installDir, consumerDir) == null;
                    string staleMessage = EngineStaging.DescribeLoadedStale(
                        loaded!.Value.Path, loaded.Value.Identity, installPath, install!, restaged);
                    return new Outcome(restaged, staleMessage);

                default:
                    string? copyError = TryCopyPair(installDir, consumerDir);
                    if (copyError == null)
                    {
                        Console.Error.WriteLine(
                            EngineStaging.DescribeStaged(localPath, local!, installPath, install!));
                        return new Outcome(Staged: true, FailureMessage: null);
                    }

                    return new Outcome(
                        Staged: false,
                        EngineStaging.DescribeUnwritable(localPath, local!, installPath, install!, copyError));
            }
        }
        catch (Exception ex)
        {
            // Total-function guarantee for the module-initializer callers: unexpected IO (an
            // unreadable dll, a vanishing directory) surfaces at boot as a setup error naming
            // the cause instead of tearing down a type initializer.
            string unexpected =
                $"The engine-assembly staging preflight failed unexpectedly evaluating '{localPath}' " +
                $"against '{installPath}': {ex.Message}";
            return new Outcome(Staged: false, unexpected);
        }
    }

    /// <summary>Evaluates and executes the staging decision for the game-shipped
    /// Newtonsoft.Json.dll alone; see <see cref="EvaluateApi"/> for why this is internal rather
    /// than private.</summary>
    /// <param name="consumerDir">The consumer directory holding the (possibly stale) copy.</param>
    /// <param name="installDir">The install directory.</param>
    /// <param name="loaded">The process's already-bound Newtonsoft.Json, if any.</param>
    /// <returns>The evaluated outcome.</returns>
    internal static Outcome EvaluateNewtonsoft(string consumerDir, string installDir, LoadedAssembly? loaded)
    {
        string localPath = Path.Combine(consumerDir, NewtonsoftDllName);
        string installPath = Path.Combine(installDir, "Lib", NewtonsoftDllName);
        try
        {
            ApiCopySync.FileIdentity? local = TryReadIdentity(localPath);
            ApiCopySync.FileIdentity? install = TryReadIdentity(installPath);
            Version? localVersion = TryReadFileVersion(localPath);
            Version? installVersion = TryReadFileVersion(installPath);
            switch (EngineStaging.DecideNewtonsoft(local, install, localVersion, installVersion, loaded?.Identity))
            {
                case EngineStaging.StageAction.None:
                    return new Outcome(Staged: false, FailureMessage: null);

                case EngineStaging.StageAction.FailLoadedStale:
                    // Same self-heal as the API path: this run is doomed (the test host bound
                    // the old build at process start), but the next one need not rebuild. The
                    // loaded file's version must be read BEFORE the restage overwrites the path
                    // it was loaded from, or the message reports the new bytes' version.
                    Version? loadedVersion = TryReadFileVersion(loaded!.Value.Path);
                    bool restaged = TryCopySingle(installPath, localPath) == null;
                    string staleMessage = EngineStaging.DescribeNewtonsoftLoadedStale(
                        loaded.Value.Path, loadedVersion, installPath, installVersion!, restaged);
                    return new Outcome(restaged, staleMessage);

                default:
                    string? copyError = TryCopySingle(installPath, localPath);
                    if (copyError == null)
                    {
                        Console.Error.WriteLine(EngineStaging.DescribeNewtonsoftStaged(
                            localPath, localVersion!, installPath, installVersion!));
                        return new Outcome(Staged: true, FailureMessage: null);
                    }

                    return new Outcome(
                        Staged: false,
                        EngineStaging.DescribeNewtonsoftUnwritable(
                            localPath, localVersion!, installPath, installVersion!, copyError));
            }
        }
        catch (Exception ex)
        {
            string unexpected =
                $"The engine-assembly staging preflight failed unexpectedly evaluating '{localPath}' " +
                $"against '{installPath}': {ex.Message}";
            return new Outcome(Staged: false, unexpected);
        }
    }

    private static Outcome Ensure(string consumerDir, string installDir)
    {
        (string ConsumerDir, string InstallDir) key = (Path.GetFullPath(consumerDir), Path.GetFullPath(installDir));
        return Outcomes.GetOrAdd(
            key,
            k => new Lazy<Outcome>(
                () => Evaluate(k.ConsumerDir, k.InstallDir, FindLoaded("VintagestoryAPI"), FindLoaded("Newtonsoft.Json")),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    /// <summary>Copies the install's dll+pdb pair over the consumer's, each through a same-
    /// directory temp file and an atomic rename so a concurrent process never observes a torn
    /// file; the pdb goes first so the dll never points at a mismatched pdb once swapped.</summary>
    /// <param name="installDir">The install directory (source).</param>
    /// <param name="consumerDir">The consumer directory (destination).</param>
    /// <returns><see langword="null"/> on success, otherwise the IO error message.</returns>
    private static string? TryCopyPair(string installDir, string consumerDir)
    {
        try
        {
            CopyAtomic(Path.Combine(installDir, ApiPdbName), Path.Combine(consumerDir, ApiPdbName));
            CopyAtomic(Path.Combine(installDir, ApiDllName), Path.Combine(consumerDir, ApiDllName));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    /// <summary>Copies one install file over one consumer file, through a same-directory temp
    /// file and an atomic rename, so a concurrent process never observes a torn file.</summary>
    /// <param name="source">The install file (source).</param>
    /// <param name="destination">The consumer file (destination).</param>
    /// <returns><see langword="null"/> on success, otherwise the IO error message.</returns>
    private static string? TryCopySingle(string source, string destination)
    {
        try
        {
            CopyAtomic(source, destination);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    private static void CopyAtomic(string source, string destination)
    {
        string temp = destination + ".atlas-staging";
        File.Copy(source, temp, overwrite: true);
        File.Move(temp, destination, overwrite: true);
    }

    private static LoadedAssembly? FindLoaded(string simpleName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic
                || !string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A loaded image without a readable backing file (byte-loaded, or deleted since)
            // gets a sentinel identity that can never match the install's, so the decision
            // fails safe rather than assuming the bytes are right.
            string location = assembly.Location;
            ApiCopySync.FileIdentity identity =
                (location.Length == 0 ? null : TryReadIdentity(location))
                ?? new ApiCopySync.FileIdentity(-1, "<unreadable>", null);
            return new LoadedAssembly(location.Length == 0 ? "<in-memory image>" : location, identity);
        }

        return null;
    }

    private static string? GetOwnAssemblyDirectory()
    {
        // Single-file publish leaves Location empty; the app-base pass already ran then.
        string location = typeof(EngineStager).Assembly.Location;
        return location.Length == 0 ? null : Path.GetDirectoryName(location);
    }

    /// <summary>The file one of the process's already-bound assemblies was loaded from.</summary>
    /// <param name="Path">The loaded file's path, or a placeholder for byte-loaded images.</param>
    /// <param name="Identity">The loaded file's identity; a sentinel that can never match the
    /// install's when the location is unreadable, so the decision fails safe.</param>
    internal readonly record struct LoadedAssembly(string Path, ApiCopySync.FileIdentity Identity);

    /// <summary>One evaluated staging outcome for a (consumer directory, install) pair.</summary>
    /// <param name="Staged">Whether the test-output copy was rewritten from the install.</param>
    /// <param name="FailureMessage">The setup error to surface at boot, or <see langword="null"/>
    /// when the process will bind the install's bytes.</param>
    internal sealed record Outcome(bool Staged, string? FailureMessage);
}
