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

    [Theory]
    [InlineData("noprivilege", "noprivilege")]
    [InlineData("nosuchcommand", "nosuchcommand")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ErrorCode_Should_ReadTheRawResultsErrorCode_And_NeverBeNull(string? raw, string expected)
    {
        // The engine's Success results carry no error code at all (the field stays null), and its
        // own refusals that name none leave it empty: both read as empty here.
        var result = new CommandResult(false, "message", new TextCommandResult { Status = EnumCommandStatus.Error, ErrorCode = raw! });

        Assert.Equal(expected, result.ErrorCode);
    }

    [Fact]
    public void Deconstruct_Should_StillYieldOkMessageAndRaw_When_StatusAndErrorCodeAreAdded()
    {
        var raw = new TextCommandResult { Status = EnumCommandStatus.Error };

        (bool ok, string message, TextCommandResult deconstructed) = new CommandResult(false, "message", raw);

        Assert.False(ok);
        Assert.Equal("message", message);
        Assert.Same(raw, deconstructed);
    }
}
