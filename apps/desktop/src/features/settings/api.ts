import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { callBackend } from "@/lib/ipc";
import type { Backup, CompanySettings, CompanySettingsInput } from "@/types/api";
import type { SettingsFormValues } from "./schema";

export const settingsKeys = {
  settings: ["company-settings"] as const,
  backups: ["backups"] as const,
};

export function useCompanySettings() {
  return useQuery({
    queryKey: settingsKeys.settings,
    queryFn: () => callBackend<CompanySettings>("get_company_settings"),
  });
}

export function toSettingsInput(values: SettingsFormValues): CompanySettingsInput {
  return {
    companyName: values.companyName.trim(),
    companyAddress: values.companyAddress?.trim() ?? "",
    taxId: values.taxId?.trim() ?? "",
    payPeriodsPerYear: Number(values.payPeriodsPerYear),
    defaultHoursPerPeriod: Number(values.defaultHoursPerPeriod),
    socialSecurityPercent: Number(values.socialSecurityPercent),
    medicarePercent: Number(values.medicarePercent),
    suiRatePercent: Number(values.suiRatePercent),
    suiWageBase: Number(values.suiWageBase),
    receivesFullFutaCredit: values.receivesFullFutaCredit,
  };
}

export function useUpdateCompanySettings() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (values: SettingsFormValues) =>
      callBackend<CompanySettings>("update_company_settings", {
        settings: toSettingsInput(values),
      }),
    onSuccess: (updated) => {
      queryClient.setQueryData(settingsKeys.settings, updated);
    },
  });
}

export function useBackups() {
  return useQuery({
    queryKey: settingsKeys.backups,
    queryFn: () =>
      callBackend<{ backups: Backup[] }>("list_backups").then((r) => r.backups),
  });
}

export function useCreateBackup() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => callBackend<{ backup: Backup }>("create_backup"),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: settingsKeys.backups });
    },
  });
}
