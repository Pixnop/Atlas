namespace Atlas.Internal.Bootstrap;

/// <summary>The pure decision core of the engine-assembly auto-staging preflight (issue #49):
/// decides, from file identities alone, how a consumer test output's <c>VintagestoryAPI.dll</c>
/// copy is brought in line with the VINTAGE_STORY install's copy before the CLR binds it, and
/// formats every outcome's message. Kept free of IO so the decision matrix and the messages are
/// testable without real files, loaded assemblies or read-only directories; the hashing, the
/// loaded-assembly lookup and the copies stay in the thin shell (<see cref="EngineStager"/>).</summary>
/// <remarks>Why staging instead of an in-process redirect: the test-output copy wins default
/// assembly probing (see build/Atlas.E2E.targets), and the redirect routes are all measurably
/// dead. <c>AssemblyLoadContext.Default.LoadFromAssemblyPath(installCopy)</c> defers to the
/// default binder, which prefers the app-path copy for a colliding simple name (measured on
/// .NET 10: it returns the test-output assembly, install bytes never load), and the resolve
/// events only fire once probing has already failed. Rewriting the test-output file itself,
/// before anything binds it, is therefore the only mechanism that keeps a single VintagestoryAPI
/// identity in the process AND makes it the install's bytes. It runs from module initializers
/// (registered in Atlas and Atlas.XUnit), which the CLR executes before the first engine-type
/// JIT in every supported flow, and the boot preflight verifies the result, failing fast with
/// these messages when staging was impossible or came too late.</remarks>
internal static class EngineStaging
{
    /// <summary>What the staging preflight must do for one consumer directory.</summary>
    internal enum StageAction
    {
        /// <summary>Nothing to do: no local copy shadows probing, no install copy exists to
        /// compare against, or the copies are already byte-identical.</summary>
        None,

        /// <summary>Rewrite the test-output copy (dll AND pdb, as a unit) from the install's.</summary>
        Stage,

        /// <summary>A diverged copy is already bound in the process: staging the file cannot fix
        /// this run anymore, only the next one. Fail with <see cref="DescribeLoadedStale"/>.</summary>
        FailLoadedStale,

        /// <summary>The install ships no <c>VintagestoryAPI.pdb</c>, so the pair cannot be staged
        /// without planting the opaque LoggerBase boot kill. Fail with
        /// <see cref="DescribeInstallPdbMissing"/>.</summary>
        FailInstallPdbMissing,
    }

    /// <summary>Decides the staging action for one consumer directory.</summary>
    /// <param name="local">Identity of the test-output copy, or <see langword="null"/> when the
    /// consumer ships none (Private=false: probing falls through to the install's own copy).</param>
    /// <param name="install">Identity of the install's copy, or <see langword="null"/> when the
    /// install ships none (nothing to compare against; a later boot failure then points at the
    /// broken install rather than at the local copy).</param>
    /// <param name="installPdbPresent">Whether the install ships <c>VintagestoryAPI.pdb</c> next
    /// to its dll; staging without it would trade one opaque boot death for another.</param>
    /// <param name="loaded">Identity of the file the process's already-bound VintagestoryAPI was
    /// loaded from, or <see langword="null"/> when the assembly is not loaded yet.</param>
    /// <returns>The action to take.</returns>
    public static StageAction Decide(
        ApiCopySync.FileIdentity? local,
        ApiCopySync.FileIdentity? install,
        bool installPdbPresent,
        ApiCopySync.FileIdentity? loaded)
    {
        if (local == null || install == null || ApiCopySync.AreIdentical(local, install))
        {
            return StageAction.None;
        }

        if (loaded != null && !ApiCopySync.AreIdentical(loaded, install))
        {
            return StageAction.FailLoadedStale;
        }

        // Not loaded yet (the normal early-trigger path), or already loaded FROM the install's
        // bytes (only the disk copy lags): staging the file makes both this run and the next
        // one coherent. Never stage a dll whose pdb cannot come along.
        return installPdbPresent ? StageAction.Stage : StageAction.FailInstallPdbMissing;
    }

    /// <summary>Formats the one-line stderr notice for a successful staging, so a cross-install
    /// run (and its CI lane) shows what was rewritten and from where.</summary>
    /// <param name="localPath">Path of the rewritten test-output copy.</param>
    /// <param name="local">Its identity before the rewrite.</param>
    /// <param name="installPath">Path of the install copy it was staged from.</param>
    /// <param name="install">The install copy's identity.</param>
    /// <returns>The notice.</returns>
    public static string DescribeStaged(
        string localPath, ApiCopySync.FileIdentity local, string installPath, ApiCopySync.FileIdentity install)
    {
        return $"[Atlas] staged VintagestoryAPI.dll and VintagestoryAPI.pdb from '{installPath}' " +
            $"({ApiCopySync.Describe(install)}) over the test-output copy '{localPath}' " +
            $"({ApiCopySync.Describe(local)}): the output was built against a different install " +
            "than VINTAGE_STORY points at (issue #49), and the prebuilt assemblies now run " +
            "against this install's bytes without a rebuild.";
    }

    /// <summary>Formats the setup error for a diverged copy that was already bound in the process
    /// before any staging trigger ran: in-process redirection is impossible (the loaded image
    /// cannot be swapped), so the run must fail; the message names both identities and every
    /// remedy, including the re-run when the disk copy was still re-staged for the next run.</summary>
    /// <param name="loadedPath">Path the process's VintagestoryAPI was loaded from.</param>
    /// <param name="loaded">The loaded file's identity.</param>
    /// <param name="installPath">Path of the install's copy.</param>
    /// <param name="install">The install copy's identity.</param>
    /// <param name="restaged">Whether the test-output copy was still rewritten, making a plain
    /// re-run (no rebuild) sufficient.</param>
    /// <returns>The complete error message.</returns>
    public static string DescribeLoadedStale(
        string loadedPath,
        ApiCopySync.FileIdentity loaded,
        string installPath,
        ApiCopySync.FileIdentity install,
        bool restaged)
    {
        string remedy = restaged
            ? "The test-output copy has now been re-staged from the install: re-run the tests " +
              "without rebuilding (or rebuild the test project against this install)."
            : "Rebuild the test project against this install, or copy the install's " +
              "VintagestoryAPI.dll AND VintagestoryAPI.pdb over the test-output copies.";
        return
            "VintagestoryAPI.dll was already loaded from '" + loadedPath + "' (" +
            ApiCopySync.Describe(loaded) + ") before Atlas's staging preflight could run, and it " +
            $"differs from the VINTAGE_STORY install's copy ('{installPath}', " +
            $"{ApiCopySync.Describe(install)}). A loaded assembly cannot be swapped in-process, " +
            "so this run would mix it with the install's VintagestoryLib and die with a cryptic " +
            "TypeLoadException or MissingFieldException. This happens when engine types are " +
            "JITted (typically a test body touching game types under a runner that executes no " +
            "Atlas code first) before any Atlas module initializer ran. " + remedy;
    }

    /// <summary>Formats the setup error for a diverged copy that could not be rewritten (read-only
    /// output, concurrent lock): probing would bind the stale bytes, so the boot must fail.</summary>
    /// <param name="localPath">Path of the stale test-output copy.</param>
    /// <param name="local">Its identity.</param>
    /// <param name="installPath">Path of the install's copy.</param>
    /// <param name="install">The install copy's identity.</param>
    /// <param name="reason">The IO error that defeated the rewrite.</param>
    /// <returns>The complete error message.</returns>
    public static string DescribeUnwritable(
        string localPath,
        ApiCopySync.FileIdentity local,
        string installPath,
        ApiCopySync.FileIdentity install,
        string reason)
    {
        return
            $"VintagestoryAPI.dll in the test output ('{localPath}', {ApiCopySync.Describe(local)}) " +
            $"differs from the VINTAGE_STORY install's copy ('{installPath}', " +
            $"{ApiCopySync.Describe(install)}), and auto-staging could not rewrite it: {reason}. " +
            "The test-output copy wins default assembly probing, so booting would mix it with the " +
            "install's VintagestoryLib and die with a cryptic TypeLoadException or " +
            "MissingFieldException. Make the test output writable, rebuild the test project " +
            "against this install, or copy the install's VintagestoryAPI.dll AND " +
            "VintagestoryAPI.pdb over the test-output copies.";
    }

    /// <summary>Formats the setup error for an install that ships <c>VintagestoryAPI.dll</c>
    /// without its pdb: staging the dll alone would replace the version mix with the opaque
    /// LoggerBase boot kill the pdb preflight exists to prevent.</summary>
    /// <param name="localPath">Path of the stale test-output copy.</param>
    /// <param name="installPath">Path of the install's dll (whose pdb is missing).</param>
    /// <returns>The complete error message.</returns>
    public static string DescribeInstallPdbMissing(string localPath, string installPath)
    {
        return
            $"VintagestoryAPI.dll in the test output ('{localPath}') differs from the " +
            $"VINTAGE_STORY install's copy ('{installPath}'), but the install ships no " +
            "VintagestoryAPI.pdb next to its dll, so Atlas cannot auto-stage the pair: the " +
            "game's logger derives source paths from pdb debug info during type initialization " +
            "and dies in an opaque TypeInitializationException without it. Restore the install's " +
            "VintagestoryAPI.pdb (a vanilla install always ships it), or rebuild the test project " +
            "against this install.";
    }

    /// <summary>Decides the staging action for the test output's game-shipped
    /// <c>Newtonsoft.Json.dll</c> copy, which is DIRECTION-aware where the API decision is not:
    /// the output's copy comes from the BUILD-time install (build/Atlas.E2E.targets), and only
    /// the older-than-install mix is fatal. The install's engine is compiled against its own
    /// (newer) game build and binds members that build added, so every boot dies in the engine's
    /// JSON serialization (measured: 1.22.3's API binds <c>JToken.WriteTo(JsonWriter)</c>, added
    /// in the game's 13.0.4 build, and a 1.21-built output carrying 13.0.3 failed 90/105 engine
    /// scenarios plus all samples at boot). The newer-than-install mix is a superset and runs
    /// green (the whole forward cross-install matrix was measured on it), so it must never be
    /// touched, and unorderable mixes (no file version on either side) fail open for the same
    /// reason.</summary>
    /// <param name="local">Identity of the test-output copy, or <see langword="null"/> when the
    /// consumer ships none (probing then falls through to the install's own copy).</param>
    /// <param name="install">Identity of the install's <c>Lib/Newtonsoft.Json.dll</c>, or
    /// <see langword="null"/> when the install ships none.</param>
    /// <param name="localFileVersion">The output copy's file version, or <see langword="null"/>
    /// when unreadable.</param>
    /// <param name="installFileVersion">The install copy's file version, or
    /// <see langword="null"/> when unreadable.</param>
    /// <param name="loaded">Identity of the file the process's already-bound Newtonsoft.Json was
    /// loaded from, or <see langword="null"/> when it is not loaded yet (the VSTest host binds it
    /// at process start; the `atlas run` host does not).</param>
    /// <returns>The action to take; never <see cref="StageAction.FailInstallPdbMissing"/>.</returns>
    public static StageAction DecideNewtonsoft(
        ApiCopySync.FileIdentity? local,
        ApiCopySync.FileIdentity? install,
        Version? localFileVersion,
        Version? installFileVersion,
        ApiCopySync.FileIdentity? loaded)
    {
        if (local == null || install == null || ApiCopySync.AreIdentical(local, install))
        {
            return StageAction.None;
        }

        if (localFileVersion == null || installFileVersion == null
            || localFileVersion >= installFileVersion)
        {
            // Fail open: the output carries the same-or-newer game build (the measured-green
            // forward mix), or the files carry no orderable version information at all.
            return StageAction.None;
        }

        if (loaded != null && !ApiCopySync.AreIdentical(loaded, install))
        {
            return StageAction.FailLoadedStale;
        }

        return StageAction.Stage;
    }

    /// <summary>Formats the one-line stderr notice for a staged Newtonsoft copy.</summary>
    /// <param name="localPath">Path of the rewritten test-output copy.</param>
    /// <param name="localFileVersion">Its file version before the rewrite.</param>
    /// <param name="installPath">Path of the install copy it was staged from.</param>
    /// <param name="installFileVersion">The install copy's file version.</param>
    /// <returns>The notice.</returns>
    public static string DescribeNewtonsoftStaged(
        string localPath, Version localFileVersion, string installPath, Version installFileVersion)
    {
        return $"[Atlas] staged Newtonsoft.Json.dll from '{installPath}' (file version " +
            $"{installFileVersion}) over the test-output copy '{localPath}' (file version " +
            $"{localFileVersion}): the output carried an older game build than the " +
            "VINTAGE_STORY install ships, and the install's engine binds members the newer " +
            "build added (issue #49).";
    }

    /// <summary>Formats the setup error for an older game Newtonsoft build that was already
    /// bound in the process before any staging trigger ran: the VSTest host binds the
    /// test-output copy at process start for its own protocol, strictly before any Atlas code
    /// can execute, so in-process staging is impossible by construction there.</summary>
    /// <param name="loadedPath">Path the process's Newtonsoft.Json was loaded from.</param>
    /// <param name="loadedFileVersion">The loaded copy's file version, or <see langword="null"/>.</param>
    /// <param name="installPath">Path of the install's copy.</param>
    /// <param name="installFileVersion">The install copy's file version.</param>
    /// <param name="restaged">Whether the test-output copy was still rewritten, making a plain
    /// re-run (no rebuild) sufficient.</param>
    /// <returns>The complete error message.</returns>
    public static string DescribeNewtonsoftLoadedStale(
        string loadedPath,
        Version? loadedFileVersion,
        string installPath,
        Version installFileVersion,
        bool restaged)
    {
        string remedy = restaged
            ? "The test-output copy has now been re-staged from the install: re-run the tests " +
              "without rebuilding (or rebuild the test project against this install)."
            : "Rebuild the test project against this install, or copy the install's " +
              "Lib/Newtonsoft.Json.dll over the test-output copy.";
        return
            $"Newtonsoft.Json.dll was already loaded from '{loadedPath}' (file version " +
            $"{loadedFileVersion?.ToString() ?? "unknown"}) before Atlas's staging preflight " +
            $"could run, and it is an older game build than the VINTAGE_STORY install's copy " +
            $"('{installPath}', file version {installFileVersion}). The install's engine is " +
            "compiled against its own build and binds members the newer build added (for " +
            "example JToken.WriteTo(JsonWriter), added in the game's 13.0.4 build and bound by " +
            "the 1.22 API), so booting would die with a cryptic MissingMethodException inside " +
            "the engine's JSON serialization. The test host binds this assembly at process " +
            "start for its own protocol, so it can never be swapped in-process. " + remedy;
    }

    /// <summary>Formats the setup error for an older Newtonsoft copy that could not be rewritten
    /// (read-only output, concurrent lock).</summary>
    /// <param name="localPath">Path of the stale test-output copy.</param>
    /// <param name="localFileVersion">Its file version.</param>
    /// <param name="installPath">Path of the install's copy.</param>
    /// <param name="installFileVersion">The install copy's file version.</param>
    /// <param name="reason">The IO error that defeated the rewrite.</param>
    /// <returns>The complete error message.</returns>
    public static string DescribeNewtonsoftUnwritable(
        string localPath,
        Version localFileVersion,
        string installPath,
        Version installFileVersion,
        string reason)
    {
        return
            $"Newtonsoft.Json.dll in the test output ('{localPath}', file version " +
            $"{localFileVersion}) is an older game build than the VINTAGE_STORY install's copy " +
            $"('{installPath}', file version {installFileVersion}), and auto-staging could not " +
            $"rewrite it: {reason}. The install's engine binds members the newer build added, " +
            "so booting would die with a cryptic MissingMethodException inside the engine's " +
            "JSON serialization. Make the test output writable, rebuild the test project " +
            "against this install, or copy the install's Lib/Newtonsoft.Json.dll over the " +
            "test-output copy.";
    }
}
