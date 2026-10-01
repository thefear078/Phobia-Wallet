using Umbrella.Wallet.Infrastructure.Network;
using Xunit;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// When the bundled Tor stops before it is ready, the wallet says why in Tor's own words — "exited before
/// it finished bootstrapping" alone hid a port held by a Tor left over from an earlier run.
/// </summary>
public class TorStartupTests
{
    [Fact]
    public void TheReasonIsTorsOwnWordsWithoutTheLogPrefix()
    {
        Assert.Equal(
            "Could not bind to 127.0.0.1:9250: Address already in use. Is Tor already running?",
            EmbeddedTorService.ProblemFromLog(
                "Oct 01 10:04:21.000 [warn] Could not bind to 127.0.0.1:9250: Address already in use. Is Tor already running?"));
    }

    [Fact]
    public void NoLoggedProblemAddsNothing()
    {
        Assert.Null(EmbeddedTorService.ProblemFromLog(null));
        Assert.Null(EmbeddedTorService.ProblemFromLog("   "));
    }

    [Fact]
    public void ALongLineIsCutToSomethingReadable()
    {
        var reason = EmbeddedTorService.ProblemFromLog("Oct 01 [err] " + new string('x', 400));
        Assert.NotNull(reason);
        Assert.True(reason!.Length <= 161);
        Assert.EndsWith("…", reason);
    }
}
