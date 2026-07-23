import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { save } from "@tauri-apps/plugin-dialog";

import { Button, EmptyState, cx } from "@/components/ui/primitives";
import { formatCurrency, formatDate } from "@/lib/format";
import type { PayStubStatement } from "@/types/api";
import { exportPayStubPdf, usePayStub } from "./api";

export function PayStubPage() {
  const { payStubId } = useParams();
  const id = payStubId ? Number(payStubId) : null;
  const navigate = useNavigate();

  const { data, isPending, isError, error } = usePayStub(id);
  const [exportState, setExportState] = useState<{ kind: "idle" | "saving" | "done" | "error"; message?: string }>({ kind: "idle" });

  async function onExport() {
    if (!data) return;
    try {
      setExportState({ kind: "saving" });
      const path = await save({
        defaultPath: `paystub-${data.employeeName.replace(/\s+/g, "-")}-${data.payDate.slice(0, 10)}.pdf`,
        filters: [{ name: "PDF", extensions: ["pdf"] }],
      });
      if (!path) {
        setExportState({ kind: "idle" });
        return;
      }
      const result = await exportPayStubPdf(data.payStubId, path);
      setExportState({ kind: "done", message: `Saved ${result.path}` });
    } catch (e) {
      setExportState({ kind: "error", message: e instanceof Error ? e.message : "Export failed." });
    }
  }

  if (isPending) return <EmptyState title="Loading pay stub…" />;
  if (isError || !data) {
    return (
      <EmptyState
        title="Could not load this pay stub"
        description={error instanceof Error ? error.message : undefined}
        action={<Button size="sm" onClick={() => navigate(-1)}>Back</Button>}
      />
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {/* Toolbar — hidden when printing. */}
      <header className="no-print flex shrink-0 items-center justify-between border-b border-outline-variant px-6 py-4">
        <button type="button" onClick={() => navigate(-1)} className="text-[13px] text-on-surface-variant hover:text-on-surface">
          ← Back
        </button>
        <div className="flex items-center gap-2">
          {exportState.kind === "done" && <span className="text-[12px] text-secondary">{exportState.message}</span>}
          {exportState.kind === "error" && <span role="alert" className="text-[12px] text-error">{exportState.message}</span>}
          <Button variant="secondary" onClick={() => window.print()}>Print</Button>
          <Button variant="primary" onClick={onExport} disabled={exportState.kind === "saving"}>
            {exportState.kind === "saving" ? "Exporting…" : "Export PDF"}
          </Button>
        </div>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto bg-surface-lowest px-6 py-6">
        {/* The statement is the print area: white paper, dark text, so it prints legibly. */}
        <div className="print-area mx-auto max-w-3xl rounded-md bg-white p-8 text-[#1a1a1a] shadow-lg">
          <Statement s={data} />
        </div>
      </div>
    </div>
  );
}

function Statement({ s }: { s: PayStubStatement }) {
  return (
    <div className="flex flex-col gap-5 font-sans text-[12px] leading-5">
      {/* Header */}
      <div className="flex items-start justify-between border-b-2 border-[#1a1a1a] pb-3">
        <div>
          <div className="text-[18px] font-bold">{s.companyName}</div>
          {s.companyTaxId && <div className="text-[11px] text-[#555]">ID: {s.companyTaxId}</div>}
          {s.companyAddress && <div className="text-[11px] text-[#555]">{s.companyAddress}</div>}
        </div>
        <div className="text-right">
          <div className="text-[15px] font-bold">EARNINGS STATEMENT</div>
          <div className="text-[11px] text-[#555]">Check #{s.checkNumber}</div>
        </div>
      </div>

      {/* Info grid */}
      <div className="grid grid-cols-4 gap-4 text-[11px]">
        <div className="col-span-2">
          <Label>Employee</Label>
          <div className="text-[13px] font-semibold">{s.employeeName}</div>
          <div className="text-[#555]">SSN: {s.maskedSsn ?? "not on file"}</div>
          {s.addressLine1 && <div className="text-[#555]">{s.addressLine1}</div>}
          {s.addressLine2 && <div className="text-[#555]">{s.addressLine2}</div>}
          {(s.jobTitle || s.department) && (
            <div className="text-[#555]">{[s.jobTitle, s.department].filter(Boolean).join(" · ")}</div>
          )}
        </div>
        <div>
          <Label>Pay Date</Label>
          <div className="font-semibold">{formatDate(s.payDate)}</div>
          <Label>Pay Period</Label>
          <div>{formatDate(s.periodStart)} –</div>
          <div>{formatDate(s.periodEnd)}</div>
        </div>
        <div>
          <Label>Pay Schedule</Label>
          <div>{s.paySchedule}</div>
          <Label>Employee ID</Label>
          <div className="font-mono">{s.employeeCode}</div>
        </div>
      </div>

      {/* Earnings */}
      <Section title="Earnings">
        <table className="w-full border-collapse text-[11px]">
          <thead>
            <tr className="border-b border-[#ccc] text-left">
              <Th>Description</Th><Th right>Rate</Th><Th right>Hours</Th><Th right>Current</Th>
            </tr>
          </thead>
          <tbody>
            {s.earnings.length > 0 ? (
              s.earnings.map((e, i) => (
                <tr key={i} className="border-b border-[#eee]">
                  <Td>{e.description}</Td>
                  <Td right>{formatCurrency(e.rate)}</Td>
                  <Td right>{e.hours > 0 ? e.hours.toFixed(2) : "—"}</Td>
                  <Td right>{formatCurrency(e.amount)}</Td>
                </tr>
              ))
            ) : (
              <tr className="border-b border-[#eee]"><Td>Earnings</Td><Td right>—</Td><Td right>—</Td><Td right>{formatCurrency(s.grossPay)}</Td></tr>
            )}
            <tr className="font-semibold">
              <Td>Gross Pay</Td><Td right></Td><Td right></Td><Td right>{formatCurrency(s.grossPay)}</Td>
            </tr>
          </tbody>
        </table>
      </Section>

      {/* Taxes — the current + YTD breakdown */}
      <Section title="Taxes Withheld">
        <table className="w-full border-collapse text-[11px]">
          <thead>
            <tr className="border-b border-[#ccc] text-left">
              <Th>Tax</Th><Th right>Current</Th><Th right>Year-to-Date</Th>
            </tr>
          </thead>
          <tbody>
            {s.taxes.map((t) => (
              <tr key={t.label} className="border-b border-[#eee]">
                <Td>{t.label}</Td>
                <Td right>{formatCurrency(t.current)}</Td>
                <Td right>{formatCurrency(t.ytd)}</Td>
              </tr>
            ))}
            <tr className="font-semibold">
              <Td>Total Taxes</Td>
              <Td right>{formatCurrency(s.totalTaxes)}</Td>
              <Td right>{formatCurrency(s.ytdTotalTaxes)}</Td>
            </tr>
          </tbody>
        </table>
      </Section>

      {/* Deductions */}
      {s.deductions.length > 0 && (
        <Section title="Deductions">
          <table className="w-full border-collapse text-[11px]">
            <thead>
              <tr className="border-b border-[#ccc] text-left">
                <Th>Description</Th><Th>Type</Th><Th right>Current</Th>
              </tr>
            </thead>
            <tbody>
              {s.deductions.map((d, i) => (
                <tr key={i} className="border-b border-[#eee]">
                  <Td>{d.description}</Td>
                  <Td>{d.isPreTax ? "Pre-tax" : "Post-tax"}</Td>
                  <Td right>{formatCurrency(d.amount)}</Td>
                </tr>
              ))}
            </tbody>
          </table>
        </Section>
      )}

      {/* Summary: current + YTD side by side */}
      <div className="grid grid-cols-2 gap-4 border-t-2 border-[#1a1a1a] pt-3">
        <SummaryBlock title="This Period" rows={[
          ["Gross Pay", s.grossPay],
          ["Pre-Tax Deductions", s.preTaxDeductions],
          ["Taxes", s.totalTaxes],
          ["Post-Tax Deductions", s.postTaxDeductions],
        ]} net={["Net Pay", s.netPay]} />
        <SummaryBlock title="Year-to-Date" rows={[
          ["Gross Pay", s.ytdGross],
          ["Pre-Tax Deductions", s.ytdPreTaxDeductions],
          ["Taxes", s.ytdTotalTaxes],
          ["Post-Tax Deductions", s.ytdPostTaxDeductions],
        ]} net={["Net Pay", s.ytdNet]} />
      </div>
    </div>
  );
}

function Label({ children }: { children: React.ReactNode }) {
  return <div className="mt-1 text-[9px] font-bold uppercase tracking-wider text-[#888]">{children}</div>;
}
function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="mb-1 text-[12px] font-bold">{title}</div>
      {children}
    </div>
  );
}
function Th({ children, right }: { children?: React.ReactNode; right?: boolean }) {
  return <th className={cx("px-1 py-1 text-[9px] font-bold uppercase tracking-wider text-[#888]", right && "text-right")}>{children}</th>;
}
function Td({ children, right }: { children?: React.ReactNode; right?: boolean }) {
  return <td className={cx("px-1 py-1", right && "text-right tabular-nums")}>{children}</td>;
}
function SummaryBlock({ title, rows, net }: { title: string; rows: [string, number][]; net: [string, number] }) {
  return (
    <div className="rounded border border-[#ddd] p-3">
      <div className="mb-2 text-[10px] font-bold uppercase tracking-wider text-[#888]">{title}</div>
      <table className="w-full text-[11px]">
        <tbody>
          {rows.map(([label, value]) => (
            <tr key={label}>
              <td className="py-0.5">{label}</td>
              <td className="py-0.5 text-right tabular-nums">{formatCurrency(value)}</td>
            </tr>
          ))}
          <tr className="border-t border-[#1a1a1a] text-[13px] font-bold">
            <td className="pt-1">{net[0]}</td>
            <td className="pt-1 text-right tabular-nums">{formatCurrency(net[1])}</td>
          </tr>
        </tbody>
      </table>
    </div>
  );
}
