import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";

import { Button, Card, Chip, EmptyState, StatCard, TextInput, cx } from "@/components/ui/primitives";
import { formatCurrency, formatDate } from "@/lib/format";
import { ApiError } from "@/types/api";
import { usePayRun, useVoidPayRun } from "./api";
import { statusTone } from "./status";

export function PayRunDetail() {
  const { payRunId } = useParams();
  const id = payRunId ? Number(payRunId) : null;
  const navigate = useNavigate();

  const { data, isPending, isError, error } = usePayRun(id);
  const [showVoid, setShowVoid] = useState(false);

  if (isPending) return <EmptyState title="Loading pay run…" />;
  if (isError || !data) {
    return (
      <EmptyState
        title="Could not load this pay run"
        description={error instanceof Error ? error.message : undefined}
        action={<Button size="sm" onClick={() => navigate("/pay-runs")}>Back to list</Button>}
      />
    );
  }

  const { summary, stubs } = data;

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="shrink-0 border-b border-outline-variant px-6 py-4">
        <button
          type="button"
          onClick={() => navigate("/pay-runs")}
          className="mb-2 text-[12px] text-on-surface-variant hover:text-on-surface"
        >
          ← Pay Runs
        </button>

        <div className="flex items-start justify-between">
          <div className="flex flex-col gap-1.5">
            <div className="flex items-center gap-3">
              <h1 className="text-[22px] font-semibold leading-7 tracking-[-0.02em] text-white">
                {formatDate(summary.periodStart)} – {formatDate(summary.periodEnd)}
              </h1>
              <Chip tone={statusTone(summary.status)}>{summary.status}</Chip>
            </div>
            <p className="text-[13px] text-on-surface-variant">
              Paid {formatDate(summary.payDate)} · {summary.employeeCount} employees
              {summary.engineVersion && ` · engine ${summary.engineVersion}`}
            </p>
          </div>

          {summary.status === "Posted" && (
            <Button variant="danger" onClick={() => setShowVoid(true)}>
              Void Run
            </Button>
          )}
        </div>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <div className="flex flex-col gap-5">
          {summary.status === "Voided" && (
            <div className="rounded-lg border border-outline-variant bg-surface-high/40 px-4 py-3 text-[13px] text-on-surface-variant">
              This run was voided{summary.voidedAtUtc ? ` on ${formatDate(summary.voidedAtUtc)}` : ""}. It is
              retained for audit and no longer counts toward year-to-date totals.
              {summary.voidReason && <span className="block pt-1 text-on-surface">Reason: {summary.voidReason}</span>}
            </div>
          )}

          <div className="grid grid-cols-4 gap-4">
            <StatCard label="Gross Payroll" value={formatCurrency(summary.grossPay)} />
            <StatCard label="Employee Taxes" value={formatCurrency(summary.employeeTaxes)} />
            <StatCard label="Net Payroll" value={formatCurrency(summary.netPay)} tone="positive" />
            <StatCard label="Employer Cost" value={formatCurrency(summary.totalEmployerCost)} caption="Gross + employer taxes" />
          </div>

          <Card className="overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[720px] border-collapse">
                <thead>
                  <tr className="border-b border-outline-variant bg-surface-lowest text-left">
                    <Th>Employee</Th>
                    <Th right>Hours</Th>
                    <Th right>Gross</Th>
                    <Th right>Taxes</Th>
                    <Th right>Net Pay</Th>
                    <Th right>Employer Tax</Th>
                  </tr>
                </thead>
                <tbody>
                  {stubs.map((s) => (
                    <tr key={s.payStubId} className="border-b border-outline-variant/60">
                      <td className="px-3 py-2.5 text-[13px] text-on-surface">{s.employeeName}</td>
                      <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{s.hoursWorked || "—"}</td>
                      <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface">{formatCurrency(s.grossPay)}</td>
                      <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{formatCurrency(s.totalTaxes)}</td>
                      <td className="tabular px-3 py-2.5 text-right text-[13px] font-medium text-primary">{formatCurrency(s.netPay)}</td>
                      <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{formatCurrency(s.employerTaxes)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        </div>
      </div>

      {showVoid && <VoidDialog payRunId={summary.id} onClose={() => setShowVoid(false)} />}
    </div>
  );
}

function Th({ children, right }: { children: React.ReactNode; right?: boolean }) {
  return (
    <th className={cx("px-3 py-2.5 text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant", right && "text-right")}>
      {children}
    </th>
  );
}

function VoidDialog({ payRunId, onClose }: { payRunId: number; onClose: () => void }) {
  const voidRun = useVoidPayRun();
  const [reason, setReason] = useState("");

  const errorMessage = voidRun.error instanceof ApiError ? voidRun.error.message : null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4" onClick={onClose}>
      {/* Stop clicks anywhere on the dialog (including its padding) from reaching the
          backdrop and closing it. */}
      <div className="w-full max-w-md" onClick={(e) => e.stopPropagation()}>
        <Card className="border-outline-variant bg-surface-high p-5 shadow-2xl">
          <h2 className="text-[16px] font-semibold text-white">Void this pay run?</h2>
          <p className="mt-1 text-[13px] text-on-surface-variant">
            The run and its pay stubs are kept permanently for audit, but stop counting toward
            year-to-date totals. A correction is made by voiding and posting an adjustment run.
          </p>

          <div className="mt-4">
            <label className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
              Reason <span className="text-error">*</span>
            </label>
            <TextInput
              className="mt-1.5"
              placeholder="e.g. Wrong hours entered"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              autoFocus
              invalid={!!errorMessage}
            />
            {errorMessage && <p className="mt-1 text-[12px] text-error">{errorMessage}</p>}
          </div>

          <div className="mt-5 flex justify-end gap-2">
            <Button variant="secondary" onClick={onClose} disabled={voidRun.isPending}>
              Cancel
            </Button>
            <Button
              variant="danger"
              disabled={!reason.trim() || voidRun.isPending}
              onClick={async () => {
                await voidRun.mutateAsync({ payRunId, reason: reason.trim() });
                onClose();
              }}
            >
              {voidRun.isPending ? "Voiding…" : "Void Run"}
            </Button>
          </div>
        </Card>
      </div>
    </div>
  );
}
