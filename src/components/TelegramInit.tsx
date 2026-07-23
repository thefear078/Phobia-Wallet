import { useEffect } from "react";
import {
  ensureTelegramScript,
  initTelegramWebApp,
  isLikelyTelegramEnv,
} from "@/lib/telegram/telegramApp";
import { isPrivacyMode } from "@/lib/privacyMode";

/**
 * Initializes the Telegram Mini App viewport. The telegram.org script is
 * injected only when the app actually runs inside Telegram (URL marker),
 * never in privacy/Tor mode — ordinary visitors make zero third-party requests.
 */
export function TelegramInit() {
  useEffect(() => {
    if (isPrivacyMode() || !isLikelyTelegramEnv()) return;
    void ensureTelegramScript().then(() => initTelegramWebApp());
  }, []);
  return null;
}
