import { EthereumProvider } from "@walletconnect/ethereum-provider";
import type { SessionTypes } from "@walletconnect/types";

import { isDemoMode } from "@/lib/demoMode";

const PROJECT_ID = import.meta.env.VITE_WALLETCONNECT_PROJECT_ID as string | undefined;

/** EVM chains supported via WalletConnect (optional = user can switch in wallet) */
export const EVM_OPTIONAL_CHAIN_IDS = [
  1, 137, 56, 42161, 10, 43114, 8453, 250, 100, 324, 59144, 42220, 1284, 1285,
] as const;

const CHAIN_ID_TO_NAME: Record<number, string> = {
  1: "ethereum",
  137: "polygon",
  56: "bsc",
  42161: "arbitrum",
  10: "optimism",
  43114: "avalanche",
  8453: "base",
  250: "fantom",
  100: "gnosis",
  324: "zksync",
  59144: "linea",
  42220: "celo",
  1284: "moonbeam",
  1285: "moonriver",
};

export const MANUAL_CHAIN_OPTIONS = [
  { id: "ethereum", label: "Ethereum (EVM)" },
  { id: "polygon", label: "Polygon" },
  { id: "bsc", label: "BNB Smart Chain" },
  { id: "arbitrum", label: "Arbitrum" },
  { id: "optimism", label: "Optimism" },
  { id: "avalanche", label: "Avalanche C-Chain" },
  { id: "base", label: "Base" },
  { id: "bitcoin", label: "Bitcoin" },
  { id: "ton", label: "TON" },
  { id: "solana", label: "Solana" },
  { id: "tron", label: "TRON" },
  { id: "other", label: "Other network" },
] as const;

type WcProvider = Awaited<ReturnType<typeof EthereumProvider.init>>;

let provider: WcProvider | null = null;
let connecting = false;

type EthereumRequestProvider = {
  request: (args: { method: string; params?: unknown[] }) => Promise<unknown>;
  isMetaMask?: boolean;
};

declare global {
  interface Window {
    ethereum?: EthereumRequestProvider;
  }
}

export function getWalletConnectProjectId(): string | undefined {
  return PROJECT_ID;
}

async function ensureProvider(): Promise<WcProvider> {
  if (isDemoMode()) {
    throw new Error("WalletConnect is unavailable in demo mode");
  }
  if (!PROJECT_ID) {
    throw new Error(
      "VITE_WALLETCONNECT_PROJECT_ID is not configured. Create a project at https://cloud.reown.com",
    );
  }
  if (!provider) {
    provider = await EthereumProvider.init({
      projectId: PROJECT_ID,
      chains: [1],
      optionalChains: [...EVM_OPTIONAL_CHAIN_IDS],
      showQrModal: true,
      metadata: {
        name: "Umbrella Wallet",
        description: "Non-custodial crypto aggregator",
        url: typeof window !== "undefined" ? window.location.origin : "https://umbra.wallet",
        icons: [
          `${typeof window !== "undefined" ? window.location.origin : ""}/umbrella-logo-white.png`,
        ],
      },
    });

    provider.on("disconnect", () => {
      provider = null;
    });
  }
  return provider;
}

/** Initialize WalletConnect provider (no modal). */
export async function initWalletConnect(): Promise<void> {
  if (isDemoMode() || !PROJECT_ID) return;
  await ensureProvider();
}

export async function connect(): Promise<{ address: string; chain: string }> {
  const result = await connectWalletConnect();
  return { address: result.address, chain: result.chain };
}

export async function disconnect(): Promise<void> {
  await disconnectWalletConnect();
}

export function getSession(): SessionTypes.Struct | null {
  return provider?.session ?? null;
}

export function chainIdToName(chainId: number): string {
  return CHAIN_ID_TO_NAME[chainId] ?? `evm-${chainId}`;
}

export function hasInjectedWallet(): boolean {
  return typeof window !== "undefined" && Boolean(window.ethereum?.request);
}

export async function connectInjectedWallet(): Promise<{
  address: string;
  chain: string;
  label: string;
}> {
  const eth = window.ethereum;
  if (!eth?.request) {
    throw new Error(
      "No browser wallet found. Install MetaMask, Rabby, Phantom (EVM), or another wallet.",
    );
  }

  const accounts = (await eth.request({ method: "eth_requestAccounts" })) as string[];
  const address = accounts[0];
  if (!address) throw new Error("Wallet did not return an address");

  const chainIdHex = (await eth.request({ method: "eth_chainId" })) as string;
  const chainId = Number.parseInt(chainIdHex, 16);
  const label = eth.isMetaMask ? "MetaMask" : "Browser wallet";

  return { address, chain: chainIdToName(chainId), label };
}

export async function connectWalletConnect(): Promise<{
  address: string;
  chain: string;
  label: string;
}> {
  if (isDemoMode()) {
    await new Promise((r) => setTimeout(r, 400));
    return {
      address: "0xDEMO742d35Cc6634C0532925a3b844Bc9e7595f0",
      chain: "ethereum",
      label: "WalletConnect (demo)",
    };
  }

  if (!PROJECT_ID) {
    throw new Error(
      "VITE_WALLETCONNECT_PROJECT_ID is not configured. Create a project at https://cloud.reown.com",
    );
  }

  if (connecting) throw new Error("Connection already in progress");
  connecting = true;

  try {
    const wc = await ensureProvider();

    if (!wc.session) {
      await wc.connect();
    }

    const address = wc.accounts[0];
    if (!address) throw new Error("Wallet did not return an address");

    const chainId = wc.chainId;
    return { address, chain: chainIdToName(chainId), label: "WalletConnect" };
  } finally {
    connecting = false;
  }
}

export async function disconnectWalletConnect() {
  if (provider?.session) {
    await provider.disconnect();
  }
  provider = null;
}

export function isWalletConnectConnected(): boolean {
  return Boolean(provider?.session);
}

export function validateManualAddress(chain: string, address: string): string {
  const trimmed = address.trim();
  if (trimmed.length < 10) throw new Error("Address is too short");

  const evmLike = [
    "ethereum",
    "polygon",
    "bsc",
    "arbitrum",
    "optimism",
    "avalanche",
    "base",
    "fantom",
    "gnosis",
    "zksync",
    "linea",
    "celo",
    "moonbeam",
    "moonriver",
  ].includes(chain);
  if (evmLike && !/^0x[a-fA-F0-9]{40}$/.test(trimmed)) {
    throw new Error("EVM address must be 0x + 40 hex characters");
  }
  if (chain === "bitcoin" && !/^(bc1|[13])[a-zA-HJ-NP-Z0-9]{25,62}$/.test(trimmed)) {
    throw new Error("Invalid Bitcoin address format");
  }
  if (chain === "solana" && !/^[1-9A-HJ-NP-Za-km-z]{32,44}$/.test(trimmed)) {
    throw new Error("Invalid Solana address format");
  }
  if (chain === "ton" && !/^(EQ|UQ)[A-Za-z0-9_-]{46}$/.test(trimmed) && trimmed.length < 48) {
    throw new Error("Check the TON address (EQ… / UQ…)");
  }
  if (chain === "tron" && !/^T[1-9A-HJ-NP-Za-km-z]{33}$/.test(trimmed)) {
    throw new Error("Invalid TRON address format (T…)");
  }

  return trimmed;
}

export function parseEthToWeiHex(amount: string): string {
  const parsed = Number.parseFloat(amount);
  if (!Number.isFinite(parsed) || parsed <= 0) {
    throw new Error("Invalid amount");
  }
  const wei = BigInt(Math.round(parsed * 1e18));
  return `0x${wei.toString(16)}`;
}

async function requestFromActiveProvider(method: string, params: unknown[]): Promise<unknown> {
  if (provider?.session) {
    return provider.request({ method, params });
  }
  const eth = window.ethereum;
  if (eth?.request) {
    return eth.request({ method, params });
  }
  throw new Error("Wallet not connected. Link WalletConnect or a browser wallet first.");
}

export async function signLinkProof(message: string, address: string): Promise<string> {
  if (isDemoMode()) {
    return "0x" + "00".repeat(65);
  }

  const sig = (await requestFromActiveProvider("personal_sign", [message, address])) as string;

  if (!sig?.startsWith("0x")) {
    throw new Error("Wallet did not return a signature");
  }
  return sig;
}

export async function sendEvmTransaction(params: {
  to: string;
  valueWeiHex?: string;
}): Promise<string> {
  if (isDemoMode()) {
    await new Promise((r) => setTimeout(r, 600));
    return "0x" + "ab".repeat(32);
  }

  let from: string;
  if (provider?.session && provider.accounts[0]) {
    from = provider.accounts[0];
  } else if (window.ethereum) {
    const accounts = (await window.ethereum.request({ method: "eth_requestAccounts" })) as string[];
    from = accounts[0];
  } else {
    throw new Error("Wallet not connected");
  }

  if (!/^0x[a-fA-F0-9]{40}$/.test(params.to)) {
    throw new Error("Invalid recipient address");
  }

  const hash = (await requestFromActiveProvider("eth_sendTransaction", [
    {
      from,
      to: params.to,
      value: params.valueWeiHex ?? "0x0",
    },
  ])) as string;

  if (!hash?.startsWith("0x")) {
    throw new Error("Wallet did not return a transaction hash");
  }
  return hash;
}

export function isEvmChainName(chain: string): boolean {
  return (
    chain === "ethereum" ||
    chain.startsWith("evm-") ||
    [
      "polygon",
      "bsc",
      "arbitrum",
      "optimism",
      "avalanche",
      "base",
      "fantom",
      "gnosis",
      "zksync",
      "linea",
      "celo",
      "moonbeam",
      "moonriver",
    ].includes(chain)
  );
}
