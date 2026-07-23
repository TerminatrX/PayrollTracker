import type { PayRunStatus } from "@/types/api";

type ChipTone = "active" | "inactive" | "warning" | "info" | "neutral";

/** Maps a pay-run status to a chip tone from the design system's semantic palette. */
export function statusTone(status: PayRunStatus): ChipTone {
  switch (status) {
    case "Posted":
      return "active"; // success green
    case "Draft":
    case "Calculated":
      return "info"; // blue
    case "Voided":
      return "inactive"; // muted slate
    default:
      return "neutral";
  }
}
