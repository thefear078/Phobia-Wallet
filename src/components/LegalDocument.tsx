import { Link } from "@tanstack/react-router";
import type { ReactNode } from "react";
import { GraniteCredit } from "@/components/GraniteCredit";

export type LegalSection = {
  id: string;
  title: string;
  body: ReactNode;
};

type Props = {
  title: string;
  updated: string;
  intro?: ReactNode;
  sections: LegalSection[];
  backTo?: string;
};

export function LegalDocument({ title, updated, intro, sections, backTo = "/" }: Props) {
  return (
    <div className="min-h-screen bg-background text-foreground px-5 py-8 pb-16 max-w-2xl mx-auto">
      <Link to={backTo} className="text-sm text-primary hover:underline">
        ← Back
      </Link>
      <h1 className="mt-4 text-2xl font-bold tracking-tight">{title}</h1>
      <p className="mt-1 text-xs text-muted-foreground">Last updated: {updated}</p>

      {intro ? (
        <div className="mt-6 text-sm text-muted-foreground leading-relaxed space-y-3">{intro}</div>
      ) : null}

      <nav className="mt-8 rounded-2xl border border-border bg-card/40 p-4 text-sm">
        <div className="font-semibold mb-2">Contents</div>
        <ol className="list-decimal list-inside space-y-1 text-muted-foreground">
          {sections.map((s) => (
            <li key={s.id}>
              <a href={`#${s.id}`} className="hover:text-foreground">
                {s.title}
              </a>
            </li>
          ))}
        </ol>
      </nav>

      <article className="mt-8 space-y-10 text-sm leading-relaxed">
        {sections.map((s) => (
          <section key={s.id} id={s.id} className="scroll-mt-6">
            <h2 className="text-lg font-semibold text-foreground mb-3">{s.title}</h2>
            <div className="text-muted-foreground space-y-3 [&_ul]:list-disc [&_ul]:pl-5 [&_ul]:space-y-1 [&_ol]:list-decimal [&_ol]:pl-5 [&_ol]:space-y-1 [&_strong]:text-foreground/90">
              {s.body}
            </div>
          </section>
        ))}
      </article>

      <footer className="mt-12 pt-6 border-t border-border text-xs text-muted-foreground space-y-4">
        <GraniteCredit />
        <p>
          Umbrella Wallet documents:{" "}
          <Link to="/legal/terms" className="text-primary">
            Terms
          </Link>
          {" · "}
          <Link to="/legal/privacy" className="text-primary">
            Privacy
          </Link>
          {" · "}
          <Link to="/legal/agreement" className="text-primary">
            User agreement
          </Link>
          {" · "}
          <Link to="/legal/rules" className="text-primary">
            Platform rules
          </Link>
        </p>
        <p>legal@umbra.wallet · support@umbra.wallet</p>
      </footer>
    </div>
  );
}
