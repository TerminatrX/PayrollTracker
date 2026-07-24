import { z } from "zod";

/**
 * Client-side validation, for immediate feedback while typing.
 *
 * This deliberately MIRRORS EmployeeValidator on the backend rather than replacing it. The
 * backend re-validates every request and is the authority - these rules exist only so the
 * user does not have to round-trip to learn a name is missing. Where the two ever disagree,
 * the backend wins and its field errors are merged onto the form.
 */
export const employeeFormSchema = z
  .object({
    firstName: z.string().trim().min(1, "First name is required.").max(100),
    lastName: z.string().trim().min(1, "Last name is required.").max(100),
    isActive: z.boolean(),
    isHourly: z.boolean(),

    annualSalary: z.coerce.number().min(0).max(100_000_000),
    hourlyRate: z.coerce.number().min(0).max(10_000),
    defaultHoursPerPeriod: z.coerce
      .number()
      .int("Whole hours only.")
      .min(0)
      .max(400, "Must be 400 or fewer."),

    preTax401kPercent: z.coerce
      .number()
      .min(0, "Cannot be negative.")
      .max(100, "Cannot exceed 100%."),
    healthInsurancePerPeriod: z.coerce.number().min(0, "Cannot be negative."),
    otherDeductionsPerPeriod: z.coerce.number().min(0, "Cannot be negative."),

    jobTitle: z.string().trim().max(120).optional().or(z.literal("")),
    department: z.string().trim().max(120).optional().or(z.literal("")),
    hireDate: z.string().optional().or(z.literal("")),
    terminationDate: z.string().optional().or(z.literal("")),

    w4OnFile: z.boolean(),
    filingStatus: z.enum([
      "single",
      "marriedFilingJointly",
      "marriedFilingSeparately",
      "headOfHousehold",
    ]),
    w4MultipleJobsChecked: z.boolean(),
    w4DependentsAndOtherCredits: z.coerce.number().min(0, "Cannot be negative."),
    w4OtherIncome: z.coerce.number().min(0, "Cannot be negative."),
    w4Deductions: z.coerce.number().min(0, "Cannot be negative."),
    w4ExtraWithholding: z.coerce
      .number()
      .min(0, "Withholding can only be added, never reduced."),

    ilBasicAllowances: z.coerce.number().int().min(0, "Cannot be negative."),
    ilAdditionalAllowances: z.coerce.number().int().min(0, "Cannot be negative."),

    streetAddress: z.string().trim().max(200).optional().or(z.literal("")),
    city: z.string().trim().max(100).optional().or(z.literal("")),
    state: z.string().trim().max(2, "Two-letter code (e.g. IL).").optional().or(z.literal("")),
    postalCode: z.string().trim().max(10).optional().or(z.literal("")),

    // Write-only. Blank leaves any existing SSN on file. Accept 9 digits with or without dashes.
    ssn: z
      .string()
      .optional()
      .or(z.literal(""))
      .refine((v) => !v || /^\d{9}$/.test(v.replace(/[\s-]/g, "")), {
        message: "SSN must be nine digits (e.g. 123-45-6789).",
      }),
  })
  // Compensation must match pay type, or a pay run silently computes zero gross.
  .refine((v) => !v.isHourly || v.hourlyRate > 0, {
    message: "An hourly employee needs an hourly rate above zero.",
    path: ["hourlyRate"],
  })
  .refine((v) => v.isHourly || v.annualSalary > 0, {
    message: "A salaried employee needs an annual salary above zero.",
    path: ["annualSalary"],
  })
  .refine(
    (v) => !v.hireDate || !v.terminationDate || v.terminationDate >= v.hireDate,
    {
      message: "Termination date cannot precede the hire date.",
      path: ["terminationDate"],
    },
  )
  .refine(
    (v) =>
      !v.isActive ||
      !v.terminationDate ||
      v.terminationDate >= new Date().toISOString().slice(0, 10),
    {
      message: "An employee with a past termination date cannot be active.",
      path: ["isActive"],
    },
  );

/**
 * `z.coerce` accepts unknown input (a form field yields a string) and produces a number, so
 * the schema's input and output types differ. react-hook-form needs both: the form state is
 * typed by the input, the validated submit payload by the output.
 */
export type EmployeeFormInput = z.input<typeof employeeFormSchema>;
export type EmployeeFormValues = z.output<typeof employeeFormSchema>;

export const EMPTY_EMPLOYEE_FORM: EmployeeFormInput = {
  firstName: "",
  lastName: "",
  isActive: true,
  isHourly: false,
  annualSalary: 0,
  hourlyRate: 0,
  defaultHoursPerPeriod: 80,
  preTax401kPercent: 0,
  healthInsurancePerPeriod: 0,
  otherDeductionsPerPeriod: 0,
  jobTitle: "",
  department: "",
  hireDate: "",
  terminationDate: "",
  w4OnFile: false,
  filingStatus: "single",
  w4MultipleJobsChecked: false,
  w4DependentsAndOtherCredits: 0,
  w4OtherIncome: 0,
  w4Deductions: 0,
  w4ExtraWithholding: 0,
  ilBasicAllowances: 0,
  ilAdditionalAllowances: 0,
  streetAddress: "",
  city: "",
  state: "",
  postalCode: "",
  ssn: "",
};
