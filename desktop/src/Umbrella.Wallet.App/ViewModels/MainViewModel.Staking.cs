using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Umbrella.Wallet.Core.Amounts;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Infrastructure.Network;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>A coin that can be staked from this wallet, with where its staking stands.</summary>
public sealed partial class StakeChainViewModel : ObservableObject
{
    public StakeChainViewModel(string symbol, string name)
    {
        Symbol = symbol;
        Name = name;
    }

    public string Symbol { get; }
    public string Name { get; }

    [ObservableProperty] private string _available = "—";
    [ObservableProperty] private string _staked = "—";
    [ObservableProperty] private string _rewards = "—";
    [ObservableProperty] private string _pending = string.Empty;
    [ObservableProperty] private string _apr = string.Empty;
    [ObservableProperty] private string _note = string.Empty;
    [ObservableProperty] private bool _loading = true;
    [ObservableProperty] private bool _canClaim;
    [ObservableProperty] private bool _canUnstake;
    [ObservableProperty] private bool _canWithdraw;

    public string StakeParameter => $"{Symbol}|stake";
    public string ClaimParameter => $"{Symbol}|claim";
    public string UnstakeParameter => $"{Symbol}|unstake";
    public string WithdrawParameter => $"{Symbol}|withdraw";

    /// <summary>TRON unstakes and withdraws the whole account's stake; Solana and Cosmos do it per position.</summary>
    public bool ChainUnstake => CanUnstake && Symbol == "TRX";
    public bool ChainWithdraw => CanWithdraw && Symbol == "TRX";

    partial void OnCanUnstakeChanged(bool value) => OnPropertyChanged(nameof(ChainUnstake));
    partial void OnCanWithdrawChanged(bool value) => OnPropertyChanged(nameof(ChainWithdraw));

    public bool HasPending => !string.IsNullOrEmpty(Pending);
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public bool HasItems => Items.Count > 0;

    partial void OnPendingChanged(string value) => OnPropertyChanged(nameof(HasPending));
    partial void OnNoteChanged(string value) => OnPropertyChanged(nameof(HasNote));

    /// <summary>The separate positions: a Solana stake account each, a Cosmos delegation each.</summary>
    public ObservableCollection<StakeItemViewModel> Items { get; } = [];

    public void SetItems(IEnumerable<StakeItemViewModel> items)
    {
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        OnPropertyChanged(nameof(HasItems));
    }

    public string BadgeColor => CoinBadge.Color(Symbol);
    public string BadgeGlyph => CoinGlyphs.For(Symbol);
    public Avalonia.Media.Imaging.Bitmap? BadgeLogo => CoinBadge.Logo(Symbol);
    public bool HasBadgeLogo => CoinBadge.HasLogo(Symbol);
    public string BadgeBg => HasBadgeLogo ? "Transparent" : BadgeColor;
}

/// <summary>One position inside a coin's staking: what it is, how much, and what can be done with it now.</summary>
public sealed record StakeItemViewModel(
    string Symbol, string Id, string Title, string Detail, string Amount, bool CanUnstake, bool CanWithdraw)
{
    public string UnstakeParameter => $"{Symbol}|unstake|{Id}";
    public string WithdrawParameter => $"{Symbol}|withdraw|{Id}";
}

/// <summary>A validator or Super Representative in the stake form's picker.</summary>
public sealed record StakeValidatorOption(string Id, string Title, string Detail);

/// <summary>
/// Staking from this wallet — real transactions, signed here, on the three networks where the wallet
/// already signs safely: TRON (Stake 2.0: freeze, vote, claim, unfreeze), Solana (native stake accounts
/// at seeds of the wallet's own key) and the Cosmos Hub (delegate, claim, undelegate). Every action is
/// built, reviewed, confirmed with the password like a send, and shown in Activity. The other networks
/// stay listed with how staking works there.
/// </summary>
public partial class MainViewModel
{
    private readonly TronStakingClient _tronStaking = new();
    private readonly SolanaStakingClient _solStaking = new();
    private readonly CosmosStakingClient _atomStaking = new();

    private TronStakePosition? _tronStake;
    private SolStakePosition? _solStake;
    private AtomStakePosition? _atomStake;
    private readonly Dictionary<string, IReadOnlyList<StakeValidatorOption>> _validatorCache = new(StringComparer.Ordinal);

    /// <summary>The coins this wallet stakes itself.</summary>
    public ObservableCollection<StakeChainViewModel> StakeChains { get; } = [];

    public bool HasStakeChains => StakeChains.Count > 0;

    // --- the action panel ---------------------------------------------------------------------------

    [ObservableProperty] private bool _stakeActionOpen;
    [ObservableProperty] private string _stakeActionTitle = string.Empty;
    [ObservableProperty] private string _stakeActionHint = string.Empty;
    [ObservableProperty] private bool _stakeNeedsAmount;
    [ObservableProperty] private bool _stakeNeedsValidator;
    [ObservableProperty] private string _stakeAmount = string.Empty;
    [ObservableProperty] private StakeValidatorOption? _selectedStakeValidator;
    [ObservableProperty] private string _stakeReview = string.Empty;
    [ObservableProperty] private string _stakeError = string.Empty;
    [ObservableProperty] private string _stakeResult = string.Empty;
    [ObservableProperty] private string _stakePassword = string.Empty;
    [ObservableProperty] private bool _isStakeWorking;

    public ObservableCollection<StakeValidatorOption> StakeValidators { get; } = [];

    public bool HasStakeReview => !string.IsNullOrEmpty(StakeReview);
    public bool HasStakeError => !string.IsNullOrEmpty(StakeError);
    public bool HasStakeResult => !string.IsNullOrEmpty(StakeResult);

    partial void OnStakeReviewChanged(string value) => OnPropertyChanged(nameof(HasStakeReview));
    partial void OnStakeErrorChanged(string value) => OnPropertyChanged(nameof(HasStakeError));
    partial void OnStakeResultChanged(string value) => OnPropertyChanged(nameof(HasStakeResult));
    partial void OnStakeAmountChanged(string value) => StakeReview = string.Empty;
    partial void OnSelectedStakeValidatorChanged(StakeValidatorOption? value) => StakeReview = string.Empty;

    private string _stakeSymbol = string.Empty;
    private string _stakeKind = string.Empty;
    private string _stakeTarget = string.Empty;
    private StakeQuote? _stakeQuote;

    private static readonly string[] StakeableHere = ["TRX", "SOL", "ATOM"];

    private WalletAccountViewModel? OwnAccount(string symbol) =>
        Accounts.FirstOrDefault(a => a.Symbol == symbol && a.SupportStatus is "Ready" or "Receive only"
                                     && IsRealAddress(a.Address) && a.Derivation != "external");

    /// <summary>Reads every position again. Called when Staking opens and after every action.</summary>
    [RelayCommand]
    private async Task RefreshStakingAsync()
    {
        if (!IsUnlocked) return;
        SyncStakeChains();
        var tasks = new List<Task>();
        foreach (var chain in StakeChains.ToList())
        {
            var account = OwnAccount(chain.Symbol);
            if (account is null) continue;
            chain.Loading = true;
            tasks.Add(chain.Symbol switch
            {
                "TRX" => LoadTronStakeAsync(chain, account),
                "SOL" => LoadSolStakeAsync(chain, account),
                _ => LoadAtomStakeAsync(chain, account),
            });
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>One card per coin this wallet can stake itself, in a fixed order.</summary>
    private void SyncStakeChains()
    {
        foreach (var symbol in StakeableHere)
        {
            var account = OwnAccount(symbol);
            var existing = StakeChains.FirstOrDefault(c => c.Symbol == symbol);
            if (account is null)
            {
                if (existing is not null) StakeChains.Remove(existing);
                continue;
            }
            if (existing is null)
            {
                var name = ChainCatalog.All.FirstOrDefault(c => c.Symbol == symbol)?.Name ?? symbol;
                StakeChains.Insert(Math.Min(StakeChains.Count, Array.IndexOf(StakeableHere, symbol)), new StakeChainViewModel(symbol, name));
            }
        }
        OnPropertyChanged(nameof(HasStakeChains));
        foreach (var chain in StakeChains)
            if (OwnAccount(chain.Symbol) is { } a)
                chain.Available = $"{a.Amount.ToString("0.######", Fx.Culture)} {chain.Symbol}";
    }

    private string Coin(decimal amount, string symbol) => $"{amount.ToString("0.######", Fx.Culture)} {symbol}";

    private string WithValue(decimal amount, string symbol)
    {
        var price = Accounts.FirstOrDefault(a => a.Symbol == symbol && a.Price > 0)?.Price ?? 0;
        return price > 0 && !IsBalanceHidden ? $"{Coin(amount, symbol)} · {Fx.Money((double)amount * price)}" : Coin(amount, symbol);
    }

    private async Task LoadTronStakeAsync(StakeChainViewModel chain, WalletAccountViewModel account)
    {
        var position = await _tronStaking.PositionAsync(account.Address);
        chain.Loading = false;
        if (position is null)
        {
            chain.Note = Loc.Instance["stake.unread"];
            return;
        }

        _tronStake = position;
        chain.Note = string.Empty;
        var frozen = position.FrozenSun / (decimal)TronStaking.SunPerTrx;
        chain.Staked = WithValue(frozen, "TRX");
        chain.Rewards = WithValue(position.RewardSun / (decimal)TronStaking.SunPerTrx, "TRX");
        chain.Apr = "~3–5%";
        chain.CanClaim = position.RewardSun > 0;
        chain.CanUnstake = position.FrozenSun > 0;
        chain.CanWithdraw = position.WithdrawableSun > 0;
        chain.Pending = position.UnfreezingSun > 0
            ? string.Format(Loc.Instance["stake.tronUnfreezing"], Coin(position.UnfreezingSun / (decimal)TronStaking.SunPerTrx, "TRX"),
                position.NextUnfreeze?.ToLocalTime().ToString("d MMM", Fx.Culture) ?? "—")
            : position.WithdrawableSun > 0
                ? string.Format(Loc.Instance["stake.ready"], Coin(position.WithdrawableSun / (decimal)TronStaking.SunPerTrx, "TRX"))
                : string.Empty;
        var votedFor = position.VotedFor is null ? null : (await StakeValidatorsAsync("TRX")).FirstOrDefault(v => v.Id == position.VotedFor)?.Title ?? Shorten(position.VotedFor);
        chain.SetItems(position.VotesCast > 0 && votedFor is not null
            ? [new StakeItemViewModel("TRX", position.VotedFor!, votedFor, string.Format(Loc.Instance["stake.tronVotes"], position.VotesCast.ToString("N0", Fx.Culture)),
                Coin(frozen, "TRX"), false, false)]
            : []);
    }

    private async Task LoadSolStakeAsync(StakeChainViewModel chain, WalletAccountViewModel account)
    {
        var position = await _solStaking.PositionAsync(account.Address);
        chain.Loading = false;
        if (position is null)
        {
            chain.Note = Loc.Instance["stake.unread"];
            return;
        }

        _solStake = position;
        chain.Note = string.Empty;
        chain.Staked = WithValue(SolanaRpc.ToSol(position.Staked), "SOL");
        chain.Rewards = Loc.Instance["stake.solCompounds"];
        chain.Apr = "~6–7%";
        chain.CanClaim = false;
        chain.CanUnstake = position.Accounts.Any(a => a.CanDeactivate);
        chain.CanWithdraw = position.Accounts.Any(a => a.CanWithdraw);
        chain.Pending = position.CoolingDown > 0
            ? string.Format(Loc.Instance["stake.solCooling"], Coin(SolanaRpc.ToSol(position.CoolingDown), "SOL"))
            : string.Empty;
        var validators = await StakeValidatorsAsync("SOL");
        chain.SetItems(position.Accounts.Select(a => new StakeItemViewModel("SOL", a.Address,
            a.Voter is null ? Shorten(a.Address) : validators.FirstOrDefault(v => v.Id == a.Voter)?.Title ?? Shorten(a.Voter),
            Loc.Instance["stake.solState." + a.State], Coin(SolanaRpc.ToSol(a.Lamports), "SOL"), a.CanDeactivate, a.CanWithdraw)));
    }

    private async Task LoadAtomStakeAsync(StakeChainViewModel chain, WalletAccountViewModel account)
    {
        var position = await _atomStaking.PositionAsync(account.Address);
        chain.Loading = false;
        if (position is null)
        {
            chain.Note = Loc.Instance["stake.unread"];
            return;
        }

        _atomStake = position;
        chain.Note = string.Empty;
        chain.Staked = WithValue(position.Staked, "ATOM");
        chain.Rewards = WithValue(decimal.Round(position.Rewards, 6), "ATOM");
        chain.Apr = "~15–20%";
        chain.CanClaim = position.Rewards >= 0.000001m;
        chain.CanUnstake = position.Delegations.Count > 0;
        chain.CanWithdraw = false;   // undelegated ATOM comes back by itself after 21 days
        chain.Pending = position.Unbonding.Count > 0
            ? string.Format(Loc.Instance["stake.atomUnbonding"], Coin(position.Unbonding.Sum(u => u.Atom), "ATOM"),
                position.Unbonding.Min(u => u.CompletesAt).ToLocalTime().ToString("d MMM", Fx.Culture))
            : string.Empty;
        var validators = await StakeValidatorsAsync("ATOM");
        chain.SetItems(position.Delegations.Select(d => new StakeItemViewModel("ATOM", d.Validator,
            validators.FirstOrDefault(v => v.Id == d.Validator)?.Title ?? Shorten(d.Validator),
            string.Format(Loc.Instance["stake.atomReward"], Coin(decimal.Round(d.RewardAtom, 6), "ATOM")),
            Coin(d.StakedAtom, "ATOM"), true, false)));
    }

    /// <summary>Who a stake can go to, read once a session per network.</summary>
    private async Task<IReadOnlyList<StakeValidatorOption>> StakeValidatorsAsync(string symbol)
    {
        if (_validatorCache.TryGetValue(symbol, out var cached)) return cached;
        IReadOnlyList<StakeValidatorOption> list = symbol switch
        {
            "TRX" => (await _tronStaking.WitnessesAsync()).Select(w => new StakeValidatorOption(w.Address,
                SrName(w), string.Format(Loc.Instance["stake.srDetail"], w.Rank, w.Votes.ToString("N0", Fx.Culture)))).ToList(),
            "SOL" => (await _solStaking.ValidatorsAsync()).Select(v => new StakeValidatorOption(v.VoteAccount,
                Shorten(v.VoteAccount), string.Format(Loc.Instance["stake.valDetail"], v.Commission,
                    (SolanaRpc.ToSol(v.ActivatedLamports) / 1_000_000m).ToString("0.#", Fx.Culture) + "M SOL"))).ToList(),
            _ => (await _atomStaking.ValidatorsAsync()).Where(v => v.Commission <= 0.10m).Take(40)
                .Select(v => new StakeValidatorOption(v.Address, v.Moniker,
                    string.Format(Loc.Instance["stake.valDetail"], (v.Commission * 100).ToString("0.#", Fx.Culture),
                        (v.BondedAtom / 1_000_000m).ToString("0.#", Fx.Culture) + "M ATOM"))).ToList(),
        };
        if (list.Count > 0) _validatorCache[symbol] = list;
        return list;
    }

    private static string SrName(TronWitness w)
    {
        var url = w.Url.Replace("https://", "").Replace("http://", "").Replace("www.", "").TrimEnd('/');
        return string.IsNullOrWhiteSpace(url) ? Shorten(w.Address) : url.Length > 32 ? url[..32] + "…" : url;
    }

    /// <summary>Opens the action panel: "SOL|stake", "TRX|claim", "SOL|unstake|&lt;stake account&gt;"…</summary>
    [RelayCommand]
    private async Task OpenStakeAction(string? parameter)
    {
        var parts = (parameter ?? "").Split('|');
        if (parts.Length < 2) return;
        _stakeSymbol = parts[0];
        _stakeKind = parts[1];
        _stakeTarget = parts.Length > 2 ? parts[2] : string.Empty;
        _stakeQuote = null;
        StakeReview = string.Empty;
        StakeError = string.Empty;
        StakeResult = string.Empty;
        StakeAmount = string.Empty;
        StakePassword = string.Empty;
        StakeActionTitle = Loc.Instance[$"stake.title.{_stakeKind}"].Replace("{0}", _stakeSymbol);
        StakeNeedsValidator = _stakeKind == "stake";
        StakeNeedsAmount = _stakeKind == "stake" || (_stakeKind == "unstake" && _stakeSymbol is "TRX" or "ATOM");
        StakeActionHint = Loc.Instance[$"stake.hint.{_stakeSymbol}.{_stakeKind}"];
        StakeActionOpen = true;

        StakeValidators.Clear();
        SelectedStakeValidator = null;
        if (StakeNeedsValidator)
        {
            foreach (var v in await StakeValidatorsAsync(_stakeSymbol)) StakeValidators.Add(v);
            SelectedStakeValidator = StakeValidators.FirstOrDefault();
            if (StakeValidators.Count == 0) StakeError = Loc.Instance["stake.noValidators"];
        }
        if (_stakeKind == "unstake" && _stakeSymbol == "ATOM" &&
            _atomStake?.Delegations.FirstOrDefault(d => d.Validator == _stakeTarget) is { } delegation)
            StakeAmount = delegation.StakedAtom.ToString("0.######", CultureInfo.InvariantCulture);
    }

    [RelayCommand]
    private void CloseStakeAction()
    {
        StakeActionOpen = false;
        _stakeQuote = null;
        StakeReview = string.Empty;
        StakePassword = string.Empty;
    }

    /// <summary>Fills the amount with everything that can be staked, leaving the fee.</summary>
    [RelayCommand]
    private void StakeMax()
    {
        if (_stakeKind == "unstake" && _stakeSymbol == "TRX" && _tronStake is { } t)
        {
            StakeAmount = (t.FrozenSun / (decimal)TronStaking.SunPerTrx).ToString("0.######", CultureInfo.InvariantCulture);
            return;
        }
        if (OwnAccount(_stakeSymbol) is not { } a) return;
        var reserve = _stakeSymbol switch { "SOL" => 0.01m, "TRX" => 2m, _ => 0.05m };
        var max = Math.Max(0, (decimal)a.Amount - reserve);
        StakeAmount = max.ToString("0.######", CultureInfo.InvariantCulture);
    }

    /// <summary>Builds the transaction and says in words what it will do.</summary>
    [RelayCommand]
    private async Task PrepareStakeAction()
    {
        StakeError = string.Empty;
        StakeResult = string.Empty;
        _stakeQuote = null;
        StakeReview = string.Empty;
        if (OwnAccount(_stakeSymbol) is not { } account) return;

        decimal amount = 0;
        if (StakeNeedsAmount && (!AmountInput.TryParse(StakeAmount, out amount) || amount <= 0))
        {
            StakeError = Loc.Instance["stake.badAmount"];
            return;
        }
        if (StakeNeedsValidator && SelectedStakeValidator is null)
        {
            StakeError = Loc.Instance["stake.pickValidator"];
            return;
        }

        IsStakeWorking = true;
        try
        {
            var (quote, error) = await BuildStakeQuoteAsync(account, amount);
            if (quote is null)
            {
                StakeError = error ?? Loc.Instance["stake.failed"];
                return;
            }
            _stakeQuote = quote;
            StakeReview = DescribeStake(quote, amount);
        }
        finally
        {
            IsStakeWorking = false;
        }
    }

    private async Task<(StakeQuote? Quote, string? Error)> BuildStakeQuoteAsync(WalletAccountViewModel account, decimal amount)
    {
        var validator = SelectedStakeValidator?.Id ?? string.Empty;
        switch (_stakeSymbol, _stakeKind)
        {
            case ("TRX", "stake"):
                if ((decimal)account.Amount < amount + 1m) return (null, Loc.Instance["stake.notEnough"]);
                return await _tronStaking.FreezeAsync(account.Address, amount);
            case ("TRX", "unstake"):
                if (_tronStake is null || amount * TronStaking.SunPerTrx > _tronStake.FrozenSun) return (null, Loc.Instance["stake.moreThanStaked"]);
                return await _tronStaking.UnfreezeAsync(account.Address, amount, TronResource.Energy);
            case ("TRX", "claim"):
                return await _tronStaking.ClaimAsync(account.Address, (_tronStake?.RewardSun ?? 0) / (decimal)TronStaking.SunPerTrx);
            case ("TRX", "withdraw"):
                return await _tronStaking.WithdrawAsync(account.Address, (_tronStake?.WithdrawableSun ?? 0) / (decimal)TronStaking.SunPerTrx);

            case ("SOL", "stake"):
                // The stake account's rent reserve (~0.0023 SOL) and the fee come on top of the amount.
                if ((decimal)account.Amount < amount + 0.003m) return (null, Loc.Instance["stake.notEnough"]);
                var position = _solStake ?? await _solStaking.PositionAsync(account.Address);
                if (position is null) return (null, Loc.Instance["stake.unread"]);
                return await _solStaking.StakeAsync(account.Address, amount, validator, position);
            case ("SOL", "unstake"):
                return _solStake?.Accounts.FirstOrDefault(a => a.Address == _stakeTarget) is { } toDeactivate
                    ? _solStaking.Deactivate(account.Address, toDeactivate)
                    : (null, Loc.Instance["stake.unread"]);
            case ("SOL", "withdraw"):
                return _solStake?.Accounts.FirstOrDefault(a => a.Address == _stakeTarget) is { } toWithdraw
                    ? _solStaking.Withdraw(account.Address, toWithdraw)
                    : (null, Loc.Instance["stake.unread"]);

            case ("ATOM", "stake") when (decimal)account.Amount < amount + 0.02m:
                return (null, Loc.Instance["stake.notEnough"]);
            case ("ATOM", _):
                var publicKey = _deriver.DeriveCosmosPublicKey(_unlockedMnemonic!).ToBytes();
                return _stakeKind switch
                {
                    "stake" => await _atomStaking.PrepareAsync(account.Address, publicKey, "stake", amount, validator,
                        [CosmosStaking.Delegate(account.Address, validator, CosmosStakingClient.Micro(amount))], amount),
                    "unstake" => await _atomStaking.PrepareAsync(account.Address, publicKey, "unstake", amount, _stakeTarget,
                        [CosmosStaking.Undelegate(account.Address, _stakeTarget, CosmosStakingClient.Micro(amount))], 0m),
                    "claim" when _atomStake is { Delegations.Count: > 0 } held => await _atomStaking.PrepareAsync(account.Address, publicKey,
                        "claim", held.Rewards, string.Join(", ", held.Delegations.Select(d => d.Validator)),
                        held.Delegations.Select(d => CosmosStaking.WithdrawReward(account.Address, d.Validator)).ToList(), 0m),
                    _ => (null, Loc.Instance["stake.failed"]),
                };
        }
        return (null, Loc.Instance["stake.failed"]);
    }

    private string DescribeStake(StakeQuote quote, decimal amount)
    {
        var to = SelectedStakeValidator?.Title ?? Shorten(quote.Counterparty);
        return (_stakeSymbol, _stakeKind) switch
        {
            ("TRX", "stake") => string.Format(Loc.Instance["stake.review.TRX.stake"], Coin(amount, "TRX"), to,
                ((_tronStake?.FrozenSun ?? 0) / TronStaking.SunPerTrx + (long)Math.Floor(amount)).ToString("N0", Fx.Culture)),
            ("SOL", "stake") => string.Format(Loc.Instance["stake.review.SOL.stake"], Coin(amount, "SOL"), to),
            ("ATOM", "stake") => string.Format(Loc.Instance["stake.review.ATOM.stake"], Coin(amount, "ATOM"), to),
            (_, "unstake") => string.Format(Loc.Instance[$"stake.review.{_stakeSymbol}.unstake"], Coin(quote.Amount, _stakeSymbol)),
            (_, "claim") => string.Format(Loc.Instance["stake.review.claim"], Coin(decimal.Round(quote.Amount, 6), _stakeSymbol)),
            _ => string.Format(Loc.Instance["stake.review.withdraw"], Coin(quote.Amount, _stakeSymbol)),
        };
    }

    /// <summary>Signs and sends the reviewed action, after the password when the wallet asks for it.</summary>
    [RelayCommand]
    private async Task ConfirmStakeAction()
    {
        if (_stakeQuote is not { } quote || _unlockedMnemonic is null || OwnAccount(_stakeSymbol) is not { } account) return;
        StakeError = string.Empty;
        if (TransportGateError() is { } routeError)
        {
            StakeError = routeError;
            return;
        }

        if (RequirePasswordForSend)
        {
            var typed = StakePassword ?? string.Empty;
            StakePassword = string.Empty;
            if (typed.Length == 0)
            {
                StakeError = Loc.Instance["send.passwordNeeded"];
                return;
            }
            try
            {
                var opened = await _vault.UnlockAsync(typed);
                if (!string.Equals(opened.Trim(), _unlockedMnemonic.Trim(), StringComparison.Ordinal))
                {
                    StakeError = Loc.Instance["send.passwordWrong"];
                    return;
                }
            }
            catch
            {
                StakeError = Loc.Instance["send.passwordWrong"];
                return;
            }
        }

        IsStakeWorking = true;
        _stakeQuote = null;
        try
        {
            var outcome = await SignStakeAsync(quote, account);
            var explorer = outcome.TxId is null ? null : _stakeSymbol switch
            {
                "TRX" => $"https://tronscan.org/#/transaction/{outcome.TxId}",
                "SOL" => $"https://solscan.io/tx/{outcome.TxId}",
                _ => $"https://www.mintscan.io/cosmos/tx/{outcome.TxId}",
            };

            // TRON: the TRX is frozen; now every vote it gives goes to the chosen Super Representative.
            if (outcome.Ok && _stakeSymbol == "TRX" && _stakeKind == "stake" && SelectedStakeValidator is { } sr)
            {
                var votes = (_tronStake?.FrozenSun ?? 0) / TronStaking.SunPerTrx + (long)Math.Floor(quote.Amount);
                StakeResult = Loc.Instance["stake.tronVoting"];
                StakeOutcome? voted = null;
                for (var attempt = 0; attempt < 6 && voted is not { Ok: true }; attempt++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt == 0 ? 4 : 3));
                    var (voteQuote, voteError) = await _tronStaking.VoteAsync(account.Address, sr.Id, votes);
                    if (voteQuote is null)
                    {
                        voted = new StakeOutcome(false, null, voteError);
                        continue;
                    }
                    voted = await SignStakeAsync(voteQuote, account);
                }
                if (voted is not { Ok: true })
                    StakeError = string.Format(Loc.Instance["stake.voteFailed"], voted?.Error ?? "—");
            }

            if (outcome.Ok)
            {
                StakeResult = string.Format(Loc.Instance["stake.done"], outcome.TxId is { } id ? Shorten(id) : "—");
                PushActivity("Staked", _stakeSymbol, $"{Loc.Instance["stake.verb." + _stakeKind]} {Coin(quote.Amount, _stakeSymbol)}",
                    SelectedStakeValidator?.Title ?? Shorten(quote.Counterparty), "now", explorer);
                StakeReview = string.Empty;
                _ = RefreshLiveDataAsync();
                await Task.Delay(TimeSpan.FromSeconds(3));
                await RefreshStakingAsync();
            }
            else
            {
                StakeError = outcome.Error ?? Loc.Instance["stake.failed"];
                if (outcome.Unclear)
                    PushActivity("Staked", _stakeSymbol, $"{Loc.Instance["stake.verb." + _stakeKind]} {Coin(quote.Amount, _stakeSymbol)}",
                        Shorten(quote.Counterparty), "now", explorer, "Pending");
            }
        }
        catch (Exception ex)
        {
            StakeError = ex.Message;
        }
        finally
        {
            IsStakeWorking = false;
        }
    }

    private async Task<StakeOutcome> SignStakeAsync(StakeQuote quote, WalletAccountViewModel account)
    {
        switch (quote.Symbol)
        {
            case "TRX":
                return await _tronStaking.SignAndBroadcastAsync(quote, _deriver.DeriveTronKey(_unlockedMnemonic!));
            case "SOL":
                var priv = _deriver.DeriveSolanaPrivateKey(_unlockedMnemonic!);
                try
                {
                    return await _solStaking.SignAndBroadcastAsync(quote, account.Address, priv);
                }
                finally
                {
                    Array.Clear(priv);
                }
            default:
                using (var key = _deriver.DeriveCosmosKey(_unlockedMnemonic!))
                    return await _atomStaking.SignAndBroadcastAsync(quote, key);
        }
    }
}
