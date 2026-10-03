using System.Net;
using MailKit.Net.Smtp;
using SrNotification.Data.Entities;
using SrNotification.NotificationSender.FanOut;
using SrNotification.NotificationSender.Messages;
using SrNotification.NotificationSender.Sending;

namespace SrNotification.RssReader.Tests;

public class FanOutPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<NotificationChannel> BothChannels = [NotificationChannel.Email, NotificationChannel.Slack];

    private static FanOutItem Item(long id = 1, int feedId = 10, int minutesAgo = 1, bool initial = false) =>
        new(id, feedId, Now.AddMinutes(-minutesAgo), initial);

    private static FanOutSubscription Sub(
        int userId = 100, int feedId = 10, bool email = true, bool slack = true,
        int followingSinceMinutesAgo = 60, bool hasEmail = true, bool hasSlack = true) =>
        new(userId, feedId, email, slack, Now.AddMinutes(-followingSinceMinutesAgo), hasEmail, hasSlack);

    private static IReadOnlyList<PlannedDelivery> Plan(
        IEnumerable<FanOutItem> items, IEnumerable<FanOutSubscription> subs, IReadOnlySet<NotificationChannel>? channels = null) =>
        FanOutPlanner.Plan(items, subs, channels ?? BothChannels, Now.AddDays(-1));

    [Fact]
    public void One_delivery_per_enabled_channel()
    {
        var planned = Plan([Item()], [Sub()]);

        Assert.Equal(2, planned.Count);
        Assert.Contains(new PlannedDelivery(1, 100, NotificationChannel.Email), planned);
        Assert.Contains(new PlannedDelivery(1, 100, NotificationChannel.Slack), planned);
    }

    [Fact]
    public void Per_feed_switches_are_respected() =>
        Assert.Equal(
            new[] { new PlannedDelivery(1, 100, NotificationChannel.Slack) },
            Plan([Item()], [Sub(email: false)]));

    [Fact]
    public void Channels_turned_off_by_an_admin_get_nothing() =>
        Assert.Equal(
            new[] { new PlannedDelivery(1, 100, NotificationChannel.Email) },
            Plan([Item()], [Sub()], new HashSet<NotificationChannel> { NotificationChannel.Email }));

    [Fact]
    public void Users_without_an_address_or_webhook_get_nothing_on_that_channel() =>
        Assert.Empty(Plan([Item()], [Sub(hasEmail: false, hasSlack: false)]));

    [Fact]
    public void A_new_feeds_backlog_is_not_sent() =>
        Assert.Empty(Plan([Item(initial: true)], [Sub()]));

    [Fact]
    public void Items_older_than_the_subscription_are_not_sent() =>
        Assert.Empty(Plan([Item(minutesAgo: 30)], [Sub(followingSinceMinutesAgo: 10)]));

    [Fact]
    public void Items_older_than_the_max_age_are_not_sent() =>
        Assert.Empty(Plan([Item(minutesAgo: 2 * 24 * 60)], [Sub(followingSinceMinutesAgo: 3 * 24 * 60)]));

    [Fact]
    public void Only_subscribers_of_the_items_feed_are_notified()
    {
        var planned = Plan([Item(feedId: 10)], [Sub(userId: 1, feedId: 10), Sub(userId: 2, feedId: 20)]);

        Assert.All(planned, p => Assert.Equal(1, p.UserId));
    }
}

public class DigestComposerTests
{
    private static readonly DateTimeOffset Published = new(2026, 10, 3, 11, 53, 0, TimeSpan.Zero);

    private static DigestItem Item(
        string feed = "Example News", string title = "Polls close", string? link = "https://example.com/a",
        string? summary = null) =>
        new(feed, title, link, summary, Published, Published.AddMinutes(1));

    [Fact]
    public void Slack_message_lists_items_by_feed_with_publish_time()
    {
        var text = DigestComposer.ComposeSlack(
            [Item(), Item(title: "Storm warning", link: "https://example.com/b"), Item(feed: "Dev Blog", title: "Release")],
            maxItems: 50, publicUrl: "https://news.example.com");

        Assert.StartsWith("*3 new items from 2 feeds*", text);
        Assert.Contains("*Example News*", text);
        Assert.Contains("*Dev Blog*", text);
        Assert.Contains("• <https://example.com/a|Polls close>  ·  Published <!date^1791028380^{date_short_pretty} at {time}|3 Oct 2026, 11:53 UTC>", text);
        Assert.EndsWith("<https://news.example.com/feeds|Manage your notifications>", text);
    }

    [Fact]
    public void Slack_text_from_feeds_is_escaped()
    {
        var text = DigestComposer.ComposeSlack([Item(title: "<!channel> & friends", link: "https://example.com/?a=1|x")], 50, null);

        Assert.Contains("&lt;!channel&gt; &amp; friends", text);
        Assert.DoesNotContain("<!channel>", text);
        Assert.Contains("https://example.com/?a=1%7Cx|", text);
    }

    [Fact]
    public void Unsafe_links_are_dropped()
    {
        var text = DigestComposer.ComposeSlack([Item(link: "javascript:alert(1)")], 50, null);

        Assert.DoesNotContain("javascript:", text);
        Assert.Contains("• Polls close  ·  Published", text);
    }

    [Fact]
    public void Long_digests_end_with_a_count_of_the_rest()
    {
        var items = Enumerable.Range(1, 5).Select(i => Item(title: $"Item {i}")).ToList();

        var text = DigestComposer.ComposeSlack(items, maxItems: 3, publicUrl: null);

        Assert.Contains("Item 3", text);
        Assert.DoesNotContain("Item 4", text);
        Assert.Contains("…and 2 more items.", text);
    }

    [Fact]
    public void Email_has_subject_text_html_and_unsubscribe_header()
    {
        var message = DigestComposer.ComposeEmail(
            "user@example.com",
            [Item(title: "Polls <close>", summary: "<p>Turnout &amp; results</p>")],
            maxItems: 50,
            publicUrl: "https://news.example.com/");

        Assert.Equal("user@example.com", message.To);
        Assert.Equal("New: Polls <close>", message.Subject);
        Assert.Contains("Published 3 Oct 2026, 11:53 UTC", message.TextBody);
        Assert.Contains("Turnout & results", message.TextBody);
        Assert.Contains("Polls &lt;close&gt;", message.HtmlBody);
        Assert.DoesNotContain("<p>Turnout", message.HtmlBody);
        Assert.Equal("<https://news.example.com/feeds>", message.Headers!["List-Unsubscribe"]);
    }

    [Fact]
    public void Summaries_become_short_plain_text()
    {
        var summary = DigestComposer.ToPlainText("<div>Hello <b>world</b></div>\n\n" + new string('x', 400), 50);

        Assert.StartsWith("Hello world x", summary);
        Assert.EndsWith("…", summary);
        Assert.Equal(51, summary!.Length);
    }
}

public class DeliveryErrorsTests
{
    [Fact]
    public void Rejected_recipient_is_permanent() =>
        Assert.True(DeliveryErrors.IsPermanent(new SmtpCommandException(
            SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "No such user")));

    [Fact]
    public void Temporary_smtp_failure_is_transient() =>
        Assert.True(DeliveryErrors.IsTransient(new SmtpCommandException(
            SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxBusy, "Try later")));

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]   // no_service: webhook deleted
    [InlineData(HttpStatusCode.Gone, true)]       // channel_is_archived
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    public void Slack_errors_are_classified(HttpStatusCode status, bool permanent) =>
        Assert.Equal(permanent, DeliveryErrors.IsPermanent(new HttpRequestException("Slack", null, status)));
}
