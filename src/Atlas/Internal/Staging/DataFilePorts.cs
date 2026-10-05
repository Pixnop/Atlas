using System.Globalization;
using System.Text.RegularExpressions;
using Atlas.Api;
using Atlas.Internal.Hosting;

namespace Atlas.Internal.Staging;

/// <summary>The ports behind the <c>{{atlas:port:NAME}}</c> tokens of one host's seeded data
/// files: resolves a token to a free port the first time its name is seen, keeps that port for
/// every later mention, and answers the scenario's lookup by name.</summary>
/// <remarks>Owned by the host, not by the per-scenario session: the seeding runs once, before the
/// boot, and every scenario of the host reads what it drew. A new host (a recycle or a restart)
/// seeds again and draws again. Not thread-safe: the seeding and the lookups both run on the game
/// thread, one after the other.</remarks>
internal sealed class DataFilePorts
{
    /// <summary>The size of the window <see cref="MayHoldToken(Stream)"/> reads a stream through,
    /// so the memory a scan takes does not grow with the file.</summary>
    internal const int ScanBufferSize = 64 * 1024;

    /// <summary>What every Atlas token starts with. A text that holds it and no recognized token
    /// is a typo, and fails instead of reaching the mod with the braces still in it.</summary>
    private const string Prefix = "{{atlas:";

    private const string Form = "{{atlas:port:NAME}}, where NAME is made of letters, digits, '_', '.' and '-'";

    private const int RegexTimeoutMs = 100;

    private static readonly Regex PortToken = new(
        @"\{\{atlas:port:([A-Za-z0-9_.\-]+)\}\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(RegexTimeoutMs));

    private readonly Dictionary<string, int> _ports = new(StringComparer.Ordinal);
    private readonly Func<ICollection<int>, int> _draw;

    /// <summary>Initializes a new instance of the <see cref="DataFilePorts"/> class.</summary>
    /// <param name="draw">Draws a port, given the ones already handed out; the default is
    /// <see cref="FreePort.Find(ICollection{int})"/>. Test seam.</param>
    internal DataFilePorts(Func<ICollection<int>, int>? draw = null)
        => _draw = draw ?? (taken => FreePort.Find(taken));

    /// <summary>Tells whether a file's bytes may hold a token, so a file that cannot is copied
    /// as it is, never decoded.</summary>
    /// <param name="bytes">The file's content.</param>
    /// <returns><see langword="true"/> when the token prefix is in the bytes.</returns>
    internal static bool MayHoldToken(ReadOnlySpan<byte> bytes) => bytes.IndexOf("{{atlas:"u8) >= 0;

    /// <summary>The same as <see cref="MayHoldToken(ReadOnlySpan{byte})"/> for a stream, read
    /// through a fixed window instead of whole: a seeded file can be as large as a world save.
    /// The end of each window is carried into the next, so a prefix split by a read is found.</summary>
    /// <param name="stream">The file's content, read from its current position to the end.</param>
    /// <returns><see langword="true"/> when the token prefix is in the stream.</returns>
    internal static bool MayHoldToken(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int carry = "{{atlas:"u8.Length - 1;
        byte[] window = new byte[ScanBufferSize];
        int kept = 0;
        int read;
        while ((read = stream.Read(window, kept, window.Length - kept)) > 0)
        {
            int filled = kept + read;
            if (MayHoldToken(window.AsSpan(0, filled)))
            {
                return true;
            }

            kept = Math.Min(carry, filled);
            window.AsSpan(filled - kept, kept).CopyTo(window);
        }

        return false;
    }

    /// <summary>Replaces every <c>{{atlas:port:NAME}}</c> token of a text with its port.</summary>
    /// <param name="text">The decoded content of a data file.</param>
    /// <param name="file">The file's path, named in the error.</param>
    /// <returns>The text with each token replaced by a decimal port.</returns>
    /// <exception cref="AtlasSetupException">Thrown when the text holds the token prefix in a
    /// form that is not a port token (a typo in the kind or the name), or when no free port
    /// could be drawn.</exception>
    internal string Resolve(string text, string file)
    {
        string resolved = PortToken.Replace(
            text, match => PortFor(match.Groups[1].Value).ToString(CultureInfo.InvariantCulture));
        int stray = resolved.IndexOf(Prefix, StringComparison.Ordinal);
        if (stray >= 0)
        {
            int end = resolved.IndexOf("}}", stray, StringComparison.Ordinal);
            int length = end < 0 || end + 2 - stray > 40 ? Math.Min(40, resolved.Length - stray) : end + 2 - stray;
            string shown = resolved.Substring(stray, length).ReplaceLineEndings(" ");
            throw new AtlasSetupException(
                $"Data file '{file}' holds '{shown}', which is not a port token. The only token is {Form}.");
        }

        return resolved;
    }

    /// <summary>Gets the port a token name got.</summary>
    /// <param name="name">The name of the token, as written between <c>port:</c> and
    /// <c>}}</c>.</param>
    /// <returns>The port.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is
    /// <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when no seeded file used the name; the message
    /// names the ones that were used.</exception>
    internal int PortOf(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_ports.TryGetValue(name, out int port))
        {
            return port;
        }

        string known = _ports.Count == 0
            ? "No seeded data file holds a {{atlas:port:NAME}} token (declare files with [AtlasDataFiles])."
            : "The names the seeded data files use: " + string.Join(", ", _ports.Keys.Order(StringComparer.Ordinal)) + ".";
        throw new ArgumentException($"No data file port is named '{name}'. {known}", nameof(name));
    }

    private int PortFor(string name)
    {
        if (!_ports.TryGetValue(name, out int port))
        {
            port = _draw(_ports.Values);
            _ports[name] = port;
        }

        return port;
    }
}
