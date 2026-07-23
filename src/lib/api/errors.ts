import { ApiError } from "./client";

const RENDER_BLUEPRINT_URL =
  "https://dashboard.render.com/blueprint/new?repo=https://github.com/kiurakku/umbra-wallet";

export function formatApiError(error: unknown): string {
  if (error instanceof ApiError) {
    if (
      /API on Render is offline|API temporarily unavailable|API server is unavailable/i.test(
        error.message,
      )
    ) {
      return `API на Render офлайн (сервіс не створено). Відкрийте ${RENDER_BLUEPRINT_URL} → Deploy Blueprint → дочекайтесь Live, потім спробуйте знову.`;
    }
    return error.message;
  }
  if (error instanceof TypeError) {
    return "Немає з'єднання з сервером. Перевірте інтернет або дочекайтесь запуску API на Render.";
  }
  if (error instanceof Error) return error.message;
  return "Невідома помилка";
}
