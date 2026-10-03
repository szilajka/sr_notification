// Thin client for the SR Notification API. Types mirror SrNotification.Api/Endpoints/Contracts.cs.

export type NotificationChannel = 'Email' | 'Slack';
export type SmtpSecurity = 'Auto' | 'None' | 'StartTls' | 'SslOnConnect';
export type DeliveryStatus = 'Pending' | 'Sent' | 'Failed';

export interface Me {
  id: number;
  email: string | null;
  displayName: string | null;
  isAdmin: boolean;
}

export interface NotificationSettings {
  email: {
    address: string | null;
    signInAddress: string | null;
    isCustom: boolean;
    pendingAddress: string | null;
    enabledForApp: boolean;
  };
  slack: {
    isConfigured: boolean;
    webhookHint: string | null;
    enabledForApp: boolean;
  };
}

export interface Subscription {
  id: number;
  url: string;
  name: string | null;
  feedTitle: string | null;
  siteUrl: string | null;
  emailEnabled: boolean;
  slackEnabled: boolean;
  createdAt: string;
  lastCheckedAt: string | null;
  lastSuccessAt: string | null;
  isFailing: boolean;
}

export interface SubscriptionInput {
  url: string;
  name: string | null;
  emailEnabled: boolean;
  slackEnabled: boolean;
}

export interface ChannelSetting {
  channel: NotificationChannel;
  isEnabled: boolean;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface SmtpSettings {
  isConfigured: boolean;
  host: string | null;
  port: number;
  security: SmtpSecurity;
  username: string | null;
  hasPassword: boolean;
  fromAddress: string | null;
  fromName: string | null;
  updatedAt: string | null;
  updatedBy: string | null;
}

export interface SmtpSettingsInput {
  host: string;
  port: number;
  security: SmtpSecurity;
  username: string | null;
  password: string | null;
  clearPassword: boolean;
  fromAddress: string;
  fromName: string | null;
}

export interface AdminFeed {
  id: number;
  url: string;
  title: string | null;
  isActive: boolean;
  subscriberCount: number;
  createdAt: string;
  lastCheckedAt: string | null;
  lastSuccessAt: string | null;
  consecutiveFailures: number;
  lastError: string | null;
}

export interface FeedErrorLogEntry {
  id: number;
  feedId: number;
  feedUrl: string;
  feedTitle: string | null;
  occurredAt: string;
  message: string;
}

export interface NotificationErrorLogEntry {
  id: number;
  channel: NotificationChannel;
  status: DeliveryStatus;
  attempts: number;
  lastError: string | null;
  createdAt: string;
  lastAttemptAt: string | null;
  userId: number;
  userEmail: string | null;
  itemId: number;
  itemTitle: string;
  feedUrl: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** A failed API call. `message` is ready to show to the user. */
export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, problem: ProblemDetails | undefined) {
    const fieldErrors = problem?.errors ?? {};
    const firstFieldError = Object.values(fieldErrors)[0]?.[0];
    // title says what failed; detail (when present) carries the reason, e.g. the SMTP server's answer.
    // For these statuses the server's title is generic ("Not Found"), so use our own wording.
    const generic = [401, 403, 404, 429].includes(status) && !problem?.detail;
    const summary = generic ? '' : [problem?.title, problem?.detail].filter(Boolean).join(' ');
    super(firstFieldError ?? (summary || defaultMessage(status)));
    this.status = status;
    this.fieldErrors = fieldErrors;
  }

  /** Message for one form field, matched case-insensitively (the API uses camelCase names). */
  field(name: string): string | undefined {
    const key = Object.keys(this.fieldErrors).find((k) => k.toLowerCase() === name.toLowerCase());
    return key ? this.fieldErrors[key][0] : undefined;
  }
}

function defaultMessage(status: number): string {
  if (status === 401) return 'Your session has ended. Sign in again.';
  if (status === 403) return "You don't have access to this.";
  if (status === 404) return 'This no longer exists.';
  if (status === 429) return 'Too many attempts. Wait a while and try again.';
  return 'Something went wrong on the server. Try again in a moment.';
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    // Required by the API for every change (CSRF protection); harmless on reads.
    'X-SRN-CSRF': '1',
  };
  if (body !== undefined) headers['Content-Type'] = 'application/json';

  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method,
      headers,
      credentials: 'same-origin',
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, { title: "The server can't be reached. Check your connection and try again." });
  }

  const text = await response.text();
  let data: unknown;
  try {
    data = text ? JSON.parse(text) : undefined;
  } catch {
    data = undefined;
  }

  if (!response.ok) throw new ApiError(response.status, data as ProblemDetails | undefined);
  return data as T;
}

const query = (params: Record<string, string | number | undefined>) => {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== '') search.set(key, String(value));
  }
  const s = search.toString();
  return s ? `?${s}` : '';
};

export const api = {
  me: () => request<Me>('GET', '/me'),

  notificationSettings: () => request<NotificationSettings>('GET', '/me/notifications'),
  updateEmail: (address: string | null) =>
    request<NotificationSettings>('PUT', '/me/notifications/email', { address }),
  confirmEmail: (token: string) => request<{ address: string }>('POST', '/email-confirmations', { token }),
  updateSlack: (webhookUrl: string | null) =>
    request<NotificationSettings>('PUT', '/me/notifications/slack', { webhookUrl }),
  testSlack: () => request<void>('POST', '/me/notifications/slack/test'),

  subscriptions: () => request<Subscription[]>('GET', '/subscriptions'),
  createSubscription: (input: SubscriptionInput) => request<Subscription>('POST', '/subscriptions', input),
  updateSubscription: (id: number, input: SubscriptionInput) =>
    request<Subscription>('PUT', `/subscriptions/${id}`, input),
  deleteSubscription: (id: number) => request<void>('DELETE', `/subscriptions/${id}`),

  admin: {
    channels: () => request<ChannelSetting[]>('GET', '/admin/channels'),
    updateChannel: (channel: NotificationChannel, isEnabled: boolean) =>
      request<ChannelSetting>('PUT', `/admin/channels/${channel}`, { isEnabled }),
    smtp: () => request<SmtpSettings>('GET', '/admin/smtp'),
    updateSmtp: (input: SmtpSettingsInput) => request<SmtpSettings>('PUT', '/admin/smtp', input),
    testSmtp: (to: string | null) => request<void>('POST', '/admin/smtp/test', { to }),
    feeds: (status: string, page: number) =>
      request<Paged<AdminFeed>>('GET', `/admin/feeds${query({ status, page })}`),
    feedErrors: (feedId: number | undefined, page: number) =>
      request<Paged<FeedErrorLogEntry>>('GET', `/admin/logs/feeds${query({ feedId, page })}`),
    notificationErrors: (channel: NotificationChannel | undefined, page: number) =>
      request<Paged<NotificationErrorLogEntry>>('GET', `/admin/logs/notifications${query({ channel, page })}`),
  },
};

/** Where the "Sign in" button goes; the API redirects to Entra and back to `returnUrl`. */
export const signInUrl = (returnUrl: string) => `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
