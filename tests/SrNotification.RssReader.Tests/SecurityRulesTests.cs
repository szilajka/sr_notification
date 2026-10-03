using System.Net;
using SrNotification.Infrastructure.Feeds;
using SrNotification.Infrastructure.Http;
using SrNotification.Infrastructure.Slack;

namespace SrNotification.RssReader.Tests;

public class PublicAddressTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.17.0.2")]        // Docker bridge
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]   // cloud metadata
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:127.0.0.1")]  // IPv4-mapped loopback
    public void Private_and_reserved_addresses_are_blocked(string address) =>
        Assert.False(PublicAddress.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("151.101.1.140")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_addresses_are_allowed(string address) =>
        Assert.True(PublicAddress.IsPublic(IPAddress.Parse(address)));
}

public class FeedUrlTests
{
    [Theory]
    [InlineData("https://example.com/feed", "https://example.com/feed")]
    [InlineData("  https://EXAMPLE.com/rss.xml  ", "https://example.com/rss.xml")]
    [InlineData("http://example.com", "http://example.com/")]
    public void Valid_urls_are_normalized(string input, string expected)
    {
        Assert.True(FeedUrl.TryNormalize(input, out var url, out _));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com/feed")]
    [InlineData("ftp://example.com/feed")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@example.com/feed")]
    [InlineData("http://localhost:5432/")]
    [InlineData("http://127.0.0.1/feed")]
    [InlineData("http://[::1]/feed")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public void Invalid_or_internal_urls_are_rejected(string input)
    {
        Assert.False(FeedUrl.TryNormalize(input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}

public class SlackWebhookTests
{
    [Fact]
    public void Slack_incoming_webhook_is_valid() =>
        Assert.True(SlackWebhook.IsValid("https://hooks.slack.com/services/T0001/B0002/abcdEFGH1234"));

    [Theory]
    [InlineData("http://hooks.slack.com/services/T0001/B0002/abcd")]
    [InlineData("https://hooks.slack.com.evil.example/services/T0001/B0002/abcd")]
    [InlineData("https://example.com/services/T0001/B0002/abcd")]
    [InlineData("https://hooks.slack.com/services/T0001/B0002")]
    [InlineData("")]
    public void Anything_else_is_rejected(string url) =>
        Assert.False(SlackWebhook.IsValid(url));

    [Fact]
    public void Mask_shows_only_the_last_characters() =>
        Assert.Equal("https://hooks.slack.com/services/…1234",
            SlackWebhook.Mask("https://hooks.slack.com/services/T0001/B0002/abcdEFGH1234"));
}
