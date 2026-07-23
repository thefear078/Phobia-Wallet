import { createFileRoute } from "@tanstack/react-router";
import { LegalDocument } from "@/components/LegalDocument";
import { privacySections } from "@/content/legal/privacySections";

export const Route = createFileRoute("/legal/privacy")({
  component: PrivacyPage,
});

function PrivacyPage() {
  return (
    <LegalDocument
      title="Privacy policy"
      updated="July 2, 2026"
      intro={
        <p>
          We minimize data collection in line with the non-custodial model. This policy explains
          what Umbrella Wallet processes, on what bases, and how you can manage your data.
        </p>
      }
      sections={privacySections}
    />
  );
}
