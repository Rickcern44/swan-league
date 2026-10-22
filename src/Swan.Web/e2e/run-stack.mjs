import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const webDir = fileURLToPath(new URL("../", import.meta.url));
const repoDir = fileURLToPath(new URL("../../../", import.meta.url));
const apphost = "src/Swan.AppHost/Swan.AppHost.csproj";

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: repoDir,
    encoding: "utf8",
    timeout: 180_000,
    ...options,
  });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(
      `${command} ${args.join(" ")} failed:\n${result.stderr || result.stdout}`,
    );
  return `${result.stdout}${result.stderr}`;
}

// CI uses the pinned local tool, while developers keep using their existing Aspire command.
function aspire(args, options = {}) {
  return process.env.ASPIRE_USE_LOCAL_TOOL === "true"
    ? run("dotnet", ["tool", "run", "aspire", "--", ...args], options)
    : run("aspire", args, options);
}

function jsonFrom(output) {
  const start = output.indexOf("{");
  const end = output.lastIndexOf("}");
  if (start < 0 || end < start) throw new Error("Aspire did not return JSON.");
  return JSON.parse(output.slice(start, end + 1));
}

function resource(resources, name) {
  const found = resources.find((item) => item.displayName === name);
  if (!found) throw new Error(`Aspire resource ${name} is missing.`);
  return found;
}

function httpUrl(item) {
  const url = item.urls?.find((entry) => entry.name === "http")?.url;
  if (!url)
    throw new Error(`Aspire resource ${item.displayName} has no HTTP URL.`);
  return url;
}

// Never attach the browser suite to a developer's running stack.
const existing = aspire(["describe", "--non-interactive", "--format", "Json"]);
if (!existing.includes("No running AppHost found"))
  throw new Error(
    "Stop the existing Aspire AppHost before running the disposable E2E stack.",
  );

let started = false;
try {
  const launchOutput = aspire(
    [
      "start",
      "--apphost",
      apphost,
      "--isolated",
      "--non-interactive",
      "--format",
      "Json",
    ],
    { env: { ...process.env, UseVolumes: "false" } },
  );
  started = true;
  const launch = jsonFrom(launchOutput);
  if (!launch.appHostPath?.endsWith(apphost))
    throw new Error("Aspire started an unexpected AppHost.");
  aspire(["wait", "web", "--non-interactive"]);
  aspire(["wait", "mailpit", "--non-interactive"]);

  const { resources } = jsonFrom(
    aspire([
      "describe",
      "--apphost",
      apphost,
      "--non-interactive",
      "--format",
      "Json",
    ]),
  );
  const postgres = resource(resources, "postgres");
  if (postgres.volumes?.length !== 0)
    throw new Error(
      "E2E Postgres has a persistent volume; refusing to run browser tests.",
    );
  const containerId = postgres.properties?.["container.id"];
  if (!/^[0-9a-f]{64}$/.test(containerId ?? ""))
    throw new Error("Could not identify the disposable Postgres container.");

  const result = spawnSync("npm", ["run", "test:e2e"], {
    cwd: webDir,
    stdio: "inherit",
    timeout: 600_000,
    env: {
      ...process.env,
      E2E_BASE_URL: httpUrl(resource(resources, "web")),
      E2E_MAILPIT_URL: httpUrl(resource(resources, "mailpit")),
      E2E_POSTGRES_CONTAINER_ID: containerId,
      E2E_CONTAINER_RUNTIME: process.env.ASPIRE_CONTAINER_RUNTIME ?? "docker",
      PLAYWRIGHT_HTML_OPEN: "never",
    },
  });
  if (result.error) throw result.error;
  if (result.status !== 0) process.exitCode = result.status ?? 1;
} finally {
  if (started) {
    try {
      aspire(["stop", "--apphost", apphost, "--non-interactive"]);
    } catch (error) {
      process.exitCode = 1;
      console.error(`Could not stop the E2E AppHost: ${error.message}`);
    }
  }
}
