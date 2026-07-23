import { useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";

import { callBackend } from "@/lib/ipc";
import { Button, Card, EmptyState, StatCard, cx } from "@/components/ui/primitives";
import { formatCurrency, formatDate } from "@/lib/format";
import type { DashboardData } from "@/types/api";

export function DashboardPage() {
  const navigate = useNavigate();
  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ["dashboard"],
    queryFn: () => callBackend<DashboardData>("get_dashboard"),
  });

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex shrink-0 items-start justify-between border-b border-outline-variant px-6 py-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-[24px] font-semibold leading-8 tracking-[-0.02em] text-white">Dashboard</h1>
          <p className="text-[13px] text-on-surface-variant">
            {data ? `Year-to-date, ${data.year}` : "Payroll at a glance"}
          </p>
        </div>
        <Button variant="primary" onClick={() => navigate("/pay-runs/new")}>Run Payroll</Button>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        {isPending && <EmptyState title="Loading dashboard…" />}

        {isError && (
          <EmptyState
            title="Could not load the dashboard"
            description={error instanceof Error ? error.message : undefined}
            action={<Button size="sm" onClick={() => void refetch()}>Retry</Button>}
          />
        )}

        {data && <DashboardBody data={data} onOpenRun={(id) => navigate(`/pay-runs/${id}`)} />}
      </div>
    </div>
  );
}

function DashboardBody({ data, onOpenRun }: { data: DashboardData; onOpenRun: (id: number) => void }) {
  const ytd = data.companyYtd;

  return (
    <div className="flex flex-col gap-5">
      {!data.suiConfigured && (
        <div role="alert" className="rounded-lg border border-tertiary/40 bg-tertiary/10 px-4 py-3 text-[13px] text-tertiary">
          State unemployment (SUI) is not configured, so employer cost below is understated.{" "}
          <a href="#/settings" className="underline">Configure it in Company Settings.</a>
        </div>
      )}

      {/* Primary KPIs */}
      <div className="grid grid-cols-4 gap-4">
        <StatCard label="YTD Gross Payroll" value={formatCurrency(ytd.grossPay)} caption={`${ytd.payStubCount} pay stubs`} />
        <StatCard label="YTD Net Paid" value={formatCurrency(ytd.netPay)} tone="positive" />
        <StatCard label="YTD Employer Cost" value={formatCurrency(ytd.totalEmployerCost)} caption="Gross + employer taxes" />
        <StatCard label="Active Employees" value={String(data.activeEmployeeCount)} caption={`${data.postedRunCount} posted runs`} />
      </div>

      {/* Next run + last run */}
      <div className="grid grid-cols-2 gap-4">
        <Card className="flex flex-col gap-3 p-5">
          <span className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Next Scheduled Pay Date</span>
          <span className="tabular text-[24px] font-semibold leading-8 tracking-[-0.02em] text-white">
            {data.nextPayDate ? formatDate(data.nextPayDate) : "—"}
          </span>
          <span className="text-[12px] text-on-surface-variant/70">Projected from your pay frequency and last run.</span>
        </Card>

        <Card className="flex flex-col gap-3 p-5">
          <span className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Last Pay Run</span>
          {data.lastPayRun ? (
            <button
              type="button"
              onClick={() => onOpenRun(data.lastPayRun!.id)}
              className="flex flex-col gap-1 text-left"
            >
              <span className="tabular text-[24px] font-semibold leading-8 tracking-[-0.02em] text-primary">
                {formatCurrency(data.lastPayRun.netPay)}
              </span>
              <span className="text-[12px] text-on-surface-variant/70">
                Paid {formatDate(data.lastPayRun.payDate)} · {data.lastPayRun.employeeCount} employees · view →
              </span>
            </button>
          ) : (
            <span className="text-[13px] text-on-surface-variant">No pay runs yet.</span>
          )}
        </Card>
      </div>

      {/* YTD tax breakdown */}
      <Card className="flex flex-col gap-4 p-5">
        <h3 className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
          Year-to-Date Withholding & Contributions
        </h3>
        <div className="grid grid-cols-3 gap-x-8 gap-y-4 sm:grid-cols-4">
          <Metric label="Federal Income Tax" value={ytd.federalTax} />
          <Metric label="Illinois Income Tax" value={ytd.stateTax} />
          <Metric label="Social Security" value={ytd.socialSecurity} />
          <Metric label="Medicare" value={ytd.medicare} />
          <Metric label="401(k) Pre-Tax" value={ytd.preTax401k} />
          <Metric label="Post-Tax Deductions" value={ytd.postTaxDeductions} />
          <Metric label="Total Withheld" value={ytd.totalTaxes} />
          <Metric label="Employer Taxes" value={ytd.employerTaxes} />
        </div>
      </Card>
    </div>
  );
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant/70">{label}</span>
      <span className={cx("tabular text-[15px] font-medium text-on-surface")}>{formatCurrency(value)}</span>
    </div>
  );
}
