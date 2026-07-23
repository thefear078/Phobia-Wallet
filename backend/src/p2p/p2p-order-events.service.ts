import { Injectable } from "@nestjs/common";
import { EventEmitter2 } from "@nestjs/event-emitter";
import { MessageEvent } from "@nestjs/common";
import { Observable, filter, map } from "rxjs";

export const P2P_ORDER_STATUS_EVENT = "p2p.order.status";

export type P2pOrderStatusEvent = {
  orderId: string;
  offerId: string;
  buyerId: string;
  sellerId: string;
  status: string;
  amount: number;
  at: string;
};

@Injectable()
export class P2pOrderEventsService {
  constructor(private readonly emitter: EventEmitter2) {}

  emitStatusChange(payload: P2pOrderStatusEvent) {
    this.emitter.emit(P2P_ORDER_STATUS_EVENT, payload);
    // Fan-out to each participant channel for SSE subscriptions.
    this.emitter.emit(`${P2P_ORDER_STATUS_EVENT}.${payload.buyerId}`, payload);
    this.emitter.emit(`${P2P_ORDER_STATUS_EVENT}.${payload.sellerId}`, payload);
  }

  streamForUser(userId: string): Observable<MessageEvent> {
    return new Observable<P2pOrderStatusEvent>((subscriber) => {
      const channel = `${P2P_ORDER_STATUS_EVENT}.${userId}`;
      const handler = (event: P2pOrderStatusEvent) => subscriber.next(event);
      this.emitter.on(channel, handler);
      return () => {
        this.emitter.off(channel, handler);
      };
    }).pipe(
      filter((event) => event.buyerId === userId || event.sellerId === userId),
      map(
        (event) =>
          ({
            data: event,
          }) satisfies MessageEvent,
      ),
    );
  }
}
