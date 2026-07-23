type Props = {
  prices: number[];
  width?: number;
  height?: number;
  className?: string;
};

export function Sparkline({ prices, width = 96, height = 32, className }: Props) {
  if (prices.length < 2) {
    return <svg width={width} height={height} className={className} aria-hidden />;
  }

  const min = Math.min(...prices);
  const max = Math.max(...prices);
  const range = max - min || 1;
  const step = width / (prices.length - 1);

  const points = prices
    .map((p, i) => {
      const x = i * step;
      const y = height - ((p - min) / range) * (height - 4) - 2;
      return `${x},${y}`;
    })
    .join(" ");

  const up = prices[prices.length - 1] >= prices[0];
  const stroke = up ? "var(--success, #22c55e)" : "var(--destructive, #ef4444)";

  return (
    <svg
      width={width}
      height={height}
      className={className}
      aria-hidden
      viewBox={`0 0 ${width} ${height}`}
    >
      <polyline
        fill="none"
        stroke={stroke}
        strokeWidth="1.5"
        strokeLinejoin="round"
        strokeLinecap="round"
        points={points}
      />
    </svg>
  );
}
