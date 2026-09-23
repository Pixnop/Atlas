namespace DependencyLibraryFixture;

// A plain dependency: no ModSystem, no ModInfoAttribute. Staged as a "mod" by
// BootDiagnosticsTests to reproduce the engine's "declared as code mod" failure.
public sealed class SharedType
{
    public int Value => 1;
}
