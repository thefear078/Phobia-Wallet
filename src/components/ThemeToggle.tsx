import { Monitor, Moon, Sun } from "lucide-react";
import { setTheme, useTheme, type Theme } from "@/lib/theme";

const OPTIONS: Array<{ value: Theme; label: string; Icon: typeof Sun }> = [
  { value: "light", label: "Light", Icon: Sun },
  { value: "dark", label: "Dark", Icon: Moon },
  { value: "system", label: "System", Icon: Monitor },
];

/** Segmented light / dark / system control. */
export function ThemeToggle({ className = "" }: { className?: string }) {
  const { theme } = useTheme();

  return (
    <div
      role="radiogroup"
      aria-label="Theme"
      className={`grid grid-cols-3 gap-1 rounded-xl bg-secondary p-1 ${className}`}
    >
      {OPTIONS.map(({ value, label, Icon }) => {
        const active = theme === value;
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => setTheme(value)}
            className={`flex items-center justify-center gap-1.5 rounded-lg py-2 text-xs font-medium transition ${
              active
                ? "bg-card text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground"
            }`}
          >
            <Icon className="h-3.5 w-3.5" />
            {label}
          </button>
        );
      })}
    </div>
  );
}

/** Compact icon-only cycle button for headers. */
export function ThemeToggleButton({ className = "" }: { className?: string }) {
  const { resolved } = useTheme();
  const next = resolved === "dark" ? "light" : "dark";
  const Icon = resolved === "dark" ? Sun : Moon;
  return (
    <button
      type="button"
      onClick={() => setTheme(next)}
      aria-label={`Switch to ${next} theme`}
      className={`h-10 w-10 rounded-full bg-secondary flex items-center justify-center hover:bg-accent transition ${className}`}
    >
      <Icon className="h-4 w-4" />
    </button>
  );
}
