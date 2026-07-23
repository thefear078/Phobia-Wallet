import { join } from "node:path";

export type StickerName = "welcome" | "thinking" | "sending" | "secret";

/** Stickers shipped with backend for Telegram bot replies (.tgs) */
export function stickerPath(name: StickerName): string {
  return join(process.cwd(), "assets", "stickers", `${name}.tgs`);
}
