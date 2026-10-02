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
  SrNotification.Data/        EF Core DbContext, entities and migrations (shared by all parts)
  SrNotification.RssReader/   Part 1: background worker that reads the RSS feeds
tests/
  SrNotification.RssReader.Tests/
docker-compose.yml            PostgreSQL + the RSS reader
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

### Database password (`.env` file) — required
The PostgreSQL password is not stored in `appsettings.json` or `docker-compose.yml`. It is read
from a `.env` file in the repository root, which is git-ignored. Create it before the first run:
```bash
cp .env.example .env        # Windows: copy .env.example .env
```
and set your own password in it:
```
POSTGRES_PASSWORD=choose-a-strong-password
```
Where the value is used:
- `docker compose` reads `.env` automatically and passes the password to the PostgreSQL and
  RSS reader containers. Without it, `docker compose up` stops with an error saying it is missing.
- The RSS reader and the `dotnet ef` tools load `.env` themselves when run locally (from the IDE
  or `dotnet run`), and add the password to the connection string.
- On servers / CI, set `POSTGRES_PASSWORD` as a normal environment variable instead; existing
  environment variables take precedence over `.env`.

PostgreSQL applies `POSTGRES_PASSWORD` only when it creates its data volume. If you change the
password later, or ran the database before this file existed, recreate the volume (this deletes
the data): `docker compose down -v`.

### Running it
One-time: create the `.env` file (see above), then the initial migration (needs the .NET 10 SDK):
```bash
dotnet tool restore
dotnet ef migrations add InitialCreate -p src/SrNotification.Data -s src/SrNotification.Data -o Migrations
```
The `dotnet ef` commands use the local docker-compose database by default; set the
`ConnectionStrings__SrNotification` environment variable to point them at another one.

Everything in Docker:
```bash
docker compose up --build
```

Or PostgreSQL in Docker and the reader from your IDE / CLI:
```bash
docker compose up -d postgres
dotnet run --project src/SrNotification.RssReader
```
In Development the reader seeds a few public feeds (see `appsettings.Development.json`).

Tests:
```bash
dotnet test
```
