import { invoke } from "@tauri-apps/api/core";
import { ApiError, type ApiErrorShape } from "@/types/api";

/**
 * Calls a backend business command through the Tauri bridge.
 *
 * The Rust side owns the sidecar process and forwards this to it over stdin/stdout. The
 * frontend has no shell access and no way to name an executable or express SQL - it can only
 * invoke the fixed set of commands the sidecar registers.
 */
export async function callBackend<T>(
  method: string,
  params?: unknown,
): Promise<T> {
  try {
    const raw = await invoke<string>("backend_command", {
      method,
      paramsJson: params === undefined ? null : JSON.stringify(params),
    });

    const envelope = JSON.parse(raw) as { result?: T; error?: ApiErrorShape };

    if (envelope.error) {
      throw new ApiError(envelope.error);
    }

    // A command with no return value yields a null result, which is legitimate.
    return envelope.result as T;
  } catch (err) {
    if (err instanceof ApiError) {
      throw err;
    }

    // Anything reaching here failed before the sidecar could answer: the bridge itself, the
    // sidecar process being down, or malformed framing. Surface it as retryable so the UI
    // can offer a retry rather than presenting it as a validation problem.
    throw new ApiError({
      code: "sidecar_unavailable",
      message:
        err instanceof Error
          ? err.message
          : "The payroll service did not respond.",
      retryable: true,
    });
  }
}
