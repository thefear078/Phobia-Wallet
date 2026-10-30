using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// The user's own explorer key (roadmap N.15): a free Etherscan key, entered in Settings, used for
/// history only — and only on the networks nothing answers for without one (Fantom, Cronos, tokens on
/// BNB Chain). It is not a privacy improvement and the card says so: every request made with it is
/// tied to the user's Etherscan account, which is why the keyless sources stay first everywhere else.
/// </summary>
public partial class MainViewModel
{
    [ObservableProperty] private string _explorerKeyInput = string.Empty;
    [ObservableProperty] private string _explorerKeyStatus = string.Empty;
    [ObservableProperty] private string _explorerKeyError = string.Empty;
    [ObservableProperty] private bool _hasExplorerKey;

    /// <summary>The open wallet's key; null when it has none. Held only while the wallet is open.</summary>
    private string? _explorerKey;

    /// <summary>The keyed reads that answered this session ("Fantom", "Cronos", "BSC:tokens").</summary>
    private readonly HashSet<string> _keyedOk = new(StringComparer.OrdinalIgnoreCase);

    private ExplorerKeyStore? _explorerKeys;

    private ExplorerKeyStore ExplorerKeys => _explorerKeys ??= new ExplorerKeyStore(_registry.DataDirectory);

    private string ExplorerKeyWalletId => _registry.Active?.Id ?? "default";

    /// <summary>Opens the wallet's key, if it saved one.</summary>
    private void LoadExplorerKey(string secret)
    {
        _keyedOk.Clear();
        _explorerKey = ExplorerKeys.Load(ExplorerKeyWalletId, secret);
        HasExplorerKey = _explorerKey is not null;
        ExplorerKeyStatus = string.Empty;
        ExplorerKeyError = string.Empty;
    }

    private void ForgetExplorerKey()
    {
        _explorerKey = null;
        _keyedOk.Clear();
        HasExplorerKey = false;
        ExplorerKeyInput = string.Empty;
        ExplorerKeyStatus = string.Empty;
        ExplorerKeyError = string.Empty;
    }

    /// <summary>Checks the key with Etherscan (no address is sent), then seals it to this wallet.</summary>
    [RelayCommand]
    private async Task SaveExplorerKeyAsync()
    {
        ExplorerKeyStatus = string.Empty;
        ExplorerKeyError = string.Empty;
        var key = ExplorerKeyInput.Trim();
        if (key.Length < 20 || key.Any(char.IsWhiteSpace) || _unlockedMnemonic is null)
        {
            ExplorerKeyError = Loc.Instance["explorerKey.bad"];
            return;
        }

        string? refusal;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            refusal = await _history.CheckEtherscanKeyAsync(key, 1, cts.Token);
        }
        catch
        {
            refusal = Loc.Instance["explorerKey.noAnswer"];
        }

        if (refusal is not null)
        {
            ExplorerKeyError = string.Format(Loc.Instance["explorerKey.refused"], refusal);
            return;
        }

        ExplorerKeys.Save(ExplorerKeyWalletId, _unlockedMnemonic, key);
        _explorerKey = key;
        HasExplorerKey = true;
        ExplorerKeyInput = string.Empty;
        ExplorerKeyStatus = Loc.Instance["explorerKey.saved"];
        _ = LoadOnChainHistoryAsync();
    }

    [RelayCommand]
    private void RemoveExplorerKey()
    {
        ExplorerKeys.Remove(ExplorerKeyWalletId);
        ForgetExplorerKey();
        ExplorerKeyStatus = Loc.Instance["explorerKey.removed"];
        RefreshHistoryCoverage();
    }

    /// <summary>
    /// The history reads the key adds: native transfers on Fantom and Cronos and tokens on BNB Chain,
    /// where the wallet holds something. A network counts as read only once Etherscan answered for it.
    /// </summary>
    private List<Task<IReadOnlyList<ChainTx>>> KeyedHistoryReads(string address, HashSet<string> heldNetworks)
    {
        var reads = new List<Task<IReadOnlyList<ChainTx>>>();
        if (_explorerKey is not { } apiKey) return reads;

        async Task<IReadOnlyList<ChainTx>> Marked(string mark, Task<IReadOnlyList<ChainTx>?> read)
        {
            var rows = await read;
            if (rows is null) return [];
            _keyedOk.Add(mark);
            return rows;
        }

        foreach (var network in new[] { "Fantom", "Cronos" })
            if (heldNetworks.Contains(network))
                reads.Add(Marked(network, _history.GetKeyedNativeAsync(apiKey, network, address)));

        var bscTokens = Accounts.ToArray()
            .Where(a => a.Derivation == "ERC20 on BSC" && a.Contract.Length > 0 && a.Amount > 0 && !a.IsSuspectedSpam)
            .Select(a => new OnChainHistoryClient.HeldToken(a.Contract, a.Symbol, a.TokenDecimals))
            .ToList();
        if (bscTokens.Count > 0)
            reads.Add(Marked("BSC:tokens", _history.GetKeyedTokensAsync(apiKey, "BSC", address, bscTokens)));

        return reads;
    }
}
