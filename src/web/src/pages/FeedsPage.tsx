import { useState, type SubmitEvent } from 'react';
import { Link } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, type NotificationSettings, type Subscription, type SubscriptionInput } from '../api';
import { Lamp } from '../components/Lamp';
import { ErrorText, Loading, Notice, TimeAgo } from '../components/Messages';
import { hostOf } from '../format';

export function FeedsPage() {
  const subscriptions = useQuery({ queryKey: ['subscriptions'], queryFn: api.subscriptions });
  const settings = useQuery({ queryKey: ['notification-settings'], queryFn: api.notificationSettings });

  return (
    <section>
      <h1>Your feeds</h1>
      <p className="lede">New items from these feeds are sent to you. Switch email or Slack on for each one.</p>

      {settings.data && <ChannelNotices settings={settings.data} />}

      <AddFeedForm settings={settings.data} />

      {subscriptions.isPending && <Loading what="your feeds" />}
      <ErrorText error={subscriptions.error} />
      {subscriptions.data?.length === 0 && (
        <p className="empty">You don't follow any feeds yet. Paste a feed address above to start.</p>
      )}
      {subscriptions.data && subscriptions.data.length > 0 && (
        <ul className="feed-list" aria-label="Followed feeds">
          {subscriptions.data.map((s) => (
            <FeedRow key={s.id} subscription={s} settings={settings.data} />
          ))}
        </ul>
      )}
    </section>
  );
}

function ChannelNotices({ settings }: { settings: NotificationSettings }) {
  const notes: string[] = [];
  if (!settings.email.enabledForApp) notes.push('Email notifications are turned off for everyone at the moment.');
  if (!settings.slack.enabledForApp) notes.push('Slack notifications are turned off for everyone at the moment.');
  if (notes.length === 0 && settings.slack.enabledForApp && !settings.slack.isConfigured) {
    return (
      <p className="muted small">
        To get items in Slack, <Link to="/notifications">add a Slack webhook</Link> first.
      </p>
    );
  }
  if (notes.length === 0) return null;
  return (
    <Notice tone="warning">
      {notes.join(' ')} Your switches are kept and work again when it's back.
    </Notice>
  );
}

/** Why a channel switch can't be turned on, or undefined if it can. */
function slackBlocker(settings: NotificationSettings | undefined) {
  if (!settings) return undefined;
  if (!settings.slack.isConfigured) return 'Add a Slack webhook in Notifications first';
  return undefined;
}

function AddFeedForm({ settings }: { settings: NotificationSettings | undefined }) {
  const queryClient = useQueryClient();
  const [url, setUrl] = useState('');
  const [name, setName] = useState('');
  const [email, setEmail] = useState(true);
  const [slack, setSlack] = useState(false);
  const slackBlocked = slackBlocker(settings);

  const create = useMutation({
    mutationFn: (input: SubscriptionInput) => api.createSubscription(input),
    onSuccess: () => {
      setUrl('');
      setName('');
      void queryClient.invalidateQueries({ queryKey: ['subscriptions'] });
    },
  });

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    create.mutate({
      url,
      name: name.trim() || null,
      emailEnabled: email,
      slackEnabled: slack && !slackBlocked,
    });
  };

  const fieldError = create.error instanceof ApiError ? create.error : undefined;

  return (
    <form className="add-feed" onSubmit={submit} noValidate>
      <div className="field field-wide">
        <label htmlFor="feed-url">Feed address</label>
        <input
          id="feed-url"
          type="url"
          inputMode="url"
          placeholder="https://example.com/feed.xml"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          aria-invalid={Boolean(fieldError?.field('url'))}
          required
        />
      </div>
      <div className="field">
        <label htmlFor="feed-name">
          Name <span className="muted">(optional)</span>
        </label>
        <input id="feed-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} />
      </div>
      <div className="add-feed-actions">
        <div className="feed-lamps" role="group" aria-label="Notify me by">
          <Lamp label="Email" on={email} onChange={setEmail} />
          <Lamp label="Slack" on={slack && !slackBlocked} onChange={setSlack} disabled={Boolean(slackBlocked)} disabledReason={slackBlocked} />
        </div>
        <button type="submit" className="button" disabled={create.isPending || url.trim() === ''}>
          {create.isPending ? 'Checking the feed…' : 'Follow feed'}
        </button>
      </div>
      <ErrorText error={create.error} />
    </form>
  );
}

function FeedRow({ subscription: s, settings }: { subscription: Subscription; settings: NotificationSettings | undefined }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [confirmingRemove, setConfirmingRemove] = useState(false);
  const slackBlocked = slackBlocker(settings);

  const update = useMutation({
    mutationFn: (input: SubscriptionInput) => api.updateSubscription(s.id, input),
    onSuccess: (updated) => {
      queryClient.setQueryData<Subscription[]>(['subscriptions'], (list) =>
        list?.map((item) => (item.id === updated.id ? updated : item)),
      );
      setEditing(false);
    },
  });

  const remove = useMutation({
    mutationFn: () => api.deleteSubscription(s.id),
    onSuccess: () => {
      queryClient.setQueryData<Subscription[]>(['subscriptions'], (list) => list?.filter((item) => item.id !== s.id));
    },
  });

  const toggle = (channel: 'email' | 'slack', on: boolean) =>
    update.mutate({
      url: s.url,
      name: s.name,
      emailEnabled: channel === 'email' ? on : s.emailEnabled,
      slackEnabled: channel === 'slack' ? on : s.slackEnabled,
    });

  const title = s.name ?? s.feedTitle ?? hostOf(s.url);

  if (editing) {
    return (
      <li className="feed-row feed-row-editing">
        <EditFeedForm subscription={s} pending={update.isPending} error={update.error}
          onCancel={() => { update.reset(); setEditing(false); }}
          onSave={(url, name) => update.mutate({ url, name, emailEnabled: s.emailEnabled, slackEnabled: s.slackEnabled })} />
      </li>
    );
  }

  return (
    <li className="feed-row">
      <div className="feed-main">
        {s.siteUrl ? (
          <a className="feed-title" href={s.siteUrl} target="_blank" rel="noreferrer">
            {title}
          </a>
        ) : (
          <span className="feed-title">{title}</span>
        )}
        <span className="feed-url">{s.url}</span>
        <FeedStatus subscription={s} />
      </div>

      <div className="feed-lamps" role="group" aria-label={`Notifications for ${title}`}>
        <Lamp label="Email" on={s.emailEnabled} onChange={(on) => toggle('email', on)} busy={update.isPending} />
        <Lamp
          label="Slack"
          on={s.slackEnabled}
          onChange={(on) => toggle('slack', on)}
          busy={update.isPending}
          disabled={Boolean(slackBlocked) && !s.slackEnabled}
          disabledReason={slackBlocked}
        />
      </div>

      <div className="feed-actions">
        {confirmingRemove ? (
          <>
            <span>Stop following?</span>
            <button type="button" className="button-danger" onClick={() => remove.mutate()} disabled={remove.isPending}>
              {remove.isPending ? 'Removing…' : 'Remove'}
            </button>
            <button type="button" className="button-quiet" onClick={() => setConfirmingRemove(false)}>
              Keep
            </button>
          </>
        ) : (
          <>
            <button type="button" className="button-quiet" onClick={() => setEditing(true)}>
              Edit
            </button>
            <button type="button" className="button-quiet" onClick={() => setConfirmingRemove(true)}>
              Remove
            </button>
          </>
        )}
      </div>
      <ErrorText error={update.error ?? remove.error} />
    </li>
  );
}

function FeedStatus({ subscription: s }: { subscription: Subscription }) {
  if (s.isFailing) {
    return (
      <span className="feed-status feed-status-failing">
        Can't be read right now{s.lastSuccessAt && <>; last read <TimeAgo iso={s.lastSuccessAt} /></>}. We keep trying.
      </span>
    );
  }
  if (s.lastSuccessAt) {
    return (
      <span className="feed-status">
        Last checked <TimeAgo iso={s.lastCheckedAt ?? s.lastSuccessAt} />
      </span>
    );
  }
  return <span className="feed-status">Waiting for the first check (within a few minutes)</span>;
}

interface EditFeedFormProps {
  subscription: Subscription;
  pending: boolean;
  error: unknown;
  onSave: (url: string, name: string | null) => void;
  onCancel: () => void;
}

function EditFeedForm({ subscription, pending, error, onSave, onCancel }: EditFeedFormProps) {
  const [url, setUrl] = useState(subscription.url);
  const [name, setName] = useState(subscription.name ?? '');

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    onSave(url, name.trim() || null);
  };

  return (
    <form className="edit-feed" onSubmit={submit} noValidate>
      <div className="field field-wide">
        <label htmlFor={`url-${subscription.id}`}>Feed address</label>
        <input id={`url-${subscription.id}`} type="url" value={url} onChange={(e) => setUrl(e.target.value)} />
      </div>
      <div className="field">
        <label htmlFor={`name-${subscription.id}`}>
          Name <span className="muted">(optional)</span>
        </label>
        <input
          id={`name-${subscription.id}`}
          value={name}
          placeholder={subscription.feedTitle ?? ''}
          onChange={(e) => setName(e.target.value)}
          maxLength={200}
        />
      </div>
      <div className="edit-feed-actions">
        <button type="submit" className="button" disabled={pending}>
          {pending ? 'Saving…' : 'Save changes'}
        </button>
        <button type="button" className="button-quiet" onClick={onCancel}>
          Cancel
        </button>
      </div>
      <ErrorText error={error} />
    </form>
  );
}
