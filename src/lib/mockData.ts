export type Asset = {
  symbol: string;
  name: string;
  balance: number;
  price: number;
  change24h: number;
  color: string;
};

export const assets: Asset[] = [
  {
    symbol: "BTC",
    name: "Bitcoin",
    balance: 0.0342,
    price: 68420.55,
    change24h: 2.14,
    color: "oklch(0.75 0.18 60)",
  },
  {
    symbol: "ETH",
    name: "Ethereum",
    balance: 1.284,
    price: 3520.1,
    change24h: -0.82,
    color: "oklch(0.68 0.14 265)",
  },
  {
    symbol: "TON",
    name: "Toncoin",
    balance: 245.5,
    price: 6.42,
    change24h: 4.31,
    color: "oklch(0.72 0.16 235)",
  },
  {
    symbol: "USDT",
    name: "Tether",
    balance: 1250.0,
    price: 1.0,
    change24h: 0.01,
    color: "oklch(0.72 0.16 155)",
  },
  {
    symbol: "SOL",
    name: "Solana",
    balance: 12.7,
    price: 148.3,
    change24h: 5.62,
    color: "oklch(0.72 0.18 300)",
  },
  {
    symbol: "USDC",
    name: "USD Coin",
    balance: 800.0,
    price: 1.0,
    change24h: -0.02,
    color: "oklch(0.68 0.14 245)",
  },
];

export type P2POffer = {
  id: string;
  merchant: string;
  rating: number;
  deals: number;
  asset: string;
  fiat: string;
  price: number;
  min: number;
  max: number;
  methods: string[];
  side: "buy" | "sell";
};

export const p2pOffers: P2POffer[] = [
  {
    id: "1",
    merchant: "CryptoKing",
    rating: 99.4,
    deals: 3421,
    asset: "USDT",
    fiat: "UAH",
    price: 41.25,
    min: 500,
    max: 50000,
    methods: ["Monobank", "PrivatBank"],
    side: "buy",
  },
  {
    id: "2",
    merchant: "FastTrader",
    rating: 98.7,
    deals: 1892,
    asset: "USDT",
    fiat: "UAH",
    price: 41.28,
    min: 1000,
    max: 25000,
    methods: ["Monobank"],
    side: "buy",
  },
  {
    id: "3",
    merchant: "SafeExchange",
    rating: 99.9,
    deals: 8214,
    asset: "USDT",
    fiat: "UAH",
    price: 41.31,
    min: 200,
    max: 100000,
    methods: ["PrivatBank", "PUMB"],
    side: "buy",
  },
  {
    id: "4",
    merchant: "NightOwl",
    rating: 97.2,
    deals: 512,
    asset: "BTC",
    fiat: "USD",
    price: 68380,
    min: 100,
    max: 15000,
    methods: ["Wise", "Revolut"],
    side: "sell",
  },
];

export const exchangePairs = [
  { from: "USDT", to: "BTC", rate: 68420.55, change: 2.14 },
  { from: "USDT", to: "ETH", rate: 3520.1, change: -0.82 },
  { from: "USDT", to: "TON", rate: 6.42, change: 4.31 },
  { from: "USDT", to: "SOL", rate: 148.3, change: 5.62 },
];

export const recentTx = [
  {
    id: "t1",
    type: "receive" as const,
    asset: "USDT",
    amount: 250,
    when: "12:04",
    from: "@alexcrypto",
  },
  {
    id: "t2",
    type: "swap" as const,
    asset: "TON → USDT",
    amount: 120,
    when: "Yesterday",
    from: "Swap",
  },
  {
    id: "t3",
    type: "send" as const,
    asset: "BTC",
    amount: 0.0021,
    when: "Apr 23",
    from: "@shopmerchant",
  },
  {
    id: "t4",
    type: "p2p" as const,
    asset: "USDT",
    amount: 500,
    when: "Apr 22",
    from: "P2P purchase",
  },
];
