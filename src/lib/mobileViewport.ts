/** Viewport meta: fixed scale, no pinch-zoom (native-app feel on mobile). */
export const MOBILE_VIEWPORT_META =
  "width=device-width, initial-scale=1, minimum-scale=1, maximum-scale=1, user-scalable=no, viewport-fit=cover";

/** Blocks iOS pinch gestures when meta viewport alone is insufficient (WKWebView). */
export function lockMobileViewportZoom() {
  if (typeof document === "undefined") return () => {};

  const blockGesture = (event: Event) => {
    event.preventDefault();
  };

  document.addEventListener("gesturestart", blockGesture, { passive: false });
  document.addEventListener("gesturechange", blockGesture, { passive: false });
  document.addEventListener("gestureend", blockGesture, { passive: false });

  return () => {
    document.removeEventListener("gesturestart", blockGesture);
    document.removeEventListener("gesturechange", blockGesture);
    document.removeEventListener("gestureend", blockGesture);
  };
}
