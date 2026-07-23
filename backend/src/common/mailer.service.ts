import { Injectable, Logger } from "@nestjs/common";
import { ConfigService } from "@nestjs/config";
import { createTransport, type Transporter } from "nodemailer";

/**
 * SMTP mailer — enabled when SMTP_URL is set (smtp://user:pass@host:port).
 * Without SMTP_URL the message is logged instead (dev), never silently dropped.
 */
@Injectable()
export class MailerService {
  private readonly logger = new Logger(MailerService.name);
  private transporter: Transporter | null = null;

  constructor(private config: ConfigService) {
    const url = this.config.get<string>("SMTP_URL");
    if (url) {
      this.transporter = createTransport(url);
    }
  }

  get enabled(): boolean {
    return this.transporter !== null;
  }

  async send(to: string, subject: string, text: string, html?: string): Promise<boolean> {
    const from = this.config.get<string>("SMTP_FROM") ?? "Umbrella Wallet <no-reply@umbra.wallet>";
    if (!this.transporter) {
      // Dev fallback: log without leaking the full token-bearing URL at info level
      this.logger.warn(`SMTP not configured — email to ${to} (“${subject}”) logged only`);
      this.logger.debug(`Email body for ${to}: ${text}`);
      return false;
    }
    try {
      await this.transporter.sendMail({ from, to, subject, text, html });
      return true;
    } catch (error) {
      this.logger.error(`Failed to send email to ${to}: ${String(error)}`);
      return false;
    }
  }
}
