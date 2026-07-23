/** Demo mode — opt-in only via VITE_DEMO_MODE=true (local/testing). */
export function isDemoMode(): boolean {
  const flag = import.meta.env.VITE_DEMO_MODE as string | undefined;
  return flag === "true";
}
