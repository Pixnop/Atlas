using System.Reflection;

namespace Atlas.Cli;

/// <summary>Loads the `Atlas` assembly (and any dependency the staging call needs) from the
/// `atlas stage` target directory's own copy, so the command reuses the exact decision code
/// (<c>Atlas.Internal.Bootstrap.EngineStager</c>/<c>EngineStaging</c>) that directory's own test
/// run would use, instead of the CLI shipping a second copy (which would shadow a scenario's own
/// Atlas.dll during `atlas run`'s default probing: the version hazard
/// <see cref="ScenarioAssemblyResolver"/> already exists to avoid).</summary>
/// <remarks>Deliberately loads by BYTES (<see cref="Assembly.Load(byte[])"/>), not by path the
/// way <see cref="ScenarioAssemblyResolver"/> does: a byte-loaded assembly reports an empty
/// <see cref="Assembly.Location"/>. Atlas's own module initializer
/// (<c>GameEnvironment.TryRegisterResolveHookEarly</c>) runs a best-effort staging pass the
/// instant anything in the Atlas module is first touched, against BOTH the process's
/// <c>AppContext.BaseDirectory</c> (always the CLI's own directory here, which never carries a
/// local VintagestoryAPI.dll, so that pass is inert) AND "the assembly's own directory"
/// (<c>Assembly.Location</c>'s directory). Loading by path would make that second directory the
/// stage target itself, so the module initializer's inert-by-design pass would silently perform
/// the real staging BEFORE this command's own explicit, reportable call ever ran, and that call
/// would then observe an already-staged, unreportable no-op. Loading by bytes makes
/// <c>Assembly.Location</c> empty, so the module initializer skips that second pass entirely,
/// leaving this command's own call as the only one that ever touches the target directory.</remarks>
internal sealed class StageAssemblyResolver : IDisposable
{
    private readonly string _directory;

    /// <summary>Initializes a new instance of the <see cref="StageAssemblyResolver"/> class and
    /// installs the resolve hook.</summary>
    /// <param name="directory">The stage target directory.</param>
    public StageAssemblyResolver(string directory)
    {
        _directory = directory;
        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    /// <summary>Uninstalls the resolve hook.</summary>
    public void Dispose() => AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;

    private Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        string? path = ScenarioAssemblyResolver.ResolvePath(args.Name, _directory, File.Exists);
        return path is null ? null : Assembly.Load(File.ReadAllBytes(path));
    }
}
