import type { LegalSection } from "@/components/LegalDocument";

export const rulesSections: LegalSection[] = [
  {
    id: "general",
    title: "1. General conduct",
    body: (
      <>
        <p>
          Users must act in good faith and must not harm other participants or the platform&apos;s
          reputation.
        </p>
      </>
    ),
  },
  {
    id: "p2p-rules",
    title: "2. P2P trading rules",
    body: (
      <>
        <ul>
          <li>Publish offers only with KYC &ldquo;approved&rdquo; when the system requires it.</li>
          <li>Price, limits, and payment methods must match the actual deal terms.</li>
          <li>Do not cancel a deal after receiving payment; do not delay crypto without cause.</li>
          <li>Provide accurate tx hashes and bank references within a reasonable time.</li>
          <li>
            Open disputes with evidence (screenshots, statements); blackmail and threats are
            forbidden.
          </li>
          <li>Wash trading, fake volume, and rating manipulation are forbidden.</li>
        </ul>
      </>
    ),
  },
  {
    id: "wallet-rules",
    title: "3. Linking wallets",
    body: (
      <>
        <ul>
          <li>
            You may link any compatible EVM/non-EVM wallet or read-only address if you control the
            keys or have a right to view.
          </li>
          <li>
            Linking addresses of sanctioned persons/addresses on the OFAC SDN list is forbidden
            (responsibility rests with the User).
          </li>
          <li>Do not use Umbrella with mixers/tumblers to conceal the origin of funds.</li>
        </ul>
      </>
    ),
  },
  {
    id: "telegram-rules",
    title: "4. Telegram bot",
    body: (
      <>
        <ul>
          <li>Bot commands are for personal use of your account only.</li>
          <li>
            Automatic deletion of messages containing a seed/private key does not replace your own
            caution.
          </li>
          <li>Spam or advertising third-party schemes in bot chat — ban.</li>
        </ul>
      </>
    ),
  },
  {
    id: "content",
    title: "5. Content and communication",
    body: (
      <>
        <p>
          Forbidden: hate speech, pornography, incitement to violence, or false information about
          Umbrella or other Users.
        </p>
      </>
    ),
  },
  {
    id: "moderation",
    title: "6. Moderation and sanctions",
    body: (
      <>
        <ul>
          <li>Warning → temporary P2P restriction → full account block.</li>
          <li>Moderation decisions may be appealed: appeals@umbra.wallet within 14 days.</li>
          <li>
            Umbrella is not obliged to provide detailed reasoning when fraud/AML is suspected.
          </li>
        </ul>
      </>
    ),
  },
  {
    id: "reporting",
    title: "7. Reporting violations",
    body: (
      <>
        <p>
          abuse@umbra.wallet — include the offender&apos;s nickname, deal ID, and evidence. Review
          within 10 business days.
        </p>
      </>
    ),
  },
];
