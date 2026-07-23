import { createBrowserRouter, Navigate } from "react-router-dom";

import { AppShell } from "@/components/layout/AppShell";
import { EmptyState } from "@/components/ui/primitives";
import { EmployeesPage } from "@/features/employees/EmployeesPage";
import { SettingsPage } from "@/features/settings/SettingsPage";
import { PayRunsPage } from "@/features/pay-runs/PayRunsPage";
import { NewPayRunWizard } from "@/features/pay-runs/NewPayRunWizard";
import { PayRunDetail } from "@/features/pay-runs/PayRunDetail";
import { DashboardPage } from "@/features/dashboard/DashboardPage";
import { ReportsPage } from "@/features/reports/ReportsPage";
import { AuditLogPage } from "@/features/audit/AuditLogPage";
import {
  EmployeeCompensationTab,
  EmployeeOverviewTab,
  EmployeeTaxesTab,
} from "@/features/employees/EmployeeDetail";
import {
  EmployeeCreateRoute,
  EmployeeDetailRoute,
  EmployeeEditRoute,
} from "@/features/employees/routes";

export const router = createBrowserRouter([
  {
    path: "/",
    element: <AppShell />,
    children: [
      { index: true, element: <Navigate to="/dashboard" replace /> },
      { path: "dashboard", element: <DashboardPage /> },
      {
        path: "employees",
        element: <EmployeesPage />,
        children: [
          { path: "new", element: <EmployeeCreateRoute /> },
          { path: ":employeeId/edit", element: <EmployeeEditRoute /> },
          {
            path: ":employeeId",
            element: <EmployeeDetailRoute />,
            children: [
              { index: true, element: <EmployeeOverviewTab /> },
              { path: "compensation", element: <EmployeeCompensationTab /> },
              { path: "taxes", element: <EmployeeTaxesTab /> },
            ],
          },
        ],
      },
      {
        path: "pay-runs",
        children: [
          { index: true, element: <PayRunsPage /> },
          { path: "new", element: <NewPayRunWizard /> },
          { path: ":payRunId", element: <PayRunDetail /> },
        ],
      },
      { path: "reports", element: <ReportsPage /> },
      { path: "settings", element: <SettingsPage /> },
      { path: "audit", element: <AuditLogPage /> },
      {
        path: "*",
        element: (
          <EmptyState
            title="Not built yet"
            description="This area is part of a later milestone. Employees is the completed vertical slice."
          />
        ),
      },
    ],
  },
]);
