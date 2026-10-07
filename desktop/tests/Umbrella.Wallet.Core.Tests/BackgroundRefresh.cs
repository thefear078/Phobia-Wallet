using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A new or unlocked wallet starts a refresh by itself. In the app it applies its results on the UI
/// thread; a test has none, so it runs on the thread pool and rewrites Accounts, Holdings and the lists
/// built from them while the test is reading them. Two tests flaked on exactly that ("Collection was
/// modified"; a list emptied between two asserts). Waiting for it to finish makes them deterministic.
/// </summary>
internal static class BackgroundRefresh
{
    /// <summary>Returns once the wallet's own refresh has finished (at most ten seconds: the suite is
    /// offline, so every read fails at once).</summary>
    internal static async Task SettleAsync(MainViewModel vm)
    {
        for (var i = 0; i < 400 && vm.IsRefreshing; i++) await Task.Delay(25);
    }
}
