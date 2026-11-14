using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The signing path is not the network path (Altumptic PSB §4.1, Key Custody addendum). Key derivation,
/// transaction building and signing live in <c>Umbrella.Wallet.Core</c>, and Core is compiled without a
/// single reference to the .NET networking libraries — so no change to a signer can quietly start talking
/// to the outside world. Everything that does lives in Infrastructure, behind the one Tor-aware client.
/// </summary>
public sealed class SigningPathIsolationTests
{
    private static readonly string[] Networking =
    [
        "System.Net.Http",
        "System.Net.Sockets",
        "System.Net.WebSockets",
        "System.Net.WebSockets.Client",
        "System.Net.Requests",
        "System.Net.Security",
        "System.Net.NameResolution",
        "System.Net.WebClient",
    ];

    [Fact]
    public void Core_references_no_networking_library()
    {
        var core = typeof(ChainCatalog).Assembly;
        Assert.Equal("Umbrella.Wallet.Core", core.GetName().Name);

        var referenced = core.GetReferencedAssemblies().Select(a => a.Name!).ToList();
        Assert.DoesNotContain(referenced, Networking.Contains);
    }

    /// <summary>The check has to be able to fail: Infrastructure, which does talk to the network, is
    /// seen referencing the same libraries.</summary>
    [Fact]
    public void The_check_sees_networking_where_it_is()
    {
        var infrastructure = typeof(Umbrella.Wallet.Infrastructure.UpdateService).Assembly;
        Assert.Contains(infrastructure.GetReferencedAssemblies(), a => a.Name == "System.Net.Http");
    }
}
