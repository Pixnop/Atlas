using Atlas.Internal.Bootstrap;

namespace Atlas.Pure.Tests.Bootstrap;

public class VsInstallTests
{
    [Fact]
    public void Locate_Should_ThrowSetupException_When_EnvVarPointsNowhere()
    {
        string? saved = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        try
        {
            Environment.SetEnvironmentVariable("VINTAGE_STORY", @"C:\definitely\not\here");
            Assert.Throws<AtlasSetupException>(() => VsInstall.Locate());
        }
        finally
        {
            Environment.SetEnvironmentVariable("VINTAGE_STORY", saved);
        }
    }
}
