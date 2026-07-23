import { spawnSync } from "node:child_process";

const hasDb = Boolean(process.env.DATABASE_URL?.trim());
let demo = process.env.DEMO_MODE === "true";

// Without Postgres, force in-memory demo on the API so auth can boot on free Render.
if (!demo && !hasDb) {
  console.warn(
    "[Umbra] DATABASE_URL missing — setting DEMO_MODE=true for this boot (in-memory store)",
  );
  process.env.DEMO_MODE = "true";
  demo = true;
}

if (!demo && hasDb) {
  const migrate = spawnSync("npx", ["prisma", "migrate", "deploy"], {
    stdio: "inherit",
    shell: true,
  });
  if (migrate.status !== 0) process.exit(migrate.status ?? 1);
}

const app = spawnSync("node", ["dist/main.js"], { stdio: "inherit" });
process.exit(app.status ?? 1);
