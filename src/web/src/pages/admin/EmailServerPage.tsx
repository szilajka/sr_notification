import { useState, type SubmitEvent, type ReactNode } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, type SmtpSecurity, type SmtpSettings } from '../../api';
import { useMe } from '../../App';
import { ErrorText, Loading, TimeAgo } from '../../components/Messages';

const securityOptions: { value: SmtpSecurity; label: string }[] = [
  { value: 'StartTls', label: 'STARTTLS (usually port 587)' },
  { value: 'SslOnConnect', label: 'TLS from the start (usually port 465)' },
  { value: 'Auto', label: 'Automatic' },
  { value: 'None', label: 'None (unencrypted, not recommended)' },
];

export function EmailServerPage() {
  const smtp = useQuery({ queryKey: ['admin', 'smtp'], queryFn: api.admin.smtp });

  return (
    <section>
      <h1>Email server</h1>
      <p className="lede">The SMTP server used for notification emails and address confirmation links.</p>
      {smtp.isPending && <Loading what="email server settings" />}
      <ErrorText error={smtp.error} />
      {smtp.data && (
        <>
          <SmtpForm current={smtp.data} />
          <TestEmail disabled={!smtp.data.isConfigured} />
        </>
      )}
    </section>
  );
}

function SmtpForm({ current }: { current: SmtpSettings }) {
  const queryClient = useQueryClient();
  const [host, setHost] = useState(current.host ?? '');
  const [port, setPort] = useState(String(current.port));
  const [security, setSecurity] = useState<SmtpSecurity>(current.security);
  const [username, setUsername] = useState(current.username ?? '');
  const [password, setPassword] = useState('');
  const [clearPassword, setClearPassword] = useState(false);
  const [fromAddress, setFromAddress] = useState(current.fromAddress ?? '');
  const [fromName, setFromName] = useState(current.fromName ?? '');

  const save = useMutation({
    mutationFn: api.admin.updateSmtp,
    onSuccess: (data) => {
      queryClient.setQueryData(['admin', 'smtp'], data);
      setPassword('');
      setClearPassword(false);
    },
  });

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    save.mutate({
      host: host.trim(),
      port: Number.parseInt(port, 10) || 0,
      security,
      username: username.trim() || null,
      password: password || null,
      clearPassword,
      fromAddress: fromAddress.trim(),
      fromName: fromName.trim() || null,
    });
  };

  const error = save.error instanceof ApiError ? save.error : undefined;
  const fieldError = (name: string) => error?.field(name);

  return (
    <form className="settings-form" onSubmit={submit} noValidate>
      <div className="field-row">
        <Field id="smtp-host" label="Server" error={fieldError('host')}>
          <input id="smtp-host" value={host} onChange={(e) => setHost(e.target.value)} placeholder="smtp.example.com" />
        </Field>
        <Field id="smtp-port" label="Port" error={fieldError('port')} narrow>
          <input id="smtp-port" inputMode="numeric" value={port} onChange={(e) => setPort(e.target.value)} />
        </Field>
      </div>
      <Field id="smtp-security" label="Encryption" error={fieldError('security')}>
        <select id="smtp-security" value={security} onChange={(e) => setSecurity(e.target.value as SmtpSecurity)}>
          {securityOptions.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
      </Field>
      <div className="field-row">
        <Field id="smtp-username" label="User name" hint="Leave empty if the server doesn't need a login." error={fieldError('username')}>
          <input id="smtp-username" value={username} onChange={(e) => setUsername(e.target.value)} autoComplete="off" />
        </Field>
        <Field
          id="smtp-password"
          label="Password"
          hint={current.hasPassword ? 'A password is saved. Leave empty to keep it.' : undefined}
        >
          <input
            id="smtp-password"
            type="password"
            value={password}
            disabled={clearPassword}
            onChange={(e) => setPassword(e.target.value)}
            autoComplete="new-password"
          />
        </Field>
      </div>
      {current.hasPassword && (
        <label className="checkbox">
          <input type="checkbox" checked={clearPassword} onChange={(e) => setClearPassword(e.target.checked)} />
          Remove the saved password
        </label>
      )}
      <div className="field-row">
        <Field id="smtp-from" label="Send from address" error={fieldError('fromAddress')}>
          <input id="smtp-from" type="email" value={fromAddress} onChange={(e) => setFromAddress(e.target.value)} placeholder="news@example.com" />
        </Field>
        <Field id="smtp-from-name" label="Sender name" hint="Optional, e.g. SR Notification." error={fieldError('fromName')}>
          <input id="smtp-from-name" value={fromName} onChange={(e) => setFromName(e.target.value)} />
        </Field>
      </div>

      <div className="button-row">
        <button type="submit" className="button" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : 'Save settings'}
        </button>
        {save.isSuccess && <span className="success-text" role="status">Saved.</span>}
        {current.updatedAt && !save.isSuccess && (
          <span className="muted small">
            Last changed <TimeAgo iso={current.updatedAt} />
            {current.updatedBy && <> by {current.updatedBy}</>}
          </span>
        )}
      </div>
      {error && Object.keys(error.fieldErrors).length === 0 && <ErrorText error={error} />}
    </form>
  );
}

function TestEmail({ disabled }: { disabled: boolean }) {
  const { data: me } = useMe();
  const [to, setTo] = useState('');
  const test = useMutation({ mutationFn: (address: string | null) => api.admin.testSmtp(address) });

  const submit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    test.mutate(to.trim() || null);
  };

  return (
    <div className="settings-block">
      <h2>Send a test email</h2>
      <form className="inline-form" onSubmit={submit} noValidate>
        <Field id="smtp-test-to" label="Send to" hint={me?.email ? `Leave empty to send it to ${me.email}.` : undefined}>
          <input id="smtp-test-to" type="email" value={to} onChange={(e) => setTo(e.target.value)} />
        </Field>
        <button type="submit" className="button-quiet" disabled={disabled || test.isPending}>
          {test.isPending ? 'Sending…' : 'Send test email'}
        </button>
      </form>
      {disabled && <p className="muted small">Save the settings first.</p>}
      {test.isSuccess && <p className="success-text" role="status">Test email sent.</p>}
      {/* Includes the SMTP server's own answer, which is what admins need to fix the settings. */}
      <ErrorText error={test.error} />
    </div>
  );
}

interface FieldProps {
  id: string;
  label: string;
  hint?: string;
  error?: string;
  narrow?: boolean;
  children: ReactNode;
}

function Field({ id, label, hint, error, narrow, children }: FieldProps) {
  return (
    <div className={narrow ? 'field field-narrow' : 'field field-wide'}>
      <label htmlFor={id}>{label}</label>
      {children}
      {hint && <span className="hint">{hint}</span>}
      {error && <span className="error-text">{error}</span>}
    </div>
  );
}
