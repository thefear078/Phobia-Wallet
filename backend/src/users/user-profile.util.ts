/** Public user profile — no password hashes, OAuth subs, or Telegram IDs. */
export type PublicUserProfile = {
  id: string;
  email: string;
  username: string | null;
  name: string | null;
  lang: string;
  emailVerified: boolean;
  tfaEnabled: boolean;
  pushEnabled: boolean;
  emailAlerts: boolean;
  priceAlerts: boolean;
  kyc: string;
  createdAt: Date;
};

export function toPublicProfile(
  user: {
    id: string;
    email: string;
    username: string | null;
    name: string | null;
    lang: string;
    emailVerified: boolean;
    tfaEnabled: boolean;
    pushEnabled: boolean;
    emailAlerts: boolean;
    priceAlerts: boolean;
    createdAt: Date;
  },
  kycStatus = "none",
): PublicUserProfile {
  return {
    id: user.id,
    email: user.email,
    username: user.username,
    name: user.name,
    lang: user.lang,
    emailVerified: user.emailVerified,
    tfaEnabled: user.tfaEnabled,
    pushEnabled: user.pushEnabled,
    emailAlerts: user.emailAlerts,
    priceAlerts: user.priceAlerts,
    kyc: kycStatus,
    createdAt: user.createdAt,
  };
}

export function isUserDeleted(user: { deletedAt: Date | null }): boolean {
  return user.deletedAt !== null;
}
