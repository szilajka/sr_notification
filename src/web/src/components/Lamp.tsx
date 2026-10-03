interface LampProps {
  label: string;
  on: boolean;
  onChange?: (on: boolean) => void;
  disabled?: boolean;
  /** Why it's disabled, shown on hover. Also show it as text near the switch. */
  disabledReason?: string;
  busy?: boolean;
}

/**
 * A notification switch drawn as a signal lamp: lit amber when the channel is on.
 * It is a real switch (role="switch"), so it works with the keyboard and screen readers.
 */
export function Lamp({ label, on, onChange, disabled, disabledReason, busy }: LampProps) {
  const inactive = disabled || !onChange;
  return (
    <button
      type="button"
      role="switch"
      aria-checked={on}
      aria-busy={busy || undefined}
      title={disabled ? disabledReason : `${label} notifications ${on ? 'on' : 'off'}`}
      className="lamp"
      data-on={on}
      disabled={inactive}
      onClick={() => onChange?.(!on)}
    >
      <span className="lamp-bulb" aria-hidden="true" />
      <span className="lamp-label">{label}</span>
    </button>
  );
}
