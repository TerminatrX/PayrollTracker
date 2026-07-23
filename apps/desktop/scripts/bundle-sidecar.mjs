/**
 * Publishes the .NET payroll sidecar and stages it where Tauri's bundler expects it.
 *
 * Tauri's `bundle.externalBin` requires the binary on disk to carry a `-<target-triple>`
 * suffix (e.g. payroll-backend-x86_64-pc-windows-msvc.exe). At bundle time Tauri copies it
 * next to the app executable with the suffix stripped, which is where the Rust shell looks
 * for it. Without this step a packaged (MSI/NSIS) install ships no sidecar, and every backend
 * command fails at runtime with "executable was not found".
 *
 * Runs automatically via `beforeBuildCommand` before `tauri build` (full publish), and via
 * `beforeDevCommand` with `--if-missing` (stage only if absent).
 *
 * `--if-missing`: Adding the sidecar to `bundle.externalBin` makes it a COMPILE-TIME
 * requirement - tauri-build validates it exists on every `cargo build`, including `tauri dev`.
 * But in dev the app runs the sidecar from the .NET Debug output (the dev-fallback path in
 * resolve_sidecar_path), so the staged copy only needs to EXIST, not be current. This flag
 * skips the slow self-contained publish when a staged binary is already present, so dev
 * launches stay fast after the first one.
 */
import { execFileSync, execSync } from "node:child_process";
import { copyFileSync, mkdirSync, existsSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const onlyIfMissing = process.argv.includes("--if-missing");

const here = dirname(fileURLToPath(import.meta.url));
const desktopDir = resolve(here, "..");
const repoRoot = resolve(desktopDir, "..", "..");
const backendProject = join(
  repoRoot,
  "services",
  "PayrollManager.Backend",
  "PayrollManager.Backend.csproj",
);
const binariesDir = join(desktopDir, "src-tauri", "binaries");

/** The Rust host triple, e.g. "x86_64-pc-windows-msvc". */
function hostTargetTriple() {
  // `rustc -vV` prints a "host: <triple>" line on every supported version. Preferred over
  // `--print host-tuple`, which only exists on newer toolchains.
  const output = execSync("rustc -vV", { encoding: "utf8" });
  const match = output.match(/^host:\s*(.+)$/m);

  if (!match) {
    throw new Error("Could not determine the Rust host target triple from `rustc -vV`.");
  }

  return match[1].trim();
}

/** Maps a Rust triple to the .NET runtime identifier for `dotnet publish`. */
function dotnetRid(triple) {
  if (triple.includes("windows")) {
    return triple.includes("aarch64") ? "win-arm64" : "win-x64";
  }
  if (triple.includes("apple-darwin")) {
    return triple.includes("aarch64") ? "osx-arm64" : "osx-x64";
  }
  if (triple.includes("linux")) {
    return triple.includes("aarch64") ? "linux-arm64" : "linux-x64";
  }
  throw new Error(`Unsupported target triple for the sidecar: ${triple}`);
}

const triple = hostTargetTriple();
const rid = dotnetRid(triple);
const isWindows = triple.includes("windows");
const exeSuffix = isWindows ? ".exe" : "";

const stagedBinary = join(binariesDir, `payroll-backend-${triple}${exeSuffix}`);

if (onlyIfMissing && existsSync(stagedBinary)) {
  console.log(`[bundle-sidecar] staged sidecar already present, skipping publish: ${stagedBinary}`);
  process.exit(0);
}

console.log(`[bundle-sidecar] host triple: ${triple} -> .NET RID: ${rid}`);

const publishDir = join(
  repoRoot,
  "services",
  "PayrollManager.Backend",
  "bin",
  "Release",
  "net8.0",
  rid,
  "publish",
);

console.log("[bundle-sidecar] publishing sidecar (self-contained, single file)...");
execFileSync(
  "dotnet",
  [
    "publish",
    backendProject,
    "-c",
    "Release",
    "-r",
    rid,
    "--self-contained",
    "true",
    "-p:PublishSingleFile=true",
  ],
  { stdio: "inherit" },
);

const publishedExe = join(publishDir, `payroll-backend${exeSuffix}`);

if (!existsSync(publishedExe)) {
  throw new Error(`Expected published sidecar not found at ${publishedExe}`);
}

mkdirSync(binariesDir, { recursive: true });
copyFileSync(publishedExe, stagedBinary);

console.log(`[bundle-sidecar] staged ${stagedBinary}`);
