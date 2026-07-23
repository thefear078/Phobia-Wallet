import { BadRequestException } from "@nestjs/common";
import { recoverMessageAddress } from "viem";

export async function verifyEvmPersonalSign(
  message: string,
  signature: string,
  expectedAddress: string,
): Promise<void> {
  if (!signature.startsWith("0x") || signature.length < 130) {
    throw new BadRequestException("Invalid signature format");
  }

  let recovered: string;
  try {
    recovered = await recoverMessageAddress({
      message,
      signature: signature as `0x${string}`,
    });
  } catch {
    throw new BadRequestException("Could not recover signer from signature");
  }

  if (recovered.toLowerCase() !== expectedAddress.toLowerCase()) {
    throw new BadRequestException("Signature does not match wallet address");
  }
}
