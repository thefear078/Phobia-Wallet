import { createFileRoute } from "@tanstack/react-router";
import { LegalDocument } from "@/components/LegalDocument";
import { termsSections } from "@/content/legal/termsSections";

export const Route = createFileRoute("/legal/terms")({
  component: TermsPage,
});

function TermsPage() {
  return (
    <LegalDocument
      title="Terms of use"
      updated="July 2, 2026"
      intro={
        <p>
          This document governs access to Umbrella Wallet as a non-custodial aggregator. Read it
          together with the User Agreement and Platform Rules before using P2P and financial
          features.
        </p>
      }
      sections={termsSections}
    />
  );
}
