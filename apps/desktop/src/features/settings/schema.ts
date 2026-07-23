import { z } from "zod";

/**
 * Mirrors the backend SettingsValidator for immediate feedback. The backend re-validates and
 * is authoritative.
 */
export const settingsFormSchema = z
  .object({
    companyName: z.string().trim().min(1, "Company name is required.").max(200),
    companyAddress: z.string().trim().max(500).optional().or(z.literal("")),
    taxId: z.string().trim().max(50).optional().or(z.literal("")),

    // Only frequencies the pay-period calculator understands; anything else silently
    // defaults to bi-weekly on the backend.
    payPeriodsPerYear: z.coerce.number().refine((v) => [52, 26, 24, 12].includes(v), {
      message: "Choose weekly, bi-weekly, semi-monthly, or monthly.",
    }),
    defaultHoursPerPeriod: z.coerce.number().int().min(0).max(400),

    socialSecurityPercent: z.coerce.number().min(0).max(20),
    medicarePercent: z.coerce.number().min(0).max(20),

    suiRatePercent: z.coerce.number().min(0, "Cannot be negative.").max(15, "Cannot exceed 15%."),
    suiWageBase: z.coerce.number().min(0, "Cannot be negative."),

    receivesFullFutaCredit: z.boolean(),
  })
  .refine((v) => !(v.suiRatePercent > 0 && v.suiWageBase <= 0), {
    message: "A wage base is required when a SUI rate is set.",
    path: ["suiWageBase"],
  })
  .refine((v) => !(v.suiWageBase > 0 && v.suiRatePercent <= 0), {
    message: "A rate is required when a SUI wage base is set.",
    path: ["suiRatePercent"],
  });

export type SettingsFormInput = z.input<typeof settingsFormSchema>;
export type SettingsFormValues = z.output<typeof settingsFormSchema>;

export const PAY_FREQUENCY_OPTIONS = [
  { value: 52, label: "Weekly (52/year)" },
  { value: 26, label: "Bi-weekly (26/year)" },
  { value: 24, label: "Semi-monthly (24/year)" },
  { value: 12, label: "Monthly (12/year)" },
] as const;
