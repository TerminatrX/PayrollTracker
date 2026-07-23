import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { callBackend } from "@/lib/ipc";
import type {
  PayRunDetail,
  PayRunDraftInput,
  PayRunEmployeeInput,
  PayRunPreview,
  PayRunSummary,
  SuggestPayRunResponse,
} from "@/types/api";

export const payRunKeys = {
  all: ["pay-runs"] as const,
  list: ["pay-runs", "list"] as const,
  detail: (id: number) => ["pay-runs", "detail", id] as const,
};

export function usePayRuns() {
  return useQuery({
    queryKey: payRunKeys.list,
    queryFn: () =>
      callBackend<{ payRuns: PayRunSummary[] }>("get_pay_runs").then((r) => r.payRuns),
  });
}

export function usePayRun(id: number | null) {
  return useQuery({
    queryKey: payRunKeys.detail(id ?? 0),
    queryFn: () => callBackend<PayRunDetail>("get_pay_run", { payRunId: id }),
    enabled: id !== null,
  });
}

/** One-shot fetch of the wizard bootstrap (next period + employees with suggested hours). */
export function suggestPayRun() {
  return callBackend<SuggestPayRunResponse>("suggest_pay_run");
}

export function previewPayRun(draft: PayRunDraftInput, employees: PayRunEmployeeInput[]) {
  return callBackend<PayRunPreview>("preview_pay_run", { draft, employees });
}

export function usePostPayRun() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (args: {
      draft: PayRunDraftInput;
      employees: PayRunEmployeeInput[];
      calculationHash: string;
    }) => callBackend<PayRunSummary>("post_pay_run", args),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: payRunKeys.all });
      // Posting changes YTD, which affects employee figures shown elsewhere.
      void queryClient.invalidateQueries({ queryKey: ["employees"] });
    },
  });
}

export function useVoidPayRun() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (args: { payRunId: number; reason: string }) =>
      callBackend<PayRunSummary>("void_pay_run", args),
    onSuccess: (updated) => {
      queryClient.setQueryData(payRunKeys.detail(updated.id), (old: PayRunDetail | undefined) =>
        old ? { ...old, summary: updated } : old,
      );
      void queryClient.invalidateQueries({ queryKey: payRunKeys.all });
      void queryClient.invalidateQueries({ queryKey: ["employees"] });
    },
  });
}
