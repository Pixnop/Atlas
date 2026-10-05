using System.Reflection;
using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: AtlasRequireCompiledGameVersion]
[assembly: AssemblyMetadata("Atlas.CompiledGameVersion", RequiredVersionScenarios.StampedVersion)]
