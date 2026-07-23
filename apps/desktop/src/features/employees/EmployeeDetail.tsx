import { NavLink, Outlet, useOutletContext } from "react-router-dom";

import { Card, Chip, StatCard, cx } from "@/components/ui/primitives";
import { formatCurrency, formatDate, formatPercent, initialsOf } from "@/lib/format";
import { FILING_STATUS_LABELS, type Employee } from "@/types/api";

const TABS = [
  { to: ".", label: "Overview", end: true },
  { to: "compensation", label: "Compensation", end: false },
  { to: "taxes", label: "Taxes", end: false },
] as const;

export function useEmployeeContext() {
  return useOutletContext<{ employee: Employee }>();
}

export function EmployeeDetail({
  employee,
  onEdit,
}: {
  employee: Employee;
  onEdit: () => void;
}) {
  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="shrink-0 border-b border-outline-variant px-6 pb-0 pt-5">
        <div className="flex items-start gap-4">
          <div className="flex size-16 shrink-0 items-center justify-center rounded-md border border-outline-variant bg-surface-high text-[20px] font-semibold text-primary">
            {initialsOf(employee.firstName, employee.lastName)}
          </div>

          <div className="flex min-w-0 flex-1 flex-col gap-1">
            <div className="flex items-center gap-3">
              <h1 className="truncate text-[24px] font-semibold leading-8 tracking-[-0.02em] text-white">
                {employee.fullName}
              </h1>
              <Chip tone={employee.isActive ? "active" : "inactive"}>
                {employee.isActive ? "Active" : "Inactive"}
              </Chip>
              {!employee.w4OnFile && <Chip tone="warning">No W-4</Chip>}
            </div>

            <p className="truncate text-[15px] text-on-surface-variant">
              {[employee.jobTitle, employee.department].filter(Boolean).join(" • ") ||
                "No job title set"}
            </p>

            <p className="text-[13px] text-on-surface-variant/70">
              {employee.employeeCode} · Hired {formatDate(employee.hireDate)}
              {employee.terminationDate && ` · Terminated ${formatDate(employee.terminationDate)}`}
            </p>
          </div>

          <button
            type="button"
            onClick={onEdit}
            className="flex size-9 shrink-0 items-center justify-center rounded-lg border border-outline-variant text-on-surface-variant transition-colors hover:border-outline hover:text-on-surface"
            aria-label="Edit employee"
            title="Edit employee"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" className="size-4">
              <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7" />
              <path d="M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4z" />
            </svg>
          </button>
        </div>

        <nav className="mt-4 flex gap-6">
          {TABS.map((tab) => (
            <NavLink
              key={tab.label}
              to={tab.to}
              end={tab.end}
              className={({ isActive }) =>
                cx(
                  "-mb-px border-b-2 pb-2.5 text-sm transition-colors",
                  isActive
                    ? "border-primary font-medium text-primary"
                    : "border-transparent text-on-surface-variant hover:text-on-surface",
                )
              }
            >
              {tab.label}
            </NavLink>
          ))}
        </nav>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <Outlet context={{ employee }} />
      </div>
    </div>
  );
}

/* ── Tabs ────────────────────────────────────────────────────────────────── */

export function EmployeeOverviewTab() {
  const { employee } = useEmployeeContext();

  return (
    <div className="flex flex-col gap-5">
      <div className="grid grid-cols-3 gap-4">
        <StatCard
          label={employee.isHourly ? "Hourly Rate" : "Annual Salary"}
          value={formatCurrency(employee.isHourly ? employee.hourlyRate : employee.annualSalary)}
          caption={employee.isHourly ? "Overtime at 1.5x above 40 hrs/week" : "Divided across pay periods"}
        />
        <StatCard
          label="Pre-Tax 401(k)"
          value={formatPercent(employee.preTax401kPercent)}
          caption="Reduces income tax, not FICA"
        />
        <StatCard
          label="Per-Period Deductions"
          value={formatCurrency(
            employee.healthInsurancePerPeriod + employee.otherDeductionsPerPeriod,
          )}
          caption="Health (pre-tax) plus other (post-tax)"
        />
      </div>

      <Card className="flex flex-col gap-4 p-5">
        <h3 className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
          Employment
        </h3>

        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <DetailRow label="Employee ID" value={employee.employeeCode} mono />
          <DetailRow label="Pay Type" value={employee.isHourly ? "Hourly" : "Salaried"} />
          <DetailRow label="Job Title" value={employee.jobTitle || "—"} />
          <DetailRow label="Department" value={employee.department || "—"} />
          <DetailRow label="Hire Date" value={formatDate(employee.hireDate)} />
          <DetailRow
            label="Termination Date"
            value={employee.terminationDate ? formatDate(employee.terminationDate) : "—"}
          />
        </dl>
      </Card>
    </div>
  );
}

export function EmployeeCompensationTab() {
  const { employee } = useEmployeeContext();

  return (
    <div className="flex flex-col gap-5">
      <Card className="flex flex-col gap-4 p-5">
        <h3 className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
          Base Pay
        </h3>

        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <DetailRow label="Pay Type" value={employee.isHourly ? "Hourly" : "Salaried"} />
          <DetailRow
            label={employee.isHourly ? "Hourly Rate" : "Annual Salary"}
            value={formatCurrency(employee.isHourly ? employee.hourlyRate : employee.annualSalary)}
          />
          <DetailRow
            label="Default Hours / Period"
            value={`${employee.defaultHoursPerPeriod} hrs`}
          />
          <DetailRow
            label="Overtime Rate"
            value={
              employee.isHourly ? `${formatCurrency(employee.hourlyRate * 1.5)} / hr` : "Not applicable"
            }
          />
        </dl>
      </Card>

      <Card className="flex flex-col gap-4 p-5">
        <h3 className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
          Recurring Deductions
        </h3>

        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <DetailRow label="401(k) Contribution" value={formatPercent(employee.preTax401kPercent)} />
          <DetailRow
            label="Health Insurance"
            value={`${formatCurrency(employee.healthInsurancePerPeriod)} / period`}
          />
          <DetailRow
            label="Other Deductions"
            value={`${formatCurrency(employee.otherDeductionsPerPeriod)} / period`}
          />
        </dl>

        <p className="border-t border-outline-variant pt-3 text-[12px] leading-4 text-on-surface-variant/70">
          Health premiums are exempt from federal income tax and from Social Security and
          Medicare. 401(k) deferrals are exempt from income tax only — they remain fully
          FICA-taxable.
        </p>
      </Card>
    </div>
  );
}

export function EmployeeTaxesTab() {
  const { employee } = useEmployeeContext();

  return (
    <div className="flex flex-col gap-5">
      {!employee.w4OnFile && (
        <div
          role="alert"
          className="rounded-lg border border-tertiary/40 bg-tertiary/10 px-4 py-3 text-[13px] text-tertiary"
        >
          No signed Form W-4 is on file. Withholding is using defaults (Single, no adjustments),
          and pay runs including this employee will warn.
        </div>
      )}

      <Card className="flex flex-col gap-4 p-5">
        <h3 className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
          Federal — Form W-4
        </h3>

        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <DetailRow label="W-4 On File" value={employee.w4OnFile ? "Yes" : "No"} />
          <DetailRow
            label="Step 1(c) Filing Status"
            value={FILING_STATUS_LABELS[employee.filingStatus]}
          />
          <DetailRow
            label="Step 2(c) Multiple Jobs"
            value={employee.w4MultipleJobsChecked ? "Checked" : "Not checked"}
          />
          <DetailRow
            label="Step 3 Dependents & Credits"
            value={`${formatCurrency(employee.w4DependentsAndOtherCredits)} / yr`}
          />
          <DetailRow
            label="Step 4(a) Other Income"
            value={`${formatCurrency(employee.w4OtherIncome)} / yr`}
          />
          <DetailRow
            label="Step 4(b) Deductions"
            value={`${formatCurrency(employee.w4Deductions)} / yr`}
          />
          <DetailRow
            label="Step 4(c) Extra Withholding"
            value={`${formatCurrency(employee.w4ExtraWithholding)} / period`}
          />
        </dl>
      </Card>

      <Card className="flex flex-col gap-4 p-5">
        <h3 className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
          Illinois — Form IL-W-4
        </h3>

        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <DetailRow label="Line 1 Basic Allowances" value={String(employee.ilBasicAllowances)} />
          <DetailRow
            label="Line 2 Additional Allowances"
            value={String(employee.ilAdditionalAllowances)}
          />
        </dl>

        <p className="border-t border-outline-variant pt-3 text-[12px] leading-4 text-on-surface-variant/70">
          Illinois withholds a flat 4.95% after subtracting the annual allowance value spread
          across pay periods.
        </p>
      </Card>
    </div>
  );
}

function DetailRow({
  label,
  value,
  mono,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant/70">
        {label}
      </dt>
      <dd className={cx("text-sm text-on-surface", mono && "font-mono text-[13px]")}>{value}</dd>
    </div>
  );
}
