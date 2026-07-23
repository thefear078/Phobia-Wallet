import { createFileRoute } from "@tanstack/react-router";
import { LegalDocument } from "@/components/LegalDocument";
import { rulesSections } from "@/content/legal/rulesSections";

export const Route = createFileRoute("/legal/rules")({
  component: RulesPage,
});

function RulesPage() {
  return (
    <LegalDocument
      title="Platform rules"
      updated="July 2, 2026"
      intro={
        <p>
          Mandatory conduct rules for P2P, wallet linking, and the Telegram bot. Violations may
          result in account restriction or blocking without prior notice.
        </p>
      }
      sections={rulesSections}
    />
  );
}
