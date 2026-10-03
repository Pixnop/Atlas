namespace Atlas.Internal.RealClient;

/// <summary>Why the stock game client cannot run on this machine, in the order the ladder in
/// <see cref="ClientAvailability.Check"/> tries them: the first rung that fails is the one
/// reported, so someone fixing them one by one meets them in this order.</summary>
internal enum ClientUnavailableReason
{
    /// <summary>Nothing is in the way (the result carries a toolchain).</summary>
    None = 0,

    /// <summary>A CI environment variable is set.</summary>
    CiEnvironment,

    /// <summary><c>ATLAS_CLIENT=off</c>.</summary>
    SwitchedOff,

    /// <summary>The platform is not Linux.</summary>
    NotLinux,

    /// <summary><c>unshare</c> is not installed.</summary>
    UnshareMissing,

    /// <summary><c>setpriv</c> is not installed.</summary>
    SetprivMissing,

    /// <summary><c>unshare</c> cannot build the sandbox's namespaces here, or <c>setpriv</c>
    /// cannot empty the capabilities inside them.</summary>
    UserNamespacesUnusable,

    /// <summary>There is no <c>Xvfb</c>.</summary>
    XvfbMissing,

    /// <summary>The game install lacks files the client needs.</summary>
    InstallIncomplete,

    /// <summary>No dotnet folder the apphost looks in holds the runtime the client needs.</summary>
    DotnetRuntimeMissing,

    /// <summary>No client data path was given.</summary>
    NoDataPath,
}
