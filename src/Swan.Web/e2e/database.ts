import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";

const containerId = process.env.E2E_POSTGRES_CONTAINER_ID;
const runtime = process.env.E2E_CONTAINER_RUNTIME ?? "docker";

export function sql(source: string) {
  if (!containerId || !/^[0-9a-f]{64}$/.test(containerId))
    throw new Error(
      "E2E_POSTGRES_CONTAINER_ID must identify the disposable database.",
    );
  const input = source.endsWith(".sql")
    ? readFileSync(new URL(`./sql/${source}`, import.meta.url), "utf8")
    : source;
  const result = spawnSync(
    runtime,
    [
      "exec",
      "-i",
      containerId,
      "sh",
      "-c",
      'PGPASSWORD="$POSTGRES_PASSWORD" psql -v ON_ERROR_STOP=1 -U postgres -d swan',
    ],
    { input, encoding: "utf8", timeout: 30_000 },
  );
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(`E2E SQL failed: ${result.stderr || result.stdout}`);
}
