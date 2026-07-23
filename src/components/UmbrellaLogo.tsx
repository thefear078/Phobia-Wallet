/**
 * Brand logo from the official Umbrella mark (faceted umbrella).
 * White PNG on transparent background — prefer on dark UI surfaces.
 */
import logoMark from "@/assets/umbrella-icon-mark.png";
import logoFull from "@/assets/umbrella-logo-white.png";

export function UmbrellaLogo({
  className = "h-10 w-10",
  accent = false,
  withWordmark = false,
}: {
  className?: string;
  /** Kept for call-site compatibility; brand mark is monochrome white. */
  accent?: boolean;
  /** Include the UMBRELLA wordmark beneath the icon. */
  withWordmark?: boolean;
}) {
  void accent;
  const src = withWordmark ? logoFull : logoMark;
  return (
    <img
      src={src}
      alt="Umbrella Wallet"
      className={`${className} object-contain object-center select-none`}
      draggable={false}
    />
  );
}
