import { useEffect, useRef } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { api } from '../api';
import { ErrorText, Loading } from '../components/Messages';

/** Opened from the link in the confirmation email. */
export function ConfirmEmailPage() {
  const [params] = useSearchParams();
  const token = params.get('token');
  const queryClient = useQueryClient();
  const confirm = useMutation({
    mutationFn: api.confirmEmail,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notification-settings'] }),
  });

  // The token works once; StrictMode runs effects twice in development, so send it only once.
  const sent = useRef(false);
  useEffect(() => {
    if (token && !sent.current) {
      sent.current = true;
      confirm.mutate(token);
    }
  }, [token, confirm]);

  return (
    <section>
      <h1>Confirm your email address</h1>
      {!token && <p>This link is incomplete. Open the link from the email again.</p>}
      {confirm.isPending && <Loading what="the confirmation" />}
      {confirm.isSuccess && (
        <p className="success-text" role="status">
          Confirmed. Email notifications now go to <strong>{confirm.data.address}</strong>.
        </p>
      )}
      <ErrorText error={confirm.error} />
      {(confirm.isSuccess || confirm.isError) && (
        <p>
          <Link to="/notifications">Go to notification settings</Link>
        </p>
      )}
    </section>
  );
}
