import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import {
  Button,
  Card,
  Checkbox,
  Field,
  NumberInput,
  SectionHeading,
  Select,
  TextInput,
} from "@/components/ui/primitives";
import { ApiError, FILING_STATUS_LABELS, type FilingStatus } from "@/types/api";
import {
  employeeFormSchema,
  type EmployeeFormInput,
  type EmployeeFormValues,
} from "./schema";

interface Props {
  defaultValues: EmployeeFormInput;
  submitLabel: string;
  onSubmit: (values: EmployeeFormValues) => Promise<unknown>;
  onCancel: () => void;
  error?: unknown;
  /** True when an SSN is already stored, so the field shows it as on file. */
  ssnOnFile?: boolean;
}

export function EmployeeForm({
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
  error,
  ssnOnFile = false,
}: Props) {
  const {
    register,
    handleSubmit,
    watch,
    setError,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<EmployeeFormInput, unknown, EmployeeFormValues>({
    resolver: zodResolver(employeeFormSchema),
    defaultValues,
    mode: "onBlur",
  });

  const isHourly = watch("isHourly");

  // Merge backend field errors onto the form. The backend is authoritative, so anything it
  // rejects must be shown against the field it belongs to rather than as an opaque banner.
  // This is why the backend keys validationErrors in camelCase matching these field names.
  useEffect(() => {
    if (!(error instanceof ApiError) || !error.validationErrors) {
      return;
    }

    for (const [field, messages] of Object.entries(error.validationErrors)) {
      const message = messages[0];
      if (message) {
        setError(field as keyof EmployeeFormInput, { type: "server", message });
      }
    }
  }, [error, setError]);

  const generalError =
    error instanceof ApiError && !error.isValidation ? error.message : null;

  return (
    <form
      onSubmit={handleSubmit(onSubmit)}
      className="flex min-h-0 flex-1 flex-col"
      noValidate
    >
      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <div className="mx-auto flex max-w-3xl flex-col gap-7">
          {generalError && (
            <div
              role="alert"
              className="rounded-lg border border-error/40 bg-error/10 px-4 py-3 text-[13px] text-error"
            >
              {generalError}
            </div>
          )}

          {/* ── Personal ───────────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Personal Information"
              description="Legal name as it should appear on pay stubs and tax filings."
            />

            <div className="grid grid-cols-2 gap-4">
              <Field label="Legal First Name" htmlFor="firstName" required error={errors.firstName?.message}>
                <TextInput
                  id="firstName"
                  placeholder="e.g. Jonathan"
                  invalid={!!errors.firstName}
                  {...register("firstName")}
                />
              </Field>

              <Field label="Legal Last Name" htmlFor="lastName" required error={errors.lastName?.message}>
                <TextInput
                  id="lastName"
                  placeholder="e.g. Smith"
                  invalid={!!errors.lastName}
                  {...register("lastName")}
                />
              </Field>
            </div>
          </section>

          {/* ── Employment ─────────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading title="Employment Details" />

            <div className="grid grid-cols-2 gap-4">
              <Field label="Job Title" htmlFor="jobTitle" error={errors.jobTitle?.message}>
                <TextInput id="jobTitle" placeholder="e.g. Senior Analyst" {...register("jobTitle")} />
              </Field>

              <Field label="Department" htmlFor="department" error={errors.department?.message}>
                <TextInput id="department" placeholder="e.g. Engineering" {...register("department")} />
              </Field>

              <Field label="Hire Date" htmlFor="hireDate" error={errors.hireDate?.message}>
                <TextInput id="hireDate" type="date" {...register("hireDate")} />
              </Field>

              <Field
                label="Termination Date"
                htmlFor="terminationDate"
                error={errors.terminationDate?.message}
                hint="Leave empty for current employees."
              >
                <TextInput id="terminationDate" type="date" {...register("terminationDate")} />
              </Field>
            </div>

            <Card className="p-4">
              <Checkbox
                label="Active employee"
                description="Only active employees are offered when building a pay run."
                {...register("isActive")}
              />
              {errors.isActive && (
                <p role="alert" className="mt-2 text-[12px] text-error">
                  {errors.isActive.message}
                </p>
              )}
            </Card>
          </section>

          {/* ── Address & Identity ─────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Address & Identity"
              description="Printed on the employee's pay stub. The SSN is stored encrypted; only the last four digits are ever shown."
            />

            <Field label="Street Address" htmlFor="streetAddress" error={errors.streetAddress?.message}>
              <TextInput id="streetAddress" placeholder="500 W Madison St" {...register("streetAddress")} />
            </Field>

            <div className="grid grid-cols-3 gap-4">
              <Field label="City" htmlFor="city" error={errors.city?.message}>
                <TextInput id="city" placeholder="Chicago" {...register("city")} />
              </Field>
              <Field label="State" htmlFor="state" error={errors.state?.message}>
                <TextInput id="state" placeholder="IL" maxLength={2} {...register("state")} />
              </Field>
              <Field label="ZIP Code" htmlFor="postalCode" error={errors.postalCode?.message}>
                <TextInput id="postalCode" placeholder="60661" {...register("postalCode")} />
              </Field>
            </div>

            <Field
              label="Social Security Number"
              htmlFor="ssn"
              error={errors.ssn?.message}
              hint={
                ssnOnFile
                  ? "An SSN is on file (stored encrypted). Type a new one to replace it, or leave blank to keep it."
                  : "Stored encrypted. Only the last four digits appear on pay stubs."
              }
            >
              <TextInput
                id="ssn"
                inputMode="numeric"
                autoComplete="off"
                placeholder={ssnOnFile ? "•••-••-•••• (on file)" : "123-45-6789"}
                invalid={!!errors.ssn}
                {...register("ssn")}
              />
            </Field>
          </section>

          {/* ── Compensation ───────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Compensation"
              description="Pay type determines which figure is used; the other is cleared on save."
            />

            <Card className="p-4">
              <Checkbox
                label="Paid hourly"
                description="Unchecked means salaried. Hourly employees accrue overtime above 40 hours in a workweek."
                {...register("isHourly")}
              />
            </Card>

            <div className="grid grid-cols-2 gap-4">
              {isHourly ? (
                <Field
                  label="Hourly Rate"
                  htmlFor="hourlyRate"
                  required
                  error={errors.hourlyRate?.message}
                  hint="Overtime is paid at 1.5x this rate."
                >
                  <NumberInput
                    id="hourlyRate"
                    step="0.01"
                    min="0"
                    invalid={!!errors.hourlyRate}
                    {...register("hourlyRate")}
                  />
                </Field>
              ) : (
                <Field
                  label="Annual Salary"
                  htmlFor="annualSalary"
                  required
                  error={errors.annualSalary?.message}
                  hint="Divided evenly across the company's pay periods."
                >
                  <NumberInput
                    id="annualSalary"
                    step="0.01"
                    min="0"
                    invalid={!!errors.annualSalary}
                    {...register("annualSalary")}
                  />
                </Field>
              )}

              <Field
                label="Default Hours / Period"
                htmlFor="defaultHoursPerPeriod"
                error={errors.defaultHoursPerPeriod?.message}
                hint="80 for a standard two-week period."
              >
                <NumberInput
                  id="defaultHoursPerPeriod"
                  step="1"
                  min="0"
                  invalid={!!errors.defaultHoursPerPeriod}
                  {...register("defaultHoursPerPeriod")}
                />
              </Field>
            </div>
          </section>

          {/* ── Deductions ─────────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Deductions"
              description="Health premiums are pre-tax for both income tax and FICA. 401(k) deferrals are pre-tax for income tax only."
            />

            <div className="grid grid-cols-3 gap-4">
              <Field
                label="401(k) %"
                htmlFor="preTax401kPercent"
                error={errors.preTax401kPercent?.message}
              >
                <NumberInput
                  id="preTax401kPercent"
                  step="0.1"
                  min="0"
                  max="100"
                  invalid={!!errors.preTax401kPercent}
                  {...register("preTax401kPercent")}
                />
              </Field>

              <Field
                label="Health / Period"
                htmlFor="healthInsurancePerPeriod"
                error={errors.healthInsurancePerPeriod?.message}
              >
                <NumberInput
                  id="healthInsurancePerPeriod"
                  step="0.01"
                  min="0"
                  invalid={!!errors.healthInsurancePerPeriod}
                  {...register("healthInsurancePerPeriod")}
                />
              </Field>

              <Field
                label="Other / Period"
                htmlFor="otherDeductionsPerPeriod"
                error={errors.otherDeductionsPerPeriod?.message}
                hint="Post-tax."
              >
                <NumberInput
                  id="otherDeductionsPerPeriod"
                  step="0.01"
                  min="0"
                  invalid={!!errors.otherDeductionsPerPeriod}
                  {...register("otherDeductionsPerPeriod")}
                />
              </Field>
            </div>
          </section>

          {/* ── Federal W-4 ────────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Federal Form W-4"
              description="Drives federal withholding via the IRS Publication 15-T percentage method."
            />

            <Card className="p-4">
              <Checkbox
                label="Signed Form W-4 on file"
                description="An employer is required to hold a signed W-4. Until this is checked, withholding uses defaults and pay runs will warn."
                {...register("w4OnFile")}
              />
            </Card>

            <div className="grid grid-cols-2 gap-4">
              <Field label="Step 1(c) — Filing Status" htmlFor="filingStatus">
                <Select id="filingStatus" {...register("filingStatus")}>
                  {(Object.keys(FILING_STATUS_LABELS) as FilingStatus[]).map((status) => (
                    <option key={status} value={status}>
                      {FILING_STATUS_LABELS[status]}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field
                label="Step 3 — Dependents & Credits"
                htmlFor="w4DependentsAndOtherCredits"
                error={errors.w4DependentsAndOtherCredits?.message}
                hint="Annual dollar amount."
              >
                <NumberInput
                  id="w4DependentsAndOtherCredits"
                  step="0.01"
                  min="0"
                  invalid={!!errors.w4DependentsAndOtherCredits}
                  {...register("w4DependentsAndOtherCredits")}
                />
              </Field>

              <Field
                label="Step 4(a) — Other Income"
                htmlFor="w4OtherIncome"
                error={errors.w4OtherIncome?.message}
                hint="Annual, not from jobs."
              >
                <NumberInput
                  id="w4OtherIncome"
                  step="0.01"
                  min="0"
                  invalid={!!errors.w4OtherIncome}
                  {...register("w4OtherIncome")}
                />
              </Field>

              <Field
                label="Step 4(b) — Deductions"
                htmlFor="w4Deductions"
                error={errors.w4Deductions?.message}
                hint="Annual, beyond the standard deduction."
              >
                <NumberInput
                  id="w4Deductions"
                  step="0.01"
                  min="0"
                  invalid={!!errors.w4Deductions}
                  {...register("w4Deductions")}
                />
              </Field>

              <Field
                label="Step 4(c) — Extra Withholding"
                htmlFor="w4ExtraWithholding"
                error={errors.w4ExtraWithholding?.message}
                hint="Per pay period."
              >
                <NumberInput
                  id="w4ExtraWithholding"
                  step="0.01"
                  min="0"
                  invalid={!!errors.w4ExtraWithholding}
                  {...register("w4ExtraWithholding")}
                />
              </Field>
            </div>

            <Card className="p-4">
              <Checkbox
                label="Step 2(c) — Multiple jobs / spouse works"
                description="Uses the higher withholding schedule. A single-job table under-withholds a two-earner household."
                {...register("w4MultipleJobsChecked")}
              />
            </Card>
          </section>

          {/* ── Illinois IL-W-4 ────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Illinois Form IL-W-4"
              description="Illinois withholds a flat 4.95% after allowances (Booklet IL-700-T)."
            />

            <div className="grid grid-cols-2 gap-4">
              <Field
                label="Line 1 — Basic Allowances"
                htmlFor="ilBasicAllowances"
                error={errors.ilBasicAllowances?.message}
                hint="Self, spouse, and dependents."
              >
                <NumberInput
                  id="ilBasicAllowances"
                  step="1"
                  min="0"
                  invalid={!!errors.ilBasicAllowances}
                  {...register("ilBasicAllowances")}
                />
              </Field>

              <Field
                label="Line 2 — Additional Allowances"
                htmlFor="ilAdditionalAllowances"
                error={errors.ilAdditionalAllowances?.message}
                hint="Age 65 or older, and/or legally blind."
              >
                <NumberInput
                  id="ilAdditionalAllowances"
                  step="1"
                  min="0"
                  invalid={!!errors.ilAdditionalAllowances}
                  {...register("ilAdditionalAllowances")}
                />
              </Field>
            </div>
          </section>
        </div>
      </div>

      {/* Sticky action bar, matching the mock's footer. */}
      <div className="flex shrink-0 items-center justify-between border-t border-outline-variant bg-surface-lowest px-6 py-3">
        <span className="text-[12px] text-on-surface-variant">
          {isDirty ? "Unsaved changes" : "No changes"}
        </span>

        <div className="flex items-center gap-2">
          <Button type="button" variant="secondary" onClick={onCancel} disabled={isSubmitting}>
            Cancel
          </Button>
          <Button type="submit" variant="primary" disabled={isSubmitting}>
            {isSubmitting ? "Saving…" : submitLabel}
          </Button>
        </div>
      </div>
    </form>
  );
}
