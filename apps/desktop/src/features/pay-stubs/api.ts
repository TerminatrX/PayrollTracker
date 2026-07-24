import { useQuery } from "@tanstack/react-query";

import { callBackend } from "@/lib/ipc";
import type { PayStubStatement } from "@/types/api";

export function usePayStub(payStubId: number | null) {
  return useQuery({
    queryKey: ["pay-stub", payStubId],
    queryFn: () => callBackend<PayStubStatement>("get_pay_stub", { payStubId }),
    enabled: payStubId !== null,
  });
}

/** Writes the pay stub PDF to a path chosen by the caller (via the native save dialog). */
export function exportPayStubPdf(payStubId: number, outputPath: string) {
  return callBackend<{ path: string }>("export_pay_stub_pdf", { payStubId, outputPath });
}
