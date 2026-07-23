import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";

import { callBackend } from "@/lib/ipc";
import { Card, Chip, EmptyState, TextInput, cx } from "@/components/ui/primitives";
import type { AuditEntry } from "@/types/api";

/** Groups the many audit actions into a few semantic tones. */
function actionTone(action: string): "active" | "info" | "warning" | "inactive" | "neutral" {
  if (action === "PayRunPosted") return "active";
  if (action === "PayRunVoided") return "warning";
  if (action.startsWith("PayRun")) return "info";
  if (action.includes("Changed") || action.includes("Compensation")) return "warning";
  if (action.includes("Created")) return "info";
  return "neutral";
}

/** Splits "PayRunPosted" → "Pay Run Posted" for display. */
function humanizeAction(action: string): string {
  return action.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/([A-Z])([A-Z][a-z])/g, "$1 $2");
}

function formatTimestamp(iso: string): string {
  // Audit timestamps are UTC; show the viewer's local time.
  const d = new Date(iso.endsWith("Z") ? iso : `${iso}Z`);
  return d.toLocaleString("en-US", {
    year: "numeric", month: "short", day: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

export function AuditLogPage() {
  const [search, setSearch] = useState("");
  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ["audit-log"],
    queryFn: () => callBackend<{ entries: AuditEntry[] }>("get_audit_log", { limit: 500 }).then((r) => r.entries),
  });

  const filtered = useMemo(() => {
    if (!data) return [];
    const term = search.trim().toLowerCase();
    if (!term) return data;
    return data.filter((e) =>
      [e.action, e.entityType, e.performedBy, e.oldValue, e.newValue, e.notes]
        .filter(Boolean)
        .some((v) => v!.toLowerCase().includes(term)),
    );
  }, [data, search]);

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <header className="flex shrink-0 items-start justify-between border-b border-outline-variant px-6 py-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-[24px] font-semibold leading-8 tracking-[-0.02em] text-white">Audit Log</h1>
          <p className="text-[13px] text-on-surface-variant">
            Append-only record of sensitive actions. Newest first.
          </p>
        </div>
        <div className="w-72">
          <TextInput
            type="search"
            placeholder="Search actions, values…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            aria-label="Search audit log"
          />
        </div>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 py-5">
        {isPending && <EmptyState title="Loading audit log…" />}

        {isError && (
          <EmptyState
            title="Could not load the audit log"
            description={error instanceof Error ? error.message : undefined}
            action={
              <button
                type="button"
                onClick={() => void refetch()}
                className="rounded-lg border border-outline-variant px-3 py-1.5 text-[13px] hover:border-outline"
              >
                Retry
              </button>
            }
          />
        )}

        {data && filtered.length === 0 && (
          <EmptyState
            title={data.length === 0 ? "Nothing recorded yet" : `No entries match “${search}”`}
            description={data.length === 0 ? "Actions like posting payroll or changing pay appear here." : undefined}
          />
        )}

        {filtered.length > 0 && (
          <Card className="overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[820px] border-collapse">
                <thead>
                  <tr className="border-b border-outline-variant bg-surface-lowest text-left">
                    <Th>When</Th>
                    <Th>Action</Th>
                    <Th>Entity</Th>
                    <Th>Change</Th>
                    <Th>By</Th>
                  </tr>
                </thead>
                <tbody>
                  {filtered.map((entry) => (
                    <tr key={entry.id} className="border-b border-outline-variant/60 align-top">
                      <td className="whitespace-nowrap px-3 py-2.5 text-[12px] text-on-surface-variant">
                        {formatTimestamp(entry.timestampUtc)}
                      </td>
                      <td className="px-3 py-2.5">
                        <Chip tone={actionTone(entry.action)} showDot={false}>
                          {humanizeAction(entry.action)}
                        </Chip>
                      </td>
                      <td className="whitespace-nowrap px-3 py-2.5 text-[12px] text-on-surface-variant">
                        {entry.entityType}
                        {entry.entityId != null && <span className="text-on-surface-variant/50"> #{entry.entityId}</span>}
                      </td>
                      <td className="px-3 py-2.5 text-[12px]">
                        <ChangeCell oldValue={entry.oldValue} newValue={entry.newValue} notes={entry.notes} />
                      </td>
                      <td className="whitespace-nowrap px-3 py-2.5 text-[12px] text-on-surface-variant">
                        {entry.performedBy || "—"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </Card>
        )}
      </div>
    </div>
  );
}

function ChangeCell({ oldValue, newValue, notes }: { oldValue?: string | null; newValue?: string | null; notes?: string | null }) {
  if (!oldValue && !newValue && !notes) return <span className="text-on-surface-variant/40">—</span>;

  return (
    <div className="flex flex-col gap-0.5 font-mono text-[11px] leading-4">
      {oldValue && <span className="text-error/80">− {oldValue}</span>}
      {newValue && <span className="text-secondary/90">+ {newValue}</span>}
      {notes && !newValue && <span className="text-on-surface-variant">{notes}</span>}
    </div>
  );
}

function Th({ children }: { children: React.ReactNode }) {
  return (
    <th className={cx("px-3 py-2.5 text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant")}>
      {children}
    </th>
  );
}
