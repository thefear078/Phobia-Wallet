import { useId, useMemo, useState } from "react";

type Props = {
  data: number[];
  color: string;
  width: number;
  height: number;
  className?: string;
};

export function PriceChart({ data, color, width, height, className }: Props) {
  const gradId = useId().replace(/:/g, "");
  const [hover, setHover] = useState<{ x: number; y: number; value: number } | null>(null);

  const { line, area, points } = useMemo(() => {
    if (data.length < 2) {
      return { line: "", area: "", points: [] as Array<{ x: number; y: number; value: number }> };
    }
    const min = Math.min(...data);
    const max = Math.max(...data);
    const range = max - min || 1;
    const pad = 2;
    const pts = data.map((value, i) => {
      const x = (i / (data.length - 1)) * width;
      const y = height - pad - ((value - min) / range) * (height - pad * 2);
      return { x, y, value };
    });
    const linePath = pts
      .map((p, i) => `${i === 0 ? "M" : "L"}${p.x.toFixed(2)},${p.y.toFixed(2)}`)
      .join(" ");
    const areaPath = `${linePath} L${width},${height} L0,${height} Z`;
    return { line: linePath, area: areaPath, points: pts };
  }, [data, width, height]);

  if (data.length < 2) {
    return <svg width={width} height={height} className={className} aria-hidden />;
  }

  return (
    <div className={`relative inline-block ${className ?? ""}`} style={{ width, height }}>
      <svg
        width={width}
        height={height}
        viewBox={`0 0 ${width} ${height}`}
        onMouseLeave={() => setHover(null)}
        onMouseMove={(e) => {
          const rect = e.currentTarget.getBoundingClientRect();
          const x = ((e.clientX - rect.left) / rect.width) * width;
          let best = points[0];
          let bestDist = Infinity;
          for (const p of points) {
            const d = Math.abs(p.x - x);
            if (d < bestDist) {
              bestDist = d;
              best = p;
            }
          }
          setHover(best);
        }}
      >
        <defs>
          <linearGradient id={gradId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={color} stopOpacity={0.35} />
            <stop offset="100%" stopColor={color} stopOpacity={0} />
          </linearGradient>
        </defs>
        <path d={area} fill={`url(#${gradId})`} />
        <path
          d={line}
          fill="none"
          stroke={color}
          strokeWidth={1.75}
          strokeLinejoin="round"
          strokeLinecap="round"
        />
        <title>
          {data[0]?.toFixed(2)} → {data[data.length - 1]?.toFixed(2)}
        </title>
        {hover && (
          <circle
            cx={hover.x}
            cy={hover.y}
            r={3.5}
            fill={color}
            stroke="var(--background)"
            strokeWidth={1.5}
          />
        )}
      </svg>
      {hover && (
        <div
          className="pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full rounded-md border border-border bg-card px-2 py-1 text-[10px] font-medium shadow-md"
          style={{ left: hover.x, top: Math.max(0, hover.y - 6) }}
        >
          {hover.value.toLocaleString("en-US", { maximumFractionDigits: 4 })}
        </div>
      )}
    </div>
  );
}
