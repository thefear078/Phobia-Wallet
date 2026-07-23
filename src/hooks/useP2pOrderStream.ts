import { useEffect } from "react";
import { getAccessToken } from "@/lib/api/client";
import { isDemoMode } from "@/lib/demoMode";

export type P2pOrderStatusEvent = {
  orderId: string;
  offerId: string;
  buyerId: string;
  sellerId: string;
  status: string;
  amount: number;
  at: string;
};

function parseSseChunk(buffer: string): { events: P2pOrderStatusEvent[]; rest: string } {
  const events: P2pOrderStatusEvent[] = [];
  const parts = buffer.split("\n\n");
  const rest = parts.pop() ?? "";
  for (const part of parts) {
    for (const line of part.split("\n")) {
      if (!line.startsWith("data:")) continue;
      const payload = line.slice(5).trim();
      if (!payload) continue;
      try {
        events.push(JSON.parse(payload) as P2pOrderStatusEvent);
      } catch {
        /* ignore malformed */
      }
    }
  }
  return { events, rest };
}

export function subscribeP2pOrderStream(
  onEvent: (event: P2pOrderStatusEvent) => void,
  onError?: (error: unknown) => void,
): () => void {
  if (typeof window === "undefined") return () => {};

  const ac = new AbortController();

  void (async () => {
    const token = getAccessToken();
    const res = await fetch("/p2p/orders/stream", {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      credentials: "include",
      signal: ac.signal,
    });
    if (!res.ok || !res.body) {
      throw new Error(`SSE failed: ${res.status}`);
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const parsed = parseSseChunk(buffer);
      buffer = parsed.rest;
      for (const event of parsed.events) onEvent(event);
    }
  })().catch((err) => {
    if (ac.signal.aborted) return;
    onError?.(err);
  });

  return () => ac.abort();
}

export function useP2pOrderStream(onEvent: (event: P2pOrderStatusEvent) => void, enabled = true) {
  useEffect(() => {
    if (!enabled || isDemoMode()) return;
    return subscribeP2pOrderStream(onEvent);
  }, [enabled, onEvent]);
}
