import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { callBackend } from "@/lib/ipc";
import { fromDateInputValue } from "@/lib/format";
import type { Employee, EmployeeInput } from "@/types/api";
import type { EmployeeFormValues } from "./schema";

export const employeeKeys = {
  all: ["employees"] as const,
  list: (includeInactive: boolean, search: string) =>
    ["employees", "list", { includeInactive, search }] as const,
  detail: (id: number) => ["employees", "detail", id] as const,
};

export function useEmployees(includeInactive: boolean, search: string) {
  return useQuery({
    queryKey: employeeKeys.list(includeInactive, search),
    queryFn: () =>
      callBackend<Employee[]>("get_employees", {
        includeInactive,
        search: search.trim() || null,
      }),
  });
}

export function useEmployee(id: number | null) {
  return useQuery({
    queryKey: employeeKeys.detail(id ?? 0),
    queryFn: () => callBackend<Employee>("get_employee", { employeeId: id }),
    enabled: id !== null,
  });
}

/** Maps form values to the backend contract. */
export function toEmployeeInput(values: EmployeeFormValues): EmployeeInput {
  return {
    firstName: values.firstName.trim(),
    lastName: values.lastName.trim(),
    isActive: values.isActive,
    isHourly: values.isHourly,

    // The backend zeroes the irrelevant field too, but sending a coherent payload keeps the
    // request self-describing rather than relying on a server-side correction.
    annualSalary: values.isHourly ? 0 : Number(values.annualSalary),
    hourlyRate: values.isHourly ? Number(values.hourlyRate) : 0,

    defaultHoursPerPeriod: Number(values.defaultHoursPerPeriod),
    preTax401kPercent: Number(values.preTax401kPercent),
    healthInsurancePerPeriod: Number(values.healthInsurancePerPeriod),
    otherDeductionsPerPeriod: Number(values.otherDeductionsPerPeriod),
    jobTitle: values.jobTitle?.trim() || null,
    department: values.department?.trim() || null,
    hireDate: fromDateInputValue(values.hireDate ?? ""),
    terminationDate: fromDateInputValue(values.terminationDate ?? ""),

    w4OnFile: values.w4OnFile,
    filingStatus: values.filingStatus,
    w4MultipleJobsChecked: values.w4MultipleJobsChecked,
    w4DependentsAndOtherCredits: Number(values.w4DependentsAndOtherCredits),
    w4OtherIncome: Number(values.w4OtherIncome),
    w4Deductions: Number(values.w4Deductions),
    w4ExtraWithholding: Number(values.w4ExtraWithholding),

    ilBasicAllowances: Number(values.ilBasicAllowances),
    ilAdditionalAllowances: Number(values.ilAdditionalAllowances),

    streetAddress: values.streetAddress?.trim() || null,
    city: values.city?.trim() || null,
    state: values.state?.trim().toUpperCase() || null,
    postalCode: values.postalCode?.trim() || null,

    // Send the SSN only when the operator actually typed one, so a blank field never wipes an
    // SSN already on file. The full value never round-trips back from the backend.
    ssn: values.ssn && values.ssn.trim() ? values.ssn.trim() : null,
  };
}

export function useCreateEmployee() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (values: EmployeeFormValues) =>
      callBackend<Employee>("create_employee", { employee: toEmployeeInput(values) }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: employeeKeys.all });
    },
  });
}

export function useUpdateEmployee(employeeId: number) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (values: EmployeeFormValues) =>
      callBackend<Employee>("update_employee", {
        employeeId,
        employee: toEmployeeInput(values),
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData(employeeKeys.detail(employeeId), updated);
      void queryClient.invalidateQueries({ queryKey: employeeKeys.all });
    },
  });
}
