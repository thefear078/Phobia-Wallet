import { IsBoolean, IsOptional, IsString, MinLength, ValidateIf } from "class-validator";
import { Transform } from "class-transformer";

export class LinkWalletDto {
  @IsString()
  @MinLength(2)
  chain!: string;

  @IsString()
  @MinLength(10)
  address!: string;

  @IsOptional()
  @IsString()
  label?: string;

  /** Read-only address — no signature required */
  @IsOptional()
  @IsBoolean()
  @Transform(({ value }) => value === true || value === "true")
  watchOnly?: boolean;

  @ValidateIf((o: LinkWalletDto) => !o.watchOnly)
  @IsString()
  @MinLength(10)
  message?: string;

  @ValidateIf((o: LinkWalletDto) => !o.watchOnly)
  @IsString()
  @MinLength(10)
  signature?: string;
}

export class WalletBalanceQueryDto {
  @IsOptional()
  @IsString()
  chain?: string;
}

export class WalletChallengeResponse {
  nonce!: string;
  message!: string;
  expiresIn!: number;
}
