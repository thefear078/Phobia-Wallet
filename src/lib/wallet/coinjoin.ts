/**
 * CoinJoin client stubs — Wasabi / WabiSabi coordinator types.
 * Full WabiSabi needs a dedicated library (wasabi-backend / BTCPayServer).
 * See README in this folder for integration notes.
 */

/** Public Wasabi coordinator (.onion — use Tor Browser or a SOCKS5 tunnel). */
export const WASABI_COORDINATOR =
  "http://wasabiukrxmkdgve5kynjztuovbg43uxcbcxn6y2okcrsg7gb6jdmbad.onion";

export type UTXO = {
  txid: string;
  vout: number;
  amount: bigint;
  scriptPubKey: string;
  address: string;
};

export type BlindedCredential = {
  /** Blinded amount credential (WabiSabi). */
  amount: string;
  /** Blinded vsize credential. */
  vsize: string;
  /** Blinded MAC / proof blob (hex). */
  proof: string;
};

export type CoinjoinRound = {
  id: string;
  phase:
    "inputRegistration" | "connectionConfirmation" | "outputRegistration" | "signing" | "ended";
  inputRegistrationEnd: string;
  amountCredentialIssuerParameters: string;
  vsizeCredentialIssuerParameters: string;
  feeRate: number;
  coordinationFeeRate: number;
  maxSuggestedAmount: number;
};

export async function getCoinjoinRounds(): Promise<CoinjoinRound[]> {
  // Stub: real integration talks to WASABI_COORDINATOR over Tor SOCKS5.
  void WASABI_COORDINATOR;
  return [];
}

export async function registerInput(
  round: CoinjoinRound,
  utxo: UTXO,
  proof: string,
): Promise<{ aliceId: string; credentials: BlindedCredential[] }> {
  void round;
  void utxo;
  void proof;
  throw new Error(
    "CoinJoin registerInput is not implemented in MVP — integrate wasabi-backend or BTCPayServer",
  );
}

export async function registerOutput(
  aliceId: string,
  outputAddress: string,
  credentials: BlindedCredential[],
): Promise<void> {
  void aliceId;
  void outputAddress;
  void credentials;
  throw new Error(
    "CoinJoin registerOutput is not implemented in MVP — integrate wasabi-backend or BTCPayServer",
  );
}
