using Atlas.XUnit;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Relative to the test assembly output dir: samples/Sample.Scenarios/bin/<Config>/net10.0/
// -> up 4 (net10.0, <Config>, bin, Sample.Scenarios) -> samples/ -> SampleMod.
[assembly: AtlasMods("../../../../SampleMod")]
