import { CommandRateLimiter } from "../src/telegram/telegram-bot-reliability";

function assert(cond: boolean, msg: string) {
  if (!cond) throw new Error(msg);
}

const limiter = new CommandRateLimiter(3, 60_000);
assert(limiter.tryConsume(1), "first");
assert(limiter.tryConsume(1), "second");
assert(limiter.tryConsume(1), "third");
assert(!limiter.tryConsume(1), "fourth should block");
assert(limiter.tryConsume(2), "other user ok");

console.log("telegram-bot-reliability OK");
