import { useState } from "react";
import { NavLink, Outlet, useNavigate, useParams } from "react-router-dom";

import { Button, Chip, EmptyState, TextInput, cx } from "@/components/ui/primitives";
import { formatCurrency, initialsOf } from "@/lib/format";
import type { Employee } from "@/types/api";
import { useEmployees } from "./api";

/**
 * Split layout from the mock: a filterable employee list beside a route-driven detail pane.
 * Selection lives in the URL (/employees/:employeeId) so a specific employee is linkable and
 * survives a refresh.
 */
export function EmployeesPage() {
  const [search, setSearch] = useState("");
  const [includeInactive, setIncludeInactive] = useState(false);

  const navigate = useNavigate();
  const { employeeId } = useParams();
  const selectedId = employeeId ? Number(employeeId) : null;

  const { data: employees, isPending, isError, error, refetch } = useEmployees(
    includeInactive,
    search,
  );

  return (
    <div className="flex h-full min-h-0">
      <aside className="flex w-[--spacing-listpane] shrink-0 flex-col border-r border-outline-variant bg-surface-low">
        <div className="flex shrink-0 flex-col gap-3 border-b border-outline-variant px-4 py-4">
          <div className="flex items-center justify-between">
            <h1 className="text-[18px] font-semibold leading-6 text-white">Employees</h1>
            <Button size="sm" variant="ghost" onClick={() => navigate("/employees/new")}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="size-4">
                <path d="M12 5v14M5 12h14" />
              </svg>
              Add
            </Button>
          </div>

          <TextInput
            type="search"
            placeholder="Search name, title, department…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            aria-label="Search employees"
          />

          <div className="flex gap-2">
            <FilterPill active={!includeInactive} onClick={() => setIncludeInactive(false)}>
              Active
            </FilterPill>
            <FilterPill active={includeInactive} onClick={() => setIncludeInactive(true)}>
              All
            </FilterPill>
          </div>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto">
          {isPending && <ListMessage>Loading employees…</ListMessage>}

          {isError && (
            <div className="flex flex-col items-start gap-3 p-4">
              <p className="text-[13px] text-error">
                {error instanceof Error ? error.message : "Could not load employees."}
              </p>
              <Button size="sm" onClick={() => void refetch()}>
                Retry
              </Button>
            </div>
          )}

          {employees && employees.length === 0 && (
            <ListMessage>
              {search ? `No employees match “${search}”.` : "No employees yet."}
            </ListMessage>
          )}

          {employees?.map((employee) => (
            <EmployeeRow
              key={employee.id}
              employee={employee}
              selected={employee.id === selectedId}
            />
          ))}
        </div>
      </aside>

      <section className="flex min-w-0 flex-1 flex-col">
        {selectedId === null && !window.location.pathname.endsWith("/new") ? (
          <EmptyState
            title="Select an employee"
            description="Choose someone from the list to view their compensation and tax setup, or add a new employee."
            action={
              <Button variant="primary" size="sm" onClick={() => navigate("/employees/new")}>
                Add Employee
              </Button>
            }
          />
        ) : (
          <Outlet />
        )}
      </section>
    </div>
  );
}

function FilterPill({
  active,
  onClick,
  children,
}: {
  active: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={active}
      className={cx(
        "h-7 flex-1 rounded-lg border text-[11px] font-bold uppercase tracking-[0.05em] transition-colors",
        active
          ? "border-primary/40 bg-primary/15 text-primary"
          : "border-outline-variant text-on-surface-variant hover:border-outline hover:text-on-surface",
      )}
    >
      {children}
    </button>
  );
}

function ListMessage({ children }: { children: React.ReactNode }) {
  return <p className="px-4 py-6 text-[13px] text-on-surface-variant">{children}</p>;
}

function EmployeeRow({ employee, selected }: { employee: Employee; selected: boolean }) {
  return (
    <NavLink
      to={`/employees/${employee.id}`}
      className={cx(
        "relative flex items-center gap-3 border-b border-outline-variant/60 px-4 py-3 transition-colors",
        selected ? "bg-surface-container" : "hover:bg-surface-container/50",
      )}
    >
      {selected && <span className="absolute left-0 top-0 h-full w-[3px] bg-primary" />}

      <div className="flex size-9 shrink-0 items-center justify-center rounded-full border border-outline-variant bg-surface-high text-[12px] font-semibold text-primary">
        {initialsOf(employee.firstName, employee.lastName)}
      </div>

      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="truncate text-sm font-medium text-on-surface">{employee.fullName}</span>
        <span className="truncate text-[12px] text-on-surface-variant">
          {employee.jobTitle || "No title"}
        </span>
        <span className="tabular truncate text-[11px] text-on-surface-variant/60">
          {employee.isHourly
            ? `${formatCurrency(employee.hourlyRate)}/hr`
            : `${formatCurrency(employee.annualSalary)}/yr`}
        </span>
      </div>

      <div className="flex shrink-0 flex-col items-end gap-1">
        <Chip tone={employee.isHourly ? "info" : "active"} showDot={false}>
          {employee.isHourly ? "Hourly" : "Salaried"}
        </Chip>
        {!employee.isActive && (
          <Chip tone="inactive" showDot={false}>
            Inactive
          </Chip>
        )}
      </div>
    </NavLink>
  );
}
