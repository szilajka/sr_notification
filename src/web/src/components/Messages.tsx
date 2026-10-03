import type { ReactNode } from 'react';
import { dateTime, timeAgo } from '../format';

export function ErrorText({ error }: { error: unknown }) {
  if (!error) return null;
  const message = error instanceof Error ? error.message : 'Something went wrong. Try again.';
  return (
    <p className="error-text" role="alert">
      {message}
    </p>
  );
}

export function Notice({ children, tone = 'info' }: { children: ReactNode; tone?: 'info' | 'warning' }) {
  return <div className={`notice notice-${tone}`}>{children}</div>;
}

export function Loading({ what }: { what: string }) {
  return (
    <p className="muted" aria-live="polite">
      Loading {what}…
    </p>
  );
}

export function TimeAgo({ iso }: { iso: string }) {
  return (
    <time dateTime={iso} title={dateTime(iso)}>
      {timeAgo(iso)}
    </time>
  );
}
