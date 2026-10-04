using Atlas.XUnit;

[assembly: AtlasMods("assembly-mod.dll")]
[assembly: AtlasDataFiles("assembly-data", TargetPath = "ModConfig")]
[assembly: AtlasAllowBootDiagnostic("assembly-level pattern", Required = true, Count = 2)]
