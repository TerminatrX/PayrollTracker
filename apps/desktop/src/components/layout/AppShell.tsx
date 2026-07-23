import type { ReactNode } from "react";
import { NavLink, Outlet } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";

import { callBackend } from "@/lib/ipc";
import type { HealthResponse } from "@/types/api";
import { cx } from "@/components/ui/primitives";

interface NavItem {
  to: string;
  label: string;
  icon: ReactNode;
  /** Routes not yet built are shown but disabled, so the shell reads honestly. */
  ready?: boolean;
}

function Icon({ path }: { path: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      className="size-[18px] shrink-0"
      aria-hidden="true"
    >
      <path d={path} />
    </svg>
  );
}

const NAV_ITEMS: NavItem[] = [
  { to: "/dashboard", label: "Dashboard", ready: true, icon: <Icon path="M4 4h7v7H4zM13 4h7v4h-7zM13 10h7v10h-7zM4 13h7v7H4z" /> },
  {
    to: "/employees",
    label: "Employees",
    icon: <Icon path="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" />,
    ready: true,
  },
  { to: "/pay-runs", label: "Pay Runs", ready: true, icon: <Icon path="M8 2v4M16 2v4M3 10h18M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2" /> },
  { to: "/reports", label: "Reports", ready: true, icon: <Icon path="M3 3v18h18M7 16v-5M12 16V8M17 16v-3" /> },
  { to: "/settings", label: "Company Settings", ready: true, icon: <Icon path="M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1" /> },
  { to: "/audit", label: "Audit Log", ready: true, icon: <Icon path="M12 8v4l3 3M3.05 11a9 9 0 1 1 .5 4" /> },
];

function ServiceStatus() {
  const { data, isError, isPending } = useQuery({
    queryKey: ["health"],
    queryFn: () => callBackend<HealthResponse>("health"),
    // Surfaces a dead sidecar without hammering it.
    refetchInterval: 30_000,
    retry: 1,
  });

  const tone = isPending ? "bg-outline" : isError ? "bg-error" : "bg-secondary";
  const label = isPending
    ? "Connecting…"
    : isError
      ? "Service unavailable"
      : `Connected · ${data?.engineVersion ?? ""}`;

  return (
    <div className="flex items-center gap-2 px-3 py-2" title={data?.databasePath ?? undefined}>
      <span className={cx("size-2 shrink-0 rounded-full", tone)} />
      <span className="truncate text-[12px] text-on-surface-variant">{label}</span>
    </div>
  );
}

export function AppShell() {
  return (
    <div className="flex h-full">
      <nav className="flex w-[--spacing-rail] shrink-0 flex-col border-r border-outline-variant bg-surface-lowest">
        <div className="flex flex-col gap-0.5 px-5 py-5">
          <span className="text-[19px] font-bold leading-6 text-white">PayrollManager</span>
          <span className="text-[10px] font-bold uppercase tracking-[0.08em] text-on-surface-variant/60">
            Illinois Edition
          </span>
        </div>

        <ul className="flex flex-1 flex-col gap-0.5 px-3">
          {NAV_ITEMS.map((item) => (
            <li key={item.to}>
              {item.ready ? (
                <NavLink
                  to={item.to}
                  className={({ isActive }) =>
                    cx(
                      "relative flex h-9 items-center gap-3 rounded-lg px-3 text-sm transition-colors",
                      isActive
                        ? "bg-surface-container font-medium text-primary"
                        : "text-on-surface-variant hover:bg-surface-container/60 hover:text-on-surface",
                    )
                  }
                >
                  {({ isActive }) => (
                    <>
                      {/* 3px left indicator on the active item, per the design spec. */}
                      {isActive && (
                        <span className="absolute left-0 top-1/2 h-5 w-[3px] -translate-y-1/2 rounded-r bg-primary" />
                      )}
                      {item.icon}
                      {item.label}
                    </>
                  )}
                </NavLink>
              ) : (
                <span
                  className="flex h-9 cursor-not-allowed items-center gap-3 rounded-lg px-3 text-sm text-on-surface-variant/35"
                  title="Not built yet"
                >
                  {item.icon}
                  {item.label}
                </span>
              )}
            </li>
          ))}
        </ul>

        <div className="border-t border-outline-variant">
          <ServiceStatus />
        </div>
      </nav>

      <main className="flex min-w-0 flex-1 flex-col">
        <Outlet />
      </main>
    </div>
  );
}
