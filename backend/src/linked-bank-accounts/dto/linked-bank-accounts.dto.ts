import { IsOptional, IsString, MinLength, ValidateIf } from "class-validator";

export class LinkMonobankDto {
  @ValidateIf((o: LinkMonobankDto) => !o.personalToken)
  @IsString()
  @MinLength(20)
  token?: string;

  @ValidateIf((o: LinkMonobankDto) => !o.token)
  @IsString()
  @MinLength(20)
  personalToken?: string;
}

export function resolveMonobankToken(dto: LinkMonobankDto): string {
  const value = dto.token?.trim() || dto.personalToken?.trim();
  if (!value) {
    throw new Error("Monobank token is required");
  }
  return value;
}

export class LinkBankAccountDto {
  @IsString()
  provider!: string;

  @IsString()
  @MinLength(1)
  providerAccountId!: string;

  @IsOptional()
  @IsString()
  bankName?: string;

  @IsOptional()
  @IsString()
  maskedNumber?: string;
}
