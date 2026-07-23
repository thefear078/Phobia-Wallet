import { useEffect } from "react";
import { useProfile } from "@/lib/profileStore";

/** Syncs `<html lang>` and `dir` (RTL for Arabic) with the profile language. */
export function LanguageDirection() {
  const { lang } = useProfile();

  useEffect(() => {
    const el = document.documentElement;
    el.lang = lang;
    el.dir = lang === "ar" ? "rtl" : "ltr";
  }, [lang]);

  return null;
}
