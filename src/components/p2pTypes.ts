export type QuoteKind = "fiat" | "crypto";

export type OfferRow = {
  id: string;
  merchant: string;
  asset: string;
  fiat: string;
  /** "fiat" → quote is an ISO code paid by bank; "crypto" → quote settled on-chain. */
  quoteKind?: QuoteKind;
  price: number;
  min: number | null;
  max: number | null;
  methods: string[];
  side: string;
  rating?: number;
  deals?: number;
  status?: string;
};

export type P2pOrderRow = {
  id: string;
  offerId: string;
  buyerId: string;
  sellerId: string;
  amount: number;
  status: string;
  paymentMethod?: string | null;
  cryptoTxHash?: string | null;
  fiatPaymentReference?: string | null;
  disputeReason?: string | null;
  createdAt?: string;
  updatedAt?: string;
  offer?: OfferRow | null;
};
