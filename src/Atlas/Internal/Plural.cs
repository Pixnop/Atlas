using System.Globalization;

namespace Atlas.Internal;

/// <summary>Agrees a noun with its count in a message, so a count of one never reads "1 ticks".</summary>
internal static class Plural
{
    /// <summary>Words a count with its noun.</summary>
    /// <param name="count">How many there are.</param>
    /// <param name="singular">The noun for exactly one.</param>
    /// <param name="plural">The noun for any other count; <paramref name="singular"/> followed by
    /// an <c>s</c> when left out.</param>
    /// <returns>The count, a space, then the noun in the form that agrees with it.</returns>
    public static string Of(int count, string singular, string? plural = null)
        => $"{count.ToString(CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";
}
