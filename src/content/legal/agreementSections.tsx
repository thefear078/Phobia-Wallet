import type { LegalSection } from "@/components/LegalDocument";

export const agreementSections: LegalSection[] = [
  {
    id: "parties",
    title: "1. Parties",
    body: (
      <>
        <p>
          This User Agreement (the &ldquo;Agreement&rdquo;) is entered into between the Operator of
          Umbrella Wallet (&ldquo;Umbrella&rdquo;) and the natural person User who accepts the
          Agreement by registering for or using the Service.
        </p>
      </>
    ),
  },
  {
    id: "subject",
    title: "2. Subject matter",
    body: (
      <>
        <p>
          Umbrella grants the User a free (or paid under published tariffs) license to access an
          information-technology platform for:
        </p>
        <ul>
          <li>linking external non-custodial wallets and bank accounts (read-only / reference);</li>
          <li>viewing aggregated balances and market rates;</li>
          <li>participating in the P2P marketplace as matchmaking without escrow;</li>
          <li>receiving notifications (Telegram, push — per settings).</li>
        </ul>
        <p>
          Umbrella does <strong>not</strong> provide asset custody, brokerage, investment advice, or
          profit guarantees.
        </p>
      </>
    ),
  },
  {
    id: "registration",
    title: "3. Registration and account",
    body: (
      <>
        <ul>
          <li>The User must provide an accurate nickname and keep the password confidential.</li>
          <li>
            One User — one account; multi-accounting to circumvent blocks or KYC is prohibited.
          </li>
          <li>The User is responsible for all activity under their account.</li>
          <li>
            Umbrella may require additional verification (e-mail, 2FA, KYC) for certain features.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "wallets",
    title: "4. Linking wallets and banks",
    body: (
      <>
        <p>
          The User confirms they have lawful rights to the addresses/accounts they link. Umbrella
          does not verify on-chain address ownership — it only displays public data.
        </p>
        <p>
          Linking via WalletConnect, a browser wallet, or a read-only address does not grant
          Umbrella signing rights. The Monobank token is used once to obtain account references.
        </p>
      </>
    ),
  },
  {
    id: "p2p-contract",
    title: "5. P2P: legal status of deals",
    body: (
      <>
        <p>
          Offers on the P2P marketplace are an invitation to negotiate, not a public offer by
          Umbrella. A sale-purchase contract arises <strong>directly between Users</strong>.
          Umbrella is not a party, guarantor, agent, or escrow holder.
        </p>
        <ul>
          <li>Each party independently assesses counterparty risk.</li>
          <li>
            Proofs (tx hash, bank reference) are stored for transparency and possible dispute
            review.
          </li>
          <li>
            Umbrella is not obliged to compensate losses from mistaken transfers, scams, or
            chargebacks.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "fees-payment",
    title: "6. Fees and payment",
    body: (
      <>
        <p>
          Core functionality may be free. Paid features (premium, higher limits) require explicit
          price confirmation. Blockchain/bank fees are borne by the User.
        </p>
      </>
    ),
  },
  {
    id: "warranties",
    title: "7. Disclaimer of warranties",
    body: (
      <>
        <p>
          The Service is provided without warranties of uptime, rate accuracy, or full compatibility
          with all wallets or networks. Umbrella does not guarantee that a P2P counterparty will
          perform their obligations.
        </p>
      </>
    ),
  },
  {
    id: "indemnity",
    title: "8. Indemnification",
    body: (
      <>
        <p>
          The User holds Umbrella harmless from third-party claims arising from the User&apos;s
          breach of this Agreement, law, or third-party rights (including unlawful P2P deals or
          sanctions violations).
        </p>
      </>
    ),
  },
  {
    id: "confidentiality",
    title: "9. Confidentiality",
    body: (
      <>
        <p>
          Data processing follows the Privacy Policy. The User must not disclose internal API keys
          or use another person&apos;s credentials.
        </p>
      </>
    ),
  },
  {
    id: "duration",
    title: "10. Term and termination",
    body: (
      <>
        <p>
          The Agreement runs from acceptance until account deletion. Umbrella may terminate access
          with notice (where possible) or immediately upon material breach. Limitation of liability,
          indemnification, and governing-law provisions survive termination.
        </p>
      </>
    ),
  },
  {
    id: "misc",
    title: "11. Miscellaneous",
    body: (
      <>
        <ul>
          <li>Invalidity of any clause does not void the rest of the Agreement.</li>
          <li>Failure to act does not waive a right for the future.</li>
          <li>The English version prevails over translations.</li>
        </ul>
        <p>Contact: legal@umbra.wallet</p>
      </>
    ),
  },
];
