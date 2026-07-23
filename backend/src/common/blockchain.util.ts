/** Public indexer reads — no private keys, addresses only */

export async function fetchEthBalance(address: string, alchemyKey: string): Promise<string | null> {
  const res = await fetch(`https://eth-mainnet.g.alchemy.com/v2/${alchemyKey}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      jsonrpc: "2.0",
      id: 1,
      method: "eth_getBalance",
      params: [address.startsWith("0x") ? address : `0x${address}`, "latest"],
    }),
  });
  const data = (await res.json()) as { result?: string };
  if (!data.result) return null;
  const wei = BigInt(data.result);
  return (Number(wei) / 1e18).toFixed(6);
}

export async function fetchTonBalance(address: string): Promise<string | null> {
  const res = await fetch(`https://tonapi.io/v2/accounts/${encodeURIComponent(address)}`, {
    headers: { Accept: "application/json" },
  });
  if (!res.ok) return null;
  const data = (await res.json()) as { balance?: number };
  if (typeof data.balance !== "number") return null;
  return (data.balance / 1e9).toFixed(4);
}

export async function fetchSolBalance(address: string): Promise<string | null> {
  const res = await fetch("https://api.mainnet-beta.solana.com", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "getBalance", params: [address] }),
  });
  if (!res.ok) return null;
  const data = (await res.json()) as { result?: { value?: number } };
  const lamports = data.result?.value;
  if (typeof lamports !== "number") return null;
  return (lamports / 1e9).toFixed(6);
}

export async function fetchTronBalance(address: string): Promise<string | null> {
  const res = await fetch(`https://api.trongrid.io/v1/accounts/${encodeURIComponent(address)}`, {
    headers: { Accept: "application/json" },
  });
  if (!res.ok) return null;
  const data = (await res.json()) as { data?: Array<{ balance?: number }> };
  const balance = data.data?.[0]?.balance;
  if (typeof balance !== "number") return null;
  return (balance / 1e6).toFixed(6);
}

export async function fetchBtcBalance(address: string): Promise<string | null> {
  const res = await fetch(`https://blockstream.info/api/address/${encodeURIComponent(address)}`, {
    headers: { Accept: "application/json" },
  });
  if (!res.ok) return null;
  const data = (await res.json()) as {
    chain_stats?: { funded_txo_sum?: number; spent_txo_sum?: number };
  };
  const funded = data.chain_stats?.funded_txo_sum ?? 0;
  const spent = data.chain_stats?.spent_txo_sum ?? 0;
  const sats = funded - spent;
  if (sats <= 0) return "0";
  return (sats / 1e8).toFixed(8);
}

export async function verifyEthTxHash(
  txHash: string,
  alchemyKey: string,
): Promise<{ ok: boolean; from?: string; to?: string }> {
  if (!/^0x[a-fA-F0-9]{64}$/.test(txHash)) {
    return { ok: false };
  }

  const res = await fetch(`https://eth-mainnet.g.alchemy.com/v2/${alchemyKey}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      jsonrpc: "2.0",
      id: 1,
      method: "eth_getTransactionByHash",
      params: [txHash],
    }),
  });
  const data = (await res.json()) as {
    result?: { from?: string; to?: string; blockNumber?: string } | null;
  };
  if (!data.result?.blockNumber) return { ok: false };
  return { ok: true, from: data.result.from, to: data.result.to };
}
