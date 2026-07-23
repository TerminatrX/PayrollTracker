import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { save } from "@tauri-apps/plugin-dialog";

import { callBackend } from "@/lib/ipc";
import { Button, Card, EmptyState, Field, StatCard, TextInput, cx } from "@/components/ui/primitives";
import { formatCurrency, fromDateInputValue } from "@/lib/format";
import type { PayrollReport } from "@/types/api";

function yearStart(): string {
  return `${new Date().getFullYear()}-01-01`;
}
function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export function ReportsPage() {
  const [start, setStart] = useState(yearStart());
  const [end, setEnd] = useState(today());
  const [range, setRange] = useState<{ start: string; end: string }>({ start: yearStart(), end: today() });
  const [exportState, setExportState] = useState<{ kind: "idle" | "saving" | "done" | "error"; message?: string }>({ kind: "idle" });

  const rangeInvalid = end < start;

  const { data, isPending, isError, error } = useReport(range.start, range.end);

  async function exportCsv() {
    try {
      setExportState({ kind: "saving" });
      const path = await save({
        defaultPath: `payroll-report-${range.start}_to_${range.end}.csv`,
        filters: [{ name: "CSV", extensions: ["csv"] }],
      });
      if (!path) {
        setExportState({ kind: "idle" }); // user cancelled
        return;
      }
      const result = await callBackend<{ path: string }>("export_payroll_report", {
        startDate: fromDateInputValue(range.start),
        endDate: fromDateInputValue(range.end),
        outputPath: path,
      });
      setExportState({ kind: "done", message: `Exported to ${result.path}` });
    } catch (e) {
      setExportState({ kind: "error", message: e instanceof Error ? e.message : "Export failed." });
    }
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex shrink-0 items-start justify-between border-b border-outline-variant px-6 py-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-[24px] font-semibold leading-8 tracking-[-0.02em] text-white">Reports</h1>
          <p className="text-[13px] text-on-surface-variant">Payroll register for a date range. Posted runs only.</p>
        </div>
        <Button
          variant="secondary"
          onClick={exportCsv}
          disabled={!data || data.employees.length === 0 || exportState.kind === "saving"}
        >
          {exportState.kind === "saving" ? "Exporting…" : "Export CSV"}
        </Button>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <div className="flex flex-col gap-5">
          {/* Range controls */}
          <Card className="flex items-end gap-4 p-4">
            <Field label="Start Date" htmlFor="reportStart" className="w-44">
              <TextInput id="reportStart" type="date" value={start} onChange={(e) => setStart(e.target.value)} />
            </Field>
            <Field
              label="End Date"
              htmlFor="reportEnd"
              className="w-44"
              error={rangeInvalid ? "End is before start." : undefined}
            >
              <TextInput id="reportEnd" type="date" value={end} invalid={rangeInvalid} onChange={(e) => setEnd(e.target.value)} />
            </Field>
            <Button variant="primary" disabled={rangeInvalid} onClick={() => setRange({ start, end })}>
              Run Report
            </Button>
          </Card>

          {exportState.kind === "done" && (
            <p className="text-[12px] text-secondary">{exportState.message}</p>
          )}
          {exportState.kind === "error" && (
            <p role="alert" className="text-[12px] text-error">{exportState.message}</p>
          )}

          {isPending && <EmptyState title="Loading report…" />}
          {isError && (
            <EmptyState title="Could not load the report" description={error instanceof Error ? error.message : undefined} />
          )}

          {data && <ReportBody report={data} />}
        </div>
      </div>
    </div>
  );
}

function useReport(start: string, end: string) {
  return useQuery({
    queryKey: ["payroll-report", start, end],
    queryFn: () =>
      callBackend<PayrollReport>("get_payroll_report", {
        startDate: fromDateInputValue(start),
        endDate: fromDateInputValue(end),
      }),
  });
}

function ReportBody({ report }: { report: PayrollReport }) {
  const c = report.company;

  if (report.employees.length === 0) {
    return <EmptyState title="No posted payroll in this range" description="Adjust the dates or post a pay run." />;
  }

  return (
    <div className="flex flex-col gap-5">
      <div className="grid grid-cols-4 gap-4">
        <StatCard label="Gross Payroll" value={formatCurrency(c.grossPay)} caption={`${c.employeeCount} employees`} />
        <StatCard label="Total Withheld" value={formatCurrency(c.totalTaxes)} />
        <StatCard label="Net Paid" value={formatCurrency(c.netPay)} tone="positive" />
        <StatCard label="Employer Cost" value={formatCurrency(c.totalEmployerCost)} caption="Gross + employer taxes" />
      </div>

      <Card className="overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] border-collapse">
            <thead>
              <tr className="border-b border-outline-variant bg-surface-lowest text-left">
                <Th>Employee</Th>
                <Th right>Pay Stubs</Th>
                <Th right>Gross</Th>
                <Th right>Taxes</Th>
                <Th right>401(k)</Th>
                <Th right>Net Pay</Th>
              </tr>
            </thead>
            <tbody>
              {report.employees.map((e) => (
                <tr key={e.employeeId} className="border-b border-outline-variant/60">
                  <td className="px-3 py-2.5 text-[13px] text-on-surface">{e.employeeName}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{e.payStubCount}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface">{formatCurrency(e.grossPay)}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{formatCurrency(e.totalTaxes)}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{formatCurrency(e.preTax401k)}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] font-medium text-primary">{formatCurrency(e.netPay)}</td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t border-outline-variant bg-surface-lowest">
                <td className="px-3 py-2.5 text-[12px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Total</td>
                <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{c.payStubCount}</td>
                <td className="tabular px-3 py-2.5 text-right text-[13px] font-medium text-on-surface">{formatCurrency(c.grossPay)}</td>
                <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface">{formatCurrency(c.totalTaxes)}</td>
                <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface">{formatCurrency(c.preTax401k)}</td>
                <td className="tabular px-3 py-2.5 text-right text-[13px] font-medium text-primary">{formatCurrency(c.netPay)}</td>
              </tr>
            </tfoot>
          </table>
        </div>
      </Card>
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
