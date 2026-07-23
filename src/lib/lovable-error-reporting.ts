import { scrubSensitive } from "./scrubSecrets";
import { isPrivacyMode } from "./privacyMode";
import { isRunningViaTor } from "./wallet/tor";

type LovableErrorOptions = {
  mechanism?: "manual" | "onerror" | "unhandledrejection" | "react_error_boundary";
  handled?: boolean;
  severity?: "error" | "warning" | "info";
};

type LovableEvents = {
  captureException?: (
    error: unknown,
    context?: Record<string, unknown>,
    options?: LovableErrorOptions,
  ) => void;
};

declare global {
  interface Window {
    __lovableEvents?: LovableEvents;
  }
}

export function reportLovableError(error: unknown, context: Record<string, unknown> = {}) {
  if (typeof window === "undefined") return;
  if (isPrivacyMode() || isRunningViaTor()) return;
  window.__lovableEvents?.captureException?.(
    error,
    scrubSensitive({
      source: "react_error_boundary",
      route: window.location.pathname,
      ...context,
    }) as Record<string, unknown>,
    {
      mechanism: "react_error_boundary",
      handled: false,
      severity: "error",
    },
  );
}
