export const P2P_STATUS_LABELS: Record<string, string> = {
  created: "created",
  awaiting_fiat_payment: "awaiting payment",
  fiat_payment_confirmed: "payment confirmed",
  crypto_sent: "crypto sent",
  completed: "completed",
  cancelled: "cancelled",
  disputed: "disputed",
};

export const BOT_COMMANDS: Array<{ command: string; description: string }> = [
  { command: "start", description: "Welcome and Mini App" },
  { command: "help", description: "Command list" },
  { command: "balance", description: "Wallet balances" },
  { command: "rates", description: "Rate (e.g. /rates BTC UAH)" },
  { command: "receive", description: "Receive address" },
  { command: "p2p", description: "P2P market" },
  { command: "orders", description: "Your P2P orders" },
  { command: "link", description: "Link a wallet" },
  { command: "notifications", description: "Notifications on/off" },
  { command: "ping", description: "Bot connection check" },
  { command: "support", description: "Support" },
];
