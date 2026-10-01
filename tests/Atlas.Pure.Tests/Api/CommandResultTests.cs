using Atlas.Api;
using Vintagestory.API.Common;

namespace Atlas.Pure.Tests.Api;

public class CommandResultTests
{
    [Theory]
    [InlineData(EnumCommandStatus.Success)]
    [InlineData(EnumCommandStatus.Error)]
    [InlineData(EnumCommandStatus.NoSuchCommand)]
    [InlineData(EnumCommandStatus.UnknownLegacy)]
    public void Status_Should_ReadTheRawResultsStatus(EnumCommandStatus status)
    {
        var result = new CommandResult(false, "message", new TextCommandResult { Status = status });

        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void Deconstruct_Should_StillYieldOkMessageAndRaw_When_StatusIsAdded()
    {
        var raw = new TextCommandResult { Status = EnumCommandStatus.Error };

        (bool ok, string message, TextCommandResult deconstructed) = new CommandResult(false, "message", raw);

        Assert.False(ok);
        Assert.Equal("message", message);
        Assert.Same(raw, deconstructed);
    }
}
