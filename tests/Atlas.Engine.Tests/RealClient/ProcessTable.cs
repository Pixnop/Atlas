using System.Globalization;

namespace Atlas.Engine.Tests.RealClient;

/// <summary>A reader of the process table through <c>/proc</c>, for tests that must know which
/// processes a sandbox started and whether any of them is still there. A process is identified by
/// its pid and its start time, so a recycled pid is never taken for a survivor.</summary>
internal static class ProcessTable
{
    /// <summary>The processes below <paramref name="root"/> (children, their children and so
    /// on), in breadth-first order, not including the root itself.</summary>
    /// <param name="root">The pid to start from.</param>
    /// <returns>The descendants alive at the time of the scan.</returns>
    public static IReadOnlyList<Entry> Descendants(int root)
    {
        var children = new Dictionary<int, List<Entry>>();
        foreach (string dir in Directory.EnumerateDirectories("/proc"))
        {
            if (int.TryParse(Path.GetFileName(dir), CultureInfo.InvariantCulture, out int pid)
                && Read(pid) is { } entry)
            {
                if (!children.TryGetValue(entry.ParentPid, out List<Entry>? list))
                {
                    children[entry.ParentPid] = list = [];
                }

                list.Add(entry);
            }
        }

        var found = new List<Entry>();
        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            if (children.TryGetValue(queue.Dequeue(), out List<Entry>? kids))
            {
                found.AddRange(kids);
                kids.ForEach(kid => queue.Enqueue(kid.Pid));
            }
        }

        return found;
    }

    /// <summary>Reads one process.</summary>
    /// <param name="pid">The pid.</param>
    /// <returns>The process, or <see langword="null"/> when there is none (or it ended during
    /// the read).</returns>
    public static Entry? Read(int pid)
    {
        try
        {
            string stat = File.ReadAllText($"/proc/{pid}/stat");
            int open = stat.IndexOf('(', StringComparison.Ordinal);
            int close = stat.LastIndexOf(')');
            string[] rest = stat[(close + 2)..].Split(' ');
            return new Entry(
                pid,
                stat[(open + 1)..close],
                int.Parse(rest[1], CultureInfo.InvariantCulture),
                long.Parse(rest[19], CultureInfo.InvariantCulture));
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>Whether the very process <paramref name="entry"/> describes is still there.</summary>
    /// <param name="entry">A process read earlier.</param>
    /// <returns>Whether it is alive (a zombie awaiting its reaper counts as gone).</returns>
    public static bool IsAlive(Entry entry)
        => Read(entry.Pid) is { } now && now.StartTime == entry.StartTime && !IsZombie(entry.Pid);

    private static bool IsZombie(int pid)
    {
        try
        {
            return File.ReadLines($"/proc/{pid}/status").Any(line => line == "State:\tZ (zombie)");
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>One process.</summary>
    /// <param name="Pid">Its pid.</param>
    /// <param name="Comm">Its command name, as the kernel keeps it (the 15 first characters).</param>
    /// <param name="ParentPid">Its parent's pid.</param>
    /// <param name="StartTime">Its start time in clock ticks since boot.</param>
    internal readonly record struct Entry(int Pid, string Comm, int ParentPid, long StartTime);
}
