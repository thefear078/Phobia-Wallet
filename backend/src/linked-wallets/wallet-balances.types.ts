export type WalletTokenBalance = {
  symbol: string;
  balance: string;
  contractAddress?: string;
};

export type WalletBalanceResponse = {
  id: string;
  chain: string;
  address: string;
  label: string | null;
  nativeBalance: string;
  tokens: WalletTokenBalance[];
  /** @deprecated use nativeBalance — kept for existing clients */
  balance: { native: string; usd: number | null };
};

export const WALLET_BALANCES_CACHE_PREFIX = "wallets:balances:";
export const WALLET_BALANCES_CACHE_TTL_SEC = 30;
