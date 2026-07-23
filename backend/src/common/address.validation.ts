import { BadRequestException } from "@nestjs/common";
import { getAddress, isAddress } from "viem";

const EVM_CHAINS = new Set([
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
]);

export function isEvmChain(chain: string): boolean {
  return EVM_CHAINS.has(chain.toLowerCase()) || chain.toLowerCase().startsWith("evm-");
}

export function validateWalletAddress(chain: string, address: string): string {
  const c = chain.toLowerCase();
  const trimmed = address.trim();

  if (trimmed.length < 10) {
    throw new BadRequestException("Address too short");
  }

  if (isEvmChain(c)) {
    if (!isAddress(trimmed)) {
      throw new BadRequestException("Invalid EVM address format");
    }
    return getAddress(trimmed);
  }

  switch (c) {
    case "bitcoin":
      if (!/^(bc1|[13])[a-zA-HJ-NP-Z0-9]{25,62}$/.test(trimmed)) {
        throw new BadRequestException("Invalid Bitcoin address");
      }
      break;
    case "solana":
      if (!/^[1-9A-HJ-NP-Za-km-z]{32,44}$/.test(trimmed)) {
        throw new BadRequestException("Invalid Solana address");
      }
      break;
    case "ton":
      if (!/^(EQ|UQ)[A-Za-z0-9_-]{46}$/.test(trimmed) && trimmed.length < 48) {
        throw new BadRequestException("Invalid TON address (EQ… / UQ…)");
      }
      break;
    case "tron":
      if (!/^T[1-9A-HJ-NP-Za-km-z]{33}$/.test(trimmed)) {
        throw new BadRequestException("Invalid TRON address");
      }
      break;
    case "other":
      break;
    default:
      throw new BadRequestException(`Unsupported chain: ${chain}`);
  }

  return trimmed;
}
