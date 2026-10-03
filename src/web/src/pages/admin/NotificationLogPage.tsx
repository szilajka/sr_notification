import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, type NotificationChannel } from '../../api';
import { ErrorText, Loading, TimeAgo } from '../../components/Messages';
import { Pager } from '../../components/Pager';

const filters: { value: NotificationChannel | undefined; label: string }[] = [
  { value: undefined, label: 'All channels' },
  { value: 'Email', label: 'Email' },
  { value: 'Slack', label: 'Slack' },
];

export function NotificationLogPage() {
  const [channel, setChannel] = useState<NotificationChannel | undefined>();
  const [page, setPage] = useState(1);
  const log = useQuery({
    queryKey: ['admin', 'notification-errors', channel, page],
    queryFn: () => api.admin.notificationErrors(channel, page),
  });

  return (
    <section>
      <h1>Failed notifications</h1>
      <p className="lede">Notifications that couldn't be delivered after all retries, newest first.</p>

      <div className="segmented" role="group" aria-label="Channel">
        {filters.map((f) => (
          <button
            key={f.label}
            type="button"
            aria-pressed={channel === f.value}
            onClick={() => { setChannel(f.value); setPage(1); }}
          >
            {f.label}
          </button>
        ))}
      </div>

      {log.isPending && <Loading what="failed notifications" />}
      <ErrorText error={log.error} />
      {log.data?.items.length === 0 && (
        <p className="empty">No failed notifications. Deliveries that fail are listed here with the reason.</p>
      )}
      {log.data && log.data.items.length > 0 && (
        <ol className="log">
          {log.data.items.map((d) => (
            <li key={d.id}>
              <span className="log-time"><TimeAgo iso={d.lastAttemptAt ?? d.createdAt} /></span>
              <span className="log-source">
                {d.channel} to {d.userEmail ?? `user ${d.userId}`}, {d.attempts} {d.attempts === 1 ? 'attempt' : 'attempts'}
              </span>
              <span className="log-message">
                “{d.itemTitle}”: {d.lastError ?? 'No reason recorded.'}
              </span>
            </li>
          ))}
        </ol>
      )}
      {log.data && <Pager {...log.data} onPage={setPage} />}
    </section>
  );
}
