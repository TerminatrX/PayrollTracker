import type { ReactNode, ButtonHTMLAttributes, InputHTMLAttributes, SelectHTMLAttributes } from "react";

export function cx(...parts: Array<string | false | null | undefined>): string {
  return parts.filter(Boolean).join(" ");
}

/* ── Buttons ─────────────────────────────────────────────────────────────── */

type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: "sm" | "md";
}

const BUTTON_VARIANTS: Record<ButtonVariant, string> = {
  primary:
    "bg-primary text-on-primary hover:brightness-110 disabled:opacity-40 disabled:hover:brightness-100",
  secondary:
    "border border-outline-variant text-on-surface hover:border-outline hover:bg-surface-high disabled:opacity-40",
  ghost: "text-on-surface-variant hover:bg-surface-high hover:text-on-surface disabled:opacity-40",
  danger:
    "border border-error/40 text-error hover:bg-error/10 disabled:opacity-40",
};

export function Button({
  variant = "secondary",
  size = "md",
  className,
  ...props
}: ButtonProps) {
  return (
    <button
      {...props}
      className={cx(
        "inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-colors",
        "focus-visible:ring-0 disabled:cursor-not-allowed",
        size === "sm" ? "h-8 px-3 text-[13px]" : "h-10 px-4 text-sm",
        BUTTON_VARIANTS[variant],
        className,
      )}
    />
  );
}

/* ── Status chip ─────────────────────────────────────────────────────────── */

type ChipTone = "active" | "inactive" | "warning" | "info" | "neutral";

const CHIP_TONES: Record<ChipTone, { wrapper: string; dot: string }> = {
  active: { wrapper: "bg-secondary/10 text-secondary", dot: "bg-secondary" },
  inactive: { wrapper: "bg-outline/10 text-on-surface-variant", dot: "bg-outline" },
  warning: { wrapper: "bg-tertiary/10 text-tertiary", dot: "bg-tertiary" },
  info: { wrapper: "bg-primary/10 text-primary", dot: "bg-primary" },
  neutral: { wrapper: "bg-surface-highest text-on-surface-variant", dot: "bg-outline" },
};

export function Chip({
  tone = "neutral",
  children,
  showDot = true,
}: {
  tone?: ChipTone;
  children: ReactNode;
  showDot?: boolean;
}) {
  const styles = CHIP_TONES[tone];

  return (
    <span
      className={cx(
        "inline-flex items-center gap-1.5 rounded-full px-2 py-0.5",
        "text-[11px] font-bold uppercase tracking-[0.05em]",
        styles.wrapper,
      )}
    >
      {showDot && <span className={cx("size-1.5 rounded-full", styles.dot)} />}
      {children}
    </span>
  );
}

/* ── Form fields ─────────────────────────────────────────────────────────── */

export function Field({
  label,
  htmlFor,
  error,
  hint,
  required,
  children,
  className,
}: {
  label: string;
  htmlFor?: string;
  error?: string;
  hint?: string;
  required?: boolean;
  children: ReactNode;
  className?: string;
}) {
  return (
    <div className={cx("flex flex-col gap-1.5", className)}>
      <label
        htmlFor={htmlFor}
        className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant"
      >
        {label}
        {required && <span className="ml-1 text-error">*</span>}
      </label>

      {children}

      {/* Error takes precedence over hint so a field never shows both. */}
      {error ? (
        <p role="alert" className="text-[12px] text-error">
          {error}
        </p>
      ) : hint ? (
        <p className="text-[12px] text-on-surface-variant/70">{hint}</p>
      ) : null}
    </div>
  );
}

const CONTROL_BASE =
  "h-9 w-full rounded-lg border bg-surface-lowest px-3 text-sm text-on-surface " +
  "placeholder:text-on-surface-variant/40 transition-colors " +
  "disabled:cursor-not-allowed disabled:opacity-50";

export function TextInput({
  invalid,
  className,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { invalid?: boolean }) {
  return (
    <input
      {...props}
      aria-invalid={invalid || undefined}
      className={cx(
        CONTROL_BASE,
        invalid ? "border-error" : "border-outline-variant hover:border-outline",
        className,
      )}
    />
  );
}

/** Numeric input. Right-aligned and tabular so currency columns line up. */
export function NumberInput({
  invalid,
  className,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { invalid?: boolean }) {
  return (
    <input
      {...props}
      type="number"
      inputMode="decimal"
      aria-invalid={invalid || undefined}
      className={cx(
        CONTROL_BASE,
        "tabular text-right",
        invalid ? "border-error" : "border-outline-variant hover:border-outline",
        className,
      )}
    />
  );
}

export function Select({
  invalid,
  className,
  children,
  ...props
}: SelectHTMLAttributes<HTMLSelectElement> & { invalid?: boolean }) {
  return (
    <select
      {...props}
      aria-invalid={invalid || undefined}
      className={cx(
        CONTROL_BASE,
        "appearance-none bg-[length:16px] bg-[right_0.6rem_center] bg-no-repeat pr-9",
        invalid ? "border-error" : "border-outline-variant hover:border-outline",
        className,
      )}
      style={{
        backgroundImage:
          "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' fill='none' stroke='%23c2c6d6' stroke-width='2'%3E%3Cpath d='M6 9l6 6 6-6'/%3E%3C/svg%3E\")",
      }}
    >
      {children}
    </select>
  );
}

export function Checkbox({
  label,
  description,
  className,
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { label: string; description?: string }) {
  return (
    <label className={cx("flex cursor-pointer items-start gap-2.5", className)}>
      <input
        {...props}
        type="checkbox"
        className="mt-0.5 size-4 shrink-0 rounded border-outline-variant bg-surface-lowest accent-primary"
      />
      <span className="flex flex-col gap-0.5">
        <span className="text-sm text-on-surface">{label}</span>
        {description && (
          <span className="text-[12px] leading-4 text-on-surface-variant/70">{description}</span>
        )}
      </span>
    </label>
  );
}

/* ── Layout ──────────────────────────────────────────────────────────────── */

export function Card({
  children,
  className,
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <div
      className={cx(
        "rounded-md border border-outline-variant bg-surface-container",
        className,
      )}
    >
      {children}
    </div>
  );
}

export function SectionHeading({
  title,
  description,
}: {
  title: string;
  description?: string;
}) {
  return (
    <div className="flex flex-col gap-1 border-b border-outline-variant pb-3">
      <h2 className="text-[18px] font-semibold leading-6 text-white">{title}</h2>
      {description && (
        <p className="text-[13px] leading-[18px] text-on-surface-variant">{description}</p>
      )}
    </div>
  );
}

/** A labelled statistic, matching the metric cards in the mock. */
export function StatCard({
  label,
  value,
  caption,
  tone = "default",
}: {
  label: string;
  value: string;
  caption?: string;
  tone?: "default" | "positive" | "negative";
}) {
  return (
    <Card className="flex flex-col gap-2 p-4">
      <span className="text-[11px] font-bold uppercase tracking-[0.05em] text-on-surface-variant">
        {label}
      </span>
      <span
        className={cx(
          "tabular text-[24px] font-semibold leading-8 tracking-[-0.02em]",
          tone === "positive" && "text-secondary",
          tone === "negative" && "text-error",
          tone === "default" && "text-white",
        )}
      >
        {value}
      </span>
      {caption && (
        <span className="text-[12px] leading-4 text-on-surface-variant/70">{caption}</span>
      )}
    </Card>
  );
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 p-8 text-center">
      <p className="text-[15px] font-medium text-on-surface">{title}</p>
      {description && (
        <p className="max-w-sm text-[13px] leading-[18px] text-on-surface-variant">{description}</p>
      )}
      {action}
    </div>
  );
}
