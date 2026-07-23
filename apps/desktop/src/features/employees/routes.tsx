import { useNavigate, useParams } from "react-router-dom";

import { Button, EmptyState, SectionHeading } from "@/components/ui/primitives";
import { toDateInputValue } from "@/lib/format";
import type { Employee } from "@/types/api";
import { useCreateEmployee, useEmployee, useUpdateEmployee } from "./api";
import { EmployeeDetail } from "./EmployeeDetail";
import { EmployeeForm } from "./EmployeeForm";
import { EMPTY_EMPLOYEE_FORM, type EmployeeFormValues } from "./schema";

function useEmployeeIdParam(): number | null {
  const { employeeId } = useParams();
  const parsed = Number(employeeId);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : null;
}

function toFormValues(employee: Employee): EmployeeFormValues {
  return {
    firstName: employee.firstName,
    lastName: employee.lastName,
    isActive: employee.isActive,
    isHourly: employee.isHourly,
    annualSalary: employee.annualSalary,
    hourlyRate: employee.hourlyRate,
    defaultHoursPerPeriod: employee.defaultHoursPerPeriod,
    preTax401kPercent: employee.preTax401kPercent,
    healthInsurancePerPeriod: employee.healthInsurancePerPeriod,
    otherDeductionsPerPeriod: employee.otherDeductionsPerPeriod,
    jobTitle: employee.jobTitle ?? "",
    department: employee.department ?? "",
    hireDate: toDateInputValue(employee.hireDate),
    terminationDate: toDateInputValue(employee.terminationDate),
    w4OnFile: employee.w4OnFile,
    filingStatus: employee.filingStatus,
    w4MultipleJobsChecked: employee.w4MultipleJobsChecked,
    w4DependentsAndOtherCredits: employee.w4DependentsAndOtherCredits,
    w4OtherIncome: employee.w4OtherIncome,
    w4Deductions: employee.w4Deductions,
    w4ExtraWithholding: employee.w4ExtraWithholding,
    ilBasicAllowances: employee.ilBasicAllowances,
    ilAdditionalAllowances: employee.ilAdditionalAllowances,
  };
}

/** Detail shell for /employees/:employeeId. Nested tab routes render inside it. */
export function EmployeeDetailRoute() {
  const employeeId = useEmployeeIdParam();
  const navigate = useNavigate();
  const { data: employee, isPending, isError, error } = useEmployee(employeeId);

  if (isPending) {
    return <EmptyState title="Loading employee…" />;
  }

  if (isError || !employee) {
    return (
      <EmptyState
        title="Could not load this employee"
        description={error instanceof Error ? error.message : undefined}
        action={
          <Button size="sm" onClick={() => navigate("/employees")}>
            Back to list
          </Button>
        }
      />
    );
  }

  return (
    <EmployeeDetail
      employee={employee}
      onEdit={() => navigate(`/employees/${employee.id}/edit`)}
    />
  );
}

export function EmployeeCreateRoute() {
  const navigate = useNavigate();
  const createEmployee = useCreateEmployee();

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="shrink-0 border-b border-outline-variant px-6 py-4">
        <SectionHeading
          title="Add New Employee"
          description="Only fields the system actually stores are shown."
        />
      </header>

      <EmployeeForm
        defaultValues={EMPTY_EMPLOYEE_FORM}
        submitLabel="Save Employee"
        error={createEmployee.error}
        onCancel={() => navigate("/employees")}
        onSubmit={async (values) => {
          const created = await createEmployee.mutateAsync(values);
          navigate(`/employees/${created.id}`);
        }}
      />
    </div>
  );
}

export function EmployeeEditRoute() {
  const employeeId = useEmployeeIdParam();
  const navigate = useNavigate();
  const { data: employee, isPending } = useEmployee(employeeId);
  const updateEmployee = useUpdateEmployee(employeeId ?? 0);

  if (isPending || !employee) {
    return <EmptyState title="Loading employee…" />;
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="shrink-0 border-b border-outline-variant px-6 py-4">
        <SectionHeading
          title={`Edit ${employee.fullName}`}
          description="Compensation changes are recorded in the audit log."
        />
      </header>

      <EmployeeForm
        defaultValues={toFormValues(employee)}
        submitLabel="Save Changes"
        error={updateEmployee.error}
        onCancel={() => navigate(`/employees/${employee.id}`)}
        onSubmit={async (values) => {
          await updateEmployee.mutateAsync(values);
          navigate(`/employees/${employee.id}`);
        }}
      />
    </div>
  );
}
