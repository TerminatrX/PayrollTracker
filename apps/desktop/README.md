# PayrollManager Desktop (Tauri 2 + React)

The replacement frontend. It renders in WebView2 and talks to the existing .NET domain
through a sidecar process, so payroll calculations stay in the tested C# engine.

## Architecture

```
React + TypeScript (this app)
        │  invoke("backend_command", { method, paramsJson })
        ▼
Rust shell (src-tauri)
        │  newline-delimited JSON-RPC over stdin/stdout
        ▼
payroll-backend.exe  (services/PayrollManager.Backend)
        │
        └── PayrollManager.Domain → EF Core → SQLite
```

**The frontend has no shell access.** The sidecar is spawned from Rust with `std::process`,
not through Tauri's shell plugin, so there is no `shell:allow-execute` permission and no way
for page code to name an executable or pass process arguments. The only bridge is
`backend_command`, which forwards a method name the sidecar either recognises or rejects.

**Money is display-only here.** Currency values arrive as JSON numbers and land in JavaScript
float64. That is exact for realistic payroll amounts, but this app must never do payroll
arithmetic — summing or deriving figures in JS would drift from the backend's decimal math,
which is authoritative. Render what the backend computed; send inputs back.

## Running

The sidecar must be built first — the Rust shell looks for it in the .NET build output.

```bash
# 1. Build the sidecar (from the repository root)
dotnet build services/PayrollManager.Backend/PayrollManager.Backend.csproj

# 2. Install frontend dependencies
cd apps/desktop
npm install

# 3. Run the app
npm run tauri dev
```

The database is created under the per-user app-data directory, **not** the install directory:

```
%LOCALAPPDATA%\com.payrollmanager.desktop\payroll.db
```

A database left at the old install-directory location is relocated automatically on first
run, and the original is left in place rather than moved.

## Scripts

| Command | Purpose |
| --- | --- |
| `npm run dev` | Vite dev server only (no desktop shell; backend calls will fail) |
| `npm run tauri dev` | Full desktop app with the sidecar |
| `npm run typecheck` | `tsc --noEmit` |
| `npm run build` | Typecheck plus production frontend bundle |
| `npm run tauri build` | Installable Windows bundle (msi/nsis) |

## Design system

Tokens come from `stitch_payrollmanager_desktop_pro/pro_density_enterprise/DESIGN.md` and are
defined once in `src/styles.css` under `@theme` (Tailwind v4).

Note that DESIGN.md's prose describes a different palette (`#0F172A` / `#3B82F6`) than its YAML
frontmatter (`#0b1326` / `#adc6ff`). The exported mock HTML uses the frontmatter values, so
those are treated as the source of truth.

## Scope

Employees is the completed vertical slice: list, search, detail tabs, create, and edit, each
going DB → repository → sidecar command → Tauri command → React Query → page. Other nav items
are visible but disabled rather than linking to empty screens.

### Fields deliberately not in the form

The `add_employee` mock includes SSN, date of birth, work email, residential address, and
employee type. The domain has no columns for these, so they are **omitted rather than
rendered**. Showing an input that silently discards what the user types is the exact defect
that was just fixed elsewhere in this codebase (`HireDate` was collected by the old WinUI form
and dropped on save).

SSN in particular needs encryption at rest and key management before it can be stored
responsibly.

The form instead includes the federal W-4 and Illinois IL-W-4 fields, which the mocks omit but
which drive withholding and are required for correct Illinois payroll.

## Icon

`src-tauri/icons/` is generated, not committed. To regenerate:

```bash
node src-tauri/generate-icon.mjs   # writes src-tauri/app-icon.png
npx tauri icon src-tauri/app-icon.png
```
