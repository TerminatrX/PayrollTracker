import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";

import {
  Button,
  Card,
  Checkbox,
  EmptyState,
  Field,
  NumberInput,
  SectionHeading,
  Select,
  TextInput,
  cx,
} from "@/components/ui/primitives";
import { formatDate } from "@/lib/format";
import { ApiError, type CompanySettings } from "@/types/api";
import {
  useBackups,
  useCompanySettings,
  useCreateBackup,
  useUpdateCompanySettings,
} from "./api";
import {
  PAY_FREQUENCY_OPTIONS,
  settingsFormSchema,
  type SettingsFormInput,
  type SettingsFormValues,
} from "./schema";

export function SettingsPage() {
  const { data: settings, isPending, isError, error, refetch } = useCompanySettings();

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="shrink-0 border-b border-outline-variant px-6 py-4">
        <SectionHeading
          title="Company Settings"
          description="Payroll configuration and employer tax rates. Federal and Illinois income tax are statutory and not configurable here."
        />
      </header>

      {isPending && <EmptyState title="Loading settings…" />}

      {isError && (
        <EmptyState
          title="Could not load settings"
          description={error instanceof Error ? error.message : undefined}
          action={
            <Button size="sm" onClick={() => void refetch()}>
              Retry
            </Button>
          }
        />
      )}

      {settings && <SettingsForm settings={settings} />}
    </div>
  );
}

function toFormValues(s: CompanySettings): SettingsFormInput {
  return {
    companyName: s.companyName,
    companyAddress: s.companyAddress,
    taxId: s.taxId,
    payPeriodsPerYear: s.payPeriodsPerYear,
    defaultHoursPerPeriod: s.defaultHoursPerPeriod,
    socialSecurityPercent: s.socialSecurityPercent,
    medicarePercent: s.medicarePercent,
    suiRatePercent: s.suiRatePercent,
    suiWageBase: s.suiWageBase,
    receivesFullFutaCredit: s.receivesFullFutaCredit,
  };
}

function SettingsForm({ settings }: { settings: CompanySettings }) {
  const update = useUpdateCompanySettings();
  const [saved, setSaved] = useState(false);

  const {
    register,
    handleSubmit,
    reset,
    setError,
    formState: { errors, isSubmitting, isDirty },
  } = useForm<SettingsFormInput, unknown, SettingsFormValues>({
    resolver: zodResolver(settingsFormSchema),
    defaultValues: toFormValues(settings),
    mode: "onBlur",
  });

  // Map backend field errors onto the form.
  useEffect(() => {
    if (!(update.error instanceof ApiError) || !update.error.validationErrors) {
      return;
    }
    for (const [field, messages] of Object.entries(update.error.validationErrors)) {
      if (messages[0]) {
        setError(field as keyof SettingsFormInput, { type: "server", message: messages[0] });
      }
    }
  }, [update.error, setError]);

  const generalError =
    update.error instanceof ApiError && !update.error.isValidation ? update.error.message : null;

  return (
    <form
      onSubmit={handleSubmit(async (values) => {
        const updated = await update.mutateAsync(values);
        reset(toFormValues(updated));
        setSaved(true);
        setTimeout(() => setSaved(false), 2500);
      })}
      className="flex min-h-0 flex-1 flex-col"
      noValidate
    >
      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        <div className="mx-auto flex max-w-3xl flex-col gap-7">
          {generalError && (
            <div role="alert" className="rounded-lg border border-error/40 bg-error/10 px-4 py-3 text-[13px] text-error">
              {generalError}
            </div>
          )}

          {!settings.suiConfigured && (
            <div role="alert" className="rounded-lg border border-tertiary/40 bg-tertiary/10 px-4 py-3 text-[13px] text-tertiary">
              State unemployment (SUI) is not configured, so employer unemployment cost is
              understated on every pay run. Enter the rate and taxable wage base from your annual
              IDES rate notice below.
            </div>
          )}

          {/* ── Company Profile ─────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading title="Company Profile" />

            <Field label="Company Name" htmlFor="companyName" required error={errors.companyName?.message}>
              <TextInput id="companyName" invalid={!!errors.companyName} {...register("companyName")} />
            </Field>

            <div className="grid grid-cols-2 gap-4">
              <Field label="Address" htmlFor="companyAddress" error={errors.companyAddress?.message}>
                <TextInput id="companyAddress" placeholder="Street, City, IL" {...register("companyAddress")} />
              </Field>
              <Field label="Federal EIN / Tax ID" htmlFor="taxId" error={errors.taxId?.message}>
                <TextInput id="taxId" placeholder="00-0000000" {...register("taxId")} />
              </Field>
            </div>
          </section>

          {/* ── Payroll Config ──────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Payroll Configuration"
              description="Pay frequency determines how salaries are divided and how overtime workweeks are counted."
            />

            <div className="grid grid-cols-2 gap-4">
              <Field label="Pay Frequency" htmlFor="payPeriodsPerYear" required error={errors.payPeriodsPerYear?.message}>
                <Select id="payPeriodsPerYear" invalid={!!errors.payPeriodsPerYear} {...register("payPeriodsPerYear")}>
                  {PAY_FREQUENCY_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>
                      {opt.label}
                    </option>
                  ))}
                </Select>
              </Field>

              <Field
                label="Default Hours / Period"
                htmlFor="defaultHoursPerPeriod"
                error={errors.defaultHoursPerPeriod?.message}
                hint="Default hours for new employees; each can be overridden per employee."
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

          {/* ── Tax Config ──────────────────────────────────────────── */}
          <section className="flex flex-col gap-4">
            <SectionHeading
              title="Employer Tax Rates"
              description="FICA is fixed by statute (shown for reference). SUI is employer-specific — enter it from your IDES rate notice."
            />

            <div className="grid grid-cols-2 gap-4">
              <Field label="Social Security %" htmlFor="socialSecurityPercent" error={errors.socialSecurityPercent?.message}>
                <NumberInput id="socialSecurityPercent" step="0.01" min="0" invalid={!!errors.socialSecurityPercent} {...register("socialSecurityPercent")} />
              </Field>
              <Field label="Medicare %" htmlFor="medicarePercent" error={errors.medicarePercent?.message}>
                <NumberInput id="medicarePercent" step="0.01" min="0" invalid={!!errors.medicarePercent} {...register("medicarePercent")} />
              </Field>

              <Field
                label="Illinois SUI Rate %"
                htmlFor="suiRatePercent"
                error={errors.suiRatePercent?.message}
                hint="From your annual IDES rate notice."
              >
                <NumberInput id="suiRatePercent" step="0.01" min="0" invalid={!!errors.suiRatePercent} {...register("suiRatePercent")} />
              </Field>
              <Field
                label="SUI Wage Base $"
                htmlFor="suiWageBase"
                error={errors.suiWageBase?.message}
                hint="Per employee, per year."
              >
                <NumberInput id="suiWageBase" step="1" min="0" invalid={!!errors.suiWageBase} {...register("suiWageBase")} />
              </Field>
            </div>

            <Card className="p-4">
              <Checkbox
                label="Receives the full FUTA state credit"
                description="Gives the effective 0.6% FUTA rate. Unchecking computes FUTA at the full 6.0% — only for a credit-reduction state."
                {...register("receivesFullFutaCredit")}
              />
            </Card>
          </section>

          {/* ── Data Management ─────────────────────────────────────── */}
          <DataManagement />
        </div>
      </div>

      <div className="flex shrink-0 items-center justify-between border-t border-outline-variant bg-surface-lowest px-6 py-3">
        <span className={cx("text-[12px]", saved ? "text-secondary" : "text-on-surface-variant")}>
          {saved ? "Settings saved" : isDirty ? "Unsaved changes" : "No changes"}
        </span>
        <Button type="submit" variant="primary" disabled={isSubmitting || !isDirty}>
          {isSubmitting ? "Saving…" : "Save Changes"}
        </Button>
      </div>
    </form>
  );
}

function DataManagement() {
  const { data: backups } = useBackups();
  const createBackup = useCreateBackup();

  return (
    <section className="flex flex-col gap-4">
      <SectionHeading
        title="Data Management"
        description="A backup is a consistent snapshot of the payroll database, safe to take while the app is running."
      />

      <Card className="flex items-center justify-between p-4">
        <div className="flex flex-col gap-0.5">
          <span className="text-sm font-medium text-on-surface">Create Backup</span>
          <span className="text-[12px] text-on-surface-variant/70">
            {createBackup.isSuccess
              ? `Saved ${createBackup.data.backup.fileName}`
              : "Snapshot the current database into the Backups folder."}
          </span>
        </div>
        {/* type="button" so it never submits the settings form. */}
        <Button type="button" size="sm" onClick={() => createBackup.mutate()} disabled={createBackup.isPending}>
          {createBackup.isPending ? "Backing up…" : "Create Backup"}
        </Button>
      </Card>

      {createBackup.error instanceof Error && (
        <p role="alert" className="text-[12px] text-error">
          {createBackup.error.message}
        </p>
      )}

      {backups && backups.length > 0 && (
        <Card className="flex flex-col divide-y divide-outline-variant/60">
          {backups.slice(0, 6).map((backup) => (
            <div key={backup.fullPath} className="flex items-center justify-between px-4 py-2.5">
              <span className="font-mono text-[12px] text-on-surface">{backup.fileName}</span>
              <span className="tabular text-[12px] text-on-surface-variant/70">
                {formatDate(backup.createdUtc)} · {(backup.sizeBytes / 1024).toFixed(0)} KB
              </span>
            </div>
          ))}
        </Card>
      )}
    </section>
  );
}
