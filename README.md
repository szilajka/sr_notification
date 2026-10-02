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



