import { useEffect, useState } from "react";
import { toast } from "sonner";
import { ShieldCheck, Landmark, Coins } from "lucide-react";
import { ActionSheet } from "@/components/ActionSheet";
import { api, ApiError } from "@/lib/api/client";
import type { OfferRow, QuoteKind } from "./p2pTypes";

const ASSETS = ["BTC", "ETH", "USDT", "TON", "SOL", "USDC"] as const;
const FIATS = ["UAH", "USD", "EUR"] as const;
const CRYPTO_QUOTES = ["USDT", "USDC", "BTC", "ETH"] as const;
const METHODS = ["Monobank", "PrivatBank", "PUMB", "Wise", "Revolut"] as const;

type Props = {
  open: boolean;
  editOffer?: OfferRow | null;
  onClose: () => void;
  onSaved: () => void;
};

export function P2pOfferSheet({ open, editOffer, onClose, onSaved }: Props) {
  const editing = Boolean(editOffer);
  const [side, setSide] = useState<"buy" | "sell">("sell");
  const [asset, setAsset] = useState<string>("BTC");
  const [quoteKind, setQuoteKind] = useState<QuoteKind>("fiat");
  const [fiat, setFiat] = useState<string>("UAH");
  const [price, setPrice] = useState("");
  const [minAmount, setMinAmount] = useState("");
  const [maxAmount, setMaxAmount] = useState("");
  const [methods, setMethods] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!open) return;
    if (editOffer) {
      setSide(editOffer.side === "buy" ? "buy" : "sell");
      setAsset(editOffer.asset);
      setQuoteKind(editOffer.quoteKind ?? "fiat");
      setFiat(editOffer.fiat);
      setPrice(String(editOffer.price));
      setMinAmount(editOffer.min != null ? String(editOffer.min) : "");
      setMaxAmount(editOffer.max != null ? String(editOffer.max) : "");
      setMethods(editOffer.methods.map(capitalize));
    } else {
      setSide("sell");
      setAsset("BTC");
      setQuoteKind("fiat");
      setFiat("UAH");
      setPrice("");
      setMinAmount("");
      setMaxAmount("");
      setMethods([]);
    }
  }, [open, editOffer]);

  // Keep the crypto quote distinct from the traded asset.
  useEffect(() => {
    if (quoteKind === "crypto" && fiat.toUpperCase() === asset.toUpperCase()) {
      setFiat(CRYPTO_QUOTES.find((q) => q !== asset.toUpperCase()) ?? "USDT");
    }
  }, [quoteKind, asset, fiat]);

  const switchQuoteKind = (kind: QuoteKind) => {
    setQuoteKind(kind);
    if (kind === "fiat") setFiat("UAH");
    else setFiat(CRYPTO_QUOTES.find((q) => q !== asset.toUpperCase()) ?? "USDT");
  };

  const toggleMethod = (m: string) =>
    setMethods((prev) => (prev.includes(m) ? prev.filter((x) => x !== m) : [...prev, m]));

  const parseNum = (s: string): number | undefined => {
    if (!s.trim()) return undefined;
    const v = Number.parseFloat(s.replace(",", "."));
    return Number.isFinite(v) ? v : undefined;
  };

  const submit = async () => {
    const p = parseNum(price);
    const minV = parseNum(minAmount);
    const maxV = parseNum(maxAmount);
    if (!p || p <= 0) {
      toast.error("Enter a valid price");
      return;
    }
    if (minV !== undefined && maxV !== undefined && minV > maxV) {
      toast.error("Minimum limit cannot exceed maximum");
      return;
    }
    if (quoteKind === "fiat" && !methods.length) {
      toast.error("Select at least one payment method");
      return;
    }
    if (quoteKind === "crypto" && fiat.toUpperCase() === asset.toUpperCase()) {
      toast.error("The quote asset must differ from the asset you trade");
      return;
    }
    setBusy(true);
    try {
      if (editing && editOffer) {
        await api.updateP2pOffer(editOffer.id, {
          price: p,
          ...(minV !== undefined ? { minAmount: minV } : {}),
          ...(maxV !== undefined ? { maxAmount: maxV } : {}),
          ...(quoteKind === "fiat" ? { paymentMethods: methods } : {}),
        });
        toast.success("Offer updated");
      } else {
        await api.createP2pOffer({
          asset,
          fiatCurrency: fiat,
          quoteKind,
          price: p,
          ...(minV !== undefined ? { minAmount: minV } : {}),
          ...(maxV !== undefined ? { maxAmount: maxV } : {}),
          paymentMethods: quoteKind === "fiat" ? methods : [],
          side,
        });
        toast.success("Offer published");
      }
      onSaved();
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) {
        toast.error("KYC level 1+ required — complete it in Settings");
      } else {
        toast.error(e instanceof Error ? e.message : "Could not save offer");
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <ActionSheet
      open={open}
      onOpenChange={(v) => !v && onClose()}
      title={editing ? "Edit offer" : "New offer"}
      description={
        editing
          ? `${editOffer?.asset}/${editOffer?.fiat} · side and asset cannot be changed`
          : "Publish your rate — Umbrella only coordinates; it never holds funds"
      }
    >
      <div className="rounded-2xl bg-card p-4 space-y-4 text-sm">
        {!editing && (
          <>
            <div className="grid grid-cols-2 rounded-2xl bg-secondary p-1">
              {(["sell", "buy"] as const).map((s) => (
                <button
                  key={s}
                  onClick={() => setSide(s)}
                  className={`py-2 rounded-xl text-sm font-semibold transition ${
                    side === s ? "bg-card text-foreground shadow" : "text-muted-foreground"
                  }`}
                >
                  {s === "sell" ? "I sell crypto" : "I buy crypto"}
                </button>
              ))}
            </div>

            <div>
              <label className="text-xs text-muted-foreground">Asset</label>
              <div className="mt-1.5 flex gap-2 overflow-x-auto pb-1">
                {ASSETS.map((a) => (
                  <button
                    key={a}
                    onClick={() => setAsset(a)}
                    className={`px-4 py-1.5 rounded-full text-sm font-medium whitespace-nowrap ${
                      asset === a
                        ? "bg-primary text-primary-foreground"
                        : "bg-secondary text-muted-foreground"
                    }`}
                  >
                    {a}
                  </button>
                ))}
              </div>
            </div>

            <div>
              <label className="text-xs text-muted-foreground">Settlement</label>
              <div className="mt-1.5 grid grid-cols-2 rounded-2xl bg-secondary p-1">
                <button
                  onClick={() => switchQuoteKind("fiat")}
                  className={`py-2 rounded-xl text-sm font-semibold transition flex items-center justify-center gap-1.5 ${
                    quoteKind === "fiat"
                      ? "bg-card text-foreground shadow"
                      : "text-muted-foreground"
                  }`}
                >
                  <Landmark className="h-4 w-4" /> For fiat
                </button>
                <button
                  onClick={() => switchQuoteKind("crypto")}
                  className={`py-2 rounded-xl text-sm font-semibold transition flex items-center justify-center gap-1.5 ${
                    quoteKind === "crypto"
                      ? "bg-card text-foreground shadow"
                      : "text-muted-foreground"
                  }`}
                >
                  <Coins className="h-4 w-4" /> For crypto
                </button>
              </div>
            </div>

            <div>
              <label className="text-xs text-muted-foreground">
                {quoteKind === "fiat" ? "Fiat" : "Quote crypto"}
              </label>
              <div className="mt-1.5 flex gap-2 flex-wrap">
                {(quoteKind === "fiat" ? FIATS : CRYPTO_QUOTES)
                  .filter((q) => quoteKind === "fiat" || q !== asset.toUpperCase())
                  .map((f) => (
                    <button
                      key={f}
                      onClick={() => setFiat(f)}
                      className={`px-4 py-1.5 rounded-full text-sm font-medium ${
                        fiat === f
                          ? "bg-primary text-primary-foreground"
                          : "bg-secondary text-muted-foreground"
                      }`}
                    >
                      {f}
                    </button>
                  ))}
              </div>
            </div>
          </>
        )}

        <div>
          <label className="text-xs text-muted-foreground">
            Price per 1 {asset} ({fiat})
          </label>
          <input
            value={price}
            onChange={(e) => setPrice(e.target.value)}
            type="number"
            inputMode="decimal"
            min={0}
            placeholder="41.25"
            className="mt-1.5 w-full bg-secondary rounded-xl px-3 py-2.5 outline-none"
          />
        </div>

        <div className="grid grid-cols-2 gap-2">
          <div>
            <label className="text-xs text-muted-foreground">Min. ({fiat})</label>
            <input
              value={minAmount}
              onChange={(e) => setMinAmount(e.target.value)}
              type="number"
              inputMode="decimal"
              min={0}
              placeholder="500"
              className="mt-1.5 w-full bg-secondary rounded-xl px-3 py-2.5 outline-none"
            />
          </div>
          <div>
            <label className="text-xs text-muted-foreground">Max. ({fiat})</label>
            <input
              value={maxAmount}
              onChange={(e) => setMaxAmount(e.target.value)}
              type="number"
              inputMode="decimal"
              min={0}
              placeholder="50000"
              className="mt-1.5 w-full bg-secondary rounded-xl px-3 py-2.5 outline-none"
            />
          </div>
        </div>

        {quoteKind === "fiat" ? (
          <div>
            <label className="text-xs text-muted-foreground">Payment methods</label>
            <div className="mt-1.5 flex flex-wrap gap-2">
              {METHODS.map((m) => (
                <button
                  key={m}
                  onClick={() => toggleMethod(m)}
                  className={`px-3 py-1.5 rounded-full text-xs font-medium ${
                    methods.includes(m)
                      ? "bg-primary text-primary-foreground"
                      : "bg-secondary text-muted-foreground"
                  }`}
                >
                  {m}
                </button>
              ))}
            </div>
          </div>
        ) : (
          <div className="flex items-start gap-2 rounded-xl bg-secondary/60 p-3 text-xs text-muted-foreground">
            <Coins className="h-4 w-4 shrink-0 text-primary" />
            <span>
              Crypto-to-crypto: both sides settle on-chain. The buyer sends {fiat}, then you send{" "}
              {asset}. Each transfer is recorded by its tx hash.
            </span>
          </div>
        )}

        <div className="flex items-start gap-2 rounded-xl bg-secondary/60 p-3 text-xs text-muted-foreground">
          <ShieldCheck className="h-4 w-4 shrink-0 text-primary" />
          <span>
            Publishing offers requires KYC verification. You may have only one active deal at a time
            alongside an offer.
          </span>
        </div>

        <button
          disabled={busy}
          onClick={() => void submit()}
          className="w-full py-3 rounded-2xl bg-primary text-primary-foreground font-semibold disabled:opacity-60"
        >
          {editing ? "Save changes" : "Publish"}
        </button>
      </div>
    </ActionSheet>
  );
}

function capitalize(s: string) {
  return s.length ? s[0].toUpperCase() + s.slice(1) : s;
}
