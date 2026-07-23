import { IsBoolean, IsIn, IsOptional, IsString, MaxLength } from "class-validator";

export class UpdateUserDto {
  @IsOptional()
  @IsString()
  @MaxLength(64)
  name?: string;

  /** Alias for display name (maps to `name`). */
  @IsOptional()
  @IsString()
  @MaxLength(64)
  display_name?: string;

  @IsOptional()
  @IsIn(["uk", "en", "ru"])
  lang?: string;

  @IsOptional()
  @IsBoolean()
  pushEnabled?: boolean;

  @IsOptional()
  @IsBoolean()
  emailAlerts?: boolean;

  @IsOptional()
  @IsBoolean()
  priceAlerts?: boolean;

  @IsOptional()
  @IsBoolean()
  tfaEnabled?: boolean;
}

export function mapUpdateUserDto(dto: UpdateUserDto) {
  const { display_name, ...rest } = dto;
  return {
    ...rest,
    ...(display_name !== undefined ? { name: display_name } : {}),
  };
}
