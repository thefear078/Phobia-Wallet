import type { LegalSection } from "@/components/LegalDocument";

export const privacySections: LegalSection[] = [
  {
    id: "controller",
    title: "1. Who processes data",
    body: (
      <>
        <p>
          The personal data controller is the Operator of Umbrella Wallet (contact:
          privacy@umbra.wallet). DPO (if appointed): dpo@umbra.wallet.
        </p>
      </>
    ),
  },
  {
    id: "scope",
    title: "2. Scope",
    body: (
      <>
        <p>
          This policy applies to the web app, API, Telegram Mini App @UmbraWBot, and related
          services.
        </p>
      </>
    ),
  },
  {
    id: "collected",
    title: "3. Data we collect",
    body: (
      <>
        <ul>
          <li>
            <strong>Account:</strong> nickname (username), internal e-mail identifier, password hash
            (Argon2id), language, notification settings.
          </li>
          <li>
            <strong>Telegram:</strong> telegram_id, @username (with Mini App consent), initData for
            authentication.
          </li>
          <li>
            <strong>Third-party accounts:</strong> not collected. Umbrella does not use Google or
            Apple sign-in; the only optional link is Telegram.
          </li>
          <li>
            <strong>Wallets:</strong> public addresses, network (chain), label; balances from public
            indexers.
          </li>
          <li>
            <strong>Banks:</strong> provider account reference, masked number, bank name — without
            PAN/CVV and without storing the Monobank token.
          </li>
          <li>
            <strong>P2P:</strong> deal statuses, amounts, public crypto tx hashes, fiat payment
            references.
          </li>
          <li>
            <strong>KYC:</strong> verification status and level (document data is held by the KYC
            provider, not Umbrella, unless otherwise stated).
          </li>
          <li>
            <strong>Technical:</strong> IP, User-Agent, session cookies, JWT metadata, error logs
            (secrets scrubbed).
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "not-collected",
    title: "4. What we do NOT collect or store",
    body: (
      <>
        <ul>
          <li>Seed phrases, private keys, mnemonics, or plaintext passwords.</li>
          <li>Monobank personal token after validation (only briefly in request memory).</li>
          <li>Full card numbers, CVV, or bank card PINs.</li>
          <li>User funds (non-custodial model).</li>
        </ul>
      </>
    ),
  },
  {
    id: "purposes",
    title: "5. Processing purposes",
    body: (
      <>
        <ul>
          <li>Providing access to the Service and authentication.</li>
          <li>Displaying aggregated balances and market rates.</li>
          <li>Coordinating P2P and notifying about deal status.</li>
          <li>Security (rate limits, fraud detection, removal of unsafe messages in Telegram).</li>
          <li>Compliance with KYC/AML requirements where applicable.</li>
          <li>Product improvement (anonymized analytics).</li>
        </ul>
      </>
    ),
  },
  {
    id: "legal-basis",
    title: "6. Legal bases (GDPR)",
    body: (
      <>
        <ul>
          <li>Performance of a contract (User Agreement) — Art. 6(1)(b) GDPR.</li>
          <li>Consent — marketing, Telegram notifications (can be disabled).</li>
          <li>Legitimate interest — security and abuse prevention.</li>
          <li>Legal obligation — responses to regulators, account record retention.</li>
        </ul>
      </>
    ),
  },
  {
    id: "sharing",
    title: "7. Sharing with third parties",
    body: (
      <>
        <ul>
          <li>
            <strong>Infrastructure:</strong> hosting, PostgreSQL, Redis (EU/US — under DPA).
          </li>
          <li>
            <strong>WalletConnect/Reown:</strong> optional wallet connection, only if you use it
            (Reown policy).
          </li>
          <li>
            <strong>Chain RPC / rate providers:</strong> configurable and optional. By default
            Umbrella reads balances from public blockchain RPC endpoints and prices from a pluggable
            rates provider — only public addresses are sent, never keys. Operators can point
            Umbrella at their own node or rate endpoint, or run it fully on cached rates.
          </li>
          <li>
            <strong>KYC provider:</strong> optional and off by default; used only if you start
            verification yourself.
          </li>
          <li>
            <strong>Telegram:</strong> bot API for auth and notifications.
          </li>
          <li>
            <strong>Public authorities:</strong> upon lawful request.
          </li>
        </ul>
        <p>We do not sell personal data to advertisers.</p>
      </>
    ),
  },
  {
    id: "retention",
    title: "8. Retention",
    body: (
      <>
        <ul>
          <li>Account — until deletion + 30 days of backups.</li>
          <li>Refresh tokens — until revocation or 30 days.</li>
          <li>P2P logs — 5 years (AML/disputes), unless law requires otherwise.</li>
          <li>Server logs — 90 days.</li>
        </ul>
      </>
    ),
  },
  {
    id: "rights",
    title: "9. Your rights",
    body: (
      <>
        <p>
          Access, rectification, erasure, restriction, portability, objection, withdrawal of consent
          — privacy@umbra.wallet. Complaints may be filed with the Ukrainian Parliament Commissioner
          for Human Rights (personal data) or an EU supervisory authority.
        </p>
      </>
    ),
  },
  {
    id: "security",
    title: "10. Security",
    body: (
      <>
        <ul>
          <li>TLS in transit, Argon2id for passwords, httpOnly refresh cookies.</li>
          <li>Helmet CSP, login throttling, secret scrubbing in logs.</li>
          <li>Principle of least privilege for admin access.</li>
        </ul>
        <p>No system is 100% secure — report incidents to: security@umbra.wallet.</p>
      </>
    ),
  },
  {
    id: "cookies",
    title: "11. Cookies and localStorage",
    body: (
      <>
        <ul>
          <li>
            <strong>umbra_refresh</strong> — httpOnly cookie, auth session.
          </li>
          <li>
            <strong>sessionStorage umbra.access</strong> — JWT access token.
          </li>
          <li>
            <strong>localStorage</strong> — UI settings, skip-link flag.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "children",
    title: "12. Children",
    body: (
      <p>The Service is not for persons under 18. If discovered — the account will be deleted.</p>
    ),
  },
  {
    id: "changes",
    title: "13. Policy changes",
    body: (
      <p>
        The current version is always at /legal/privacy. Material changes will be announced in the
        app.
      </p>
    ),
  },
];
