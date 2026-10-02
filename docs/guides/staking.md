# Staking

Stake **TRX**, **SOL** and **ATOM** right inside the wallet. Every staking action is a real
transaction: built, checked and signed in this wallet, confirmed with your password like a send, and
written to **Activity**. Your coins never leave your keys — they are frozen, delegated or bonded on
their own chain, and only your wallet can take them back.

<!-- TOC -->
- [At a glance](#at-a-glance)
- [Stake](#stake)
- [Claim rewards](#claim-rewards)
- [Unstake and withdraw](#unstake-and-withdraw)
- [What the wallet checks for you](#what-the-wallet-checks-for-you)
- [Risks](#risks)
- [Other networks](#other-networks)
- [Questions](#questions)
<!-- /TOC -->

---

## At a glance

| | TRON (TRX) | Solana (SOL) | Cosmos Hub (ATOM) |
|---|---|---|---|
| What staking is | Freeze TRX (Stake 2.0) and give its votes to a **Super Representative** | Move SOL into a **stake account** delegated to a **validator** | **Delegate** ATOM to a **validator** |
| Who you choose from | The 27 elected Super Representatives, by rank and votes | Validators that vote, commission ≤ 10%, by stake | Active validators, commission ≤ 10%, by stake |
| Starts earning | After the vote | Next epoch (about 2 days) | Next block |
| Rewards | Claimed by you, once a day at most | Added to the stake automatically every epoch | Claimed by you, any time |
| Getting out | Unfreeze → **14 days** → withdraw | Undelegate → end of the epoch → withdraw | Undelegate → **21 days** → back by itself |
| Typical reward (approx.) | ~3–5% a year | ~6–7% a year | ~15–20% a year, before commission |
| Fee | A little bandwidth per transaction | A few thousandths of a SOL; ~0.0023 SOL reserve, returned on withdraw | A few hundredths of an ATOM |

Rates are the networks' typical rewards, shown as a guide, not a promise.

## Stake

1. Open **Discover → Staking**. A card for each of TRON, Solana and Cosmos Hub shows what is
   **available**, **staked** and earned as **rewards**.
2. Press **Stake** on the coin's card. A panel opens at the top:
   - **With** — pick a Super Representative or validator. Each line shows its rank or commission and
     how much is staked with it.
   - **Amount** — type it, or press **Max** (it leaves enough for the fees).
3. Press **Review**. The wallet builds the transaction and says in plain words what it will do, for
   example: *"Freeze 100 TRX for energy, then give all 100 of your votes to poloniex.com."*
4. Type your wallet password (when **password before every send** is on, which it is by default) and
   press **Confirm and sign**.
5. The result shows below the panel, the action appears in **Activity** with an explorer link, and the
   card reads the position again a few seconds later.

**TRON is two transactions:** the freeze, then the vote. If the freeze succeeds and the vote does not
(for example, TronGrid was busy), the TRX stays frozen and yours — open **Stake** again and the vote
goes through. All of your votes go to the Super Representative you pick; votes cast before move to it
too.

## Claim rewards

- **TRON:** press **Claim** on the card when rewards are shown. TRON allows one claim every 24 hours.
- **Cosmos Hub:** press **Claim**; the rewards of every delegation come to the balance in one
  transaction.
- **Solana:** nothing to claim — rewards are added to the stake account each epoch.

## Unstake and withdraw

- **TRON:** **Unstake** and type how much to unfreeze. It stops earning at once and can be withdrawn
  **14 days** later: the card shows when, and **Withdraw** appears when the time has come.
- **Solana:** every stake account is listed under the card. **Unstake** on an account undelegates it;
  after the epoch ends it shows *inactive* and **Withdraw** moves everything in it — the reserve
  included — back to the wallet.
- **Cosmos Hub:** every delegation is listed under the card. **Unstake** on one (the amount is filled
  in, change it to unstake part). The ATOM stops earning and comes back to the balance by itself after
  **21 days**.

## What the wallet checks for you

- **TRON:** TronGrid builds the transaction. Before signing, the wallet reads the bytes TronGrid
  returned and refuses unless they are exactly one contract of the expected kind, from this wallet, for
  the amount, resource and Super Representative you chose — and the id it signs is the SHA-256 of those
  very bytes. A server that built anything else gets a refusal, not a signature.
- **Solana:** the stake account is created at an address derived from your own key and a seed
  (`phobia-stake-0`, `-1`, …), so your one key creates, finds and controls it — there is no second key
  to lose. You are both its staker and its withdrawer, and there is no lockup.
- **Cosmos Hub:** the node simulates the transaction first to work out the gas, and the wallet checks
  just before signing that the account has not sent anything since the review.
- Everywhere: nothing is signed before you confirm, the password is checked against this wallet's own
  vault, and the route must pass the same Tor / proxy rules as any send.

## Risks

- **Locked time.** TRX takes 14 days and ATOM 21 days to come back; you cannot sell it meanwhile.
- **Validator behaviour.** On the Cosmos Hub a validator that misbehaves can be *slashed*, and a small
  part of what is delegated to it is lost. Picking an established validator with a moderate commission
  reduces that risk. Solana and TRON do not slash delegators today.
- **Rates change.** Rewards depend on the network and on the validator's commission.

## Other networks

Ethereum, Cardano, TON and Polygon are listed under **Other networks** with how staking works there.
Staking them from inside the wallet is not in this build yet.

## Questions

**Why does my SOL stake say "activating"?** Solana activates stakes at the start of the next epoch,
roughly every two days. It earns from then on.

**Why is "staked" lower than what I froze on TRON?** It is not: frozen TRX is shown as staked. Your
spendable TRX is lower by the same amount until you unfreeze and withdraw.

**Can I stake from a watched address?** No. Staking signs with your keys, so only the open wallet's own
TRON, Solana and Cosmos accounts can stake.

---

📖 [User guides](README.md) · [Documentation Index](../INDEX.md)
