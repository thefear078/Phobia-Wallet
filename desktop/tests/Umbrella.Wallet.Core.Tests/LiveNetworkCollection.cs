using Umbrella.Wallet.Core.Safety;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Every test that reaches the real internet runs in this one collection, one test at a time.
///
/// The suite starts offline (<see cref="TestDataIsolation.GoOffline"/>: the kill-switch armed and every
/// chain pointed at a closed port), and each live class used to lift that for itself. They ran in
/// parallel, so one class finishing and putting the wall back up cut the others off mid-request: a
/// live run reported XRP, Solana, Polkadot, Cosmos, Nano and even GitHub as down ("no server answered",
/// "a task was canceled", a disposed HttpClient) when every one of them was answering. A live failure
/// has to mean the service, never the order the classes happened to run in.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LiveNetworkCollection : ICollectionFixture<LiveNetwork>
{
    public const string Name = "live network";
}

/// <summary>Opens the network once for the live collection, and closes it again after the last test.</summary>
public sealed class LiveNetwork : IDisposable
{
    public LiveNetwork()
    {
        PublicHttp.SetProxy(null);
        PublicHttp.SetRequireProxy(false);
        ChainEndpoints.ClearAll();
    }

    public void Dispose() => TestDataIsolation.GoOffline();
}
