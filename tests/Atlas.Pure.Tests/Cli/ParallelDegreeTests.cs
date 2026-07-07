using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class ParallelDegreeTests
{
    [Fact]
    public void Resolve_Should_UseExplicitRequest_When_OneIsGiven()
    {
        Assert.Equal(3, ParallelDegree.Resolve(requested: 3, processorCount: 16, classCount: 10));
    }

    [Fact]
    public void Resolve_Should_CapExplicitRequestAtClassCount_When_RequestExceedsIt()
    {
        Assert.Equal(3, ParallelDegree.Resolve(requested: 8, processorCount: 16, classCount: 3));
    }

    [Fact]
    public void Resolve_Should_DefaultToHalfTheProcessors_When_NoRequestGiven()
    {
        Assert.Equal(4, ParallelDegree.Resolve(requested: null, processorCount: 8, classCount: 10));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Resolve_Should_DefaultToOne_When_MachineHasFewProcessors(int processorCount)
    {
        Assert.Equal(1, ParallelDegree.Resolve(requested: null, processorCount, classCount: 10));
    }

    [Fact]
    public void Resolve_Should_CapDefaultAtClassCount_When_ClassesAreScarce()
    {
        Assert.Equal(2, ParallelDegree.Resolve(requested: null, processorCount: 16, classCount: 2));
    }

    [Fact]
    public void Resolve_Should_ReturnOne_When_ThereAreNoClasses()
    {
        Assert.Equal(1, ParallelDegree.Resolve(requested: null, processorCount: 16, classCount: 0));
    }
}
