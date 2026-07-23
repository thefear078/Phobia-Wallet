import type { LegalSection } from "@/components/LegalDocument";

export const termsSections: LegalSection[] = [
  {
    id: "definitions",
    title: "1. Definitions",
    body: (
      <>
        <p>
          <strong>&ldquo;Umbrella Wallet&rdquo;</strong>, <strong>&ldquo;Platform&rdquo;</strong>,{" "}
          <strong>&ldquo;Service&rdquo;</strong> — the software suite (web interface, Telegram Mini
          App, API) that provides informational and coordination functions of a non-custodial
          aggregator of crypto assets and fiat accounts.
        </p>
        <p>
          <strong>&ldquo;Operator&rdquo;</strong> — the legal entity or sole proprietor that
          administers the Platform (details are published in the &ldquo;Contacts&rdquo; section).
        </p>
        <p>
          <strong>&ldquo;User&rdquo;</strong> — a natural person aged 18 or older who has registered
          for or uses the Service.
        </p>
        <p>
          <strong>&ldquo;Non-custodial model&rdquo;</strong> — Umbrella does not store private keys,
          seed phrases, or wallet PINs, has no access to the User&apos;s funds, and cannot initiate
          transactions on the User&apos;s behalf without a signature in an external wallet.
        </p>
        <p>
          <strong>&ldquo;P2P deal&rdquo;</strong> — an arrangement between two Users to exchange
          assets outside Umbrella escrow; the Platform only displays status and public proofs
          (transaction hash, bank reference).
        </p>
      </>
    ),
  },
  {
    id: "acceptance",
    title: "2. Acceptance of terms",
    body: (
      <>
        <p>
          Registration, sign-in, clicking &ldquo;Continue&rdquo;, or using the dashboard, P2P, or
          Telegram bot constitutes full acceptance of these Terms, the{" "}
          <a href="/legal/privacy" className="text-primary underline">
            Privacy Policy
          </a>
          , the{" "}
          <a href="/legal/agreement" className="text-primary underline">
            User Agreement
          </a>
          , and the{" "}
          <a href="/legal/rules" className="text-primary underline">
            Platform Rules
          </a>
          .
        </p>
        <p>
          If you do not agree — do not use the Service. The Operator may update these documents;
          material changes will be announced in the interface or by e-mail/Telegram. Continued use
          after an update constitutes acceptance.
        </p>
      </>
    ),
  },
  {
    id: "non-custody",
    title: "3. Non-custodial nature of the services",
    body: (
      <>
        <ul>
          <li>
            Umbrella is not a bank, payment institution, or virtual asset service provider (VASP)
            within the meaning of a national regulator, unless expressly stated in the
            Operator&apos;s license.
          </li>
          <li>
            All crypto transactions are signed in your wallet (WalletConnect, browser extension,
            hardware wallet, etc.).
          </li>
          <li>
            Umbrella does not guarantee recovery of account access if a password is lost, unless
            backup mechanisms are configured (2FA, e-mail recovery — where available).
          </li>
          <li>
            Balances are shown based on public blockchain/API data; delays or inaccuracies are
            possible.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "p2p",
    title: "4. P2P marketplace",
    body: (
      <>
        <p>
          Umbrella provides only <strong>matchmaking</strong> (finding a counterparty) and deal
          status tracking.
        </p>
        <ul>
          <li>
            The Operator <strong>does not hold</strong> crypto or fiat in escrow.
          </li>
          <li>
            Settlement is directly between buyer and seller (transfer to IBAN, Monobank, Wise, etc.,
            plus an on-chain tx).
          </li>
          <li>
            The Operator is not liable for non-performance, delay, counterparty fraud, or incorrect
            payment details.
          </li>
          <li>
            A dispute is opened in the interface; review follows the platform rules with no
            guarantee of fund recovery.
          </li>
          <li>
            KYC may be required to publish offers — refusal of KYC equals refusal of access to the
            feature.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "fees",
    title: "5. Fees and taxes",
    body: (
      <>
        <p>
          Blockchain network, bank, and third-party service fees are paid by the User. Any Umbrella
          fee (if introduced) is disclosed before payment. The User is solely responsible for
          declaring taxes under the laws of their jurisdiction.
        </p>
      </>
    ),
  },
  {
    id: "prohibited",
    title: "6. Prohibited activity",
    body: (
      <>
        <ul>
          <li>Money laundering, terrorist financing, or circumvention of OFAC/EU/NBU sanctions.</li>
          <li>Fraud, phishing, or impersonating the Operator or another User.</li>
          <li>Publishing a seed/mnemonic/private key in chat, tickets, or P2P messages.</li>
          <li>Automated scraping of the API without written consent.</li>
          <li>Exploiting vulnerabilities, DDoS, or reverse engineering intended to cause harm.</li>
        </ul>
        <p>
          Violations may result in account blocking without notice and disclosure of data to
          competent authorities as required by law.
        </p>
      </>
    ),
  },
  {
    id: "liability",
    title: "7. Limitation of liability",
    body: (
      <>
        <p>
          The Service is provided &ldquo;as is&rdquo;. To the fullest extent permitted by law, the
          Operator is not liable for: indirect damages, lost profits, data loss, network outages,
          force majeure, or acts of third parties (wallets, banks, blockchain RPC or rate providers,
          WalletConnect, Telegram).
        </p>
        <p>
          The Operator&apos;s aggregate liability is limited to the fees actually paid by the User
          to Umbrella in the preceding 12 months, or USD 100 — whichever is less.
        </p>
      </>
    ),
  },
  {
    id: "ip",
    title: "8. Intellectual property",
    body: (
      <>
        <p>
          The Umbrella brand, UI, code, and documentation belong to the Operator or its licensors.
          Copying, modification, or creation of derivative works without consent is prohibited.
        </p>
      </>
    ),
  },
  {
    id: "termination",
    title: "9. Termination of access",
    body: (
      <>
        <p>
          The User may delete their account in settings. The Operator may suspend or terminate
          access upon breach of terms, suspected fraud, or regulator requirements. Data is processed
          under the Privacy Policy (retention as required by law).
        </p>
      </>
    ),
  },
  {
    id: "law",
    title: "10. Governing law and disputes",
    body: (
      <>
        <p>
          These Terms are governed by the laws of Ukraine unless otherwise agreed in writing.
          Disputes are resolved by negotiation; failing agreement — in the courts of Ukraine at the
          Operator&apos;s place of business.
        </p>
        <p>
          EU consumers retain rights under applicable EU consumer law directives where they apply.
        </p>
      </>
    ),
  },
  {
    id: "contact",
    title: "11. Contacts",
    body: (
      <>
        <p>legal@umbra.wallet · support@umbra.wallet</p>
      </>
    ),
  },
];
