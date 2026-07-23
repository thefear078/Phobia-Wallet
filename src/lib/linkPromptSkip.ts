const SKIP_KEY = "umbra.skipLinkPrompt.v1";

export function skipLinkPrompt() {
  if (typeof window !== "undefined") {
    localStorage.setItem(SKIP_KEY, "1");
  }
}

export function hasSkippedLinkPrompt(): boolean {
  if (typeof window === "undefined") return false;
  return localStorage.getItem(SKIP_KEY) === "1";
}

export function clearSkippedLinkPrompt() {
  if (typeof window !== "undefined") {
    localStorage.removeItem(SKIP_KEY);
  }
}
