import "./lib/error-capture";

import { consumeLastCapturedError } from "./lib/error-capture";
import {
  buildSecurityHeaders,
  generateCspNonce,
  injectScriptNonces,
  type SecurityHeaderOptions,
} from "./lib/csp";
import { renderErrorPage } from "./lib/error-page";
import { scrubSensitive } from "./lib/scrubSecrets";
import { isApiPath, resolveApiOrigin } from "./lib/api/config";

type ServerEntry = {
  fetch: (request: Request, env: unknown, ctx: unknown) => Promise<Response> | Response;
};

let serverEntryPromise: Promise<ServerEntry> | undefined;

const RENDER_BLUEPRINT_URL =
  "https://dashboard.render.com/blueprint/new?repo=https://github.com/kiurakku/umbra-wallet";

const API_UNAVAILABLE_MESSAGE = `API on Render is offline (no live umbra-api service). Deploy the blueprint once: ${RENDER_BLUEPRINT_URL}`;

function apiUnavailableResponse(status = 503): Response {
  return new Response(JSON.stringify({ statusCode: status, message: API_UNAVAILABLE_MESSAGE }), {
    status,
    headers: { "content-type": "application/json; charset=utf-8" },
  });
}

function isDeadUpstream(res: Response, body: string): boolean {
  if (res.headers.get("x-render-routing") === "no-server") return true;
  if (res.status === 404 && /^\s*not\s*found\s*$/i.test(body.trim())) return true;
  if (res.status === 502 || res.status === 503 || res.status === 504) return true;
  return false;
}

async function proxyApiRequest(request: Request): Promise<Response | null> {
  const origin = resolveApiOrigin();
  if (!origin) return null;

  const url = new URL(request.url);
  if (!isApiPath(url.pathname)) return null;

  const target = `${origin}${url.pathname}${url.search}`;
  const headers = new Headers(request.headers);
  headers.delete("host");

  const init: RequestInit & { duplex?: "half" } = {
    method: request.method,
    headers,
    redirect: "manual",
  };

  if (request.method !== "GET" && request.method !== "HEAD") {
    init.body = request.body;
    init.duplex = "half";
  }

  let upstream: Response;
  try {
    upstream = await fetch(target, init);
  } catch {
    return apiUnavailableResponse(503);
  }

  // Render returns plain "Not Found" when the service does not exist — never forward that raw.
  if (upstream.status >= 400) {
    const body = await upstream.text();
    if (isDeadUpstream(upstream, body)) {
      return apiUnavailableResponse(503);
    }
    return new Response(body, {
      status: upstream.status,
      statusText: upstream.statusText,
      headers: upstream.headers,
    });
  }

  return upstream;
}

async function getServerEntry(): Promise<ServerEntry> {
  if (!serverEntryPromise) {
    serverEntryPromise = import("@tanstack/react-start/server-entry").then(
      (m) => (m.default ?? m) as ServerEntry,
    );
  }
  return serverEntryPromise;
}

/** Onion requests are detected per-request; ONION_LOCATION advertises the mirror on clearnet. */
function securityOptionsFor(request: Request): SecurityHeaderOptions {
  const host = (request.headers.get("host") ?? "").split(":")[0];
  const onion = host.endsWith(".onion");
  const onionLocation = process.env.ONION_LOCATION?.trim() || undefined;
  return { onion, onionLocation };
}

async function applySecurityHeaders(
  response: Response,
  nonce: string,
  opts: SecurityHeaderOptions,
): Promise<Response> {
  const headers = new Headers(response.headers);
  for (const [key, value] of Object.entries(buildSecurityHeaders(nonce, opts))) {
    headers.set(key, value);
  }

  const contentType = headers.get("content-type") ?? "";
  if (!contentType.includes("text/html")) {
    return new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers,
    });
  }

  const html = await response.text();
  const body = injectScriptNonces(html, nonce);
  return new Response(body, { status: response.status, statusText: response.statusText, headers });
}

// h3 swallows in-handler throws into a normal 500 Response with body
// {"unhandled":true,"message":"HTTPError"} — try/catch alone never fires for those.
async function normalizeCatastrophicSsrResponse(
  response: Response,
  nonce: string,
  opts: SecurityHeaderOptions,
): Promise<Response> {
  if (response.status < 500) return applySecurityHeaders(response, nonce, opts);
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) return applySecurityHeaders(response, nonce, opts);

  const body = await response.clone().text();
  if (!isH3SwallowedErrorBody(body)) return applySecurityHeaders(response, nonce, opts);

  console.error(
    scrubSensitive(consumeLastCapturedError() ?? new Error(`h3 swallowed SSR error: ${body}`)),
  );
  return applySecurityHeaders(
    new Response(renderErrorPage(), {
      status: 500,
      headers: { "content-type": "text/html; charset=utf-8" },
    }),
    nonce,
    opts,
  );
}

function isH3SwallowedErrorBody(body: string): boolean {
  try {
    const payload = JSON.parse(body) as { unhandled?: unknown; message?: unknown };
    return payload.unhandled === true && payload.message === "HTTPError";
  } catch {
    return false;
  }
}

export default {
  async fetch(request: Request, env: unknown, ctx: unknown) {
    const nonce = generateCspNonce();
    const secOpts = securityOptionsFor(request);
    try {
      const proxied = await proxyApiRequest(request);
      if (proxied) return proxied;

      const handler = await getServerEntry();
      const response = await handler.fetch(request, env, ctx);
      return await normalizeCatastrophicSsrResponse(response, nonce, secOpts);
    } catch (error) {
      console.error(scrubSensitive(error));
      return applySecurityHeaders(
        new Response(renderErrorPage(), {
          status: 500,
          headers: { "content-type": "text/html; charset=utf-8" },
        }),
        nonce,
        secOpts,
      );
    }
  },
};
