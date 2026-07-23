import { NestMiddleware, Injectable } from "@nestjs/common";
import type { NextFunction, Request, Response } from "express";

const META_KEYS = new Set([
  "requestId",
  "request_id",
  "traceId",
  "trace_id",
  "serverTime",
  "server_time",
  "meta",
  "debug",
]);

function stripMeta(body: unknown): unknown {
  if (body == null || typeof body !== "object" || Array.isArray(body)) return body;
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(body as Record<string, unknown>)) {
    if (META_KEYS.has(k)) continue;
    out[k] = v;
  }
  return out;
}

/**
 * When the client sends X-Privacy-Mode: 1:
 * - replace client IP with '[privacy]' for logging
 * - strip optional metadata fields from JSON responses
 */
@Injectable()
export class PrivacyModeMiddleware implements NestMiddleware {
  use(req: Request, res: Response, next: NextFunction) {
    if (req.headers["x-privacy-mode"] !== "1") {
      next();
      return;
    }

    Object.defineProperty(req, "ip", {
      configurable: true,
      enumerable: true,
      get: () => "[privacy]",
    });

    const socket = req.socket as { remoteAddress?: string } | undefined;
    if (socket) {
      try {
        Object.defineProperty(socket, "remoteAddress", {
          configurable: true,
          enumerable: true,
          get: () => "[privacy]",
        });
      } catch {
        /* ignore non-configurable */
      }
    }

    const originalJson = res.json.bind(res);
    res.json = ((body: unknown) => originalJson(stripMeta(body))) as typeof res.json;

    next();
  }
}
