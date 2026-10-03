import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, type ChannelSetting, type NotificationChannel } from '../../api';
import { Lamp } from '../../components/Lamp';
import { ErrorText, Loading, TimeAgo } from '../../components/Messages';

const descriptions: Record<NotificationChannel, string> = {
  Email: 'Notification emails to all users.',
  Slack: 'Messages to all users’ Slack webhooks.',
};

export function ChannelsPage() {
  const queryClient = useQueryClient();
  const channels = useQuery({ queryKey: ['admin', 'channels'], queryFn: api.admin.channels });
  const update = useMutation({
    mutationFn: ({ channel, isEnabled }: { channel: NotificationChannel; isEnabled: boolean }) =>
      api.admin.updateChannel(channel, isEnabled),
    onSuccess: (updated) =>
      queryClient.setQueryData<ChannelSetting[]>(['admin', 'channels'], (list) =>
        list?.map((c) => (c.channel === updated.channel ? updated : c)),
      ),
  });

  return (
    <section>
      <h1>Notification channels</h1>
      <p className="lede">
        Turning a channel off stops it for every user. Their own switches are kept and apply again when you turn it
        back on.
      </p>
      {channels.isPending && <Loading what="channels" />}
      <ErrorText error={channels.error ?? update.error} />
      <ul className="channel-list">
        {channels.data?.map((c) => (
          <li key={c.channel} className="channel-row">
            <Lamp
              label={c.channel}
              on={c.isEnabled}
              busy={update.isPending}
              onChange={(isEnabled) => update.mutate({ channel: c.channel, isEnabled })}
            />
            <div>
              <p>{descriptions[c.channel]}</p>
              {c.updatedAt && (
                <p className="muted small">
                  Turned {c.isEnabled ? 'on' : 'off'} <TimeAgo iso={c.updatedAt} />
                  {c.updatedBy && <> by {c.updatedBy}</>}
                </p>
              )}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}
