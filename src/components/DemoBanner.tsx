import { isDemoMode } from "@/lib/demoMode";

export function DemoBanner() {
  if (!isDemoMode()) return null;
  return (
    <div className="sticky top-0 z-50 border-b border-primary/30 bg-primary/10 px-4 py-2 text-center text-[11px] text-primary">
      Demo mode — local data only, no live API or external services
    </div>
  );
}
