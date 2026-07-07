using System.Collections.Concurrent;
using Atlas.Cli;

namespace Atlas.Pure.Tests.Cli;

public class ClassWorkQueueTests
{
    [Fact]
    public void TryTake_Should_YieldClassesInDiscoveryOrder_When_DrainedSequentially()
    {
        var queue = new ClassWorkQueue(["Ns.A", "Ns.B", "Ns.C"]);
        var taken = new List<string>();

        while (queue.TryTake(out string? className))
        {
            taken.Add(className!);
        }

        Assert.Equal(["Ns.A", "Ns.B", "Ns.C"], taken);
    }

    [Fact]
    public void TryTake_Should_ReturnFalseWithNullClass_When_QueueIsEmpty()
    {
        var queue = new ClassWorkQueue([]);

        Assert.False(queue.TryTake(out string? className));
        Assert.Null(className);
    }

    [Fact]
    public async Task TryTake_Should_DispatchEveryClassExactlyOnce_When_DrainedConcurrently()
    {
        List<string> classes = Enumerable.Range(0, 500).Select(i => $"Ns.Class{i}").ToList();
        var queue = new ClassWorkQueue(classes);
        var taken = new ConcurrentBag<string>();

        IEnumerable<Task> workers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            while (queue.TryTake(out string? className))
            {
                taken.Add(className!);
            }
        }));
        await Task.WhenAll(workers);

        Assert.Equal(classes.Count, taken.Count);
        Assert.Equal(classes.Order(StringComparer.Ordinal), taken.Order(StringComparer.Ordinal));
    }
}
