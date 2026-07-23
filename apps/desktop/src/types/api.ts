/**
 * Types mirroring the sidecar contracts in
 * services/PayrollManager.Backend/Contracts/.
 *
 * MONEY: every `number` below that represents currency arrives as a JSON number and lands in
 * a JavaScript float64. That is exact for realistic payroll amounts, but the frontend must
 * NEVER do payroll arithmetic on these values - summing, prorating, or deriving taxes here
 * would drift from the decimal math the backend performs and is authoritative for. Display
 * them; send inputs back; let the backend compute.
 */

export type FilingStatus =
  | "single"
  | "marriedFilingJointly"
  | "marriedFilingSeparately"
  | "headOfHousehold";

export const FILING_STATUS_LABELS: Record<FilingStatus, string> = {
  single: "Single",
  marriedFilingJointly: "Married filing jointly",
  marriedFilingSeparately: "Married filing separately",
  headOfHousehold: "Head of household",
};

export interface Employee {
  id: number;
  firstName: string;
  lastName: string;
  fullName: string;
  employeeCode: string;
  isActive: boolean;
  isHourly: boolean;
  annualSalary: number;
  hourlyRate: number;
  defaultHoursPerPeriod: number;
  preTax401kPercent: number;
  healthInsurancePerPeriod: number;
  otherDeductionsPerPeriod: number;
  jobTitle?: string | null;
  department?: string | null;
  hireDate?: string | null;
  terminationDate?: string | null;

  w4OnFile: boolean;
  filingStatus: FilingStatus;
  w4MultipleJobsChecked: boolean;
  w4DependentsAndOtherCredits: number;
  w4OtherIncome: number;
  w4Deductions: number;
  w4ExtraWithholding: number;

  ilBasicAllowances: number;
  ilAdditionalAllowances: number;
}

/** Fields the frontend may set. Mirrors EmployeeInput on the backend. */
export interface EmployeeInput {
  firstName: string;
  lastName: string;
  isActive: boolean;
  isHourly: boolean;
  annualSalary: number;
  hourlyRate: number;
  defaultHoursPerPeriod: number;
  preTax401kPercent: number;
  healthInsurancePerPeriod: number;
  otherDeductionsPerPeriod: number;
  jobTitle?: string | null;
  department?: string | null;
  hireDate?: string | null;
  terminationDate?: string | null;

  w4OnFile: boolean;
  filingStatus: FilingStatus;
  w4MultipleJobsChecked: boolean;
  w4DependentsAndOtherCredits: number;
  w4OtherIncome: number;
  w4Deductions: number;
  w4ExtraWithholding: number;

  ilBasicAllowances: number;
  ilAdditionalAllowances: number;
}

export interface HealthResponse {
  status: string;
  engineVersion: string;
  databasePath: string;
  migratedFromLegacyLocation: boolean;
  backupPath?: string | null;
  appliedMigrations: string[];
}

/** Error codes defined once in the backend's ErrorCodes class. */
export type ApiErrorCode =
  | "bad_request"
  | "unknown_method"
  | "validation_failed"
  | "not_found"
  | "conflict"
  | "business_rule_violation"
  | "internal_error"
  | "sidecar_unavailable";

export interface ApiErrorShape {
  code: ApiErrorCode;
  message: string;
  retryable: boolean;
  /** Keyed by camelCase field name, matching the input field it belongs to. */
  validationErrors?: Record<string, string[]>;
}

/**
 * A failure returned by the backend, carried as a real Error so React Query treats it as one.
 */
export class ApiError extends Error {
  readonly code: ApiErrorCode;
  readonly retryable: boolean;
  readonly validationErrors?: Record<string, string[]>;

  constructor(shape: ApiErrorShape) {
    super(shape.message);
    this.name = "ApiError";
    this.code = shape.code;
    this.retryable = shape.retryable;
    this.validationErrors = shape.validationErrors;
  }

  get isValidation(): boolean {
    return this.code === "validation_failed";
  }
}
