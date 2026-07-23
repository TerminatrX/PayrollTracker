import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";

import { Button, Card, Chip, EmptyState, StatCard, cx } from "@/components/ui/primitives";
import { formatCurrency, formatDate } from "@/lib/format";
import type { PayRunStatus, PayRunSummary } from "@/types/api";
import { usePayRuns } from "./api";
import { statusTone } from "./status";

const STATUS_FILTERS: Array<{ value: "All" | PayRunStatus; label: string }> = [
  { value: "All", label: "All" },
  { value: "Posted", label: "Posted" },
  { value: "Draft", label: "Draft" },
  { value: "Voided", label: "Voided" },
];

export function PayRunsPage() {
  const navigate = useNavigate();
  const { data: payRuns, isPending, isError, error, refetch } = usePayRuns();
  const [filter, setFilter] = useState<"All" | PayRunStatus>("All");

  const filtered = useMemo(
    () => (payRuns ?? []).filter((r) => filter === "All" || r.status === filter),
    [payRuns, filter],
  );

  // Year-to-date paid = net pay of posted runs whose pay date is in the current year.
  // This is a display aggregate of already-authoritative per-run net figures (the backend
  // computed each). Summed in integer cents rather than float dollars so accumulation cannot
  // drift a fraction of a cent - the codebase never does payroll math in float. (The dashboard
  // remains the authoritative company-wide YTD source.)
  const stats = useMemo(() => {
    const posted = (payRuns ?? []).filter((r) => r.status === "Posted");
    const thisYear = new Date().getFullYear();
    const ytdNetCents = posted
      .filter((r) => Number(r.payDate.slice(0, 4)) === thisYear)
      .reduce((cents, r) => cents + Math.round(r.netPay * 100), 0);
    const lastPosted = posted[0]; // list is pay-date desc
    return { ytdNet: ytdNetCents / 100, postedCount: posted.length, lastPosted };
  }, [payRuns]);

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex shrink-0 items-start justify-between border-b border-outline-variant px-6 py-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-[24px] font-semibold leading-8 tracking-[-0.02em] text-white">Pay Runs</h1>
          <p className="text-[13px] text-on-surface-variant">
            Preview, post, and void payroll. Posted runs are permanent records.
          </p>
        </div>
        <Button variant="primary" onClick={() => navigate("/pay-runs/new")}>
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="size-4">
            <path d="M12 5v14M5 12h14" />
          </svg>
          New Pay Run
        </Button>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <div className="flex flex-col gap-5">
          {/* Stat cards */}
          <div className="grid grid-cols-3 gap-4">
            <StatCard
              label="Paid This Year (Net)"
              value={formatCurrency(stats.ytdNet)}
              caption={`${stats.postedCount} posted run${stats.postedCount === 1 ? "" : "s"}`}
              tone="positive"
            />
            <StatCard
              label="Last Pay Date"
              value={stats.lastPosted ? formatDate(stats.lastPosted.payDate) : "—"}
              caption={stats.lastPosted ? `${stats.lastPosted.employeeCount} employees` : "No runs yet"}
            />
            <StatCard
              label="Last Employer Cost"
              value={stats.lastPosted ? formatCurrency(stats.lastPosted.totalEmployerCost) : "—"}
              caption="Gross plus employer taxes"
            />
          </div>

          {/* Filter */}
          <div className="flex gap-2">
            {STATUS_FILTERS.map((f) => (
              <button
                key={f.value}
                type="button"
                onClick={() => setFilter(f.value)}
                aria-pressed={filter === f.value}
                className={cx(
                  "h-8 rounded-lg border px-3 text-[12px] font-bold uppercase tracking-[0.05em] transition-colors",
                  filter === f.value
                    ? "border-primary/40 bg-primary/15 text-primary"
                    : "border-outline-variant text-on-surface-variant hover:border-outline hover:text-on-surface",
                )}
              >
                {f.label}
              </button>
            ))}
          </div>

          {/* Table */}
          <Card className="overflow-hidden">
            {isPending && <p className="px-4 py-6 text-[13px] text-on-surface-variant">Loading pay runs…</p>}

            {isError && (
              <div className="flex flex-col items-start gap-3 p-4">
                <p className="text-[13px] text-error">
                  {error instanceof Error ? error.message : "Could not load pay runs."}
                </p>
                <Button size="sm" onClick={() => void refetch()}>Retry</Button>
              </div>
            )}

            {payRuns && filtered.length === 0 && (
              <EmptyState
                title={payRuns.length === 0 ? "No pay runs yet" : `No ${filter.toLowerCase()} runs`}
                description={payRuns.length === 0 ? "Start your first payroll to see it here." : undefined}
                action={
                  payRuns.length === 0 ? (
                    <Button variant="primary" size="sm" onClick={() => navigate("/pay-runs/new")}>
                      New Pay Run
                    </Button>
                  ) : undefined
                }
              />
            )}

            {filtered.length > 0 && (
              <div className="overflow-x-auto">
                <table className="w-full min-w-[820px] border-collapse">
                  <thead>
                    <tr className="border-b border-outline-variant bg-surface-lowest text-left">
                      <Th>Pay Period</Th>
                      <Th>Pay Date</Th>
                      <Th>Status</Th>
                      <Th right>Employees</Th>
                      <Th right>Gross</Th>
                      <Th right>Net Payroll</Th>
                      <Th right>Employer Cost</Th>
                    </tr>
                  </thead>
                  <tbody>
                    {filtered.map((run) => (
                      <PayRunRow key={run.id} run={run} onOpen={() => navigate(`/pay-runs/${run.id}`)} />
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </div>
      </div>
    </div>
  );
}

function Th({ children, right }: { children: React.ReactNode; right?: boolean }) {
  return (
    <th
      className={cx(
        "px-3 py-2.5 text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant",
        right && "text-right",
      )}
    >
      {children}
    </th>
  );
}

function PayRunRow({ run, onOpen }: { run: PayRunSummary; onOpen: () => void }) {
  return (
    <tr
      onClick={onOpen}
      className="cursor-pointer border-b border-outline-variant/60 transition-colors hover:bg-surface-high/40"
    >
      <td className="px-3 py-3 text-[13px] text-on-surface">
        {formatDate(run.periodStart)} – {formatDate(run.periodEnd)}
      </td>
      <td className="px-3 py-3 text-[13px] text-on-surface-variant">{formatDate(run.payDate)}</td>
      <td className="px-3 py-3">
        <Chip tone={statusTone(run.status)}>{run.status}</Chip>
      </td>
      <td className="tabular px-3 py-3 text-right text-[13px] text-on-surface">{run.employeeCount}</td>
      <td className="tabular px-3 py-3 text-right text-[13px] text-on-surface">{formatCurrency(run.grossPay)}</td>
      <td className="tabular px-3 py-3 text-right text-[13px] font-medium text-primary">{formatCurrency(run.netPay)}</td>
      <td className="tabular px-3 py-3 text-right text-[13px] text-on-surface-variant">{formatCurrency(run.totalEmployerCost)}</td>
    </tr>
  );
}
