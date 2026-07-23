import { Global, Module } from "@nestjs/common";
import { DemoModeService } from "./demo-mode.service";
import { DemoStoreService } from "./demo-store.service";

@Global()
@Module({
  providers: [DemoModeService, DemoStoreService],
  exports: [DemoModeService, DemoStoreService],
})
export class DemoModule {}
