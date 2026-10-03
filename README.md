# SR Notification App

This web app allows the users to follow their favourite news sites' RSS and receive notifications when news are published.
Currently Email sending and Slack channel messages are supported (as notifications).

## Application technical overview
The app is made of 3 main parts.
- The first one is the RSS reader.
  - It reads the sites' RSS and saves them to the db.
- The Web UI.
  - 2 main use cases:
    - Users can add new/delete old RSS links and turn on/off notifications.
    - Admins can view logs (why the notification sending failed) and turn on/off notifications for the whole app (by notification types), and also for setting the SMTP server for email notifications.
- The message sender.
  - It sends notifications for users.
  - Uses resiliency (Polly or other similar nuget package) for sending messages.
  - Logs failed attempts.

## Technologies
- Docker
- PostgreSQL
- .NET 10 (LTS)

## Technical details
The app uses PostgreSQL for storing read RSS items, users (id from AZ AD or other IAM) and their preferences. Using the built-in SyndicationFeed class to read RSS, then update the page after X minutes.
A background job will do the reading and refreshing.
A Web API will serve as the Backend for the React Frontend.
Another background job will send the notifications to the users, using Polly.




## Solution layout
```
SrNotification.slnx
src/
  SrNotification.Data/            EF Core DbContext, entities and migrations (shared by all parts)
  SrNotification.Infrastructure/  Shared code: feed download/parsing, SSRF guard, email (SMTP), Slack
  SrNotification.RssReader/       Part 1: background worker that reads the RSS feeds
  SrNotification.Api/             Part 2: Web API; also serves the built React app
  web/                            Part 2: React frontend (Vite + TypeScript)
  SrNotification.NotificationSender/  Part 3: background worker that sends email and Slack notifications
tests/
  SrNotification.RssReader.Tests/ Unit tests for all parts
docs/slack-examples/              Example Slack messages (see Part 3)
docker-compose.yml                PostgreSQL + RSS reader + API/web app + notification sender
```

## Getting started
You need the .NET 10 SDK and Docker. Node.js 22+ is only needed to run the web app outside Docker.

### 1. Create the `.env` file (required)
Passwords and other secrets are not stored in `appsettings.json` or `docker-compose.yml`. They are
read from a `.env` file in the repository root, which is git-ignored. Create it from the template:
```bash
cp .env.example .env        # Windows: copy .env.example .env
```
and set your own database password in it:
```
POSTGRES_PASSWORD=choose-a-strong-password
```
The sign-in settings (`OIDC_*`) are filled in during step 2; `.env.example` describes every value.

Where the values are used:
- `docker compose` reads `.env` automatically and passes the values to the containers. Without
  `POSTGRES_PASSWORD`, `docker compose up` stops with an error saying it is missing.
- The API, the RSS reader, the notification sender and the `dotnet ef` tools load `.env` themselves
  when run locally (from the IDE or `dotnet run`).
- On servers / CI, set them as normal environment variables instead; existing environment variables
  take precedence over `.env`.

PostgreSQL applies `POSTGRES_PASSWORD` only when it creates its data volume. If you change the
password later, recreate the volume (this deletes the data): `docker compose down -v`.

### 2. Set up sign-in
Follow [Setting up Entra External ID](#setting-up-entra-external-id-one-time) and put the values in `.env`.

### 3. Run
Everything in Docker (web app at http://localhost:8080):
```bash
docker compose up --build
```
This starts PostgreSQL, the RSS reader, the API with the web app, and the notification sender.

Or PostgreSQL in Docker and the rest locally, with hot reload for the web app:
```bash
docker compose up -d postgres
dotnet run --project src/SrNotification.Api                  # http://localhost:5080
dotnet run --project src/SrNotification.RssReader
dotnet run --project src/SrNotification.NotificationSender
cd src/web && npm install && npm run dev                       # open http://localhost:5173
```
Vite forwards `/api`, `/auth` and the sign-in callbacks to the API, so the browser sees one origin.
It expects the API on `http://localhost:5080` (`dotnet run`). If the API runs in Docker instead
(port 8080), set `API_URL=http://localhost:8080` in `src/web/.env.local` (git-ignored) or as an
environment variable before `npm run dev`.

Database migrations are applied automatically at startup (`Database:ApplyMigrationsOnStartup`, on
in Development and in docker compose). In Development the RSS reader also adds a few public feeds
(see its `appsettings.Development.json`).

### 4. First steps in the app
1. Sign up on the sign-in page and make that account an admin (see the note in
   [Setting up Entra External ID](#setting-up-entra-external-id-one-time), step 6).
2. Open **Admin → Email server** and set up SMTP. Until then no emails are sent, and users can't
   switch to a different notification address (the confirmation email can't be sent).
3. To see a notification end to end: follow a frequently updated feed with email and/or Slack
   switched on, and wait for its next new item (the feed's existing items are not sent).

### Tests
```bash
dotnet test
```

## Part 1: RSS reader
A .NET Worker Service (`BackgroundService`) that:
1. Every `PollInterval` (default 1 min) loads the active feeds from the `RssFeeds` table.
2. Picks the ones whose `RefreshInterval` (default 5 min) has elapsed since their last check.
   Feeds that keep failing back off exponentially (10, 20, 40 min … up to `MaxBackoff`).
3. Fetches them in parallel (`MaxParallelFeeds`) with a conditional GET (ETag / Last-Modified),
   so unchanged feeds cost a `304 Not Modified`. HTTP calls go through the standard resilience
   handler (retries, timeouts, circuit breaker).
4. Parses RSS 2.0 or Atom 1.0 with `SyndicationFeed` (lenient about non-standard dates).
5. Stores only the items not yet in `RssItems`. An item is identified within its feed by its
   guid/id, else its link, else a hash of title + summary (unique index on `FeedId, ExternalId`).
6. Records `LastCheckedAt`, `LastSuccessAt`, `LastError` and `ConsecutiveFailures` on the feed,
   which the admin UI can show.

Items stored by a feed's very first fetch get `IsFromInitialFetch = true`, so the notification
sender can skip a newly added feed's backlog instead of flooding users.

### Configuration (`appsettings.json`, section `RssReader`)
| Key | Default | Meaning |
|---|---|---|
| `PollInterval` | `00:01:00` | How often the worker looks for due feeds |
| `RefreshInterval` | `00:05:00` | How often each feed is refreshed |
| `MaxBackoff` | `01:00:00` | Longest wait for a failing feed |
| `MaxParallelFeeds` | `4` | Feeds fetched at once |
| `MaxFeedSizeBytes` | `10485760` | Larger responses are rejected |
| `SeedFeeds` | `[]` | Feed URLs inserted at startup if missing (dev only) |

`ConnectionStrings:SrNotification` points at PostgreSQL. It deliberately has no password: the
password is added at startup from `POSTGRES_PASSWORD` (see below). `Database:ApplyMigrationsOnStartup`
applies EF Core migrations at startup (on in Development and in docker-compose).

## Part 2: Web app and API
Users sign up and sign in with **Microsoft Entra External ID**; the app stores no passwords.
They follow RSS feeds and choose, per feed, whether new items come by **email**, **Slack**, or both.
Admins switch channels on/off for everyone, set up the SMTP server, and read the error logs.

### How it fits together
```
Browser (React)  --same origin, HttpOnly cookie-->  SrNotification.Api  --OpenID Connect-->  Entra External ID
                                                         |
                                                     PostgreSQL  <--  RSS reader (part 1), notification sender (part 3)
```
- **Sign-in ("backend for frontend")**: the API runs the OpenID Connect flow and keeps the session in
  an HttpOnly cookie. The React app never handles tokens. State-changing API calls must send the
  `X-SRN-CSRF` header (cross-site request forgery protection).
- **Users** are created on first sign-in (`Users` table, keyed by Entra's `oid`).
- **Feeds are shared**: users who follow the same URL share one `RssFeeds` row; each user has their own
  `Subscriptions` row with its own email/Slack switches. Changing a feed's URL moves only that user's
  subscription. A feed nobody follows any more is set inactive, so the reader stops polling it.
  `Subscriptions.FollowingSince` tells the sender (part 3) not to send items older than the subscription.
- **Notification email** defaults to the sign-in address. A different address only takes effect after
  the user opens the confirmation link mailed to it, so nobody can send notifications to someone
  else's inbox.
- **Slack** uses each user's own incoming-webhook URL. Webhook URLs and the SMTP password are encrypted
  with ASP.NET Core Data Protection; its keys are stored in the database (`DataProtectionKeys`).
- **SSRF protection**: users choose which URLs the server fetches, so outgoing requests (feeds, Slack)
  refuse to connect to private, loopback, link-local and other reserved addresses. For a local test
  feed, set `App:AllowPrivateNetworkFeeds` (API) and `RssReader:AllowPrivateNetworkFeeds` (reader).
- **Rate limits** on confirmation emails, Slack tests and feed checks.
- **Admin error logs**: reader failures go to `FeedFetchErrors` (kept 30 days); failed notifications
  are the `Failed` rows of `NotificationDeliveries`, written by the notification sender (part 3).

### API
| Endpoint | Who | Purpose |
|---|---|---|
| `GET /auth/login?returnUrl=`, `POST /auth/logout` | anyone | Sign in / sign up, sign out |
| `GET /api/me` | user | Profile, admin flag |
| `GET /api/me/notifications` | user | Email address, Slack webhook status, app-wide channel state |
| `PUT /api/me/notifications/email` | user | Change the notification address (sends a confirmation link) |
| `POST /api/email-confirmations` | anyone with the token | Confirm the new address |
| `PUT /api/me/notifications/slack`, `POST /api/me/notifications/slack/test` | user | Set/remove the webhook, send a test |
| `GET/POST /api/subscriptions`, `PUT/DELETE /api/subscriptions/{id}` | user | Followed feeds and their switches |
| `GET /api/admin/channels`, `PUT /api/admin/channels/{Email\|Slack}` | admin | App-wide channel switches |
| `GET/PUT /api/admin/smtp`, `POST /api/admin/smtp/test` | admin | SMTP server, test email |
| `GET /api/admin/feeds?status=all\|failing\|inactive` | admin | Feed health |
| `GET /api/admin/logs/feeds`, `GET /api/admin/logs/notifications` | admin | Error logs |

In Development the OpenAPI document is at `/openapi/v1.json` and an API explorer at `/scalar`.

### Setting up Entra External ID (one time)
Some settings belong to the **app registration** (open it under **Entra ID → App registrations →
(your app)**); others are **tenant-wide** and live outside the app registration. Each step says which.
Steps 2–6 happen in the new external tenant.

1. Create an **external tenant**: in the [Microsoft Entra admin center](https://entra.microsoft.com),
   go to **Entra ID → Overview → Manage tenants → Create**, choose **External**, then **Continue**.
   You need an Azure subscription and at least the **Tenant Creator** role on it, or pick the
   30-day free trial (no subscription needed the first time). Creation can take up to 30 minutes.
   Then switch to the new tenant (Settings → Directories + subscriptions) for the steps below.
   Full guide: [Create an external tenant](https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-create-external-tenant-portal).
2. **Register the app** (tenant-wide menu): **Entra ID → App registrations → New registration**
   (single tenant). Then, *inside the app registration*: **Authentication → Add a platform → Web**,
   and add the redirect URIs
   - `http://localhost:5173/signin-oidc` (local development through Vite)
   - `http://localhost:8080/signin-oidc` (docker compose)
   - `https://<your domain>/signin-oidc` (production)

   and, for each of those hosts, also `.../signout-callback-oidc` (where users land after signing out).
3. **Client secret** (*inside the app registration*): **Certificates & secrets → New client secret**.
   Copy the value right away; it's shown only once.
4. **Email claim** (*inside the app registration*): **Token configuration → Add optional claim → ID → `email`**.
5. **Sign-up and sign-in flow** (*tenant-wide, not in the app registration*): open **User flows**
   from the external tenant's **Overview** page, or under **External Identities → User flows**.
   Select **New user flow** (sign up and sign in, with email + password or email one-time passcode).
   Then open the new flow, go to **Applications → Add application**, and select your app.
6. **Admin role**:
   - *inside the app registration*: **App roles → Create app role** with value `Admin`
     (allowed member types: Users);
   - *tenant-wide*: **Entra ID → Enterprise applications → (your app) → Users and groups →
     Add user/group**, and assign the `Admin` role to each admin.

   To bootstrap the first admin quickly, put their sign-in email in `ADMIN_EMAIL` instead.

   > **Your tenant admin account can't sign in to the app.** The account you created the tenant with
   > manages the tenant but isn't a customer account, and the app's sign-in page only finds customer
   > accounts (created through the user flow from step 5). So, to use the app yourself, select
   > **Sign up** on the sign-in page, even with the same email address. That creates a separate
   > customer account. Give *that* account the `Admin` role (assign it as above, or put its email in
   > `ADMIN_EMAIL`), and keep the tenant admin account for managing the tenant in the portal.
   > The app tells users apart by Entra's user ID, not by email, so the two accounts are never merged.
7. Fill in `.env` (see `.env.example`):
   ```
   OIDC_AUTHORITY=https://<tenant-subdomain>.ciamlogin.com/<tenant-id>/v2.0
   OIDC_CLIENT_ID=<application (client) id>
   OIDC_CLIENT_SECRET=<client secret>
   ADMIN_EMAIL=you@example.com
   ```
   Where to find the values:
   - **`OIDC_AUTHORITY`**: there is no ready-made value in the portal; build it from two values on
     the external tenant's **Overview** page:
     - **Tenant ID** (a GUID) → `<tenant-id>`
     - **Primary domain**, e.g. `srnotification.onmicrosoft.com` → the part before
       `.onmicrosoft.com` is `<tenant-subdomain>` (the domain name you chose when creating the tenant)

     Example: `https://srnotification.ciamlogin.com/aaaabbbb-1111-2222-3333-ccccddddeeee/v2.0`.
     To check it, open `<authority>/.well-known/openid-configuration` in a browser: you should get a
     JSON document. The app reads that same document when users sign in.
     Use this full form. The short `https://<tenant-subdomain>.ciamlogin.com/` seen in some Microsoft
     samples is for the MSAL / Microsoft.Identity.Web libraries, not for ASP.NET Core's OpenID Connect
     handler that this app uses.
   - **`OIDC_CLIENT_ID`**: the app registration's **Overview** page → **Application (client) ID**.
   - **`OIDC_CLIENT_SECRET`**: the secret *value* you copied in step 3 (not its "Secret ID").

   Any other OpenID Connect provider works too (e.g. Keycloak): set its authority, client id and secret.

## Part 3: Notification sender
A .NET Worker Service that turns new feed items into email and Slack messages. Every `PollInterval`
(default 30 s) it runs one cycle:

```
RssItems (new) --1. fan-out--> NotificationDeliveries (Pending) --2. send--> Sent / retry later / Failed / Skipped
```

**1. Fan-out** picks up items it hasn't seen (`RssItems.FannedOutAt` is null) and creates one `Pending`
row per item, user and channel, only when:
- the item isn't part of a newly added feed's backlog (`IsFromInitialFetch`);
- the item was found after the user started following the feed (`Subscriptions.FollowingSince`);
- the item is less than `FanOutMaxAge` old (default 1 day), so the first start, or a sender that was
  down for days, doesn't send old news;
- the user switched that channel on for that feed and has a confirmed email address / a Slack webhook;
- the admin hasn't turned the channel off. Items found while a channel is off are never sent on it,
  so turning it back on doesn't release a burst of old notifications.

Each batch inserts the rows and sets `FannedOutAt` in one transaction, and (item, user, channel) is
unique, so items are never lost or sent twice. A PostgreSQL advisory lock lets only one sender
instance fan out at a time.

**2. Send** claims due rows (`UPDATE … FOR UPDATE SKIP LOCKED`, so several instances can run) and
sends **one message per user and channel per cycle** listing all their new items, grouped by feed,
with each item's publish time. Before sending it checks again: if the user or an admin switched the
channel off in the meantime, the rows become `Skipped`. One SMTP connection is reused for the cycle.

- **Email**: HTML + plain text, linked titles, publish time (UTC), a short plain-text summary, a
  "Manage your notifications" link and a `List-Unsubscribe` header.
- **Slack**: one mrkdwn message to the user's incoming webhook. Publish times use Slack's date
  formatting, so each reader sees their own time zone. Examples: `docs/slack-examples/`; post one
  to your webhook to see it:
  ```powershell
  Invoke-RestMethod -Uri '<webhook url>' -Method Post -ContentType 'application/json; charset=utf-8' -InFile docs\slack-examples\mrkdwn.json
  ```

Text from feeds is untrusted: it is HTML-/Slack-escaped, only http(s) links are kept, and summaries
are reduced to plain text.

### Resilience (Polly) and error logging
- **Quick retries** (Polly pipeline per channel): 2 retries with exponential backoff and jitter,
  a 30 s timeout per attempt, and a circuit breaker that pauses a channel for a minute when most of
  its sends fail (e.g. the SMTP server is down) instead of hammering it.
- **Retries stored in the database**: if the quick retries fail, the row stays `Pending` with
  `Attempts + 1` and `NextAttemptAt` pushed back by `RetryDelays` (1 min, 5 min, 30 min, 2 h). After the
  last one it becomes `Failed`. This survives restarts and longer outages.
- **Permanent errors fail at once**: a recipient the mail server rejects for good (5xx), an invalid
  address, or Slack answering 400/403/404/410 (e.g. `no_service`, `channel_is_archived`).
- Every failed attempt records `LastError`; `Failed` rows appear in the web app under
  **Admin → Failed notifications**. Sent and skipped rows are deleted after 30 days, failed ones after 90.

### Configuration (`appsettings.json`, section `NotificationSender`)
| Key | Default | Meaning |
|---|---|---|
| `PollInterval` | `00:00:30` | How often a cycle runs |
| `FanOutMaxAge` | `1.00:00:00` | Older items get no notifications |
| `MaxItemsPerMessage` | `50` | More items end with "…and N more" |
| `RetryDelays` | `1m, 5m, 30m, 2h` | Waits between stored retries; attempts = count + 1 |
| `SentRetention` / `FailedRetention` | `30` / `90` days | Clean-up of old rows |
| `PublicUrl` | from `PUBLIC_URL` | Base of the "Manage your notifications" link |

The sender uses the same Data Protection keys (stored in the database) as the API, to decrypt the
SMTP password and Slack webhooks.
