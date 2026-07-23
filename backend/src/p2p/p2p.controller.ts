import {
  Body,
  Controller,
  Delete,
  Get,
  Param,
  Patch,
  Post,
  Query,
  Req,
  Sse,
  UseGuards,
} from "@nestjs/common";
import { Throttle } from "@nestjs/throttler";
import type { Request } from "express";
import { Observable } from "rxjs";
import { MessageEvent } from "@nestjs/common";
import { P2pService } from "./p2p.service";
import { P2pOrderEventsService } from "./p2p-order-events.service";
import { JwtAuthGuard } from "../auth/jwt-auth.guard";
import {
  CreateOfferDto,
  CreateOrderDto,
  UpdateOfferDto,
  CryptoProofDto,
  FiatProofDto,
  DisputeOrderDto,
} from "./dto/p2p.dto";

@Controller("p2p")
export class P2pController {
  constructor(
    private p2p: P2pService,
    private orderEvents: P2pOrderEventsService,
  ) {}

  @Get("offers")
  listOffers(
    @Query("asset") asset?: string,
    @Query("method") method?: string,
    @Query("fiat") fiat?: string,
    @Query("side") side?: string,
    @Query("direction") direction?: string,
    @Query("min_amount") min_amount?: string,
    @Query("max_amount") max_amount?: string,
  ) {
    return this.p2p.listOffers({
      asset,
      method,
      fiat,
      side: side ?? direction,
      min_amount: min_amount ? Number(min_amount) : undefined,
      max_amount: max_amount ? Number(max_amount) : undefined,
    });
  }

  @Get("offers/mine")
  @UseGuards(JwtAuthGuard)
  listMyOffers(@Req() req: Request) {
    const user = req.user as { id: string };
    return this.p2p.listMyOffers(user.id);
  }

  @Post("offers")
  @UseGuards(JwtAuthGuard)
  createOffer(@Req() req: Request, @Body() dto: CreateOfferDto) {
    const user = req.user as { id: string };
    return this.p2p.createOffer(user.id, dto);
  }

  @Patch("offers/:id")
  @UseGuards(JwtAuthGuard)
  updateOffer(@Req() req: Request, @Param("id") id: string, @Body() dto: UpdateOfferDto) {
    const user = req.user as { id: string };
    return this.p2p.updateOffer(user.id, id, dto);
  }

  @Delete("offers/:id")
  @UseGuards(JwtAuthGuard)
  deactivateOffer(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.p2p.deactivateOffer(user.id, id);
  }

  @Post("orders")
  @UseGuards(JwtAuthGuard)
  @Throttle({ default: { limit: 10, ttl: 3_600_000 } })
  createOrder(@Req() req: Request, @Body() dto: CreateOrderDto) {
    const user = req.user as { id: string };
    return this.p2p.createOrder(user.id, dto);
  }

  @Get("orders")
  @UseGuards(JwtAuthGuard)
  listOrders(@Req() req: Request, @Query("role") role?: string) {
    const user = req.user as { id: string };
    const normalized = role === "buyer" || role === "seller" ? role : undefined;
    return this.p2p.listOrders(user.id, normalized);
  }

  @Get("orders/stream")
  @Sse()
  @UseGuards(JwtAuthGuard)
  orderStream(@Req() req: Request): Observable<MessageEvent> {
    const user = req.user as { id: string };
    return this.orderEvents.streamForUser(user.id);
  }

  @Get("orders/:id")
  @UseGuards(JwtAuthGuard)
  getOrder(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.p2p.getOrder(user.id, id);
  }

  @Patch("orders/:id/await-fiat")
  @UseGuards(JwtAuthGuard)
  awaitFiat(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.p2p.startFiatPayment(user.id, id);
  }

  @Patch("orders/:id/fiat-proof")
  @UseGuards(JwtAuthGuard)
  fiatProof(@Req() req: Request, @Param("id") id: string, @Body() dto: FiatProofDto) {
    const user = req.user as { id: string };
    return this.p2p.submitFiatProof(user.id, id, dto);
  }

  @Patch("orders/:id/crypto-proof")
  @UseGuards(JwtAuthGuard)
  cryptoProof(@Req() req: Request, @Param("id") id: string, @Body() dto: CryptoProofDto) {
    const user = req.user as { id: string };
    return this.p2p.submitCryptoProof(user.id, id, dto);
  }

  @Patch("orders/:id/complete")
  @UseGuards(JwtAuthGuard)
  complete(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.p2p.completeOrder(user.id, id);
  }

  @Post("orders/:id/dispute")
  @UseGuards(JwtAuthGuard)
  dispute(@Req() req: Request, @Param("id") id: string, @Body() dto: DisputeOrderDto) {
    const user = req.user as { id: string };
    return this.p2p.disputeOrder(user.id, id, dto.reason);
  }

  @Patch("orders/:id/cancel")
  @UseGuards(JwtAuthGuard)
  cancel(@Req() req: Request, @Param("id") id: string) {
    const user = req.user as { id: string };
    return this.p2p.cancelOrder(user.id, id);
  }
}
