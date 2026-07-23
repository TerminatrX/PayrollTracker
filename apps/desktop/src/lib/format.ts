/**
 * Display formatting only.
 *
 * Nothing here computes a payroll figure. Every currency value shown has already been
 * calculated by the backend in decimal arithmetic; these helpers just render it.
 */

const currencyFormatter = new Intl.NumberFormat("en-US", {
  style: "currency",
  currency: "USD",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const percentFormatter = new Intl.NumberFormat("en-US", {
  minimumFractionDigits: 0,
  maximumFractionDigits: 2,
});

export function formatCurrency(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "—";
  }

  return currencyFormatter.format(value);
}

export function formatPercent(value: number | null | undefined): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return "—";
  }

  return `${percentFormatter.format(value)}%`;
}

/**
 * Formats a backend date. Values arrive as unzoned local timestamps ("2024-03-15T00:00:00"),
 * so the date part is parsed directly rather than through `new Date()`, which would apply a
 * timezone shift and can display the previous day.
 */
export function formatDate(value: string | null | undefined): string {
  if (!value) {
    return "—";
  }

  const datePart = value.slice(0, 10);
  const [year, month, day] = datePart.split("-").map(Number);

  if (!year || !month || !day) {
    return "—";
  }

  return new Date(year, month - 1, day).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

/** Converts a backend timestamp into the yyyy-MM-dd an <input type="date"> expects. */
export function toDateInputValue(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : "";
}

/** Converts a date input back into the unzoned timestamp the backend parses. */
export function fromDateInputValue(value: string): string | null {
  return value ? `${value}T00:00:00` : null;
}

export function initialsOf(firstName: string, lastName: string): string {
  return `${firstName.charAt(0)}${lastName.charAt(0)}`.toUpperCase();
}
