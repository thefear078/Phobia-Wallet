import { createFileRoute } from "@tanstack/react-router";
import { LegalDocument } from "@/components/LegalDocument";
import { agreementSections } from "@/content/legal/agreementSections";

export const Route = createFileRoute("/legal/agreement")({
  component: AgreementPage,
});

function AgreementPage() {
  return (
    <LegalDocument
      title="User agreement"
      updated="July 2, 2026"
      intro={
        <p>
          A legally binding agreement between you and Umbrella Wallet. Registration or continued use
          of the Service constitutes full acceptance of all sections below.
        </p>
      }
      sections={agreementSections}
    />
  );
}
