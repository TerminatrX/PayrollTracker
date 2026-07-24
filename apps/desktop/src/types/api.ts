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

  streetAddress?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  /** Last four digits only; the full SSN is never sent to the frontend. */
  ssnLast4?: string | null;
  ssnOnFile: boolean;

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

  streetAddress?: string | null;
  city?: string | null;
  state?: string | null;
  postalCode?: string | null;
  /** Write-only full SSN; sent only when set/changed, never returned. Blank = leave unchanged. */
  ssn?: string | null;

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

export interface CompanySettings {
  companyName: string;
  companyAddress: string;
  taxId: string;
  payPeriodsPerYear: number;
  defaultHoursPerPeriod: number;
  payFrequencyLabel: string;
  socialSecurityPercent: number;
  medicarePercent: number;
  suiRatePercent: number;
  suiWageBase: number;
  receivesFullFutaCredit: boolean;
  suiConfigured: boolean;
}

export interface CompanySettingsInput {
  companyName: string;
  companyAddress: string;
  taxId: string;
  payPeriodsPerYear: number;
  defaultHoursPerPeriod: number;
  socialSecurityPercent: number;
  medicarePercent: number;
  suiRatePercent: number;
  suiWageBase: number;
  receivesFullFutaCredit: boolean;
}

export interface Backup {
  fileName: string;
  fullPath: string;
  sizeBytes: number;
  createdUtc: string;
}

export type PayRunStatus = "Draft" | "Calculated" | "Posted" | "Voided";

export interface PayRunSummary {
  id: number;
  periodStart: string;
  periodEnd: string;
  payDate: string;
  status: PayRunStatus;
  employeeCount: number;
  grossPay: number;
  netPay: number;
  employeeTaxes: number;
  employerTaxes: number;
  totalEmployerCost: number;
  postedAtUtc?: string | null;
  voidedAtUtc?: string | null;
  voidReason?: string | null;
  engineVersion?: string | null;
}

export interface PayRunWarning {
  code: string;
  message: string;
  blocksPosting: boolean;
  employeeId?: number | null;
}

export interface PayRunLine {
  employeeId: number;
  employeeName: string;
  hoursWorked: number;
  grossPay: number;
  preTaxDeductions: number;
  totalTaxes: number;
  postTaxDeductions: number;
  netPay: number;
  employerTaxes: number;
}

export interface PayRunTotals {
  employeeCount: number;
  grossPay: number;
  preTaxDeductions: number;
  employeeTaxes: number;
  postTaxDeductions: number;
  netPay: number;
  employerTaxes: number;
  totalEmployerCost: number;
}

export interface PayRunPreview {
  periodStart: string;
  periodEnd: string;
  payDate: string;
  lines: PayRunLine[];
  totals: PayRunTotals;
  warnings: PayRunWarning[];
  calculationHash: string;
  canPost: boolean;
}

export interface PayRunDraftInput {
  periodStart: string;
  periodEnd: string;
  payDate: string;
}

export interface PayRunEmployeeInput {
  employeeId: number;
  regularHours: number;
  overtimeHours: number;
  bonusAmount: number;
  commissionAmount: number;
  bonusDescription?: string | null;
  commissionDescription?: string | null;
}

export interface PayRunEmployeeSuggestion {
  employeeId: number;
  fullName: string;
  jobTitle?: string | null;
  department?: string | null;
  isHourly: boolean;
  hourlyRate: number;
  annualSalary: number;
  salaryPerPeriod: number;
  suggestedRegularHours: number;
  suggestedOvertimeHours: number;
  w4OnFile: boolean;
}

export interface SuggestPayRunResponse {
  draft: PayRunDraftInput;
  employees: PayRunEmployeeSuggestion[];
}

export interface PayStubLine {
  payStubId: number;
  employeeId: number;
  employeeName: string;
  hoursWorked: number;
  grossPay: number;
  totalTaxes: number;
  postTaxDeductions: number;
  netPay: number;
  employerTaxes: number;
}

export interface PayRunDetail {
  summary: PayRunSummary;
  stubs: PayStubLine[];
}

export interface CompanyTotals {
  employeeCount: number;
  payStubCount: number;
  grossPay: number;
  federalTax: number;
  stateTax: number;
  socialSecurity: number;
  medicare: number;
  totalTaxes: number;
  preTax401k: number;
  postTaxDeductions: number;
  netPay: number;
  employerTaxes: number;
  totalEmployerCost: number;
}

export interface EmployeeTotals {
  employeeId: number;
  employeeName: string;
  grossPay: number;
  totalTaxes: number;
  preTax401k: number;
  postTaxDeductions: number;
  netPay: number;
  payStubCount: number;
}

export interface DashboardData {
  year: number;
  companyYtd: CompanyTotals;
  activeEmployeeCount: number;
  postedRunCount: number;
  lastPayRun?: PayRunSummary | null;
  nextPayDate?: string | null;
  suiConfigured: boolean;
}

export interface PayrollReport {
  startDate: string;
  endDate: string;
  company: CompanyTotals;
  employees: EmployeeTotals[];
}

export interface AuditEntry {
  id: number;
  timestampUtc: string;
  action: string;
  entityType: string;
  entityId?: number | null;
  oldValue?: string | null;
  newValue?: string | null;
  performedBy?: string | null;
  notes?: string | null;
}

export interface PayStubEarning {
  description: string;
  hours: number;
  rate: number;
  amount: number;
}

export interface PayStubDeduction {
  description: string;
  amount: number;
  isPreTax: boolean;
}

export interface PayStubTax {
  label: string;
  current: number;
  ytd: number;
}

export interface PayStubStatement {
  payStubId: number;
  employeeId: number;
  employeeName: string;
  employeeCode: string;
  jobTitle?: string | null;
  department?: string | null;
  maskedSsn?: string | null;
  addressLine1?: string | null;
  addressLine2?: string | null;

  companyName: string;
  companyAddress?: string | null;
  companyTaxId?: string | null;

  periodStart: string;
  periodEnd: string;
  payDate: string;
  paySchedule: string;
  checkNumber: string;

  earnings: PayStubEarning[];
  deductions: PayStubDeduction[];
  taxes: PayStubTax[];

  grossPay: number;
  preTaxDeductions: number;
  totalTaxes: number;
  postTaxDeductions: number;
  netPay: number;

  ytdGross: number;
  ytdPreTaxDeductions: number;
  ytdTotalTaxes: number;
  ytdPostTaxDeductions: number;
  ytdNet: number;
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
