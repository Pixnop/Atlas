using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class WorkerCommandTests
{
    private const string CliDll = "/opt/atlas/Atlas.Cli.dll";

    [Fact]
    public void Resolve_Should_ReinvokeTheMuxerWithTheDll_When_HostedByDotnet()
    {
        WorkerCommand command = WorkerCommand.Resolve("/usr/lib/dotnet/dotnet", CliDll);

        Assert.Equal("/usr/lib/dotnet/dotnet", command.FileName);
        Assert.Equal([CliDll], command.LeadingArguments);
    }

    [Fact]
    public void Resolve_Should_DetectTheMuxer_When_ProcessPathHasAnExeExtension()
    {
        WorkerCommand command = WorkerCommand.Resolve("/mnt/c/dotnet/dotnet.exe", CliDll);

        Assert.Equal("/mnt/c/dotnet/dotnet.exe", command.FileName);
        Assert.Equal([CliDll], command.LeadingArguments);
    }

    [Fact]
    public void Resolve_Should_ReinvokeTheProcessImage_When_RunningAsAnApphostOrToolShim()
    {
        WorkerCommand command = WorkerCommand.Resolve("/home/user/.dotnet/tools/atlas", CliDll);

        Assert.Equal("/home/user/.dotnet/tools/atlas", command.FileName);
        Assert.Empty(command.LeadingArguments);
    }

    [Fact]
    public void Resolve_Should_FallBackToDotnetOnThePath_When_ProcessPathIsUnknown()
    {
        WorkerCommand command = WorkerCommand.Resolve(processPath: null, CliDll);

        Assert.Equal("dotnet", command.FileName);
        Assert.Equal([CliDll], command.LeadingArguments);
    }
}
