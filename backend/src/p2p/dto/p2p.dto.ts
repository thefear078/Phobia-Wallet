import {
  ArrayMaxSize,
  IsArray,
  IsIn,
  IsNumber,
  IsOptional,
  IsString,
  Matches,
  Max,
  MaxLength,
  Min,
  MinLength,
  Validate,
  ValidateIf,
  ValidationArguments,
  ValidatorConstraint,
  ValidatorConstraintInterface,
} from "class-validator";

/** Upper bound for fiat/crypto numeric inputs — rejects absurd values and float overflow games. */
const MAX_NUMERIC = 1_000_000_000_000;

@ValidatorConstraint({ name: "minLteMax", async: false })
class MinLteMaxConstraint implements ValidatorConstraintInterface {
  validate(_: unknown, args: ValidationArguments) {
    const dto = args.object as CreateOfferDto;
    const min = dto.min_amount ?? dto.minAmount;
    const max = dto.max_amount ?? dto.maxAmount;
    if (min === undefined || max === undefined) return true;
    return min <= max;
  }

  defaultMessage() {
    return "min_amount must be less than or equal to max_amount";
  }
}

export class CreateOfferDto {
  @IsString()
  @Matches(/^[A-Za-z0-9]{2,10}$/, { message: "asset must be a 2-10 char ticker" })
  asset!: string;

  // "fiat" (default) → quote is an ISO fiat code paid by bank transfer.
  // "crypto" → quote is a crypto ticker settled on-chain (buyer sends a tx too).
  @ValidateIf((o: CreateOfferDto) => !o.quote_kind)
  @IsOptional()
  @IsIn(["fiat", "crypto"])
  quoteKind?: "fiat" | "crypto";

  @ValidateIf((o: CreateOfferDto) => !o.quoteKind)
  @IsOptional()
  @IsIn(["fiat", "crypto"])
  quote_kind?: "fiat" | "crypto";

  // Quote ticker: 3-letter ISO for fiat, or 2-10 char ticker for crypto.
  // The precise rule per quoteKind is enforced in resolveOfferFields().
  @ValidateIf((o: CreateOfferDto) => !o.fiatCurrency)
  @IsString()
  @Matches(/^[A-Za-z0-9]{2,10}$/, { message: "quote must be a 2-10 char ticker/ISO code" })
  fiat_currency?: string;

  @ValidateIf((o: CreateOfferDto) => !o.fiat_currency)
  @IsString()
  @Matches(/^[A-Za-z0-9]{2,10}$/, { message: "quote must be a 2-10 char ticker/ISO code" })
  fiatCurrency?: string;

  @IsNumber()
  @Min(0.00000001)
  @Max(MAX_NUMERIC)
  price!: number;

  @ValidateIf((o: CreateOfferDto) => o.minAmount === undefined)
  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  min_amount?: number;

  @ValidateIf((o: CreateOfferDto) => o.min_amount === undefined)
  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  minAmount?: number;

  @ValidateIf((o: CreateOfferDto) => o.maxAmount === undefined)
  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  max_amount?: number;

  @ValidateIf((o: CreateOfferDto) => o.max_amount === undefined)
  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  maxAmount?: number;

  // Optional at the DTO layer — resolveOfferFields() requires ≥1 method for
  // fiat quotes and defaults crypto quotes to on-chain settlement.
  @IsOptional()
  @IsArray()
  @ArrayMaxSize(10)
  @IsString({ each: true })
  @MaxLength(32, { each: true })
  payment_methods?: string[];

  @IsOptional()
  @IsArray()
  @ArrayMaxSize(10)
  @IsString({ each: true })
  @MaxLength(32, { each: true })
  paymentMethods?: string[];

  @ValidateIf((o: CreateOfferDto) => !o.side)
  @IsOptional()
  @IsIn(["buy", "sell"])
  direction?: "buy" | "sell";

  @ValidateIf((o: CreateOfferDto) => !o.direction)
  @IsOptional()
  @IsIn(["buy", "sell"])
  side?: "buy" | "sell";

  @Validate(MinLteMaxConstraint)
  private readonly _minLteMax?: never;
}

export class UpdateOfferDto {
  @IsOptional()
  @IsNumber()
  @Min(0.00000001)
  @Max(MAX_NUMERIC)
  price?: number;

  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  min_amount?: number;

  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  minAmount?: number;

  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  max_amount?: number;

  @IsOptional()
  @IsNumber()
  @Min(0)
  @Max(MAX_NUMERIC)
  maxAmount?: number;

  @IsOptional()
  @IsArray()
  @ArrayMaxSize(10)
  @IsString({ each: true })
  @MaxLength(32, { each: true })
  payment_methods?: string[];

  @IsOptional()
  @IsArray()
  @ArrayMaxSize(10)
  @IsString({ each: true })
  @MaxLength(32, { each: true })
  paymentMethods?: string[];
}

export class CreateOrderDto {
  @ValidateIf((o: CreateOrderDto) => !o.offerId)
  @IsString()
  offer_id?: string;

  @ValidateIf((o: CreateOrderDto) => !o.offer_id)
  @IsString()
  offerId?: string;

  @IsNumber()
  @Min(0.00000001)
  @Max(MAX_NUMERIC)
  amount!: number;

  @IsOptional()
  @IsString()
  @MaxLength(32)
  payment_method?: string;

  @IsOptional()
  @IsString()
  @MaxLength(32)
  paymentMethod?: string;
}

export class FiatProofDto {
  @ValidateIf((o: FiatProofDto) => !o.fiatPaymentReference)
  @IsString()
  @MinLength(4)
  @MaxLength(128)
  fiat_payment_reference?: string;

  @ValidateIf((o: FiatProofDto) => !o.fiat_payment_reference)
  @IsString()
  @MinLength(4)
  @MaxLength(128)
  fiatPaymentReference?: string;
}

export class CryptoProofDto {
  @ValidateIf((o: CryptoProofDto) => !o.cryptoTxHash)
  @IsString()
  @Matches(/^0x[a-fA-F0-9]{64}$/, {
    message: "crypto_tx_hash must be an EVM tx hash (0x + 64 hex)",
  })
  crypto_tx_hash?: string;

  @ValidateIf((o: CryptoProofDto) => !o.crypto_tx_hash)
  @IsString()
  @Matches(/^0x[a-fA-F0-9]{64}$/, { message: "cryptoTxHash must be an EVM tx hash (0x + 64 hex)" })
  cryptoTxHash?: string;
}

export class DisputeOrderDto {
  @IsString()
  @MinLength(3)
  @MaxLength(500)
  reason!: string;
}

export function resolveOfferFields(dto: CreateOfferDto) {
  const quoteKind = (dto.quote_kind ?? dto.quoteKind ?? "fiat") as "fiat" | "crypto";
  const quote = (dto.fiat_currency ?? dto.fiatCurrency ?? "").toUpperCase();
  const asset = dto.asset.toUpperCase();
  const methods = (dto.payment_methods ?? dto.paymentMethods ?? []).map((m) => m.toLowerCase());
  const side = dto.direction ?? dto.side ?? "buy";
  const minAmount = dto.min_amount ?? dto.minAmount;
  const maxAmount = dto.max_amount ?? dto.maxAmount;

  if (!quote) throw new Error("quote currency is required");

  if (quoteKind === "fiat") {
    if (!/^[A-Z]{3}$/.test(quote)) throw new Error("fiat quote must be a 3-letter ISO code");
    if (!methods.length) throw new Error("payment_methods must not be empty");
    return { fiat: quote, quoteKind, methods, side, minAmount, maxAmount };
  }

  // crypto quote: on-chain settlement, no bank methods
  if (!/^[A-Z0-9]{2,10}$/.test(quote)) throw new Error("crypto quote must be a 2-10 char ticker");
  if (quote === asset) throw new Error("quote asset must differ from the traded asset");
  return {
    fiat: quote,
    quoteKind,
    methods: methods.length ? methods : ["onchain"],
    side,
    minAmount,
    maxAmount,
  };
}

export function resolveOrderFields(dto: CreateOrderDto) {
  const offerId = dto.offer_id ?? dto.offerId;
  const paymentMethod = dto.payment_method ?? dto.paymentMethod;
  if (!offerId) throw new Error("offer_id is required");
  return { offerId, paymentMethod };
}

export function resolveFiatReference(dto: FiatProofDto): string {
  return (dto.fiat_payment_reference ?? dto.fiatPaymentReference ?? "").trim();
}

export function resolveCryptoHash(dto: CryptoProofDto): string {
  return (dto.crypto_tx_hash ?? dto.cryptoTxHash ?? "").trim();
}
