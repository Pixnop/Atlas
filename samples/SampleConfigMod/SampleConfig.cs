namespace SampleConfigMod;

/// <summary>Config POCO read from <c>ModConfig/sampleconfig.json</c>.</summary>
public sealed class SampleConfig
{
    public string Greeting { get; set; } = "greeting-not-set";
}
