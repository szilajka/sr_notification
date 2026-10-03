import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Navigate, NavLink, Outlet, Route, Routes, useLocation } from 'react-router';
import { api, ApiError, signInUrl, type Me } from './api';
import { Loading, ErrorText } from './components/Messages';
import { WelcomePage } from './pages/WelcomePage';
import { FeedsPage } from './pages/FeedsPage';
import { NotificationsPage } from './pages/NotificationsPage';
import { ConfirmEmailPage } from './pages/ConfirmEmailPage';
import { ChannelsPage } from './pages/admin/ChannelsPage';
import { EmailServerPage } from './pages/admin/EmailServerPage';
import { FeedHealthPage } from './pages/admin/FeedHealthPage';
import { NotificationLogPage } from './pages/admin/NotificationLogPage';

/** The signed-in user, or null when signed out. */
export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: async (): Promise<Me | null> => {
      try {
        return await api.me();
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null;
        throw error;
      }
    },
    staleTime: 5 * 60 * 1000,
  });
}

export function App() {
  return (
    <Routes>
      <Route element={<Shell />}>
        <Route index element={<Home />} />
        <Route path="confirm-email" element={<ConfirmEmailPage />} />
        <Route element={<RequireSignIn />}>
          <Route path="feeds" element={<FeedsPage />} />
          <Route path="notifications" element={<NotificationsPage />} />
          <Route path="admin" element={<RequireAdmin />}>
            <Route index element={<Navigate to="channels" replace />} />
            <Route path="channels" element={<ChannelsPage />} />
            <Route path="email-server" element={<EmailServerPage />} />
            <Route path="feeds" element={<FeedHealthPage />} />
            <Route path="notification-errors" element={<NotificationLogPage />} />
          </Route>
        </Route>
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  );
}

function Shell() {
  const { data: me } = useMe();
  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <header className="topbar">
        <div className="topbar-inner">
          <NavLink to="/" className="brand">
            SR Notification
          </NavLink>
          {me && (
            <nav className="mainnav" aria-label="Main">
              <NavLink to="/feeds">Feeds</NavLink>
              <NavLink to="/notifications">Notifications</NavLink>
              {me.isAdmin && <NavLink to="/admin">Admin</NavLink>}
            </nav>
          )}
          {me && (
            <form method="post" action="/auth/logout" className="signout">
              <span className="muted signout-user">{me.email ?? me.displayName}</span>
              <button type="submit" className="button-quiet">
                Sign out
              </button>
            </form>
          )}
        </div>
      </header>
      <main id="main" className="page">
        <Outlet />
      </main>
    </>
  );
}

function Home() {
  const { data: me, isPending } = useMe();
  if (isPending) return null;
  return me ? <Navigate to="/feeds" replace /> : <WelcomePage />;
}

function RequireSignIn() {
  const { data: me, isPending, error } = useMe();
  const location = useLocation();
  const signedOut = !isPending && !error && !me;

  useEffect(() => {
    // Full page load: the API takes over and redirects to the sign-in page.
    if (signedOut) window.location.assign(signInUrl(location.pathname + location.search));
  }, [signedOut, location.pathname, location.search]);

  if (isPending || signedOut) return <Loading what="your account" />;
  if (error) return <ErrorText error={error} />;
  return <Outlet />;
}

function RequireAdmin() {
  const { data: me } = useMe();
  if (!me?.isAdmin) return <NotFound />;
  return (
    <div className="admin">
      <nav className="subnav" aria-label="Admin">
        <NavLink to="/admin/channels">Notification channels</NavLink>
        <NavLink to="/admin/email-server">Email server</NavLink>
        <NavLink to="/admin/feeds">Feeds and reader errors</NavLink>
        <NavLink to="/admin/notification-errors">Failed notifications</NavLink>
      </nav>
      <Outlet />
    </div>
  );
}

function NotFound() {
  return (
    <section>
      <h1>Page not found</h1>
      <p>
        This page doesn't exist. Go to <NavLink to="/feeds">your feeds</NavLink>.
      </p>
    </section>
  );
}
