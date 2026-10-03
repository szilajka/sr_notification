import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { api, type AdminFeed } from '../../api';
import { ErrorText, Loading, TimeAgo } from '../../components/Messages';
import { Pager } from '../../components/Pager';
import { hostOf } from '../../format';

const filters = [
  { value: 'all', label: 'All feeds' },
  { value: 'failing', label: 'Failing' },
  { value: 'inactive', label: 'Not followed' },
] as const;

export function FeedHealthPage() {
  const [status, setStatus] = useState<string>('all');
  const [page, setPage] = useState(1);
  const [selectedFeed, setSelectedFeed] = useState<AdminFeed | undefined>();
  const feeds = useQuery({
    queryKey: ['admin', 'feeds', status, page],
    queryFn: () => api.admin.feeds(status, page),
  });

  return (
    <section>
      <h1>Feeds and reader errors</h1>
      <p className="lede">Every feed the RSS reader knows about, with the most troubled first.</p>

      <div className="segmented" role="group" aria-label="Show">
        {filters.map((f) => (
          <button
            key={f.value}
            type="button"
            aria-pressed={status === f.value}
            onClick={() => { setStatus(f.value); setPage(1); }}
          >
            {f.label}
          </button>
        ))}
      </div>

      {feeds.isPending && <Loading what="feeds" />}
      <ErrorText error={feeds.error} />
      {feeds.data?.items.length === 0 && <p className="empty">No feeds match this filter.</p>}
      {feeds.data && feeds.data.items.length > 0 && (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Feed</th>
                <th scope="col" className="num">Followers</th>
                <th scope="col">Last read</th>
                <th scope="col" className="num">Failures in a row</th>
                <th scope="col">Last error</th>
              </tr>
            </thead>
            <tbody>
              {feeds.data.items.map((f) => (
                <tr key={f.id} data-failing={f.consecutiveFailures > 0}>
                  <td>
                    <span className="cell-title">{f.title ?? hostOf(f.url)}</span>
                    <span className="cell-sub">{f.url}</span>
                    {!f.isActive && <span className="cell-sub">Not polled: nobody follows it</span>}
                  </td>
                  <td className="num">{f.subscriberCount}</td>
                  <td>{f.lastSuccessAt ? <TimeAgo iso={f.lastSuccessAt} /> : 'Never'}</td>
                  <td className="num">{f.consecutiveFailures}</td>
                  <td>
                    {f.lastError && <span className="cell-error">{f.lastError}</span>}
                    <button type="button" className="button-link" onClick={() => setSelectedFeed(f)}>
                      Error history
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {feeds.data && <Pager {...feeds.data} onPage={setPage} />}

      <ReaderErrorLog feed={selectedFeed} onClearFeed={() => setSelectedFeed(undefined)} />
    </section>
  );
}

function ReaderErrorLog({ feed, onClearFeed }: { feed: AdminFeed | undefined; onClearFeed: () => void }) {
  const [page, setPage] = useState(1);
  const feedId = feed?.id;
  const errors = useQuery({
    queryKey: ['admin', 'feed-errors', feedId, page],
    queryFn: () => api.admin.feedErrors(feedId, page),
  });

  return (
    <div className="settings-block" id="reader-errors">
      <h2>{feed ? `Reader errors for ${feed.title ?? hostOf(feed.url)}` : 'Reader errors'}</h2>
      <p className="muted small">
        Failed attempts to read a feed, newest first. Kept for 30 days.
        {feed && (
          <>
            {' '}
            <button type="button" className="button-link" onClick={() => { onClearFeed(); setPage(1); }}>
              Show all feeds
            </button>
          </>
        )}
      </p>
      {errors.isPending && <Loading what="errors" />}
      <ErrorText error={errors.error} />
      {errors.data?.items.length === 0 && <p className="empty">No reader errors.</p>}
      {errors.data && errors.data.items.length > 0 && (
        <ol className="log">
          {errors.data.items.map((e) => (
            <li key={e.id}>
              <span className="log-time"><TimeAgo iso={e.occurredAt} /></span>
              <span className="log-source">{e.feedTitle ?? hostOf(e.feedUrl)}</span>
              <span className="log-message">{e.message}</span>
            </li>
          ))}
        </ol>
      )}
      {errors.data && <Pager {...errors.data} onPage={setPage} />}
    </div>
  );
}
