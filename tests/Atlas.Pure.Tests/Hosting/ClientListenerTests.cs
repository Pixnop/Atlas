using System.Net;
using System.Net.Sockets;
using Atlas.Internal.Hosting;

namespace Atlas.Pure.Tests.Hosting;

/// <summary>The parts of the loopback client listener that need no engine: the endpoint's shape,
/// the password and its removal from a saved config, the port choice and the retry loop. What
/// the engine does with the sockets is covered by <c>ClientListenerTests</c> in the engine
/// suite.</summary>
public class ClientListenerTests : IDisposable
{
    private const string Password = "0123456789ABCDEF0123456789ABCDEF";

    private readonly DirectoryInfo _dataPath = Directory.CreateTempSubdirectory("atlas-listener-config");

    public void Dispose()
    {
        _dataPath.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Loopback_Should_BeTheIpv4LoopbackAddress_When_Read()
        => Assert.Equal(IPAddress.Loopback, IPAddress.Parse(ClientListener.Loopback));

    [Fact]
    public void Address_Should_JoinHostAndPort_When_Formatted()
        => Assert.Equal("127.0.0.1:41234", new ClientEndpoint("127.0.0.1", 41234, "pw").Address);

    [Fact]
    public void NewPassword_Should_Be32HexDigits_When_Generated()
    {
        string password = ClientListener.NewPassword();

        Assert.Equal(32, password.Length);
        Assert.All(password, c => Assert.True(Uri.IsHexDigit(c), $"'{c}' is not a hex digit"));
    }

    [Fact]
    public void NewPassword_Should_DifferEachTime_When_GeneratedRepeatedly()
        => Assert.Equal(50, Enumerable.Range(0, 50).Select(_ => ClientListener.NewPassword()).Distinct().Count());

    [Fact]
    public void FindFreePort_Should_ReturnAPortBothProtocolsCanBind_When_Asked()
    {
        for (int i = 0; i < 20; i++)
        {
            int port = ClientListener.FindFreePort();

            Assert.InRange(port, 1024, 65535);
            var tcp = new TcpListener(IPAddress.Loopback, port);
            tcp.Start();
            tcp.Stop();
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        }
    }

    [Fact]
    public void Bind_Should_ReturnTheFirstSuccess_When_TheFirstCandidateBinds()
    {
        var offered = new List<int>();
        int ports = 100;

        int bound = ClientListener.Bind(
            () => ports++,
            port =>
            {
                offered.Add(port);
                return (int?)port;
            });

        Assert.Equal(100, bound);
        Assert.Equal([100], offered);
    }

    [Fact]
    public void Bind_Should_TryTheNextCandidate_When_TheFirstIsTaken()
    {
        var offered = new List<int>();
        int ports = 100;

        int bound = ClientListener.Bind(
            () => ports++,
            port =>
            {
                offered.Add(port);
                return port >= 102 ? port : (int?)null;
            });

        Assert.Equal(102, bound);
        Assert.Equal([100, 101, 102], offered);
    }

    [Fact]
    public void Bind_Should_GiveUpWithASetupError_When_EveryCandidateIsTaken()
    {
        int attempts = 0;

        AtlasSetupException ex = Assert.Throws<AtlasSetupException>(
            () => ClientListener.Bind(
                () => 5,
                _ =>
                {
                    attempts++;
                    return (int?)null;
                }));

        Assert.Equal(ClientListener.MaxPortAttempts, attempts);
        Assert.Contains($"{ClientListener.MaxPortAttempts} attempts", ex.Message);
    }

    [Fact]
    public void ScrubPassword_Should_BlankThePasswordAndLeaveTheRestAlone_When_TheEngineSavedIt()
    {
        string saved = $"{{\n  \"ServerName\": \"Atlas \u00e9\",\n  \"Password\": \"{Password}\",\n  \"MaxClients\": 16\n}}";
        string file = Path.Combine(_dataPath.FullName, ClientListener.ConfigFileName);
        File.WriteAllText(file, saved);

        bool clean = ClientListener.ScrubPassword(_dataPath.FullName, Password);

        Assert.True(clean);
        Assert.Equal(saved.Replace(Password, string.Empty, StringComparison.Ordinal), File.ReadAllText(file));
        Assert.DoesNotContain(Password, File.ReadAllText(file));
        Assert.Contains("\"Password\": \"\",", File.ReadAllText(file));
    }

    [Fact]
    public void ScrubPassword_Should_BlankEveryCopy_When_TheConfigHoldsItMoreThanOnce()
    {
        string file = Path.Combine(_dataPath.FullName, ClientListener.ConfigFileName);
        File.WriteAllText(file, $"{{\"Password\": \"{Password}\", \"Other\": \"x{Password}y\"}}");

        Assert.True(ClientListener.ScrubPassword(_dataPath.FullName, Password));

        Assert.Equal("{\"Password\": \"\", \"Other\": \"xy\"}", File.ReadAllText(file));
    }

    [Fact]
    public void ScrubPassword_Should_ScrubAFileThatIsNotValidJson_When_ItHoldsThePassword()
    {
        string file = Path.Combine(_dataPath.FullName, ClientListener.ConfigFileName);
        File.WriteAllText(file, $"{{\"Password\": \"{Password}\", \"Roles\": [");

        Assert.True(ClientListener.ScrubPassword(_dataPath.FullName, Password));

        Assert.Equal("{\"Password\": \"\", \"Roles\": [", File.ReadAllText(file));
    }

    [Fact]
    public void ScrubPassword_Should_LeaveAConfigWithoutThePasswordUntouched_When_Called()
    {
        string file = Path.Combine(_dataPath.FullName, ClientListener.ConfigFileName);
        File.WriteAllText(file, "{\"Password\": null}");
        DateTime written = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, written);

        Assert.True(ClientListener.ScrubPassword(_dataPath.FullName, Password));

        Assert.Equal("{\"Password\": null}", File.ReadAllText(file));
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void ScrubPassword_Should_ReportCleanAndCreateNothing_When_NoConfigWasSaved()
    {
        Assert.True(ClientListener.ScrubPassword(_dataPath.FullName, Password));
        Assert.True(ClientListener.ScrubPassword(Path.Combine(_dataPath.FullName, "not-there"), Password));

        Assert.Empty(_dataPath.GetFileSystemInfos());
    }

    [Fact]
    public void ScrubPassword_Should_ReportCleanWithoutTouchingTheFile_When_ThereIsNoPasswordToLookFor()
    {
        string file = Path.Combine(_dataPath.FullName, ClientListener.ConfigFileName);
        File.WriteAllText(file, "{\"Password\": \"\"}");

        Assert.True(ClientListener.ScrubPassword(_dataPath.FullName, string.Empty));

        Assert.Equal("{\"Password\": \"\"}", File.ReadAllText(file));
    }

    [Fact]
    public void ScrubPassword_Should_ReportItAndNotThrow_When_TheConfigCannotBeRewritten()
    {
        // Root writes through a read-only file, which would turn this into a different test.
        if (Environment.IsPrivilegedProcess)
        {
            return;
        }

        string file = Path.Combine(_dataPath.FullName, ClientListener.ConfigFileName);
        string saved = $"{{\"Password\": \"{Password}\"}}";
        File.WriteAllText(file, saved);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        bool clean = true;
        string stderr;
        try
        {
            stderr = CaptureStderr(() => clean = ClientListener.ScrubPassword(_dataPath.FullName, Password));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Assert.False(clean);
        Assert.Equal(saved, File.ReadAllText(file));

        // The line names the file and the failure, and never the password itself.
        Assert.Contains("could not remove the client listener's password", stderr);
        Assert.Contains(file, stderr);
        Assert.DoesNotContain(Password, stderr);
    }

    /// <summary>Runs <paramref name="action"/> with stderr redirected and returns what it wrote.</summary>
    private static string CaptureStderr(Action action)
    {
        var capture = new StringWriter();
        TextWriter real = Console.Error;
        try
        {
            Console.SetError(capture);
            action();
        }
        finally
        {
            Console.SetError(real);
        }

        return capture.ToString();
    }
}
