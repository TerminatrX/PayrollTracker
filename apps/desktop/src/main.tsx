import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";

import { router } from "@/app/router";
import { ApiError } from "@/types/api";
import "./styles.css";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // The sidecar is local, so data is effectively instant to re-fetch; but a payroll
      // operator should never see a stale employee record after an edit.
      staleTime: 0,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        // Never retry a validation or business-rule rejection - it will fail identically.
        if (error instanceof ApiError && !error.retryable) {
          return false;
        }

        return failureCount < 2;
      },
    },
    mutations: {
      // A payroll write must never be silently replayed.
      retry: false,
    },
  },
});

const rootElement = document.getElementById("root");

if (!rootElement) {
  throw new Error("Root element not found");
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
