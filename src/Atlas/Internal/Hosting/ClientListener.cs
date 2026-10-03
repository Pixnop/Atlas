using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Atlas.Api;
using Vintagestory.Server;
using Vintagestory.Server.Network;

namespace Atlas.Internal.Hosting;

/// <summary>Opens, on an embedded (non dedicated) server that already finished <c>Launch()</c>,
/// the two listeners the engine's dedicated server opens for real clients, bound to the loopback
/// address only.</summary>
/// <remarks><para>What the engine does for a dedicated server (<c>ServerMain.AfterConfigLoaded</c>
/// and <c>startSockets</c>, identical on 1.20.12, 1.21.7, 1.22.3 and 1.22.7): put a
/// <see cref="TcpNetServer"/> in <c>MainSockets[1]</c> and a <see cref="UdpNetServer"/> in
/// <c>UdpSockets[1]</c>, give both the same address and port, and start them. The game's own
/// <c>/allowlan</c> command runs the same recipe on a singleplayer server, so this is the engine's
/// own way to open a non dedicated server to real connections. Slot 1 is the one
/// <see cref="Player.DummyClientConnector"/> never claims (see its <c>EngineTcpSlot</c>), so test
/// players and a real client coexist: the dummies ride <c>MainSockets[0]</c>, the slots grown past
/// 1 and <c>UdpSockets[0]</c>; the real client rides slot 1 of both.</para>
/// <para>Three guards keep it from being a server open to the network. The listeners bind to
/// <see cref="Loopback"/> and not to the engine's default (every interface, which is what a
/// <c>null</c> address means to it). The engine then asks every non dummy connection for a random
/// password, generated per opening. And nothing calls <see cref="Open(ServerMain)"/> unless the
/// host was told to (<c>ServerHost.OpenClientListener</c>). The password is a guard against
/// another program on the machine connecting by accident or by port scan, not against a hostile
/// local user: it travels on the launched client's command line.</para>
/// <para>Where the password must not linger. The password is set on the live config, and the
/// engine writes that config to <c>serverconfig.json</c> in the data path whenever it saves it
/// (measured: within seconds of a test player joining, with nothing asking for it), password
/// included. The engine gives no way to keep one field out of that file, and a scratch directory
/// kept as a post-mortem (a red class, a crash, <c>ATLAS_KEEP_SCRATCH</c>) would keep the copy.
/// <see cref="ScrubPassword"/> therefore blanks it in the file once the host has stopped, from
/// <c>ServerHost.DisposeAsync</c>, whether or not the scratch is going to be kept. The listener
/// itself is untouched: the password still guards the join for as long as the host runs.</para>
/// <para><c>VerifyPlayerAuth</c> is switched off in the same breath, because a real client
/// presents the session token of an account the embedded host has no authentication server to
/// ask about. The dummy connections never went through that check (<c>IsSinglePlayerClient</c>
/// skips it), so they are unaffected. Nothing closes the listeners explicitly:
/// <c>ServerMain.Dispose</c> disposes every entry of both socket arrays, which is what releases
/// the port when the host goes.</para>
/// <para>Every engine type and member named here is public and present on the supported
/// versions; <c>EngineCompat.ValidateClientListener</c> checks that before the first call, so a
/// version without them fails with a named symbol instead of a <see cref="TypeLoadException"/>.</para></remarks>
internal static class ClientListener
{
    /// <summary>The only address the listeners bind to.</summary>
    internal const string Loopback = "127.0.0.1";

    /// <summary>The engine's slot for its real listeners, in both socket arrays.</summary>
    internal const int Slot = 1;

    /// <summary>How many ports are tried, one start attempt each, and how many ephemeral TCP
    /// ports <see cref="FindFreePort"/> may draw to find one that UDP also has free.</summary>
    internal const int MaxPortAttempts = 20;

    /// <summary>The file the engine saves its server config to, directly under the data path.</summary>
    internal const string ConfigFileName = "serverconfig.json";

    /// <summary>Opens the listeners on a free loopback port and arms the host's config for a real
    /// client: authentication check off, random server password.</summary>
    /// <param name="server">The launched embedded server.</param>
    /// <returns>Where a client connects.</returns>
    /// <exception cref="AtlasSetupException">Thrown when slot 1 is already taken, or when no free
    /// port was found within <see cref="MaxPortAttempts"/> attempts.</exception>
    /// <remarks>Runs on the game thread, after <c>Launch()</c> and before the first pump pass that
    /// should serve a real client. The engine's packet parser thread re-reads the socket array
    /// on every pass (the dummy connector relies on the same), so slots filled here are picked up
    /// without a restart. The config is armed before the slots are installed: nothing reads a
    /// message off the new sockets until the parser sees them in the array, and the UDP slot goes
    /// in before the TCP one because a client only reaches UDP through a TCP session.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ClientEndpoint Open(ServerMain server) => Open(server, FindFreePort);

    /// <summary>The same as <see cref="Open(ServerMain)"/>, with the source of candidate ports
    /// handed in: a test makes it name ports that are taken, to drive the retry.</summary>
    /// <param name="server">The launched embedded server.</param>
    /// <param name="pickPort">Gives the next candidate port, called once per attempt.</param>
    /// <returns>Where a client connects.</returns>
    /// <exception cref="AtlasSetupException">Thrown when slot 1 is already taken, or when no
    /// candidate could be bound within <see cref="MaxPortAttempts"/> attempts.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static ClientEndpoint Open(ServerMain server, Func<int> pickPort)
    {
        if (server.MainSockets[Slot] != null || server.UdpSockets[Slot] != null)
        {
            throw new AtlasSetupException(
                $"The engine's listener slot ({Slot}) is already in use on this server: " +
                "Atlas will not replace a listener it did not open.");
        }

        string password = NewPassword();
        (TcpNetServer tcp, UdpNetServer udp, int port) = Bind(pickPort, candidate => TryStart(server, candidate));

        server.Config.VerifyPlayerAuth = false;
        server.Config.Password = password;
        server.UdpSockets[Slot] = udp;
        server.MainSockets[Slot] = tcp;
        return new ClientEndpoint(Loopback, port, password);
    }

    /// <summary>Blanks the listener's password in the server config the engine saved under a
    /// data path, so a scratch directory that outlives the host holds no copy of it.</summary>
    /// <param name="dataPath">The host's data path (its scratch directory).</param>
    /// <param name="password">The password <see cref="Open(ServerMain, Func{int})"/> generated for
    /// this host.</param>
    /// <returns><see langword="true"/> when the config holds no copy of the password afterwards,
    /// which includes a data path with no saved config; <see langword="false"/> when the file
    /// could not be read or rewritten.</returns>
    /// <remarks><para>What it does. Every occurrence of the exact password in
    /// <c>serverconfig.json</c> is replaced by nothing, so <c>"Password": "..."</c> becomes
    /// <c>"Password": ""</c>, which the engine reads as no password (<c>IsPasswordProtected</c>
    /// tests for an empty string). The rest of the file is left as it was, byte for byte, and a
    /// file that is not valid JSON is scrubbed just the same since nothing is parsed. It never
    /// throws: a failure to rewrite is reported on stderr, because a leftover credential is worth
    /// a line, and must not turn a teardown into a second error.</para>
    /// <para>What it does not cover. It runs after the game thread joined, so the engine is not
    /// saving any more. A host whose thread was abandoned after the join timed out may still
    /// save the config, with the password, later. A process killed from outside never reaches
    /// the host's dispose, so its scratch keeps the copy until it is deleted: the password is
    /// new per run, guards a loopback port that is closed with the host, and is no longer valid
    /// for anything once the process is gone.</para></remarks>
    internal static bool ScrubPassword(string dataPath, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return true;
        }

        string file = Path.Combine(dataPath, ConfigFileName);
        try
        {
            if (!File.Exists(file))
            {
                return true;
            }

            string text = File.ReadAllText(file);
            if (text.Contains(password, StringComparison.Ordinal))
            {
                File.WriteAllText(file, text.Replace(password, string.Empty, StringComparison.Ordinal));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                $"[Atlas] could not remove the client listener's password from '{file}': " +
                $"{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}");
            return false;
        }
    }

    /// <summary>Makes the random password a real client is asked for: 16 bytes from the system
    /// generator, as 32 hex digits.</summary>
    /// <returns>A new password.</returns>
    internal static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Picks a port that is free for TCP and for UDP at once, by asking the system for
    /// an ephemeral TCP port and probing the same number for UDP. A race with another process is
    /// still possible between this probe and the engine's own bind, which
    /// <see cref="Open(ServerMain, Func{int})"/> covers by retrying.</summary>
    /// <returns>A loopback port that was free for both protocols a moment ago.</returns>
    /// <exception cref="AtlasSetupException">Thrown when every ephemeral port drawn was taken for
    /// UDP, which would take a pathological machine.</exception>
    internal static int FindFreePort()
    {
        for (int attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                return port;
            }
            catch (SocketException)
            {
                // The ephemeral TCP port is in use for UDP: draw another.
            }
        }

        throw new AtlasSetupException(
            $"No loopback port was free for TCP and UDP at once after {MaxPortAttempts} draws.");
    }

    /// <summary>The retry loop: asks <paramref name="pickPort"/> for a candidate, hands it to
    /// <paramref name="tryBind"/>, and repeats while that reports a bind failure by returning
    /// <see langword="null"/>.</summary>
    /// <typeparam name="T">What a successful bind returns.</typeparam>
    /// <param name="pickPort">Gives the next candidate port.</param>
    /// <param name="tryBind">Binds one candidate, or returns <see langword="null"/> when it was
    /// taken (and has already released whatever it half opened).</param>
    /// <returns>The first successful bind's result.</returns>
    /// <exception cref="AtlasSetupException">Thrown after <see cref="MaxPortAttempts"/> failures.</exception>
    internal static T Bind<T>(Func<int> pickPort, Func<int, T?> tryBind)
        where T : struct
    {
        for (int attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            if (tryBind(pickPort()) is { } bound)
            {
                return bound;
            }
        }

        throw new AtlasSetupException(
            $"The loopback client listener could not bind a port after {MaxPortAttempts} attempts: " +
            "every candidate was taken by the time the engine's sockets tried it.");
    }

    /// <summary>Starts the TCP and the UDP listener on one port.</summary>
    /// <param name="server">The launched embedded server (the UDP side reads its client table).</param>
    /// <param name="port">The candidate port.</param>
    /// <returns>Both started listeners and the port, or <see langword="null"/> when the port was
    /// taken for either protocol, with nothing left open.</returns>
    private static (TcpNetServer Tcp, UdpNetServer Udp, int Port)? TryStart(ServerMain server, int port)
    {
        var tcp = new TcpNetServer();
        tcp.SetIpAndPort(Loopback, port);
        try
        {
            tcp.Start();
        }
        catch (SocketException)
        {
            tcp.Dispose();
            return null;
        }

        var udp = new UdpNetServer(server.Clients);
        udp.SetIpAndPort(Loopback, port);
        try
        {
            udp.Start();
        }
        catch (SocketException)
        {
            tcp.Dispose();
            DisposeFailedStart(udp);
            return null;
        }

        return (tcp, udp, port);
    }

    /// <summary>Disposes a <see cref="UdpNetServer"/> whose <c>Start()</c> threw at the bind.</summary>
    /// <param name="udp">The server that failed to start.</param>
    /// <remarks>The engine's <c>UdpNetServer.Dispose()</c> assumes a completed start: it cancels
    /// its token and disposes its socket, which is what is wanted, and then dereferences the
    /// listen task a failed start never created. The exception is thrown after the socket is
    /// already released, so swallowing exactly that one loses nothing (identical code on 1.20.12,
    /// 1.21.7, 1.22.3 and 1.22.7).</remarks>
    [SuppressMessage(
        "Major Bug",
        "S1696:NullReferenceException should not be caught",
        Justification = "UdpNetServer.Dispose() dereferences the listen task a failed Start() never created, after it has already released the socket; there is no null to test for on our side.")]
    private static void DisposeFailedStart(UdpNetServer udp)
    {
        try
        {
            udp.Dispose();
        }
        catch (NullReferenceException)
        {
            // See the remarks: the socket and the token were released before the throw.
        }
    }
}
