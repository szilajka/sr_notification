import { signInUrl } from '../api';
import { Lamp } from '../components/Lamp';

/** What a signed-out visitor sees: what the app does and one way in. */
export function WelcomePage() {
  return (
    <section className="welcome">
      <h1 className="welcome-title">Hear about new articles from the sites you follow, as they're published.</h1>
      <p className="lede">
        Add the RSS feeds of the news sites and blogs you read. When a new item appears, we send it to your
        email, your Slack, or both. You decide for each feed.
      </p>

      <div className="welcome-example" aria-label="Example of a followed feed">
        <div className="feed-row feed-row-example">
          <div className="feed-main">
            <span className="feed-title">BBC News – World</span>
            <span className="feed-url">feeds.bbci.co.uk</span>
          </div>
          <div className="feed-lamps">
            <Lamp label="Email" on />
            <Lamp label="Slack" on={false} />
          </div>
        </div>
        <div className="feed-row feed-row-example">
          <div className="feed-main">
            <span className="feed-title">.NET Blog</span>
            <span className="feed-url">devblogs.microsoft.com</span>
          </div>
          <div className="feed-lamps">
            <Lamp label="Email" on />
            <Lamp label="Slack" on />
          </div>
        </div>
      </div>

      <a className="button" href={signInUrl('/feeds')}>
        Sign in or create an account
      </a>
      <p className="muted small">Accounts are free. You can sign up with your email address on the next page.</p>
    </section>
  );
}
