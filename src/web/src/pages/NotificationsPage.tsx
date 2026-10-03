import { useState, type SubmitEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type NotificationSettings } from '../api';
import { ErrorText, Loading, Notice } from '../components/Messages';

export function NotificationsPage() {
  const settings = useQuery({ queryKey: ['notification-settings'], queryFn: api.notificationSettings });

  return (
    <section>
      <h1>Notifications</h1>
      <p className="lede">Where we send new items. You choose email or Slack for each feed on the Feeds page.</p>
      {settings.isPending && <Loading what="your settings" />}
      <ErrorText error={settings.error} />
      {settings.data && (
        <>
          <EmailSection settings={settings.data} />
          <SlackSection settings={settings.data} />
        </>
      )}
    </section>
  );
}

function useSaveSettings<TArg>(fn: (arg: TArg) => Promise<NotificationSettings>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: (data) => queryClient.setQueryData(['notification-settings'], data),
  });
}

function EmailSection({ settings }: { settings: NotificationSettings }) {
  const { email } = settings;
  const [address, setAddress] = useState('');
  const save = useSaveSettings(api.updateEmail);

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    save.mutate(address.trim(), { onSuccess: () => setAddress('') });
  };

  return (
    <div className="settings-block">
      <h2>Email</h2>
      {!email.enabledForApp && (
        <Notice tone="warning">Email notifications are turned off for everyone at the moment.</Notice>
      )}

      {email.address ? (
        <p>
          Emails go to <strong>{email.address}</strong>
          {email.isCustom ? '.' : ', the address you signed in with.'}
        </p>
      ) : (
        <p>Your sign-in account didn't give us an email address. Add one below to get emails.</p>
      )}

      {email.isCustom && email.signInAddress && (
        <p>
          <button type="button" className="button-quiet" disabled={save.isPending} onClick={() => save.mutate(null)}>
            Use {email.signInAddress} instead
          </button>
        </p>
      )}

      {email.pendingAddress && (
        <Notice>
          We sent a confirmation link to <strong>{email.pendingAddress}</strong>. Open it within 24 hours to start
          getting emails there.
        </Notice>
      )}

      <form className="inline-form" onSubmit={submit} noValidate>
        <div className="field field-wide">
          <label htmlFor="notification-email">Send emails to a different address</label>
          <input
            id="notification-email"
            type="email"
            autoComplete="email"
            placeholder="name@example.com"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
          />
          <span className="hint">We email that address a link. Nothing is sent there until you open it.</span>
        </div>
        <button type="submit" className="button" disabled={save.isPending || address.trim() === ''}>
          {save.isPending ? 'Sending…' : 'Send confirmation link'}
        </button>
      </form>
      <ErrorText error={save.error} />
    </div>
  );
}

function SlackSection({ settings }: { settings: NotificationSettings }) {
  const { slack } = settings;
  const [webhookUrl, setWebhookUrl] = useState('');
  const save = useSaveSettings(api.updateSlack);
  const test = useMutation({ mutationFn: api.testSlack });

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    test.reset();
    save.mutate(webhookUrl.trim(), { onSuccess: () => setWebhookUrl('') });
  };

  return (
    <div className="settings-block">
      <h2>Slack</h2>
      {!slack.enabledForApp && (
        <Notice tone="warning">Slack notifications are turned off for everyone at the moment.</Notice>
      )}

      {slack.isConfigured ? (
        <>
          <p>
            Slack messages go to the webhook <code>{slack.webhookHint ?? 'saved earlier'}</code>.
          </p>
          <div className="button-row">
            <button
              type="button"
              className="button-quiet"
              disabled={test.isPending || !slack.enabledForApp}
              onClick={() => test.mutate()}
            >
              {test.isPending ? 'Sending…' : 'Send a test message'}
            </button>
            <button
              type="button"
              className="button-quiet"
              disabled={save.isPending}
              onClick={() => { test.reset(); save.mutate(null); }}
            >
              Remove webhook
            </button>
          </div>
          {test.isSuccess && <p className="success-text" role="status">Test message sent. Check your Slack channel.</p>}
          <ErrorText error={test.error} />
        </>
      ) : (
        <p>
          Slack is not connected. Create an{' '}
          <a href="https://api.slack.com/messaging/webhooks" target="_blank" rel="noreferrer">
            incoming webhook
          </a>{' '}
          for the channel you want, then paste its URL here.
        </p>
      )}

      <form className="inline-form" onSubmit={submit} noValidate>
        <div className="field field-wide">
          <label htmlFor="slack-webhook">{slack.isConfigured ? 'Replace the webhook URL' : 'Webhook URL'}</label>
          <input
            id="slack-webhook"
            type="url"
            placeholder="https://hooks.slack.com/services/…"
            value={webhookUrl}
            onChange={(e) => setWebhookUrl(e.target.value)}
            autoComplete="off"
          />
        </div>
        <button type="submit" className="button" disabled={save.isPending || webhookUrl.trim() === ''}>
          {save.isPending ? 'Saving…' : 'Save webhook'}
        </button>
      </form>
      <ErrorText error={save.error} />
    </div>
  );
}
