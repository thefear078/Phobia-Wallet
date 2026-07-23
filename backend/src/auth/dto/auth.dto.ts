import {
  registerDecorator,
  ValidationOptions,
  MinLength,
  IsString,
  IsEmail,
  IsOptional,
  ValidatorConstraint,
  ValidatorConstraintInterface,
  Validate,
  ValidationArguments,
} from "class-validator";

const USERNAME_RE = /^[a-zA-Z0-9_\u0400-\u04FF.-]{3,32}$/;

export function IsUsername(validationOptions?: ValidationOptions) {
  return function (object: object, propertyName: string) {
    registerDecorator({
      name: "isUsername",
      target: object.constructor,
      propertyName,
      options: validationOptions,
      validator: {
        validate(value: unknown) {
          return typeof value === "string" && USERNAME_RE.test(value.trim());
        },
        defaultMessage() {
          return "Username: 3–32 characters (letters, digits, _ . -)";
        },
      },
    });
  };
}

@ValidatorConstraint({ name: "registerIdentifier", async: false })
class RegisterIdentifierConstraint implements ValidatorConstraintInterface {
  validate(_: unknown, args: ValidationArguments) {
    const dto = args.object as RegisterDto;
    const email = dto.email?.trim();
    const username = dto.username?.trim();
    return Boolean(email || username);
  }

  defaultMessage() {
    return "Provide email or username to register";
  }
}

export class RegisterDto {
  @IsOptional()
  @IsEmail()
  email?: string;

  @IsOptional()
  @IsUsername()
  username?: string;

  @IsString()
  @MinLength(8)
  password!: string;

  @Validate(RegisterIdentifierConstraint)
  private readonly _registerIdentifierCheck?: never;
}

export class LoginDto {
  /** Email or username (`username` field for client compatibility). */
  @IsString()
  @MinLength(3)
  username!: string;

  @IsString()
  password!: string;
}

export class RequestEmailVerificationDto {
  @IsEmail()
  email!: string;
}

export class VerifyEmailDto {
  @IsString()
  @MinLength(10)
  token!: string;
}

export class TelegramAuthDto {
  @IsString()
  initData!: string;
}
