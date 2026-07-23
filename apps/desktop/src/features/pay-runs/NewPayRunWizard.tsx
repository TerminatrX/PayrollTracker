import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";

import {
  Button,
  Card,
  EmptyState,
  Field,
  NumberInput,
  StatCard,
  TextInput,
  cx,
} from "@/components/ui/primitives";
import { formatCurrency, fromDateInputValue, toDateInputValue } from "@/lib/format";
import { ApiError } from "@/types/api";
import type {
  PayRunEmployeeInput,
  PayRunEmployeeSuggestion,
  PayRunPreview,
} from "@/types/api";
import { previewPayRun, suggestPayRun, usePostPayRun } from "./api";

type Step = 1 | 2 | 3;

interface RowState {
  suggestion: PayRunEmployeeSuggestion;
  included: boolean;
  regularHours: string;
  overtimeHours: string;
  bonusAmount: string;
}

const STEPS: Array<{ n: Step; label: string }> = [
  { n: 1, label: "Pay Period" },
  { n: 2, label: "Employees & Hours" },
  { n: 3, label: "Review & Post" },
];

export function NewPayRunWizard() {
  const navigate = useNavigate();
  const postPayRun = usePostPayRun();

  const [step, setStep] = useState<Step>(1);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [periodStart, setPeriodStart] = useState("");
  const [periodEnd, setPeriodEnd] = useState("");
  const [payDate, setPayDate] = useState("");
  const [rows, setRows] = useState<Record<number, RowState>>({});

  const [preview, setPreview] = useState<PayRunPreview | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [previewError, setPreviewError] = useState<string | null>(null);

  // Bootstrap from the backend suggestion once.
  useEffect(() => {
    let active = true;
    suggestPayRun()
      .then((res) => {
        if (!active) return;
        setPeriodStart(toDateInputValue(res.draft.periodStart));
        setPeriodEnd(toDateInputValue(res.draft.periodEnd));
        setPayDate(toDateInputValue(res.draft.payDate));

        const initial: Record<number, RowState> = {};
        for (const s of res.employees) {
          initial[s.employeeId] = {
            suggestion: s,
            included: true,
            regularHours: String(s.suggestedRegularHours),
            overtimeHours: String(s.suggestedOvertimeHours),
            bonusAmount: "0",
          };
        }
        setRows(initial);
      })
      .catch((e: unknown) => active && setLoadError(e instanceof Error ? e.message : "Failed to load."))
      .finally(() => active && setLoading(false));
    return () => {
      active = false;
    };
  }, []);

  const includedInputs = useMemo<PayRunEmployeeInput[]>(
    () =>
      Object.values(rows)
        .filter((r) => r.included)
        .map((r) => ({
          employeeId: r.suggestion.employeeId,
          regularHours: r.suggestion.isHourly ? Number(r.regularHours) || 0 : 0,
          overtimeHours: r.suggestion.isHourly ? Number(r.overtimeHours) || 0 : 0,
          bonusAmount: Number(r.bonusAmount) || 0,
          commissionAmount: 0,
        })),
    [rows],
  );

  const includedCount = includedInputs.length;

  const draftPayload = useMemo(
    () => ({
      periodStart: fromDateInputValue(periodStart) ?? "",
      periodEnd: fromDateInputValue(periodEnd) ?? "",
      payDate: fromDateInputValue(payDate) ?? "",
    }),
    [periodStart, periodEnd, payDate],
  );

  const datesValid =
    periodStart && periodEnd && payDate && periodEnd >= periodStart && payDate > periodEnd;

  async function goToReview() {
    setStep(3);
    setPreview(null);
    setPreviewError(null);
    setPreviewing(true);
    try {
      const result = await previewPayRun(draftPayload, includedInputs);
      setPreview(result);
    } catch (e) {
      setPreviewError(e instanceof Error ? e.message : "Preview failed.");
    } finally {
      setPreviewing(false);
    }
  }

  async function post() {
    if (!preview) return;
    const summary = await postPayRun.mutateAsync({
      draft: draftPayload,
      employees: includedInputs,
      calculationHash: preview.calculationHash,
    });
    navigate(`/pay-runs/${summary.id}`);
  }

  if (loading) return <EmptyState title="Preparing pay run…" />;
  if (loadError) {
    return (
      <EmptyState
        title="Could not start a pay run"
        description={loadError}
        action={<Button size="sm" onClick={() => navigate("/pay-runs")}>Back</Button>}
      />
    );
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="shrink-0 border-b border-outline-variant px-6 py-4">
        <div className="flex items-center justify-between">
          <h1 className="text-[20px] font-semibold text-white">New Pay Run</h1>
          <button
            type="button"
            onClick={() => navigate("/pay-runs")}
            className="text-[13px] text-on-surface-variant hover:text-on-surface"
          >
            Cancel
          </button>
        </div>
        <Stepper current={step} />
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <div className="mx-auto max-w-4xl">
          {step === 1 && (
            <StepDates
              periodStart={periodStart}
              periodEnd={periodEnd}
              payDate={payDate}
              setPeriodStart={setPeriodStart}
              setPeriodEnd={setPeriodEnd}
              setPayDate={setPayDate}
              valid={!!datesValid}
            />
          )}

          {step === 2 && <StepHours rows={rows} setRows={setRows} />}

          {step === 3 && (
            <StepReview
              preview={preview}
              previewing={previewing}
              error={previewError}
              postError={postPayRun.error}
            />
          )}
        </div>
      </div>

      {/* Footer nav */}
      <div className="flex shrink-0 items-center justify-between border-t border-outline-variant bg-surface-lowest px-6 py-3">
        <div className="text-[12px] text-on-surface-variant">
          {step === 2 && `${includedCount} of ${Object.keys(rows).length} employees included`}
          {step === 3 && preview && `${preview.totals.employeeCount} employees · net ${formatCurrency(preview.totals.netPay)}`}
        </div>

        <div className="flex items-center gap-2">
          {step > 1 && (
            <Button variant="secondary" onClick={() => setStep((s) => (s - 1) as Step)} disabled={postPayRun.isPending}>
              Back
            </Button>
          )}

          {step === 1 && (
            <Button variant="primary" onClick={() => setStep(2)} disabled={!datesValid}>
              Next: Hours
            </Button>
          )}

          {step === 2 && (
            <Button variant="primary" onClick={goToReview} disabled={includedCount === 0}>
              Next: Review
            </Button>
          )}

          {step === 3 && (
            <Button variant="primary" onClick={post} disabled={!preview?.canPost || postPayRun.isPending}>
              {postPayRun.isPending ? "Posting…" : "Post Pay Run"}
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}

function Stepper({ current }: { current: Step }) {
  return (
    <div className="mt-4 flex items-center gap-2">
      {STEPS.map((s, i) => (
        <div key={s.n} className="flex items-center gap-2">
          <div className="flex items-center gap-2">
            <span
              className={cx(
                "flex size-6 items-center justify-center rounded-full text-[12px] font-bold",
                current === s.n && "bg-primary text-on-primary",
                current > s.n && "bg-secondary/20 text-secondary",
                current < s.n && "bg-surface-high text-on-surface-variant",
              )}
            >
              {current > s.n ? "✓" : s.n}
            </span>
            <span className={cx("text-[13px]", current === s.n ? "font-medium text-on-surface" : "text-on-surface-variant")}>
              {s.label}
            </span>
          </div>
          {i < STEPS.length - 1 && <div className="h-px w-8 bg-outline-variant" />}
        </div>
      ))}
    </div>
  );
}

function StepDates(props: {
  periodStart: string;
  periodEnd: string;
  payDate: string;
  setPeriodStart: (v: string) => void;
  setPeriodEnd: (v: string) => void;
  setPayDate: (v: string) => void;
  valid: boolean;
}) {
  const periodOrderError = props.periodEnd && props.periodEnd < props.periodStart;
  const payDateError = props.payDate && props.periodEnd && props.payDate <= props.periodEnd;

  return (
    <div className="flex flex-col gap-4">
      <p className="text-[13px] text-on-surface-variant">
        Suggested from your pay frequency and the last run. Adjust if needed.
      </p>
      <Card className="grid grid-cols-3 gap-4 p-5">
        <Field label="Period Start" htmlFor="periodStart" required>
          <TextInput id="periodStart" type="date" value={props.periodStart} onChange={(e) => props.setPeriodStart(e.target.value)} />
        </Field>
        <Field
          label="Period End"
          htmlFor="periodEnd"
          required
          error={periodOrderError ? "End must be on or after start." : undefined}
        >
          <TextInput id="periodEnd" type="date" value={props.periodEnd} invalid={!!periodOrderError} onChange={(e) => props.setPeriodEnd(e.target.value)} />
        </Field>
        <Field
          label="Pay Date"
          htmlFor="payDate"
          required
          error={payDateError ? "Pay date must be after the period ends." : undefined}
          hint="When employees are paid."
        >
          <TextInput id="payDate" type="date" value={props.payDate} invalid={!!payDateError} onChange={(e) => props.setPayDate(e.target.value)} />
        </Field>
      </Card>
    </div>
  );
}

function StepHours({
  rows,
  setRows,
}: {
  rows: Record<number, RowState>;
  setRows: React.Dispatch<React.SetStateAction<Record<number, RowState>>>;
}) {
  const list = Object.values(rows);

  function update(id: number, patch: Partial<RowState>) {
    setRows((prev) => ({ ...prev, [id]: { ...prev[id]!, ...patch } }));
  }

  if (list.length === 0) {
    return <EmptyState title="No active employees" description="Add an active employee before running payroll." />;
  }

  return (
    <Card className="overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[760px] border-collapse">
          <thead>
            <tr className="border-b border-outline-variant bg-surface-lowest text-left">
              <th className="w-10 px-3 py-2.5" />
              <th className="px-3 py-2.5 text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Employee</th>
              <th className="px-3 py-2.5 text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Pay Type</th>
              <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Regular Hrs</th>
              <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Overtime Hrs</th>
              <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Bonus $</th>
              <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Basis</th>
            </tr>
          </thead>
          <tbody>
            {list.map((row) => {
              const s = row.suggestion;
              return (
                <tr key={s.employeeId} className={cx("border-b border-outline-variant/60", !row.included && "opacity-45")}>
                  <td className="px-3 py-2">
                    <input
                      type="checkbox"
                      checked={row.included}
                      onChange={(e) => update(s.employeeId, { included: e.target.checked })}
                      className="size-4 rounded border-outline-variant bg-surface-lowest accent-primary"
                      aria-label={`Include ${s.fullName}`}
                    />
                  </td>
                  <td className="px-3 py-2">
                    <div className="flex flex-col">
                      <span className="text-[13px] font-medium text-on-surface">{s.fullName}</span>
                      <span className="text-[11px] text-on-surface-variant">{s.jobTitle || "—"}</span>
                    </div>
                    {!s.w4OnFile && <span className="text-[10px] font-bold uppercase text-tertiary">No W-4</span>}
                  </td>
                  <td className="px-3 py-2">
                    <span className="text-[12px] text-on-surface-variant">{s.isHourly ? "Hourly" : "Salaried"}</span>
                  </td>
                  <td className="px-3 py-2 text-right">
                    {s.isHourly ? (
                      <NumberInput
                        value={row.regularHours}
                        onChange={(e) => update(s.employeeId, { regularHours: e.target.value })}
                        step="0.25"
                        min="0"
                        disabled={!row.included}
                        className="h-8 w-24"
                      />
                    ) : (
                      <span className="text-[12px] text-on-surface-variant/50">—</span>
                    )}
                  </td>
                  <td className="px-3 py-2 text-right">
                    {s.isHourly ? (
                      <NumberInput
                        value={row.overtimeHours}
                        onChange={(e) => update(s.employeeId, { overtimeHours: e.target.value })}
                        step="0.25"
                        min="0"
                        disabled={!row.included}
                        className="h-8 w-24"
                      />
                    ) : (
                      <span className="text-[12px] text-on-surface-variant/50">—</span>
                    )}
                  </td>
                  <td className="px-3 py-2 text-right">
                    <NumberInput
                      value={row.bonusAmount}
                      onChange={(e) => update(s.employeeId, { bonusAmount: e.target.value })}
                      step="0.01"
                      min="0"
                      disabled={!row.included}
                      className="h-8 w-24"
                    />
                  </td>
                  <td className="tabular px-3 py-2 text-right text-[12px] text-on-surface-variant">
                    {s.isHourly ? `${formatCurrency(s.hourlyRate)}/hr` : `${formatCurrency(s.salaryPerPeriod)}/period`}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </Card>
  );
}

function StepReview({
  preview,
  previewing,
  error,
  postError,
}: {
  preview: PayRunPreview | null;
  previewing: boolean;
  error: string | null;
  postError: unknown;
}) {
  if (previewing) return <EmptyState title="Calculating payroll…" />;
  if (error) return <EmptyState title="Could not calculate this run" description={error} />;
  if (!preview) return null;

  const blockers = preview.warnings.filter((w) => w.blocksPosting);
  const advisories = preview.warnings.filter((w) => !w.blocksPosting);
  const postMessage = postError instanceof ApiError ? postError.message : null;

  return (
    <div className="flex flex-col gap-5">
      {postMessage && (
        <div role="alert" className="rounded-lg border border-error/40 bg-error/10 px-4 py-3 text-[13px] text-error">
          {postMessage}
        </div>
      )}

      {blockers.length > 0 && (
        <div className="rounded-lg border border-error/40 bg-error/10 p-4">
          <p className="mb-2 text-[13px] font-medium text-error">This run cannot be posted until resolved:</p>
          <ul className="flex flex-col gap-1">
            {blockers.map((w, i) => (
              <li key={i} className="text-[12px] text-error">• {w.message}</li>
            ))}
          </ul>
        </div>
      )}

      {advisories.length > 0 && (
        <div className="rounded-lg border border-tertiary/40 bg-tertiary/10 p-4">
          <p className="mb-2 text-[13px] font-medium text-tertiary">Review before posting:</p>
          <ul className="flex flex-col gap-1">
            {advisories.map((w, i) => (
              <li key={i} className="text-[12px] text-tertiary">• {w.message}</li>
            ))}
          </ul>
        </div>
      )}

      <div className="grid grid-cols-4 gap-4">
        <StatCard label="Gross Payroll" value={formatCurrency(preview.totals.grossPay)} />
        <StatCard label="Employee Taxes" value={formatCurrency(preview.totals.employeeTaxes)} />
        <StatCard label="Net Payroll" value={formatCurrency(preview.totals.netPay)} tone="positive" caption="Leaves the bank" />
        <StatCard label="Total Employer Cost" value={formatCurrency(preview.totals.totalEmployerCost)} caption="Gross + employer taxes" />
      </div>

      <Card className="overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[720px] border-collapse">
            <thead>
              <tr className="border-b border-outline-variant bg-surface-lowest text-left">
                <th className="px-3 py-2.5 text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Employee</th>
                <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Hours</th>
                <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Gross</th>
                <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Taxes</th>
                <th className="px-3 py-2.5 text-right text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">Net</th>
              </tr>
            </thead>
            <tbody>
              {preview.lines.map((line) => (
                <tr key={line.employeeId} className="border-b border-outline-variant/60">
                  <td className="px-3 py-2.5 text-[13px] text-on-surface">{line.employeeName}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{line.hoursWorked || "—"}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface">{formatCurrency(line.grossPay)}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] text-on-surface-variant">{formatCurrency(line.totalTaxes)}</td>
                  <td className="tabular px-3 py-2.5 text-right text-[13px] font-medium text-primary">{formatCurrency(line.netPay)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}
